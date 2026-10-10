using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    public async Task<bool> FlashDeviceAsync(string? serial, List<FlashCommand> commands, bool eraseUserData = true, CancellationToken cancellationToken = default)
    {
        if (commands.Count == 0) return false;

        long totalBytes = 0;
        foreach (var cmd in commands)
        {
            totalBytes += cmd.FileSize;
        }

        long flashedBytes = 0;
        string serialPrefix = string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ";

        for (int i = 0; i < commands.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) return false;

            var cmd = commands[i];

            // If user opted to keep data, skip erasing userdata/metadata
            if (!eraseUserData && (cmd.Partition.Equals("userdata", StringComparison.OrdinalIgnoreCase) || 
                                  cmd.Partition.Equals("metadata", StringComparison.OrdinalIgnoreCase) ||
                                  cmd.Operation.Equals("format", StringComparison.OrdinalIgnoreCase) && cmd.Partition.Contains("data", StringComparison.OrdinalIgnoreCase)))
            {
                LogReceived?.Invoke($"[{i + 1}/{commands.Count}] [SKIP] Preservando partição {cmd.Partition} (Opção 'Manter Dados' ativada)");
                continue;
            }

            string fastbootArg = serialPrefix + cmd.ToFastbootArgument();

            LogReceived?.Invoke($"[{i + 1}/{commands.Count}] Running: fastboot {fastbootArg}");

            var output = await _fastboot.RunCommandAsync(fastbootArg, cmd.TimeoutMs);
            LogReceived?.Invoke(output.Trim());

            if (output.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                LogReceived?.Invoke($"[ERROR] Command failed at step {i + 1}: {fastbootArg}");
                return false;
            }

            flashedBytes += cmd.FileSize;
            double progress = totalBytes > 0
                ? (double)flashedBytes / totalBytes * 100.0
                : (double)(i + 1) / commands.Count * 100.0;

            ProgressChanged?.Invoke(this, new FlashProgressEventArgs
            {
                CurrentStep = i + 1,
                TotalSteps = commands.Count,
                ProgressPercentage = Math.Min(100.0, progress),
                CurrentOperation = cmd.Operation,
                CurrentPartition = cmd.Partition,
                OutputLog = output
            });
        }

        LogReceived?.Invoke("[SUCCESS] Flashing completed successfully!");
        return true;
    }
}
