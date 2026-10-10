using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        var response = await SendRawRequestAsync(HttpMethod.Post, Configurations.UrlPublicKey, null, false);
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
        var response = await SendAuthorizedRequestAsync(HttpMethod.Post, Configurations.UrlInitToken, payload);
        if (response.IsSuccessStatusCode)
        {
            string content = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            _jwtToken = json["content"]?["token"]?.ToString() ?? json["content"]?.ToString();
        }
    }

    /// <summary>
    /// Replicates official WarrantyService from lenovo.mbg.service.common.webservices.dll
    /// Queries official WARRANTY_URL / POI_URL dictionary endpoints.
    /// </summary>
    public async Task<WarrantyInfoModel?> GetWarrantyAsync(string imeiOrSn)
    {
        if (string.IsNullOrWhiteSpace(imeiOrSn)) return null;

        try
        {
            // 1. Try official WARRANTY_URL dictionary call
            var payload = new { key = "WARRANTY_URL", param = imeiOrSn.Trim() };
            var response = await SendAuthorizedRequestAsync(HttpMethod.Post, Configurations.UrlCallApi, payload);
            if (response.IsSuccessStatusCode)
            {
                string content = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);
                var rawResult = json["content"]?.ToString();
                if (!string.IsNullOrEmpty(rawResult) && rawResult.Contains("Warranties"))
                {
                    var parsed = ParseSupportWarrantyJson(rawResult);
                    if (parsed != null) return parsed;
                }
            }

            // 2. Fallback to POI_V1_URL (SDE)
            string poiParam = (imeiOrSn.Length > 10 ? "IMEI" : "") + imeiOrSn.Trim();
            var poiPayload = new { key = "POI_V1_URL", param = poiParam };
            var poiResponse = await SendAuthorizedRequestAsync(HttpMethod.Post, Configurations.UrlCallApi, poiPayload);
            if (poiResponse.IsSuccessStatusCode)
            {
                string poiContent = await poiResponse.Content.ReadAsStringAsync();
                var poiJson = JObject.Parse(poiContent);
                var poiRaw = poiJson["content"]?.ToString();
                if (!string.IsNullOrEmpty(poiRaw) && poiRaw.Contains("serviceInfoList"))
                {
                    var parsed = ParseSdeWarrantyJson(poiRaw);
                    if (parsed != null) return parsed;
                }
            }
        }
        catch
        {
            // Offline / network failure handling
        }

        return null;
    }

    private WarrantyInfoModel? ParseSupportWarrantyJson(string jsonString)
    {
        try
        {
            var jobj = JObject.Parse(jsonString);
            var lastWarranty = jobj.SelectToken("$.Warranties")?.OrderByDescending(n => n.Value<string>("End")).FirstOrDefault();
            if (lastWarranty == null) return null;

            string? endStr = lastWarranty.Value<string>("End");
            string? startStr = lastWarranty.Value<string>("Start");
            if (string.IsNullOrEmpty(endStr)) return null;

            DateTime endDate = DateTime.Parse(endStr);
            DateTime startDate = string.IsNullOrEmpty(startStr) ? DateTime.MinValue : DateTime.Parse(startStr);
            DateTime now = DateTime.Today;

            int daysRemaining = Math.Max(0, (endDate - now).Days);
            int monthsDiff = (endDate.Year - now.Year) * 12 + endDate.Month - now.Month;

            string countryName = "Brasil";
            var countries = jobj.SelectToken("$.Countries") as JArray;
            if (countries != null && countries.Count > 0)
            {
                countryName = countries[0].Value<string>("Name") ?? "Brasil";
            }

            return new WarrantyInfoModel
            {
                StartMonth = startDate.ToString("MMM.yyyy", CultureInfo.InvariantCulture),
                EndMonth = endDate.ToString("MMM.yyyy", CultureInfo.InvariantCulture),
                WarrantyStartDate = startDate.ToString("yyyy-MM-dd"),
                WarrantyEndDate = endDate.ToString("yyyy-MM-dd"),
                ExpiredDays = daysRemaining,
                MonthDiff = monthsDiff,
                InWarranty = now <= endDate,
                ShipLocation = countryName,
                Brand = "Motorola",
                Msn = jobj.SelectToken("$.Serial")?.ToString() ?? ""
            };
        }
        catch
        {
            return null;
        }
    }

    private WarrantyInfoModel? ParseSdeWarrantyJson(string jsonString)
    {
        try
        {
            var jobj = JObject.Parse(jsonString);
            var service = jobj.SelectToken("$.serviceInfoList[0]");
            if (service == null) return null;

            string? startStr = service.Value<string>("warrantyStartDate");
            string? endStr = service.Value<string>("warrantyEndDate");
            string? country = service.Value<string>("countryName");

            if (string.IsNullOrEmpty(endStr)) return null;

            DateTime endDate = DateTime.Parse(endStr);
            DateTime startDate = string.IsNullOrEmpty(startStr) ? DateTime.MinValue : DateTime.Parse(startStr);
            DateTime now = DateTime.Today;

            int daysRemaining = Math.Max(0, (endDate - now).Days);

            return new WarrantyInfoModel
            {
                StartMonth = startDate.ToString("MMM.yyyy", CultureInfo.InvariantCulture),
                EndMonth = endDate.ToString("MMM.yyyy", CultureInfo.InvariantCulture),
                WarrantyStartDate = startDate.ToString("yyyy-MM-dd"),
                WarrantyEndDate = endDate.ToString("yyyy-MM-dd"),
                ExpiredDays = daysRemaining,
                InWarranty = now <= endDate,
                ShipLocation = country ?? "Brasil",
                Brand = "Motorola",
                Msn = jobj.SelectToken("$.machineInfo.serialNumber")?.ToString() ?? ""
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Replicates official getModelNames.jhtml endpoint for manual device matching.
    /// </summary>
    public async Task<DeviceModelInfoListModel?> GetAllModelNamesAsync(string category = "Phone")
    {
        var payload = new Dictionary<string, string>
        {
            { "country", "Brazil" },
            { "category", category }
        };

        var response = await SendAuthorizedRequestAsync(HttpMethod.Post, Configurations.UrlGetModelNames, payload);
        if (!response.IsSuccessStatusCode) return null;

        string content = await response.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        var contentToken = json["content"];
        if (contentToken == null) return null;

        return contentToken.ToObject<DeviceModelInfoListModel>();
    }

    /// <summary>
    /// Replicates official getRomMatchParams.jhtml endpoint to find required parameter mapping.
    /// </summary>
    public async Task<List<string>> GetRomMatchParamsAsync(string modelName)
    {
        var payload = new { modelName = modelName };
        var response = await SendAuthorizedRequestAsync(HttpMethod.Post, Configurations.UrlRomMatchParams, payload);
        if (!response.IsSuccessStatusCode) return new List<string>();

        string content = await response.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        var paramsToken = json["content"]?["params"] as JArray;
        if (paramsToken != null)
        {
            return paramsToken.Select(t => t.ToString()).ToList();
        }

        return new List<string>();
    }

    /// <summary>
    /// Replicates official RESUCE_AUTOMATCH_GETROM (/rescueDevice/getNewResource.jhtml)
    /// </summary>
    public async Task<ResourceResponseModel?> FindRomByDeviceAsync(DeviceRescueInfo device)
    {
        // Construct official param dictionary as in FlashBusiness.cs
        var devParams = new Dictionary<string, string>
        {
            { "modelName", string.IsNullOrEmpty(device.Sku) ? device.ModelName : device.Sku },
            { "roCarrier", device.RoCarrier },
            { "fsgVersion.qcom", device.BasebandVersion },
            { "fsgVersion.mtk", device.BasebandVersion },
            { "fingerPrint", device.Fingerprint },
            { "softwareVersion", device.SoftwareVersion },
            { "blurVersion", device.BlurVersion },
            { "simCount", string.IsNullOrEmpty(device.DualSim) ? "Dual" : device.DualSim },
            { "category", "Phone" }
        };

        var queryPayload = new Dictionary<string, object>
        {
            { "modelName", string.IsNullOrEmpty(device.Sku) ? device.ModelName : device.Sku },
            { "params", devParams },
            { "imei", device.Imei },
            { "sn", device.SerialNumber },
            { "channelId", device.ChannelId },
            { "matchType", 0 }
        };

        var response = await SendAuthorizedRequestAsync(HttpMethod.Post, Configurations.UrlGetRom, queryPayload);
        if (!response.IsSuccessStatusCode) return null;

        string content = await response.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        if (json["code"]?.ToString() != "0000" && json["success"]?.Value<bool>() != true) return null;

        var listToken = json["content"] as JArray;
        if (listToken != null && listToken.Count > 0)
        {
            return listToken[0].ToObject<ResourceResponseModel>();
        }

        return null;
    }

    /// <summary>
    /// Replicates official getNewResourceByImei.jhtml & getNewResourceBySN.jhtml
    /// </summary>
    public async Task<ResourceResponseModel?> FindRomByImeiOrSnAsync(string imeiOrSn)
    {
        string endpoint = imeiOrSn.Length >= 14
            ? Configurations.UrlGetRomByImei
            : Configurations.UrlGetRomBySn;

        var payload = new Dictionary<string, string>
        {
            { "imei", imeiOrSn.Trim() },
            { "sn", imeiOrSn.Trim() }
        };

        var response = await SendAuthorizedRequestAsync(HttpMethod.Post, endpoint, payload);
        if (!response.IsSuccessStatusCode) return null;

        string content = await response.Content.ReadAsStringAsync();
        var json = JObject.Parse(content);
        var listToken = json["content"] as JArray;
        if (listToken != null && listToken.Count > 0)
        {
            return listToken[0].ToObject<ResourceResponseModel>();
        }

        return null;
    }

    /// <summary>
    /// Replicates official getRescueModelRecipe.jhtml
    /// </summary>
    public async Task<string?> GetRescueRecipeXmlOrJsonAsync(string modelName)
    {
        var payload = new { modelName = modelName };
        var response = await SendAuthorizedRequestAsync(HttpMethod.Post, Configurations.UrlGetRescueModelRecipe, payload);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadAsStringAsync();
    }

    private async Task<HttpResponseMessage> SendAuthorizedRequestAsync(HttpMethod method, string url, object? body)
    {
        return await SendRawRequestAsync(method, url, body, true);
    }

    private async Task<HttpResponseMessage> SendRawRequestAsync(HttpMethod method, string url, object? body, bool authorize)
    {
        using var request = new HttpRequestMessage(method, url);

        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.3; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.79 Safari/537.36");
        request.Headers.Add("Request-Tag", "lmsa");
        request.Headers.Add("clientVersion", Configurations.Version);
        request.Headers.Add("windowsInfo", Convert.ToBase64String(Encoding.UTF8.GetBytes("Linux x86_64, 64bit")));
        request.Headers.Add("language", "pt-BR");
        request.Headers.Add("clientUUID", _clientUuid);

        if (authorize && !string.IsNullOrEmpty(_jwtToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
            if (_rsaPublicKey != null)
            {
                string fingerprint = RsaHelper.GenerateDeviceFingerprint(_rsaPublicKey, _jwtToken, url);
                request.Headers.Add("X-Device-Fingerprint", fingerprint);
            }
        }

        if (body != null)
        {
            // Wrap in official RequestEnvelope
            var envelope = new RequestEnvelope(body);
            string jsonBody = JsonConvert.SerializeObject(envelope);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return await _http.SendAsync(request);
    }
}
