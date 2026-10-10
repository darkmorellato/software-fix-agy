using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SoftwareFix.Core.Models;

namespace SoftwareFix.Device;

public class FastbootRunner
{
    /// <summary>Binário fastboot. Sobrescrevível via env SOFTWARE_FIX_FASTBOOT (também usado nos testes de paridade).</summary>
    public string FastbootBinary { get; set; } =
        Environment.GetEnvironmentVariable("SOFTWARE_FIX_FASTBOOT") ?? "fastboot";

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

    public async Task<string> GetCurrentSlotAsync(string? serial = null)
    {
        string cmd = string.IsNullOrEmpty(serial) ? "getvar current-slot" : $"-s {serial} getvar current-slot";
        var output = await RunCommandAsync(cmd, 5000);
        var match = Regex.Match(output, @"current-slot:\s*(?<slot>[a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups["slot"].Value.Trim();
        }
        return "a";
    }

    public async Task<int?> GetDeviceSecurityVersionAsync(string? serial = null)
    {
        string cmd = string.IsNullOrEmpty(serial) ? "oem read_sv" : $"-s {serial} oem read_sv";
        var output = await RunCommandAsync(cmd, 6000);
        if (string.IsNullOrEmpty(output)) return null;

        // Matches outputs from FastbootDeviceMatchCheck.cs: "signinfo id sum:" or "device rollback id sum:"
        var match = Regex.Match(output, @"(?:rollback|security|sv)[\s\w]*:\s*(?<ver>\d+)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups["ver"].Value, out int ver))
        {
            return ver;
        }

        return null;
    }

    public async Task<DeviceRescueInfo?> InspectDeviceAsync(string? serial = null)
    {
        var vars = await GetAllVariablesAsync(serial);
        if (vars.Count == 0) return null;

        var info = new DeviceRescueInfo();
        info.SerialNumber = serial ?? vars.GetValueOrDefault("serialno", "");
        
        // Primary sku lookup with fallback to ro.boot.hardware.sku
        string sku = vars.GetValueOrDefault("sku", "");
        if (string.IsNullOrEmpty(sku))
        {
            sku = vars.GetValueOrDefault("ro.boot.hardware.sku", "");
        }

        // Secondary retry if still missing (matching FastbootDeviceMatchCheck.cs lines 653-696)
        if (string.IsNullOrEmpty(sku))
        {
            string cmdSku = string.IsNullOrEmpty(serial) ? "getvar sku" : $"-s {serial} getvar sku";
            var outSku = await RunCommandAsync(cmdSku, 3000);
            var m = Regex.Match(outSku, @"sku:\s*(?<val>[^\r\n]*)");
            if (m.Success && !m.Groups["val"].Value.Contains("not found"))
            {
                sku = m.Groups["val"].Value.Trim();
            }
        }

        info.Sku = sku;
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

        // Paridade ReadPropertiesInFastboot.cs (lista completa de elementos)
        info.QcomVersion = vars.GetValueOrDefault("ro.build.version.qcom", "");
        info.Emmc = vars.GetValueOrDefault("emmc", "");
        info.AndroidVer = vars.GetValueOrDefault("androidVer", "");
        info.IsWarrantyVoid = vars.GetValueOrDefault("iswarrantyvoid", "");

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

        // Corrida: término normal vs. timeout.
        // (O padrão anterior — `await Task.WhenAny(...) == Task.CompletedTask` — era
        // sempre falso e fazia TODA chamada retornar "ERROR: timeout".)
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
