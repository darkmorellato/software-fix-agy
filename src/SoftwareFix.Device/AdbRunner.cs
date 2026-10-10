using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SoftwareFix.Core.Models;

namespace SoftwareFix.Device;

public class AdbDeviceInfo
{
    public string SerialNumber { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string Sku { get; set; } = "";
    public string AndroidVersion { get; set; } = "";
    public string DisplayBuildId { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Baseband { get; set; } = "";
    public string Carrier { get; set; } = "";
    public string Imei { get; set; } = "";
    public int BatteryPercentage { get; set; } = 85;
    public string StorageInfo { get; set; } = "";
    public string Processor { get; set; } = "";
    public string SimConfig { get; set; } = "Dual";
}

public class AdbRunner
{
    /// <summary>Binário adb. Sobrescrevível via env SOFTWARE_FIX_ADB (também usado nos testes de paridade).</summary>
    public string AdbBinary { get; set; } =
        Environment.GetEnvironmentVariable("SOFTWARE_FIX_ADB") ?? "adb";

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

    public async Task<AdbDeviceInfo?> InspectAdbDeviceAsync(string serial)
    {
        try
        {
            var info = new AdbDeviceInfo { SerialNumber = serial };

            info.Sku = await GetPropertyAsync(serial, "ro.boot.hardware.sku");
            info.ModelName = await GetPropertyAsync(serial, "ro.product.model");
            if (string.IsNullOrEmpty(info.ModelName)) info.ModelName = info.Sku;

            info.AndroidVersion = await GetPropertyAsync(serial, "ro.build.version.release");
            info.DisplayBuildId = await GetPropertyAsync(serial, "ro.build.display.id");
            info.Fingerprint = await GetPropertyAsync(serial, "ro.build.fingerprint");
            info.Carrier = await GetPropertyAsync(serial, "ro.carrier");
            info.Baseband = await GetPropertyAsync(serial, "gsm.version.baseband");
            if (string.IsNullOrEmpty(info.Baseband))
            {
                info.Baseband = await GetPropertyAsync(serial, "ril.baseband.config.version");
            }

            info.Processor = await GetPropertyAsync(serial, "ro.soc.model");
            if (string.IsNullOrEmpty(info.Processor))
            {
                info.Processor = await GetPropertyAsync(serial, "ro.board.platform");
            }

            var sim = await GetPropertyAsync(serial, "persist.radio.multisim.config");
            info.SimConfig = (sim.Equals("dsds", StringComparison.OrdinalIgnoreCase) || sim.Equals("dsda", StringComparison.OrdinalIgnoreCase)) ? "Dual" : "Single";

            // Battery level from dumpsys battery
            var batteryOut = await RunCommandAsync($"-s {serial} shell dumpsys battery", 5000);
            var batMatch = Regex.Match(batteryOut, @"level:\s*(\d+)");
            if (batMatch.Success && int.TryParse(batMatch.Groups[1].Value, out int bLevel))
            {
                info.BatteryPercentage = bLevel;
            }

            // Storage info from df
            var dfOut = await RunCommandAsync($"-s {serial} shell df -h /data", 5000);
            var dfLines = dfOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (dfLines.Length >= 2)
            {
                info.StorageInfo = dfLines[1].Trim();
            }

            return info;
        }
        catch
        {
            return null;
        }
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

        // Corrida: término normal vs. timeout (mesmo fix do FastbootRunner).
        var waitTask = process.WaitForExitAsync();
        var completed = await Task.WhenAny(waitTask, Task.Delay(timeoutMs));
        if (completed == waitTask)
        {
            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            return string.IsNullOrEmpty(stdout) ? stderr : stdout + "\n" + stderr;
        }

        try { process.Kill(entireProcessTree: true); } catch { }
        return "ERROR: timeout";
    }
}
