using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SoftwareFix.Core.Crypto;
using SoftwareFix.Core.Models;

namespace SoftwareFix.Core.Services;

public class LenovoApiClient
{
    private readonly HttpClient _http;
    private RSA? _rsaPublicKey;
    private string? _jwtToken;
    private readonly string _clientUuid;

    public LenovoApiClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
        _clientUuid = Guid.NewGuid().ToString("N");
    }

    public async Task<bool> InitializeAsync()
    {
        try
        {
            await FetchPublicKeyAsync();
            await InitTokenAsync();
            return !string.IsNullOrEmpty(_jwtToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task FetchPublicKeyAsync()
    {
        var response = await SendRawRequestAsync(HttpMethod.Post, "/common/rsa.jhtml", null, false);
        if (response.IsSuccessStatusCode)
        {
            string content = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            string? base64Key = json["desc"]?.ToString();
            if (!string.IsNullOrEmpty(base64Key))
            {
                _rsaPublicKey = RsaHelper.CreateRsaFromJavaPublicKey(base64Key);
            }
        }
    }

    public async Task InitTokenAsync()
    {
        var payload = new { clientUUID = _clientUuid };
        var response = await SendAuthorizedRequestAsync(HttpMethod.Post, "/client/initToken.jhtml", payload);
        if (response.IsSuccessStatusCode)
        {
            string content = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            _jwtToken = json["content"]?["token"]?.ToString() ?? json["content"]?.ToString();
        }
    }

    public async Task<RomPackage?> FindRomByDeviceAsync(DeviceRescueInfo device)
    {
        var queryPayload = new
        {
            modelName = device.ModelName,
            sku = device.Sku,
            roCarrier = device.RoCarrier,
            fingerprint = device.Fingerprint,
            blurVersion = device.BlurVersion,
            fsgVersion = device.BasebandVersion,
            simCount = device.DualSim,
            imei = device.Imei,
            sn = device.SerialNumber,
            brand = device.Brand
        };

        var response = await SendAuthorizedRequestAsync(HttpMethod.Post, "/rescueDevice/getNewResource.jhtml", queryPayload);
        if (!response.IsSuccessStatusCode) return null;

        string content = await response.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["code"]?.ToString() != "0000" && json["success"]?.Value<bool>() != true) return null;

        var romToken = json["content"]?["romResources"] ?? json["content"];
        if (romToken == null) return null;

        return romToken.ToObject<RomPackage>();
    }

    public async Task<RomPackage?> FindRomByImeiOrSnAsync(string imeiOrSn)
    {
        string endpoint = imeiOrSn.Length >= 14
            ? "/rescueDevice/getNewResourceByImei.jhtml"
            : "/rescueDevice/getNewResourceBySN.jhtml";

        var payload = new Dictionary<string, string> { { "imei", imeiOrSn }, { "sn", imeiOrSn } };
        var response = await SendAuthorizedRequestAsync(HttpMethod.Post, endpoint, payload);
        if (!response.IsSuccessStatusCode) return null;

        string content = await response.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        var romToken = json["content"]?["romResources"] ?? json["content"];
        return romToken?.ToObject<RomPackage>();
    }

    public async Task<string?> GetRescueRecipeXmlOrJsonAsync(string modelName)
    {
        var payload = new { modelName = modelName };
        var response = await SendAuthorizedRequestAsync(HttpMethod.Post, "/rescueDevice/getRescueModelRecipe.jhtml", payload);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadAsStringAsync();
    }

    private async Task<HttpResponseMessage> SendAuthorizedRequestAsync(HttpMethod method, string path, object? body)
    {
        return await SendRawRequestAsync(method, path, body, true);
    }

    private async Task<HttpResponseMessage> SendRawRequestAsync(HttpMethod method, string path, object? body, bool authorize)
    {
        string url = Configurations.BaseHttpUrl + path;
        using var request = new HttpRequestMessage(method, url);

        request.Headers.Add("User-Agent", "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        request.Headers.Add("Request-Tag", "lmsa");
        request.Headers.Add("clientVersion", Configurations.Version);
        request.Headers.Add("windowsInfo", Convert.ToBase64String(Encoding.UTF8.GetBytes("Linux x86_64")));
        request.Headers.Add("language", "pt-BR");
        request.Headers.Add("clientUUID", _clientUuid);

        if (authorize && !string.IsNullOrEmpty(_jwtToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
            if (_rsaPublicKey != null)
            {
                string fingerprint = RsaHelper.GenerateDeviceFingerprint(_rsaPublicKey, _jwtToken, path);
                request.Headers.Add("X-Device-Fingerprint", fingerprint);
            }
        }

        if (body != null)
        {
            string jsonBody = JsonConvert.SerializeObject(body);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return await _http.SendAsync(request);
    }
}
