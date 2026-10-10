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

            case "login":
            {
                var loginClient = new LenovoApiClient();
                Console.WriteLine("[*] Preparando cliente (chave pública RSA)...");
                await loginClient.InitializeAsync();

                if (loginClient.HasSession)
                {
                    Console.WriteLine("[+] Sessão salva encontrada. Validando com o servidor...");
                    var current = await loginClient.GetUserInfoAsync();
                    if (current.Success)
                    {
                        Console.WriteLine($"[+] Sessão válida! Usuário: {current.Content?["name"] ?? current.Content?["email"] ?? "desconhecido"}");
                        return 0;
                    }
                    Console.WriteLine("[!] Sessão expirada. Continue o login para renovar.");
                    loginClient.ClearSession();
                }

                var loginUrls = await loginClient.GetLoginUrlAsync();
                if (!loginUrls.Success || loginUrls.Content == null)
                {
                    Console.WriteLine($"[-] Falha ao obter URL de login: code={loginUrls.Code} desc={loginUrls.Desc}");
                    return 1;
                }

                string loginUrl = loginUrls.Content.ToString()!;
                if (!loginUrl.Contains("lenovoid.lang="))
                {
                    loginUrl += "&lenovoid.lang=pt_BR";
                }

                Console.WriteLine();
                Console.WriteLine("[*] Abra a URL abaixo no navegador e faça login com sua conta Lenovo ID:");
                Console.WriteLine();
                Console.WriteLine("    " + loginUrl);
                Console.WriteLine();
                Console.WriteLine("[*] Após o login, você será redirecionado para uma página 'lenovoIdSuccess'.");
                Console.WriteLine("    Copie a URL COMPLETA dessa página (contém 'lenovoid.wust=...') e cole aqui.");
                Console.WriteLine("    (Ou cole apenas o valor do wust.)");
                Console.WriteLine();
                Console.Write("> URL/wust: ");
                string? pasted = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(pasted))
                {
                    Console.WriteLine("[-] Entrada vazia. Abortado.");
                    return 1;
                }

                var loginResult = await loginClient.LoginWithWustAsync(pasted);
                if (loginResult.Success)
                {
                    Console.WriteLine("[+] Login validado com sucesso! Sessão salva em: " + Configurations.SessionFile);
                    Console.WriteLine("    Usuário: " + (loginResult.Content?["name"] ?? loginResult.Content?["email"] ?? "(sem nome)"));
                    return 0;
                }
                Console.WriteLine($"[-] Falha no login: code={loginResult.Code} desc={loginResult.Desc}");
                return 1;
            }

            case "api-test":
            {
                Console.WriteLine("[*] Teste 1: POST /common/rsa.jhtml (chave pública RSA)");
                var testClient = new LenovoApiClient();
                await testClient.FetchPublicKeyAsync();
                var ping = await testClient.GetLoginUrlAsync();
                Console.WriteLine(ping.Success || ping.Code == "0000"
                    ? "    [+] rsa.jhtml OK — chave pública obtida"
                    : $"    [-] rsa.jhtml: {ping.Code} {ping.Desc}");

                Console.WriteLine("[*] Teste 2: POST /dictionary/getApiInfo.jhtml {key:TIP_URL}");
                if (ping.Success && ping.Content != null)
                {
                    string url = ping.Content.ToString()!;
                    Console.WriteLine($"    [+] OAuth URL recebida: {url[..Math.Min(100, url.Length)]}...");
                }
                else
                {
                    Console.WriteLine($"    [-] Falha: code={ping.Code} desc={ping.Desc}");
                }

                Console.WriteLine("[*] Teste 3: GET /user/getSFUserInfo.jhtml SEM token (anônimo)");
                var anon = new LenovoApiClient();
                await anon.FetchPublicKeyAsync();
                var anonymous = await anon.GetUserInfoAsync();
                Console.WriteLine($"    [+] resposta do servidor: code={anonymous.Code} desc={anonymous.Desc}");
                Console.WriteLine("        (code 410 'Missing device fingerprint' = contrato validado pelo servidor)");

                Console.WriteLine("[*] Teste 4: validação da sessão salva (se existir)");
                var sessionClient = new LenovoApiClient();
                if (sessionClient.TryLoadSession())
                {
                    await sessionClient.FetchPublicKeyAsync();
                    var authed = await sessionClient.GetUserInfoAsync();
                    Console.WriteLine(authed.Success
                        ? "    [+] Sessão autenticada — fingerprint RSA ACEITO pelo servidor"
                        : $"    [-] Sessão rejeitada: code={authed.Code} desc={authed.Desc}");
                }
                else
                {
                    Console.WriteLine("    [i] Sem sessão salva — rode 'software-fix login' para autenticar.");
                }
                return 0;
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
        Console.WriteLine("  software-fix login                 Autenticar com Lenovo ID (OAuth) e salvar a sessão");
        Console.WriteLine("  software-fix api-test              Testar os contratos da API Lenovo (smoke test)");
        Console.WriteLine("  software-fix help                  Show this help message\n");
    }
}
