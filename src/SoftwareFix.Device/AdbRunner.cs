using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SoftwareFix.Device;

public class AdbRunner
{
    public string AdbBinary { get; set; } = "adb";

    public async Task<List<string>> GetConnectedDevicesAsync()
    {
        var output = await RunCommandAsync("devices");
        var result = new List<string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[1].Trim() == "device")
            {
                result.Add(parts[0].Trim());
            }
        }
        return result;
    }

    public async Task<string> GetPropertyAsync(string serial, string propName)
    {
        string cmd = $"-s {serial} shell getprop {propName}";
        var res = await RunCommandAsync(cmd, 10000);
        return res.Trim();
    }

    public async Task<bool> RebootToBootloaderAsync(string serial)
    {
        var res = await RunCommandAsync($"-s {serial} reboot bootloader", 20000);
        return !res.Contains("error", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> RunCommandAsync(string arguments, int timeoutMs = 30000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = AdbBinary,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (await Task.WhenAny(Task.Run(() => process.WaitForExit(timeoutMs))) == Task.CompletedTask)
        {
            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            return string.IsNullOrEmpty(stdout) ? stderr : stdout + "\n" + stderr;
        }

        try { process.Kill(); } catch { }
        return "ERROR: timeout";
    }
}
