using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SoftwareFix.Core.Services;

/// <summary>
/// Servidor local de assinatura ODM ("GPS auth") — TCP em 127.0.0.1:5000.
///
/// PARIDADE com o descompilado (Software Fix 7.6.2.10, smartdevice):
///   - Steps/RunODMSocketServer.cs  -> o passo da receita que inicia este servidor
///     (imei do aparelho + usuário/senha de serviço vindos dos PARAMS da receita);
///   - ODMSocketServer/ODMServerMain.cs -> bind 127.0.0.1:5000, protocolo e logs.
///
/// Protocolo (ASCII sobre TCP, como no original):
///   1. Sonda: mensagem "IsRunning" -> resposta "0".
///      (No original a comparação é feita contra a representação HEX da mensagem e a
///      resposta "0" seria parseada como hex — dois defeitos latentes que tornam esse
///      caminho inalcançável/crash. Aqui implementamos o COMPORTAMENTO PRETENDIDO:
///      "IsRunning" (texto) -> "0". Documentado em docs/ODM_SLA_AUTH.md.)
///   2. Assinatura: "<tipo 2 chars><hash hex do aparelho>" -> o hash é assinado na
///      nuvem (OdmSigningClient) e a assinatura (hex -> bytes, buffer de 512,
///      zeros à direita) é devolvida no socket. O lado fastboot instala os 256 bytes
///      resultantes no bootloader via `Download("signature")` +
///      `RawCommand("signature", "installing signature")` (mecanismo padrão do
///      fastboot AOSP, presente também no fastboot do Linux).
///
/// Uma mensagem por conexão (como no original: o ReceiveCallback não re-arma a leitura);
/// o cliente reconecta para a próxima mensagem.
/// </summary>
public class OdmSocketServer : IAsyncDisposable
{
    // ---- Constantes do protocolo (ODMServerMain.InitParams + Web.DataSignODM) ----
    public const int DefaultPort = 5000;
    public const string DefaultLogId = "91C44564934F4E96A1533641D2694B38";
    public const string DefaultProdId = "M6102";
    public const string DefaultKeyType = "23";
    public const string DefaultKeyName = "1";
    public const string DefaultClientReqType = "0x00";
    public const string DefaultMascId = "GC-7777777-TEST-190";
    /// <summary>IP fixo enviado ao serviço ODM (original: clientIp = "99.99.99.99" em Web.DataSignODM).</summary>
    public const string ClientIp = "99.99.99.99";
    /// <summary>Tamanho do buffer da assinatura (original: Array.Resize(ref array3, 512)).</summary>
    public const int SignatureBufferSize = 512;
    private const int BufferSize = 2048; // original: BUFFER_SIZE / array de 2048

    private readonly OdmSigningClient _client;
    private readonly string _imei;
    private readonly int _port;
    private readonly Action<string> _log;

    private TcpListener? _listener;

    /// <summary>Porta efetiva do servidor.</summary>
    public int Port => _port;

    public OdmSocketServer(string imei, string user, string password,
        int port = DefaultPort, string? odmUrl = null, Action<string>? log = null)
    {
        _imei = imei;
        _port = port;
        _log = log ?? Console.WriteLine;
        _client = new OdmSigningClient(user, password, odmUrl) { Log = _log };
    }

    /// <summary>Inicia o servidor e atende até o cancelamento (Ctrl+C no CLI).</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _log("Start ODMSocketServer");
        _listener = new TcpListener(IPAddress.Loopback, _port);
        try
        {
            _listener.Start(100); // original: serverSocket.Listen(100)
            _log($"Setting up server...\nServer started..... (127.0.0.1:{_port})");

            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient connection;
                try
                {
                    connection = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break; // listener fechado no Dispose
                }

                _log("Client connected, waiting for request...");
                _ = HandleConnectionAsync(connection);
            }
        }
        finally
        {
            await DisposeAsync();
        }
    }

    private async Task HandleConnectionAsync(TcpClient connection)
    {
        try
        {
            using (connection)
            {
                connection.NoDelay = true;
                var stream = connection.GetStream();
                var buffer = new byte[BufferSize];
                int received = await stream.ReadAsync(buffer);

                if (received <= 0)
                {
                    _log("Client forcefully disconnected");
                    return;
                }

                // Remove CR/LF/NUL à direita (robustez; o hash chega em texto/hex puro)
                int len = received;
                while (len > 0 && (buffer[len - 1] == 0 || buffer[len - 1] == (byte)'\r' || buffer[len - 1] == (byte)'\n'))
                    len--;

                byte[] data = new byte[len];
                Array.Copy(buffer, data, len);
                string text = Encoding.ASCII.GetString(data);
                string hex = ToHex(data); // paridade: CustomConvert.ByteArrayToString (hex minúsculo)

                _log("Received: " + text);

                // ---- Sonda de disponibilidade: "IsRunning" -> "0" ----
                if (text == "IsRunning" || hex == "IsRunning")
                {
                    // Resposta pretendida pelo original: byte ASCII '0'
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("0"));
                    _log("IsRunning -> 0");
                    return;
                }

                // ---- Assinatura: <tipo 2 chars><hash hex> ----
                string type = text.Length >= 2 ? text[..2] : string.Empty;       // original: text.Substring(0, 2)
                string phoneHash = hex.Length >= 4 ? hex[4..] : string.Empty;    // original: text3.Substring(4)
                _log("Get type: " + type);
                _log("Received Phone Hash: " + phoneHash);
                _log("Start to do DataSignODM:" + phoneHash);

                string signed;
                try
                {
                    signed = await _client.SignDataWithRetryAsync(
                        _imei, DefaultMascId, ClientIp, DefaultClientReqType,
                        DefaultLogId, DefaultProdId, DefaultKeyType, DefaultKeyName, phoneHash);
                    _log("GPS Signed Hash: " + signed);
                }
                catch (Exception ex)
                {
                    // Original: no caminho de falha a assinatura vira vazia e o buffer de
                    // 512 bytes é enviado zerado — o bootloader rejeita, como no app oficial.
                    _log("GPS Sign FAILED " + ex.Message);
                    signed = string.Empty;
                }

                byte[] signature = HexToBytes(signed); // paridade: CustomConvert.StringToByteArray (hex -> bytes)
                Array.Resize(ref signature, SignatureBufferSize); // original: Array.Resize(ref array3, 512)
                await stream.WriteAsync(signature);
                _log("Signed HASH sent to SLA_Challenge");
            }
        }
        catch (Exception ex)
        {
            _log("Erro ao atender cliente ODM: " + ex.Message);
        }
    }

    /// <summary>Paridade: CustomConvert.ByteArrayToString — hex minúsculo, sem separadores.</summary>
    internal static string ToHex(byte[] data) => Convert.ToHexString(data).ToLowerInvariant();

    /// <summary>
    /// Paridade: CustomConvert.StringToByteArray — interpreta a string como hex, pares de
    /// caracteres por byte. String vazia -> array vazio; comprimento ímpar lança (como o
    /// original, cujo Substring(x, 2) estoura no último caractere).
    /// </summary>
    internal static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
        var result = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
        {
            result[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
        return result;
    }

    public ValueTask DisposeAsync()
    {
        if (_listener != null)
        {
            try
            {
                _listener.Stop();
                _log("Close ODMSocketServer");
            }
            catch { }
            _listener = null;
        }
        return ValueTask.CompletedTask;
    }
}
