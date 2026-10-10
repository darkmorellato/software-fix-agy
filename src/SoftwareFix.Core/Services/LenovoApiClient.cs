using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SoftwareFix.Core.Crypto;
using SoftwareFix.Core.Models;

namespace SoftwareFix.Core.Services;

/// <summary>Resposta genérica da API Lenovo (code/desc/content — ResponseModel.cs).</summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public string? Code { get; set; }
    public string? Desc { get; set; }
    public JToken? Content { get; set; }

    public ApiResponse() { }

    public ApiResponse(JObject json)
    {
        Code = json["code"]?.ToString();
        Desc = json["desc"]?.ToString();
        Success = Code == "0000" || json["success"]?.Value<bool>() == true;
        Content = json["content"];
    }
}

/// <summary>
/// Cliente REST oficial da Lenovo (https://lsa.lenovo.com/Interface).
///
/// PARIDADE 1:1 com lenovo.mbg.service.common.webservices (descompilado de
/// lenovo.mbg.service.common.webservices.dll, Software Fix 7.6.2.10):
///   - Headers: User-Agent, Request-Tag: lmsa, Cache-Control: no-cache,
///     clientVersion, windowsInfo (Base64), language, clientUUID,
///     Authorization: Bearer &lt;token&gt; e X-Device-Fingerprint;
///   - X-Device-Fingerprint = RSA("{unix_ms}|{Authorization completo}|{segmento}interface")
///     com a chave pública de /common/rsa.jhtml (PKCS#1 v1.5) — replica
///     WebApiHttpRequest.CreateClientRequestFinger;
///   - Envelope JSON: {client:{version}, dparams, language, windowsInfo} — replica RequestModel;
///   - Retry 3x com jitter aleatório — replica ApiBaseService.RequestBase.
///
/// FLUXO DE SESSÃO (replica LenovoIdWindow / LenovoIdUserLogin / SplashScreenWindow):
///   1) POST /common/rsa.jhtml  -> chave pública (campo "desc");
///   2) POST /dictionary/getApiInfo.jhtml {key:"TIP_URL"} -> login_url (OAuth2+PKCE Lenovo ID);
///   3) usuário autentica no navegador; o servidor redireciona para
///      https://lsa.lenovo.com/Tips/lenovoIdSuccess.html?lenovoid.wust=&lt;WUST&gt;;
///   4) o WUST é usado como Bearer token; GET /user/getSFUserInfo.jhtml valida a sessão.
/// </summary>
public class LenovoApiClient
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 6.3; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.79 Safari/537.36";

    private static readonly Regex WustRegex = new(
        @"lenovoid\.wust=(?<token>[^&\s]+)|(?<=\?|&)Authorization=(?<token>[^&\s]+)|(?<=\?|&)wust=(?<token>[^&\s]+)",
        RegexOptions.IgnoreCase);

    private readonly HttpClient _http;
    private RSA? _rsaPublicKey;
    private string? _jwtToken;
    private string? _tokenUrl;
    private string _clientUuid;

    public string? JwtToken => _jwtToken;
    public bool HasSession => !string.IsNullOrEmpty(_jwtToken);
    public string ClientUuid => _clientUuid;

    public LenovoApiClient(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _http = httpClient;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 200,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(120000) };
        }
        _clientUuid = LoadOrCreateClientUuid();
    }

    // ------------------------------------------------------------------
    // Sessão / persistência (XDG: ~/.config/software-fix/session.json)
    // ------------------------------------------------------------------

    private static string LoadOrCreateClientUuid()
    {
        try
        {
            if (File.Exists(Configurations.ClientUuidFile))
            {
                string existing = File.ReadAllText(Configurations.ClientUuidFile).Trim();
                if (!string.IsNullOrEmpty(existing)) return existing;
            }
            string uuid = Guid.NewGuid().ToString();
            File.WriteAllText(Configurations.ClientUuidFile, uuid);
            return uuid;
        }
        catch
        {
            return Guid.NewGuid().ToString();
        }
    }

    /// <summary>Carrega sessão salva (~/.config/software-fix/session.json) se existir.</summary>
    public bool TryLoadSession()
    {
        try
        {
            if (!File.Exists(Configurations.SessionFile)) return false;
            var session = JObject.Parse(File.ReadAllText(Configurations.SessionFile));
            string? token = session["token"]?.ToString();
            if (string.IsNullOrEmpty(token)) return false;
            _jwtToken = token;
            _tokenUrl = session["tokenUrl"]?.ToString();
            string? uuid = session["clientUuid"]?.ToString();
            if (!string.IsNullOrEmpty(uuid)) _clientUuid = uuid;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SaveSession()
    {
        try
        {
            var session = new JObject
            {
                ["token"] = _jwtToken,
                ["tokenUrl"] = _tokenUrl,
                ["clientUuid"] = _clientUuid,
                ["savedAt"] = DateTimeOffset.UtcNow.ToString("o")
            };
            File.WriteAllText(Configurations.SessionFile, session.ToString());
        }
        catch
        {
            // persistência é best-effort (como no original, que grava em options criptografadas)
        }
    }

    public void ClearSession()
    {
        _jwtToken = null;
        try
        {
            if (File.Exists(Configurations.SessionFile))
                File.Delete(Configurations.SessionFile);
        }
        catch { }
    }

    // ------------------------------------------------------------------
    // Inicialização
    // ------------------------------------------------------------------

    /// <summary>
    /// Prepara o cliente: obtém a chave pública RSA e carrega a sessão salva.
    /// (O app oficial não usa initToken no boot — a sessão vem do fluxo OAuth.)
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        try
        {
            if (_rsaPublicKey == null)
                await FetchPublicKeyAsync();

            if (_jwtToken == null)
                TryLoadSession();

            return _rsaPublicKey != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>POST /common/rsa.jhtml — chave pública retornada no campo "desc" (Java X.509).</summary>
    public async Task FetchPublicKeyAsync()
    {
        var response = await SendWithRetryAsync(HttpMethod.Post, Configurations.UrlPublicKey, null, authorize: false);
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
        response.Dispose();
    }

    // ------------------------------------------------------------------
    // Fluxo de login Lenovo ID (OAuth + PKCE)
    // ------------------------------------------------------------------

    /// <summary>
    /// POST /dictionary/getApiInfo.jhtml {key:"TIP_URL"} — devolve a URL de login
    /// OAuth2 Lenovo ID (o servidor já inclui state/client_id/PKCE). Replica o
    /// carregamento feito por LenovoIdWindow ao abrir o WebView2.
    /// O "content" pode ser objeto {login_url, token_url} (loginResModel) ou a URL crua.
    /// </summary>
    public async Task<ApiResponse> GetLoginUrlAsync()
    {
        var json = await PostJsonAsync(WebApiUrl.CALL_API_URL, new { key = "TIP_URL" }, authorize: false);
        var result = new ApiResponse(json);

        var contentToken = json["content"];
        if (contentToken is JObject obj)
        {
            result.Content = obj["login_url"] ?? obj["token_url"];
            _tokenUrl = obj["token_url"]?.ToString();
            if (result.Content == null) result.Content = obj.ToString();
        }
        else if (contentToken is JValue { Value: string url } && url.Contains("authorize"))
        {
            // formato atual do servidor: URL crua
            _tokenUrl = DeriveTokenBaseUrl(url);
            result.Content = url;
            result.Success = true;
        }
        else if (result.Desc != null && result.Desc.StartsWith("http"))
        {
            _tokenUrl = DeriveTokenBaseUrl(result.Desc);
            result.Content = result.Desc;
            result.Success = true;
        }
        return result;
    }

    private static string DeriveTokenBaseUrl(string loginUrl)
    {
        int idx = loginUrl.IndexOf("/authorize", StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? loginUrl[..idx] : loginUrl;
    }

    /// <summary>
    /// Valida e aplica o token WUST obtido no redirect do OAuth:
    ///   GET /user/getSFUserInfo.jhtml com Authorization: Bearer &lt;WUST&gt; +
    ///   X-Device-Fingerprint. Se code == "0000", a sessão é salva.
    /// Aceita o WUST puro ou a URL completa de retorno (extrai "lenovoid.wust=").
    /// </summary>
    public async Task<ApiResponse> LoginWithWustAsync(string wustOrUrl)
    {
        string wust = ExtractWust(wustOrUrl);
        if (string.IsNullOrEmpty(wust))
            return new ApiResponse { Success = false, Code = "PARAM", Desc = "WUST não encontrado na entrada." };

        _jwtToken = wust;
        var result = await GetUserInfoAsync();
        if (result.Success)
        {
            SaveSession();
        }
        else
        {
            _jwtToken = null; // token inválido: não manter sessão quebrada
        }
        return result;
    }

    /// <summary>Extrai o WUST de uma URL de retorno ou aceita um valor puro.</summary>
    public static string ExtractWust(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var m = WustRegex.Match(input);
        if (m.Success)
        {
            string keyed = m.Groups["token"].Value;
            if (!string.IsNullOrEmpty(keyed)) return keyed.Trim();
        }
        // sem parâmetro: assume que já é o token (sem espaços/URL)
        string trimmed = input.Trim();
        if (!trimmed.Contains(' ') && !trimmed.Contains("://") && trimmed.Length > 8)
            return trimmed;
        return string.Empty;
    }

    /// <summary>GET /user/getSFUserInfo.jhtml — valida a sessão e retorna os dados do usuário.</summary>
    public async Task<ApiResponse> GetUserInfoAsync()
    {
        var json = await GetJsonAsync(WebApiUrl.LENOVOID_LOGIN_CALLBACK, authorize: true);
        var result = new ApiResponse(json);
        if (json["content"] != null)
            result.Content = json["content"];
        return result;
    }

    /// <summary>POST /user/guestLogin.jhtml {accountId} — login convidado (GuestLogin.cs).</summary>
    public async Task<ApiResponse> GuestLoginAsync(string accountId)
    {
        var json = await PostJsonAsync(WebApiUrl.USER_GUEST_LOGIN, new { accountId }, authorize: true);
        var result = new ApiResponse(json);
        if (result.Success && json["content"]?["token"] != null)
        {
            _jwtToken = json["content"]!["token"]!.ToString();
            SaveSession();
        }
        return result;
    }

    /// <summary>
    /// POST /client/initToken.jhtml {clientUUID} — token inicial de bootstrap do cliente
    /// (o app oficial obtém este token antes de operações sem login de usuário).
    /// </summary>
    public async Task<ApiResponse> InitTokenAsync()
    {
        var json = await PostJsonAsync(WebApiUrl.INIT_TOKEN, new { clientUUID = _clientUuid }, authorize: true);
        var result = new ApiResponse(json);
        if (json["content"] != null)
            result.Content = json["content"];
        return result;
    }

    // ------------------------------------------------------------------
    // Serviços de negócio (mantidos da versão anterior; assinaturas iguais)
    // ------------------------------------------------------------------

    /// <summary>Replica WarrantyService: consulta garantia por IMEI/SN via dictionary getApiInfo.</summary>
    public async Task<WarrantyInfoModel?> GetWarrantyAsync(string imeiOrSn)
    {
        if (string.IsNullOrWhiteSpace(imeiOrSn)) return null;

        try
        {
            var json = await PostJsonAsync(WebApiUrl.CALL_API_URL,
                new { key = "WARRANTY_URL", param = imeiOrSn.Trim() }, authorize: true);
            var rawResult = json["content"]?.ToString();
            if (!string.IsNullOrEmpty(rawResult) && rawResult.Contains("Warranties"))
            {
                var parsed = ParseSupportWarrantyJson(rawResult);
                if (parsed != null) return parsed;
            }

            string poiParam = (imeiOrSn.Length > 10 ? "IMEI" : "") + imeiOrSn.Trim();
            var poiJson = await PostJsonAsync(WebApiUrl.CALL_API_URL,
                new { key = "POI_V1_URL", param = poiParam }, authorize: true);
            var poiRaw = poiJson["content"]?.ToString();
            if (!string.IsNullOrEmpty(poiRaw) && poiRaw.Contains("serviceInfoList"))
            {
                var parsed = ParseSdeWarrantyJson(poiRaw);
                if (parsed != null) return parsed;
            }
        }
        catch
        {
            // offline / falha de rede
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

    /// <summary>Replica RESUCE_AUTOMATCH_GETROM (/rescueDevice/getNewResource.jhtml).</summary>
    public async Task<ResourceResponseModel?> FindRomByDeviceAsync(DeviceRescueInfo device)
    {
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

        var json = await PostJsonAsync(Configurations.UrlGetRom, queryPayload, authorize: true);
        if (json["code"]?.ToString() != "0000" && json["success"]?.Value<bool>() != true) return null;

        var listToken = json["content"] as JArray;
        if (listToken != null && listToken.Count > 0)
        {
            return listToken[0].ToObject<ResourceResponseModel>();
        }

        return null;
    }

    /// <summary>Replica GET_RESOURCES_BY_IMEI / GET_RESOURCES_BY_SN (plugin Rescue).</summary>
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

        var json = await PostJsonAsync(endpoint, payload, authorize: true);
        var listToken = json["content"] as JArray;
        if (listToken != null && listToken.Count > 0)
        {
            return listToken[0].ToObject<ResourceResponseModel>();
        }

        return null;
    }

    /// <summary>Replica WebServiceProxy.GetNotice (NOTICE_BROADCAST_URL).</summary>
    public async Task<JToken?> GetNoticeBroadcastAsync()
    {
        var json = await PostJsonAsync(WebApiUrl.NOTICE_BROADCAST_URL, null, authorize: true);
        return json["content"];
    }

    /// <summary>Replica getModelNames manual matching (plugin Rescue).</summary>
    public async Task<DeviceModelInfoListModel?> GetAllModelNamesAsync(string category = "Phone")
    {
        var payload = new Dictionary<string, string>
        {
            { "country", "Brazil" },
            { "category", category }
        };

        var json = await PostJsonAsync(Configurations.UrlGetModelNames, payload, authorize: true);
        var contentToken = json["content"];
        if (contentToken == null) return null;

        return contentToken.ToObject<DeviceModelInfoListModel>();
    }

    /// <summary>Replica RESUCE_AUTOMATCH_GETPARAMS_MAPPING.</summary>
    public async Task<List<string>> GetRomMatchParamsAsync(string modelName)
    {
        var json = await PostJsonAsync(Configurations.UrlRomMatchParams, new { modelName }, authorize: true);
        var paramsToken = json["content"]?["params"] as JArray;
        if (paramsToken != null)
        {
            return paramsToken.Select(t => t.ToString()).ToList();
        }

        return new List<string>();
    }

    /// <summary>Replica GET_FASTBOOTDATA_RECIPE (/rescueDevice/getRescueModelRecipe.jhtml).</summary>
    public async Task<string?> GetRescueRecipeXmlOrJsonAsync(string modelName)
    {
        var response = await SendWithRetryAsync(HttpMethod.Post, Configurations.UrlGetRescueModelRecipe,
            new { modelName }, authorize: true);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return null;
        }
        string content = await response.Content.ReadAsStringAsync();
        response.Dispose();
        return content;
    }

    // ------------------------------------------------------------------
    // Núcleo HTTP (paridade WebApiHttpRequest)
    // ------------------------------------------------------------------

    private async Task<JObject> PostJsonAsync(string url, object? body, bool authorize)
    {
        var response = await SendWithRetryAsync(HttpMethod.Post, url, body, authorize);
        string content = await response.Content.ReadAsStringAsync();
        response.Dispose();
        try
        {
            return JObject.Parse(content);
        }
        catch
        {
            return new JObject { ["code"] = "PARSE_ERROR", ["content"] = content };
        }
    }

    private async Task<JObject> GetJsonAsync(string url, bool authorize)
    {
        var response = await SendWithRetryAsync(HttpMethod.Get, url, null, authorize);
        string content = await response.Content.ReadAsStringAsync();
        response.Dispose();
        try
        {
            return JObject.Parse(content);
        }
        catch
        {
            return new JObject { ["code"] = "PARSE_ERROR", ["content"] = content };
        }
    }

    /// <summary>Retry 3x com jitter — replica ApiBaseService.RequestBase (tryCount=3, sleep aleatório).</summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpMethod method, string url, object? body, bool authorize)
    {
        HttpResponseMessage? last = null;
        Exception? lastError = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                last = await SendRawRequestAsync(method, url, body, authorize);
                if (last.IsSuccessStatusCode) return last;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
            await Task.Delay(new Random().Next(300, 1000));
        }
        if (last != null) return last;
        throw lastError ?? new HttpRequestException("Falha ao conectar em " + url);
    }

    private async Task<HttpResponseMessage> SendRawRequestAsync(HttpMethod method, string url, object? body, bool authorize)
    {
        using var request = new HttpRequestMessage(method, url);

        // Headers fixos (WebApiHttpRequest.RequestBase)
        request.Headers.Add("User-Agent", UserAgent);
        request.Headers.Add("Request-Tag", "lmsa");
        request.Headers.Add("Cache-Control", "no-cache");
        request.Headers.Add("clientVersion", Configurations.Version);
        request.Headers.Add("windowsInfo",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("Linux x86_64, 64bit")));
        request.Headers.Add("language", "pt-BR");
        request.Headers.Add("clientUUID", _clientUuid);

        // Authorization + fingerprint.
        // IMPORTANTE: o guestLogin (catálogo sem Lenovo ID) precisa do fingerprint MESMO
        // sem token — por isso o fingerprint é anexado sempre que authorize=true (com o
        // header Authorization vazio quando não há sessão), igual ao fluxo pós-hit do SDK.
        if (authorize)
        {
            string authHeaderValue = "";
            if (!string.IsNullOrEmpty(_jwtToken))
            {
                authHeaderValue = "Bearer " + _jwtToken;
                request.Headers.TryAddWithoutValidation("Authorization", authHeaderValue);
            }
            if (_rsaPublicKey != null && url != WebApiUrl.GET_PUBLIC_KEY)
            {
                try
                {
                    string fingerprint = RsaHelper.GenerateDeviceFingerprint(_rsaPublicKey, authHeaderValue, url);
                    request.Headers.Add("X-Device-Fingerprint", fingerprint);
                }
                catch
                {
                    // sem fingerprint: o servidor responderá 410 (comportamento anterior)
                }
            }
        }

        // Corpo: envelope oficial (RequestModel) — e Content-Length 0 em POST sem corpo
        var envelope = new RequestEnvelope(body);
        string jsonBody = JsonConvert.SerializeObject(envelope);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        return await _http.SendAsync(request);
    }
}
