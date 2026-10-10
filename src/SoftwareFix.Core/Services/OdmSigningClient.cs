using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SoftwareFix.Core.Services;

/// <summary>
/// Falha ao assinar no serviço ODM (equivalente ao WebException que o original lança
/// em Web.DataSignODM quando responseCode != "0", ou a erros de rede).
/// </summary>
public class OdmSigningException : Exception
{
    public OdmSigningException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Cliente do serviço de assinatura ODM ("GPS auth") da Motorola — usado no resgate
/// de modelos Motorola MediaTek (SLA / secure boot no fastboot).
///
/// PARIDADE 1:1 com o descompilado (Software Fix 7.6.2.10, smartdevice):
///   - ODMSocketServer/Web.cs                 -> SignDataAsync (DataSignODM)
///   - ODMSocketServer/DataSigningODMService.cs -> corpo JSON e parsing {responseCode,responseMsg,msg}
///   - ODMSocketServer/RestService.cs         -> POST JSON + Basic auth + timeout 100s
///   - ODMServerMain.signDataCollectPenang    -> SignDataWithRetryAsync (3 tentativas, 500ms)
///
/// Endpoint: https://ebiz-esb.cloud.motorola.net/callsimunlockservice
/// Autenticação: HTTP Basic (conta de serviço/técnico — no app oficial vem dos
/// PARAMS da receita de resgate; no port é fornecida pelo usuário).
/// </summary>
public class OdmSigningClient
{
    public const string DefaultUrl = "https://ebiz-esb.cloud.motorola.net/callsimunlockservice";

    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _user;
    private readonly string _password;

    /// <summary>Log de depuração (replica os LogHelper.LogInstance.Debug do original). Nunca loga a senha.</summary>
    public Action<string>? Log { get; set; }

    public OdmSigningClient(string user, string password, string? url = null, HttpClient? httpClient = null)
    {
        _user = user;
        _password = password;
        // url explícita > env SOFTWARE_FIX_ODM_URL (usada pelos testes offline) > endpoint oficial
        _url = url ?? Environment.GetEnvironmentVariable("SOFTWARE_FIX_ODM_URL") ?? DefaultUrl;

        if (httpClient != null)
        {
            _http = httpClient;
        }
        else
        {
            // Timeout 100s — paridade com RestService.Timeout (TimeSpan.FromSeconds(100.0))
            _http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = TimeSpan.FromSeconds(100)
            };
        }
    }

    /// <summary>
    /// Assinatura em 1 tentativa — replica Web.DataSignODM -> DataSigningODMService.SignData.
    /// Devolve o campo "msg" da resposta (hash assinado, em hex).
    /// Lança <see cref="OdmSigningException"/> se responseCode != "0" (como o WebException original).
    /// </summary>
    public async Task<string> SignDataAsync(string imei, string mascId, string clientIp, string clientReqType,
        string logId, string prodId, string keyType, string keyName, string data)
    {
        // Campos exatamente como DataSigningODMInput.Fields (o original OMITE valores nulos)
        var body = new JObject();
        void Add(string key, string? value) { if (value != null) body[key] = value; }
        Add("newIMEI", imei);
        Add("mascid", mascId);
        Add("clientIP", clientIp);
        Add("clientReqType", clientReqType);
        Add("rsd_log_id", logId);
        Add("prod_id", prodId);
        Add("type", keyType);
        Add("keyname", keyName);
        Add("data", data);

        string json = body.ToString(Formatting.Indented); // original: SerializeObject(..., Formatting.Indented)

        Log?.Invoke("Contacting DataSigningODMService");
        Log?.Invoke("DataSigningODMService request:\n" + json);

        using var request = new HttpRequestMessage(HttpMethod.Post, _url);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        // Basic auth — paridade com NetworkCredential(Login.UserName, Login.Password) do original
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(_user + ":" + _password)));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (Exception ex)
        {
            throw new OdmSigningException("Falha de rede ao contatar o serviço ODM: " + ex.Message, ex);
        }

        bool success = response.IsSuccessStatusCode;
        int status = (int)response.StatusCode;
        string text = await response.Content.ReadAsStringAsync();
        response.Dispose();

        Log?.Invoke("DataSigningODMService reply: " + text);

        if (!success)
        {
            throw new OdmSigningException($"HTTP {status} do serviço ODM: {Truncate(text)}");
        }

        JObject parsed;
        try
        {
            parsed = JObject.Parse(text);
        }
        catch (Exception ex)
        {
            throw new OdmSigningException("Resposta inválida (não-JSON) do serviço ODM: " + Truncate(text), ex);
        }

        string? code = parsed["responseCode"]?.ToString();
        string? message = parsed["responseMsg"]?.ToString();
        string? returned = parsed["msg"]?.ToString();

        if (code != "0")
        {
            // Paridade com Web.DataSignODM: throw em responseCode != "0"
            throw new OdmSigningException(
                $"Invalid response code '{code}' from ODM Data Signing service: {message}");
        }

        Log?.Invoke("DataSigningODMService request completed");
        return returned ?? string.Empty;
    }

    /// <summary>
    /// Replica ODMServerMain.signDataCollectPenang: até 3 tentativas com 500ms de espera
    /// entre falhas; devolve o resultado em minúsculas (o original faz text.ToLower()).
    /// </summary>
    public async Task<string> SignDataWithRetryAsync(string imei, string mascId, string clientIp, string clientReqType,
        string logId, string prodId, string keyType, string keyName, string data)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                string text = await SignDataAsync(imei, mascId, clientIp, clientReqType, logId, prodId, keyType, keyName, data);
                return text.ToLowerInvariant();
            }
            catch (Exception ex)
            {
                last = ex;
                Log?.Invoke($"DataSignODM tentativa {attempt}/3 falhou: {ex.Message}");
                if (attempt < 3) await Task.Delay(500); // original: Thread.Sleep(500)
            }
        }
        throw new OdmSigningException("DataSignODM falhou após 3 tentativas: " + last?.Message, last);
    }

    private static string Truncate(string? value, int max = 300)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value[..max] + "...";
    }
}
