using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SoftwareFix.Core;

namespace SoftwareFix.Device;

/// <summary>
/// Resultado de uma execução do mtkclient.
/// </summary>
public sealed class MtkRunResult
{
    public int ExitCode { get; set; } = -1;
    public bool TimedOut { get; set; }
    /// <summary>PreLoader travado (loop de "Handshake failed after retries") — exige desligar o aparelho.</summary>
    public bool Wedged { get; set; }
    /// <summary>O aparelho já estava conectado quando o mtkclient iniciou ("Please disconnect...").</summary>
    public bool NeedsReconnect { get; set; }
    /// <summary>Viu marcadores de sucesso no log (GPT lido / stage 2 carregado).</summary>
    public bool SawSuccessMarker { get; set; }
    public string? LogFile { get; set; }
    public int LinesSeen { get; set; }

    public bool Success => ExitCode == 0 && !TimedOut && !Wedged && !NeedsReconnect;
}

/// <summary>
/// Runner do mtkclient para o fluxo MediaTek (PreLoader + DA assinado).
///
/// Receita validada no moto g17 / XT2623-1 (MT6768 k68v1), 10/10/2026
/// (ver docs/MTK_RESCUE.md e ~/.local/share/software-fix/analysis-2026-10-10/):
///  1. Iniciar o mtkclient ANTES de conectar o aparelho (senão o DaHandler aborta com
///     "Please disconnect, start mtkclient and reconnect.");
///  2. Aparelho desligado; conectar segurando Volume ↓ → PreLoader (0e8d:2000);
///  3. Usar o DA ASSINADO do vendor como --loader (o DA do mtkclient é rejeitado pelo
///     bootloader com DAA_SIG_VERIFY_FAILED 0x7024);
///  4. ATENÇÃO (gotcha do argparse do mtkclient): opções vão DEPOIS do subcomando —
///     `mtk printgpt --loader X` funciona; `mtk --loader X printgpt` descarta o --loader.
/// </summary>
public class MtkClientRunner
{
    /// <summary>Binário mtk. Sobrescrevível via env SOFTWARE_FIX_MTK.</summary>
    public string MtkBinary { get; set; } =
        Environment.GetEnvironmentVariable("SOFTWARE_FIX_MTK") ?? FindDefaultBinary();

    /// <summary>DA assinado (--loader). Sobrescrevível via env SOFTWARE_FIX_MTK_DA.</summary>
    public string DaFile { get; set; } =
        Environment.GetEnvironmentVariable("SOFTWARE_FIX_MTK_DA") ?? Configurations.MtkDaDefaultFile;

    /// <summary>Emite cada linha de saída do mtkclient (para streaming no console).</summary>
    public event Action<string>? LogReceived;

    public bool BinaryAvailable => File.Exists(MtkBinary) || FindOnPath(MtkBinary) != null;
    public bool DaAvailable => File.Exists(DaFile);

