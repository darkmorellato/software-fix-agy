using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SoftwareFix.Device;

namespace SoftwareFix.Flash;

public class FlashProgressEventArgs : EventArgs
{
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public double ProgressPercentage { get; set; }
    public string CurrentOperation { get; set; } = string.Empty;
    public string CurrentPartition { get; set; } = string.Empty;
    public string OutputLog { get; set; } = string.Empty;
}

public class FlashEngine
{
    private readonly FastbootRunner _fastboot;

    public event EventHandler<FlashProgressEventArgs>? ProgressChanged;
    public event Action<string>? LogReceived;

    public FlashEngine(FastbootRunner? fastboot = null)
    {
        _fastboot = fastboot ?? new FastbootRunner();
    }

    public async Task<bool> FlashDeviceAsync(string? serial, List<FlashCommand> commands, bool eraseUserData = true, string? romDir = null, CancellationToken cancellationToken = default)
    {
        if (commands.Count == 0) return false;

        // 1. Check current slot on device (matches FastbootModifyFlashFile.cs)
        string currentSlot = await _fastboot.GetCurrentSlotAsync(serial);
        LogReceived?.Invoke($"[Fastboot] Slot atual do aparelho: '{currentSlot}'");

        var adjustedCommands = new List<FlashCommand>(commands);

        // If current slot is 'b', swap _a partitions to _b (matching lines 60-82 of FastbootModifyFlashFile.cs)
        if (currentSlot.Equals("b", StringComparison.OrdinalIgnoreCase))
        {
            LogReceived?.Invoke("[Fastboot] Aparelho no slot B detectado. Adaptando partições dinâmicas para _b...");
            foreach (var cmd in adjustedCommands)
            {
                if (cmd.Partition.EndsWith("_a", StringComparison.OrdinalIgnoreCase))
                {
                    cmd.Partition = cmd.Partition[..^2] + "_b";
                }
            }
        }

        // 2. Rollback Protection check (matches FastbootDeviceMatchCheck.cs)
        if (!string.IsNullOrEmpty(romDir))
        {
            int? romSecVer = GetRomSecurityVersion(romDir);
            if (romSecVer.HasValue)
            {
                int? devSecVer = await _fastboot.GetDeviceSecurityVersionAsync(serial);
                if (devSecVer.HasValue)
                {
                    LogReceived?.Invoke($"[Segurança] Versão de segurança da ROM: {romSecVer.Value}, Dispositivo: {devSecVer.Value}");
                    if (romSecVer.Value < devSecVer.Value)
                    {
                        LogReceived?.Invoke($"[ALERTA DE DOWNGRADE] A ROM selecionada ({romSecVer.Value}) é anterior à versão do aparelho ({devSecVer.Value}). Downgrade bloqueado pelo bootloader!");
                        return false;
                    }
                }
            }
        }

        // 3. Calculate total bytes for progress
        long totalBytes = 0;
        foreach (var cmd in adjustedCommands)
        {
            totalBytes += cmd.FileSize;
        }

        long flashedBytes = 0;
        string serialPrefix = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";

        for (int i = 0; i < adjustedCommands.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) return false;

            var cmd = adjustedCommands[i];

            // Ignore skip commands as in FastbootFlash.cs
            if (cmd.Operation.Contains("fb_mode_set") || cmd.Operation.Contains("fb_mode_clear"))
            {
                continue;
            }

            // If user opted to keep data, skip erasing userdata/metadata (matches EraseUserData.cs)
            if (!eraseUserData && (cmd.Partition.Equals("userdata", StringComparison.OrdinalIgnoreCase) || 
                                  cmd.Partition.Equals("metadata", StringComparison.OrdinalIgnoreCase) ||
                                  cmd.Operation.Equals("format", StringComparison.OrdinalIgnoreCase) && cmd.Partition.Contains("data", StringComparison.OrdinalIgnoreCase)))
            {
                LogReceived?.Invoke($"[{i + 1}/{adjustedCommands.Count}] [SKIP] Preservando partição {cmd.Partition} (Opção 'Manter Dados' ativada)");
                continue;
            }

            string fastbootArg = serialPrefix + cmd.ToFastbootArgument();

            LogReceived?.Invoke($"[{i + 1}/{adjustedCommands.Count}] Executando: fastboot {fastbootArg}");

            var output = await _fastboot.RunCommandAsync(fastbootArg, cmd.TimeoutMs);
            LogReceived?.Invoke(output.Trim());

            if (output.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                // In official app, failing to erase userdata doesn't fail entire flash if non-critical
                if (!eraseUserData && cmd.Partition.Contains("userdata", StringComparison.OrdinalIgnoreCase))
                {
                    LogReceived?.Invoke($"[AVISO] Partição {cmd.Partition} ignorada.");
                }
                else
                {
                    LogReceived?.Invoke($"[ERRO] Falha crítica no comando {i + 1}: {fastbootArg}");
                    return false;
                }
            }

            flashedBytes += cmd.FileSize;
            double progress = totalBytes > 0
                ? (double)flashedBytes / totalBytes * 100.0
                : (double)(i + 1) / adjustedCommands.Count * 100.0;

            ProgressChanged?.Invoke(this, new FlashProgressEventArgs
            {
                CurrentStep = i + 1,
                TotalSteps = adjustedCommands.Count,
                ProgressPercentage = Math.Min(100.0, progress),
                CurrentOperation = cmd.Operation,
                CurrentPartition = cmd.Partition,
                OutputLog = output
            });
        }

        LogReceived?.Invoke("[SUCESSO] Gravação de firmware concluída com 100% de integridade!");
        return true;
    }

    private int? GetRomSecurityVersion(string romDir)
    {
        string[] candidates = { "signing-info.txt", "sign_info.txt" };
        foreach (var c in candidates)
        {
            string p = Path.Combine(romDir, c);
            if (File.Exists(p))
            {
                foreach (var line in File.ReadAllLines(p))
                {
                    if (line.Contains("HAB_SECURITY_VERSION") || line.Contains("signinfo id sum"))
                    {
                        var parts = line.Trim().Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && int.TryParse(parts[^1], out int v))
                        {
                            return v;
                        }
                    }
                }
            }
        }
        return null;
    }
}
