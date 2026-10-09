using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SoftwareFix.Core.Models;

namespace SoftwareFix.Device;

public class FastbootRunner
{
    public string FastbootBinary { get; set; } = "fastboot";

    public async Task<List<string>> GetConnectedDevicesAsync()
    {
        var output = await RunCommandAsync("devices");
        var result = new List<string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[1].Trim() == "fastboot")
            {
                result.Add(parts[0].Trim());
            }
        }
        return result;
    }

    public async Task<Dictionary<string, string>> GetAllVariablesAsync(string? serial = null)
    {
        string cmd = string.IsNullOrEmpty(serial) ? "getvar all" : $"-s {serial} getvar all";
        var output = await RunCommandAsync(cmd, 15000);
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Typical fastboot getvar output:
        // (bootloader) sku: XT2153-1
        // (bootloader) ro.carrier: retbr
        var regex = new Regex(@"\(bootloader\)\s+(?<key>[^:]+):\s*(?<val>.*)", RegexOptions.Compiled);
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = regex.Match(line);
            if (match.Success)
            {
                string key = match.Groups["key"].Value.Trim();
                string val = match.Groups["val"].Value.Trim();
                vars[key] = val;
            }
            else if (line.Contains(':'))
            {
                var idx = line.IndexOf(':');
                string key = line[..idx].Trim();
                string val = line[(idx + 1)..].Trim();
                vars[key] = val;
            }
        }

        return vars;
    }

    public async Task<DeviceRescueInfo?> InspectDeviceAsync(string? serial = null)
    {
        var vars = await GetAllVariablesAsync(serial);
        if (vars.Count == 0) return null;

        var info = new DeviceRescueInfo();
        info.SerialNumber = serial ?? vars.GetValueOrDefault("serialno", "");
        info.Sku = vars.GetValueOrDefault("sku", vars.GetValueOrDefault("ro.boot.hardware.sku", ""));
        info.ModelName = info.Sku;
        info.Imei = vars.GetValueOrDefault("imei", "");
        info.RoCarrier = vars.GetValueOrDefault("ro.carrier", "");
        info.Fingerprint = vars.GetValueOrDefault("ro.build.fingerprint", "");
        info.BlurVersion = vars.GetValueOrDefault("ro.build.version.full", "");
        info.SoftwareVersion = vars.GetValueOrDefault("softwareVersion", "");
        info.BasebandVersion = vars.GetValueOrDefault("version-baseband", "");
        info.Ram = vars.GetValueOrDefault("ram", "");
        info.DualSim = vars.GetValueOrDefault("oem hw dualsim", "");
        info.ChannelId = vars.GetValueOrDefault("channelid", "");
        info.Cid = vars.GetValueOrDefault("cid", "");
        info.SecureState = vars.GetValueOrDefault("securestate", "");
        info.ErasePersonalData = vars.GetValueOrDefault("erase_personal_data", "");
        info.FdrAllowed = vars.GetValueOrDefault("fdr-allowed", "");

        if (int.TryParse(vars.GetValueOrDefault("rescuemark", "0"), out int mark))
        {
            info.RescueMark = mark;
        }

        return info;
    }

    public async Task<string> RunCommandAsync(string arguments, int timeoutMs = 60000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FastbootBinary,
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
