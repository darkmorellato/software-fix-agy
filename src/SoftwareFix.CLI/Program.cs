using System;
using System.IO;
using System.Threading.Tasks;
using SoftwareFix.Core;
using SoftwareFix.Core.Models;
using SoftwareFix.Core.Services;
using SoftwareFix.Device;
using SoftwareFix.Download;
using SoftwareFix.Flash;

namespace SoftwareFix.CLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine($" Lenovo / Motorola Software Fix - Linux Edition ({Configurations.Version})");
        Console.WriteLine("=================================================");

        if (args.Length == 0 || args[0] == "help" || args[0] == "--help" || args[0] == "-h")
        {
            PrintUsage();
            return 0;
        }

        string cmd = args[0].ToLowerInvariant();
        var fb = new FastbootRunner();
        var adb = new AdbRunner();

        switch (cmd)
        {
            case "scan":
            {
                Console.WriteLine("[*] Scanning for connected devices...");
                var fbDevices = await fb.GetConnectedDevicesAsync();
                if (fbDevices.Count > 0)
                {
                    Console.WriteLine($"[+] Found {fbDevices.Count} device(s) in Fastboot mode:");
                    foreach (var s in fbDevices)
                    {
                        var info = await fb.InspectDeviceAsync(s);
                        if (info != null)
                        {
                            Console.WriteLine($"    - Serial:       {info.SerialNumber}");
                            Console.WriteLine($"      Model/SKU:    {info.Sku}");
                            Console.WriteLine($"      Carrier:      {info.RoCarrier}");
                            Console.WriteLine($"      Baseband:     {info.BasebandVersion}");
                            Console.WriteLine($"      Fingerprint:  {info.Fingerprint}");
                            Console.WriteLine($"      RAM:          {info.Ram}");
                            Console.WriteLine($"      Dual SIM:     {info.DualSim}");
                        }
                    }
                }

                var adbDevices = await adb.GetConnectedDevicesAsync();
                if (adbDevices.Count > 0)
                {
                    Console.WriteLine($"[+] Found {adbDevices.Count} device(s) in ADB mode:");
                    foreach (var s in adbDevices)
                    {
                        string sku = await adb.GetPropertyAsync(s, "ro.boot.hardware.sku");
                        string model = await adb.GetPropertyAsync(s, "ro.product.model");
                        Console.WriteLine($"    - Serial:  {s}");
                        Console.WriteLine($"      SKU:     {sku}");
                        Console.WriteLine($"      Model:   {model}");
                    }
                }

                if (fbDevices.Count == 0 && adbDevices.Count == 0)
                {
                    Console.WriteLine("[-] No devices found. Check USB connection and udev rules.");
                }
                return 0;
            }

            case "match":
            {
                Console.WriteLine("[*] Detecting device in Fastboot mode...");
                var fbDevices = await fb.GetConnectedDevicesAsync();
                if (fbDevices.Count == 0)
                {
                    Console.WriteLine("[-] No device found in Fastboot mode. Put device in fastboot and retry.");
                    return 1;
                }

                var info = await fb.InspectDeviceAsync(fbDevices[0]);
                if (info == null)
                {
                    Console.WriteLine("[-] Failed to read device fastboot variables.");
                    return 1;
                }

                Console.WriteLine($"[+] Device identified: {info.Sku} (Carrier: {info.RoCarrier}, Serial: {info.SerialNumber})");
                Console.WriteLine("[*] Connecting to Lenovo Cloud API...");

                var client = new LenovoApiClient();
                bool ok = await client.InitializeAsync();
                if (!ok)
                {
                    Console.WriteLine("[-] Warning: Failed to obtain authenticated token. Proceeding anonymously...");
                }

                Console.WriteLine("[*] Querying official ROM firmware...");
                var rom = await client.FindRomByDeviceAsync(info);
                if (rom == null && !string.IsNullOrEmpty(info.Imei))
                {
                    rom = await client.FindRomByImeiOrSnAsync(info.Imei);
                }

                if (rom != null)
                {
                    Console.WriteLine($"[+] ROM matched successfully!");
                    Console.WriteLine($"    - ROM Name:    {rom.RomName}");
                    Console.WriteLine($"    - Version:     {rom.RomVersion}");
                    Console.WriteLine($"    - Android:     {rom.AndroidVersion}");
                    Console.WriteLine($"    - File Size:   {rom.FileSize / (1024 * 1024)} MB");
                    Console.WriteLine($"    - Download URL: {rom.DownloadUrl}");
                }
                else
                {
                    Console.WriteLine("[-] No matching ROM found on server for this device.");
                }
                return 0;
            }

            case "download":
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: software-fix download <url> [destination-file]");
                    return 1;
                }

                string url = args[1];
                string dest = args.Length >= 3 ? args[2] : Path.Combine(Configurations.RomPath, Path.GetFileName(new Uri(url).LocalPath));

                Console.WriteLine($"[*] Downloading firmware to: {dest}");
                var downloader = new RomDownloader();
                downloader.ProgressChanged += (_, e) =>
                {
                    Console.Write($"\r[*] Progress: {e.ProgressPercentage:F1}% ({e.BytesDownloaded / 1024 / 1024}MB / {e.TotalBytes / 1024 / 1024}MB) - {e.SpeedMbps:F2} Mbps   ");
                };

                await downloader.DownloadFileAsync(url, dest);
                Console.WriteLine("\n[+] Download completed!");
                return 0;
            }

            case "flash":
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: software-fix flash <flashfile.xml>");
                    return 1;
                }

                string xmlPath = args[1];
                if (!File.Exists(xmlPath))
                {
                    Console.WriteLine($"[-] File not found: {xmlPath}");
                    return 1;
                }

                var commands = FlashFileParser.ParseXml(xmlPath);
                if (commands.Count == 0)
                {
                    Console.WriteLine("[-] No flashing steps found in XML file.");
                    return 1;
                }

                Console.WriteLine($"[*] Loaded {commands.Count} flash commands from: {xmlPath}");
                Console.WriteLine("[!] WARNING: Flashing will overwrite device firmware!");
                Console.Write("Proceed? (y/N): ");
                string? confirm = Console.ReadLine();
                if (confirm?.Trim().ToLowerInvariant() != "y")
                {
                    Console.WriteLine("Flashing aborted by user.");
                    return 0;
                }

                var engine = new FlashEngine(fb);
                engine.LogReceived += log => Console.WriteLine(log);
                engine.ProgressChanged += (_, e) =>
                {
                    Console.WriteLine($"--> Step {e.CurrentStep}/{e.TotalSteps} ({e.ProgressPercentage:F1}%): {e.CurrentOperation} {e.CurrentPartition}");
                };

                bool success = await engine.FlashDeviceAsync(null, commands);
                if (success)
                {
                    Console.WriteLine("[+] Flashing finished successfully! You can reboot your device now.");
                    return 0;
                }
                else
                {
                    Console.WriteLine("[-] Flashing failed. Check logs above.");
                    return 1;
                }
            }

            default:
                PrintUsage();
                return 1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("\nUsage:");
        Console.WriteLine("  software-fix scan                  Detect connected devices (Fastboot/ADB)");
        Console.WriteLine("  software-fix match                 Query Lenovo servers for connected device ROM");
        Console.WriteLine("  software-fix download <url> [path] Download firmware ROM with progress & resume");
        Console.WriteLine("  software-fix flash <flashfile.xml> Flash firmware using Motorola fastboot steps");
        Console.WriteLine("  software-fix help                  Show this help message\n");
    }
}
