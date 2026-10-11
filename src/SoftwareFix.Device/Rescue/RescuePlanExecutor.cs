using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SoftwareFix.Device.Rescue;

/// <summary>
/// Executa um RescuePlan: valida o aparelho (getvar product), roda os comandos
/// fastboot um a um com log ao vivo. Suporta dry-run (só mostra) — mesma filosofia
/// do "Rescue Lite (Dry run)" do LenovoMotoFirmwareDownloader.
/// </summary>
public static class RescuePlanExecutor
{
    public static string ResolveFastboot()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Path.Combine(home, ".local", "share", "software-fix", "tools", "platform-tools", "fastboot");
        return File.Exists(local) ? local : "fastboot";
    }

    /// <summary>Lista aparelhos em modo fastboot (fastboot devices).</summary>
    public static async Task<List<string>> ListFastbootDevicesAsync(CancellationToken ct = default)
    {
        var (ok, output) = await RunProcessAsync(ResolveFastboot(), new[] { "devices" }, Environment.CurrentDirectory, 30, ct);
        if (!ok) return new List<string>();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.Contains("no permissions", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Split('\t', ' ')[0])
            .Where(s => s.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Executa o plano. dryRun=true apenas imprime os comandos.
    /// Serial nulo = primeiro device detectado. Retorna true se todos os passos ok.
    /// </summary>
    public static async Task<bool> ExecuteAsync(
        RescuePlan plan, string romDir, string? serial, bool dryRun,
        Action<string> log, CancellationToken ct = default)
    {
        if (plan.Commands.Count == 0) { log("[!] Plano vazio."); return false; }

        if (plan.Method is RescueMethod.EdlFirehose or RescueMethod.UnisocPac)
        {
            log($"[i] Este pacote usa '{plan.Commands[0].Tool}' ({plan.MethodDisplay}).");
            log("    Comando preparado (execute quando o aparelho estiver no modo indicado):");
            foreach (var c in plan.Commands) log("    " + c.Render(serial));
            foreach (var w in plan.Warnings) log("  ⚠ " + w);
            return false;
        }

        var fastboot = ResolveFastboot();
        log($"[i] fastboot: {fastboot}");

        if (!dryRun)
        {
            var devices = await ListFastbootDevicesAsync(ct);
            if (devices.Count == 0)
            {
                log("[!] Nenhum aparelho em modo FASTBOOT detectado.");
                log("    Ligue o aparelho no modo fastboot (Volume ↓ + Power) e conecte o cabo USB.");
                return false;
            }
            serial ??= devices[0];
            log($"[✓] Aparelho: {serial}");

            // validação do codename (fiel ao flashall.sh: getvar product + match)
            if (!string.IsNullOrEmpty(plan.Codename))
            {
                var (_, outText) = await RunProcessAsync(fastboot, new[] { "-s", serial, "getvar", "product" }, romDir, 30, ct);
                var m = System.Text.RegularExpressions.Regex.Match(outText, @"product:\s*(\S+)");
                var product = m.Success ? m.Groups[1].Value : "";
                var expected = plan.Codename.Split('_')[0]; // fogos_g → fogos ; lamuC → lamuC
                if (product.Length > 0 && !product.StartsWith(expected, StringComparison.OrdinalIgnoreCase) &&
                    !plan.Codename.StartsWith(product, StringComparison.OrdinalIgnoreCase))
                {
                    log($"[✗] PRODUTO NÃO CONFERE: aparelho diz '{product}', pacote espera '{plan.Codename}'.");
                    log("    Abortando por segurança (o script oficial também para aqui).");
                    return false;
                }
                if (product.Length > 0) log($"[✓] Codename confere: {product}");
            }
        }
        else
        {
            log("[i] DRY-RUN — nenhum comando será executado.");
        }

        int index = 0;
        int failures = 0;
        foreach (var cmd in plan.Commands)
        {
            index++;
            log($"[{index}/{plan.Commands.Count}] {cmd.Render(serial)}");
            if (dryRun) continue;

            var timeout = cmd.Kind == "flash" ? 900 : 90;
            var (ok, output) = await RunProcessAsync(fastboot, BuildArgs(cmd, serial), romDir, timeout, ct);
            var trimmed = output.Trim();
            if (trimmed.Length > 0 && (trimmed.Contains("FAILED", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("error", StringComparison.OrdinalIgnoreCase) && cmd.Kind == "flash"))
                log("    " + trimmed.Split('\n').Last().Trim());

            if (!ok)
            {
                if (cmd.SoftFail) { log($"    ~ aviso tolerado ({cmd.Kind})"); continue; }
                failures++;
                log($"[✗] FALHOU: {cmd.Label}");
                if (trimmed.Length > 0) log("    " + trimmed.Split('\n').Last().Trim());
                log("[!] Abortando o restante do plano.");
                return false;
            }
        }

        log(failures == 0
            ? $"[✓] Plano concluído: {plan.FlashCount} flashes, {plan.EraseCount} apagamentos."
            : $"[!] Concluído com {failures} falha(s).");
        return failures == 0;
    }

    private static List<string> BuildArgs(RescueCommand cmd, string? serial)
    {
        var args = new List<string>();
        if (!string.IsNullOrEmpty(serial)) { args.Add("-s"); args.Add(serial); }
        args.AddRange(cmd.Args);
        return args;
    }

    private static async Task<(bool ok, string output)> RunProcessAsync(
        string exe, IEnumerable<string> args, string cwd, int timeoutSeconds, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = new Process { StartInfo = psi };
            proc.Start();
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return (false, "timeout");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return (proc.ExitCode == 0, stdout + "\n" + stderr);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