    static string FindDefaultBinary()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string venv = Path.Combine(home, ".venvs", "lmsa-tools", "bin", "mtk");
        if (File.Exists(venv)) return venv;
        return "mtk";
    }

    static string? FindOnPath(string name)
    {
        if (name.Contains(Path.DirectorySeparatorChar)) return null;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
        {
            try
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Monta os argumentos do mtkclient. O subcomando vem PRIMEIRO e as opções DEPOIS
    /// (o argparse do mtkclient descarta opções antes do subcomando — default clobber
    /// do subparser com parents=[base]).
    /// </summary>
    public string BuildArguments(string command, string commandArgs = "", bool debugLog = true)
    {
        var sb = new StringBuilder();
        sb.Append(command);
        if (!string.IsNullOrWhiteSpace(commandArgs))
        {
            sb.Append(' ');
            sb.Append(commandArgs);
        }
        sb.Append(" --loader \"").Append(DaFile).Append('"');
        if (debugLog) sb.Append(" --loglevel debug");
        return sb.ToString();
    }

    /// <summary>Lista dispositivos MediaTek/Motorola USB no barramento (via /sys/bus/usb).</summary>
    public static List<(string Vid, string Pid, string Info)> GetMtkUsbDevices()
    {
        var result = new List<(string, string, string)>();
        const string sysUsb = "/sys/bus/usb/devices";
        if (!Directory.Exists(sysUsb)) return result;

        string[] mtkVids = { "0e8d", "22b8" };
        foreach (var dir in Directory.EnumerateDirectories(sysUsb))
        {
            try
            {
                string vidFile = Path.Combine(dir, "idVendor");
                string pidFile = Path.Combine(dir, "idProduct");
                if (!File.Exists(vidFile) || !File.Exists(pidFile)) continue;

                string vid = File.ReadAllText(vidFile).Trim().ToLowerInvariant();
                if (!mtkVids.Contains(vid)) continue;
                string pid = File.ReadAllText(pidFile).Trim().ToLowerInvariant();

                string info = "";
                foreach (var f in new[] { "manufacturer", "product" })
                {
                    string p = Path.Combine(dir, f);
                    if (File.Exists(p)) info += " " + File.ReadAllText(p).Trim();
                }
                result.Add((vid, pid, info.Trim()));
            }
            catch { }
        }
        return result;
    }

    /// <summary>True se há um PreLoader/BROM (0e8d) presente agora (o mtkclient precisa
    /// iniciar com o dispositivo AUSENTE).</summary>
    public static bool PreloaderPresentNow() =>
        GetMtkUsbDevices().Any(d => d.Vid == "0e8d");

    static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Executa o mtkclient com streaming de saída, gravação de log e detecção de
    /// estado travado. Um Poderoso por vez (Gate).
    /// </summary>
    public async Task<MtkRunResult> RunAsync(
        string command,
        string commandArgs = "",
        string? logName = null,
        TimeSpan? timeout = null,
        int wedgedThreshold = 8,
        CancellationToken ct = default)
    {
        var result = new MtkRunResult();
        string logFile = Path.Combine(
            Configurations.MtkLogDir,
            $"{logName ?? command}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        result.LogFile = logFile;

        await Gate.WaitAsync(ct);
        var logLines = new List<string>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = MtkBinary,
                Arguments = BuildArguments(command, commandArgs),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Configurations.MtkLogDir,
            };

            var proc = new Process { StartInfo = psi };
            int handshakeFails = 0;
            bool killed = false;

            void Emit(string line)
            {
                lock (logLines)
                {
                    logLines.Add(line);
                    result.LinesSeen++;
                    if (line.Contains("Handshake failed after retries")) handshakeFails++;
                    if (line.Contains("Please disconnect, start mtkclient and reconnect.")) result.NeedsReconnect = true;
                    if (Regex.IsMatch(line, @"GPT Table:|Successfully uploaded stage 2|Successfully received DA sync"))
                        result.SawSuccessMarker = true;
                }
                LogReceived?.Invoke(line);
            }

            proc.OutputDataReceived += (_, e) => { if (e.Data != null) Emit(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Emit(e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            var exitTask = proc.WaitForExitAsync(ct);

            while (!exitTask.IsCompleted)
            {
                var completed = await Task.WhenAny(exitTask, Task.Delay(1000, ct));
                if (completed == exitTask) break;

                if (handshakeFails >= wedgedThreshold && !killed)
                {
                    result.Wedged = true;
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    killed = true;
                    break;
                }
                if (timeout.HasValue && proc.StartTime.Add(timeout.Value) < DateTime.Now && !killed)
                {
                    result.TimedOut = true;
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    killed = true;
                    break;
                }
                ct.ThrowIfCancellationRequested();
            }

            try { await exitTask; } catch { }
            if (!killed)
            {
                // aguarda drenar o restante da saída
                proc.WaitForExit(3000);
                result.ExitCode = proc.ExitCode;
            }
        }
        finally
        {
            Gate.Release();
        }

        try { File.WriteAllLines(logFile, logLines); } catch { }
        return result;
    }

    /// <summary>
    /// Pré-checagem + ritual: garante que não há dispositivo conectado (o mtkclient
    /// precisa iniciar com o barramento "limpo" para engatar o aparelho depois).
    /// Retorna false se o usuário deve desconectar/desligar primeiro.
    /// </summary>
    public static async Task<bool> EnsureDeviceAbsentAsync(TimeSpan wait, bool interactive)
    {
        if (!PreloaderPresentNow()) return true;

        Console.WriteLine("[*] Detectei um aparelho MediaTek conectado agora (PreLoader/outro).");
        Console.WriteLine("    O mtkclient PRECISA iniciar ANTES de o aparelho aparecer - então:");
        Console.WriteLine("     1) DESCONECTE o cabo USB;");
        Console.WriteLine("     2) Se as tentativas anteriores travaram o PreLoader, segure o Power por 20-30s para desligar;");
        Console.WriteLine("     3) Volte a rodar este comando e conecte o aparelho só quando ele pedir.");
        Console.WriteLine();
        if (!interactive) return false;

        Console.Write("[*] Aguardando a desconexão");
        var start = DateTime.Now;
        while (PreloaderPresentNow())
        {
            if (DateTime.Now - start > wait)
            {
                Console.WriteLine("\n[-] Ainda conectado. Desconecte e rode o comando novamente.");
                return false;
            }
            await Task.Delay(500);
            Console.Write('.');
        }
        Console.WriteLine("\n[+] Desconectado. Prosseguindo...");
        return true;
    }
}
