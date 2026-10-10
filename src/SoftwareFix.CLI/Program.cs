using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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

            case "odm-server":
            {
                string? imei = GetArg(args, "--imei");
                if (string.IsNullOrEmpty(imei))
                {
                    Console.WriteLine("Usage: software-fix odm-server --imei <IMEI> [--user <usuário>] [--port <porta>]");
                    Console.WriteLine("  Servidor local de assinatura ODM (\"GPS auth\") em 127.0.0.1:5000 —");
                    Console.WriteLine("  usado no resgate de modelos Motorola MediaTek (handshake SLA do fastboot).");
                    return 1;
                }

                string? user = GetArg(args, "--user") ?? Environment.GetEnvironmentVariable("SOFTWARE_FIX_ODM_USER");
                if (string.IsNullOrEmpty(user))
                {
                    Console.Write("[*] Usuário do serviço (conta Motorola): ");
                    user = Console.ReadLine()?.Trim();
                }
                string? password = Environment.GetEnvironmentVariable("SOFTWARE_FIX_ODM_PASSWORD");
                if (string.IsNullOrEmpty(password))
                {
                    password = PromptPassword("[*] Senha do serviço (não aparece na tela): ");
                }
                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
                {
                    Console.WriteLine("[-] Usuário e senha do serviço são obrigatórios (a assinatura usa a conta Motorola).");
                    return 1;
                }

                int port = int.TryParse(GetArg(args, "--port") ?? Environment.GetEnvironmentVariable("SOFTWARE_FIX_ODM_PORT"), out int parsedPort)
                    ? parsedPort
                    : OdmSocketServer.DefaultPort;

                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

                Console.WriteLine($"[*] IMEI: {imei}");
                Console.WriteLine($"[*] Servidor ODM no ar em 127.0.0.1:{port} (Ctrl+C encerra).");
                Console.WriteLine("[*] Deixe este processo rodando durante o resgate — o lado fastboot conecta aqui para assinar o hash (SLA).");
                var odmServer = new OdmSocketServer(imei, user!, password!, port);
                await odmServer.RunAsync(cts.Token);
                return 0;
            }

            case "odm-sign":
            {
                string? imei = GetArg(args, "--imei");
                if (string.IsNullOrEmpty(imei))
                {
                    Console.WriteLine("Usage: software-fix odm-sign --imei <IMEI> [--data <hash-hex>] [--user <usuário>]");
                    Console.WriteLine("  Testa o contrato de assinatura ODM contra o serviço real da Motorola.");
                    return 1;
                }
                string data = GetArg(args, "--data") ?? new string('0', 64);

                string? user = GetArg(args, "--user") ?? Environment.GetEnvironmentVariable("SOFTWARE_FIX_ODM_USER");
                if (string.IsNullOrEmpty(user))
                {
                    Console.Write("[*] Usuário do serviço (conta Motorola): ");
                    user = Console.ReadLine()?.Trim();
                }
                string? password = Environment.GetEnvironmentVariable("SOFTWARE_FIX_ODM_PASSWORD");
                if (string.IsNullOrEmpty(password))
                {
                    password = PromptPassword("[*] Senha do serviço (não aparece na tela): ");
                }
                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
                {
                    Console.WriteLine("[-] Usuário e senha do serviço são obrigatórios.");
                    return 1;
                }

                Console.WriteLine($"[*] Assinando hash de teste com IMEI {imei}... (sem --data: 64 zeros)");
                var odmClient = new OdmSigningClient(user!, password) { Log = Console.WriteLine };
                try
                {
                    string signed = await odmClient.SignDataWithRetryAsync(
                        imei, OdmSocketServer.DefaultMascId, OdmSocketServer.ClientIp, OdmSocketServer.DefaultClientReqType,
                        OdmSocketServer.DefaultLogId, OdmSocketServer.DefaultProdId, OdmSocketServer.DefaultKeyType,
                        OdmSocketServer.DefaultKeyName, data);
                    Console.WriteLine($"[+] ASSINATURA RECEBIDA: {signed.Length} chars hex = {signed.Length / 2} bytes.");
                    Console.WriteLine("    " + signed[..Math.Min(80, signed.Length)] + (signed.Length > 80 ? "..." : ""));
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[!] Falha na assinatura: {ex.Message}");
                    return 1;
                }
            }

            case "mtk-status":
            {
                var mtk = new MtkClientRunner();
                Console.WriteLine("[*] Fluxo MediaTek (PreLoader + DA assinado) — configuração:");
                Console.WriteLine($"    - mtkclient:    {mtk.MtkBinary}" + (mtk.BinaryAvailable ? "  [ok]" : "  [AUSENTE]"));
                Console.WriteLine($"    - DA assinado:  {mtk.DaFile}" + (mtk.DaAvailable ? "  [ok]" : "  [AUSENTE]"));
                Console.WriteLine($"    - logs:         {Configurations.MtkLogDir}");
                Console.WriteLine();

                var usb = MtkClientRunner.GetMtkUsbDevices();
                if (usb.Count == 0)
                {
                    Console.WriteLine("[i] Nenhum aparelho MediaTek/Motorola no USB agora.");
                }
                else
                {
                    Console.WriteLine("[i] Dispositivos MediaTek/Motorola no USB agora:");
                    foreach (var d in usb)
                    {
                        Console.WriteLine($"    - {d.Vid}:{d.Pid} {d.Info}");
                    }
                    if (usb.Any(d => d.Vid == "0e8d"))
                    {
                        Console.WriteLine("[!] MediaTek presente: desconecte (e desligue com Power 20-30s se estiver travado) antes de rodar mtk-connect/mtk-dump.");
                    }
                }
                if (!mtk.BinaryAvailable)
                {
                    Console.WriteLine("[i] Instale o mtkclient (ex.: venv em ~/.venvs/lmsa-tools) ou defina SOFTWARE_FIX_MTK.");
                }
                if (!mtk.DaAvailable)
                {
                    Console.WriteLine($"[i] Coloque o DA assinado em {Configurations.MtkDaDefaultFile} (ou defina SOFTWARE_FIX_MTK_DA).");
                }
                return 0;
            }

            case "mtk-connect":
            {
                var mtk = new MtkClientRunner();
                if (!CheckMtkPrereqs(mtk)) return 1;
                if (!await MtkClientRunner.EnsureDeviceAbsentAsync(TimeSpan.FromSeconds(300), !HasFlag(args, "--no-wait"))) return 1;
                PrintConnectRitual("CONEXÃO (somente leitura: lê o GPT/tabela de partições)");
                Console.WriteLine("[*] Iniciando mtkclient — ele vai AGUARDAR o aparelho aparecer...");
                var r = await RunMtkStreaming(mtk, "printgpt", "", "connect", TimeSpan.FromSeconds(ParseTimeout(args, 900)));
                return ReportMtkResult(r, "Conexão OK — GPT/tabela de partições lido do aparelho.");
            }

            case "mtk-dump":
            {
                var mtk = new MtkClientRunner();
                if (!CheckMtkPrereqs(mtk)) return 1;
                const string defaultParts = "lk_a,lk_b,boot_a,boot_b,init_boot_a,init_boot_b,dtbo_a,dtbo_b,vbmeta_a,vbmeta_b,vbmeta_system_a,vbmeta_system_b,vbmeta_vendor_a,vbmeta_vendor_b,tee_a,tee_b,logo_a,logo_b,mrdump";
                string partsCsv = GetArg(args, "--partitions") ?? defaultParts;
                string outDir = GetArg(args, "--out") ?? Path.Combine(Configurations.DumpDir, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                if (outDir.Contains(' '))
                {
                    Console.WriteLine("[-] O caminho de saída não pode conter espaços (limitação do mtkclient).");
                    return 1;
                }
                Directory.CreateDirectory(outDir);
                var parts = partsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0) { Console.WriteLine("[-] Lista de partições vazia."); return 1; }
                var files = parts.Select(p => Path.Combine(outDir, p + ".img")).ToArray();

                Console.WriteLine($"[*] Dump (SOMENTE LEITURA) de {parts.Length} partição(ões) → {outDir}");
                for (int i = 0; i < parts.Length; i++)
                {
                    Console.WriteLine($"    - {parts[i]} → {Path.GetFileName(files[i])}");
                }
                if (!await MtkClientRunner.EnsureDeviceAbsentAsync(TimeSpan.FromSeconds(300), !HasFlag(args, "--no-wait"))) return 1;
                PrintConnectRitual("LEITURA (dump de partições — nada é gravado)");
                Console.WriteLine("[*] Iniciando mtkclient — aguardando o aparelho...");
                var r = await RunMtkStreaming(mtk, "r", $"{string.Join(',', parts)} {string.Join(',', files)}", "dump", TimeSpan.FromSeconds(ParseTimeout(args, 3600)));
                int report = ReportMtkResult(r, "Dump concluído.");
                if (report == 0)
                {
                    foreach (var f in files)
                    {
                        Console.WriteLine($"    - {Path.GetFileName(f)}: {(File.Exists(f) ? new FileInfo(f).Length / 1024 + " KB" : "AUSENTE")}");
                    }
                }
                return report;
            }

            case "mtk-write-test":
            {
                var mtk = new MtkClientRunner();
                if (!CheckMtkPrereqs(mtk)) return 1;
                string part = GetArg(args, "--partition") ?? "mrdump";
                string outDir = GetArg(args, "--out") ?? Path.Combine(Configurations.DumpDir, "write-test-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                if (outDir.Contains(' ')) { Console.WriteLine("[-] Caminho sem espaços, por favor."); return 1; }
                Directory.CreateDirectory(outDir);
                string orig = Path.Combine(outDir, part + ".orig.img");
                string reread = Path.Combine(outDir, part + ".reread.img");
                string script = Path.Combine(outDir, "write-test.script");

                Console.WriteLine($"[*] TESTE DE ESCRITA em '{part}': lê → regrava os MESMOS bytes → lê de novo → compara SHA-256.");
                Console.WriteLine("    (Homologação da escrita: o conteúdo final é idêntico ao original.)");
                if (!HasFlag(args, "--yes"))
                {
                    Console.Write("Continuar? (y/N): ");
                    if (Console.ReadLine()?.Trim().ToLowerInvariant() != "y") { Console.WriteLine("Abortado."); return 0; }
                }
                File.WriteAllLines(script, new[] { $"r {part} {orig}", $"w {part} {orig}", $"r {part} {reread}" });

                if (!await MtkClientRunner.EnsureDeviceAbsentAsync(TimeSpan.FromSeconds(300), !HasFlag(args, "--no-wait"))) return 1;
                PrintConnectRitual("TESTE DE ESCRITA (leitura + regravação idêntica + verificação)");
                Console.WriteLine("[*] Iniciando mtkclient — aguardando o aparelho...");
                var r = await RunMtkStreaming(mtk, "script", $"\"{script}\"", "write-test", TimeSpan.FromSeconds(ParseTimeout(args, 1800)));
                int report = ReportMtkResult(r, "");
                if (report != 0) return report;

                if (!File.Exists(orig) || !File.Exists(reread))
                {
                    Console.WriteLine("[-] Arquivos de verificação ausentes (a gravação/leitura não completou?).");
                    return 1;
                }
                string h1 = Sha256File(orig);
                string h2 = Sha256File(reread);
                Console.WriteLine($"    SHA-256 (antes):  {h1}");
                Console.WriteLine($"    SHA-256 (depois): {h2}");
                if (h1 == h2)
                {
                    Console.WriteLine("[+] VERIFY OK — escrita validada: releitura idêntica byte a byte.");
                    return 0;
                }
                Console.WriteLine("[-] VERIFY FALHOU — os bytes mudaram. NÃO prossiga com flash até investigar.");
                return 1;
            }

            case "mtk-flash":
            {
                var mtk = new MtkClientRunner();
                if (!CheckMtkPrereqs(mtk)) return 1;
                string? dir = GetArg(args, "--dir");
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    Console.WriteLine("Usage: software-fix mtk-flash --dir <pasta-da-ROM-extraída> [--dry-run] [--only p1,p2] [--slotted] [--verify] [--yes]");
                    Console.WriteLine("  Usa o *scatter*.txt da ROM para montar o plano partição→imagem.");
                    return 1;
                }

                var plan = MtkFlashPlan.Build(dir, GetArg(args, "--only"), HasFlag(args, "--slotted"));
                if (plan.Count == 0)
                {
                    Console.WriteLine("[-] Nenhum par partição→imagem encontrado (procurei *scatter*.txt na pasta).");
                    return 1;
                }

                Console.WriteLine($"[*] Plano de flash: {plan.Count} partição(ões) a partir de {dir}");
                long total = 0;
                foreach (var (p, f) in plan)
                {
                    long size = new FileInfo(f).Length;
                    total += size;
                    Console.WriteLine($"    - {p} ← {Path.GetFileName(f)} ({size / (1024 * 1024)} MB)");
                }
                Console.WriteLine($"    TOTAL: {total / (1024 * 1024)} MB");

                if (HasFlag(args, "--dry-run"))
                {
                    Console.WriteLine("[i] --dry-run: nada foi gravado.");
                    return 0;
                }

                Console.WriteLine("[!] ATENÇÃO: isto vai SOBRESCREVER partições do aparelho (conforme o plano acima).");
                if (!HasFlag(args, "--yes"))
                {
                    Console.Write("Digite 'FLASH' para confirmar: ");
                    if (Console.ReadLine()?.Trim() != "FLASH") { Console.WriteLine("Abortado."); return 0; }
                }

                string outDir = Path.Combine(Configurations.MtkLogDir, "flash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(outDir);
                string script = Path.Combine(outDir, "flash.script");
                File.WriteAllLines(script, plan.Select(x => $"w {x.Partition} {x.File}"));

                if (!await MtkClientRunner.EnsureDeviceAbsentAsync(TimeSpan.FromSeconds(300), !HasFlag(args, "--no-wait"))) return 1;
                PrintConnectRitual("FLASH (gravação das partições do plano)");
                Console.WriteLine("[*] Iniciando mtkclient — aguardando o aparelho...");
                var r = await RunMtkStreaming(mtk, "script", $"\"{script}\"", "flash", TimeSpan.FromSeconds(ParseTimeout(args, 7200)));
                int report = ReportMtkResult(r, "");
                if (report != 0) return report;

                if (HasFlag(args, "--verify"))
                {
                    Console.WriteLine("[*] Verificação pós-flash (releitura + SHA-256)...");
                    string vdir = Path.Combine(outDir, "verify");
                    Directory.CreateDirectory(vdir);
                    var vparts = plan.Select(x => x.Partition).ToArray();
                    var vfiles = vparts.Select(p => Path.Combine(vdir, p + ".img")).ToArray();
                    var vr = await RunMtkStreaming(mtk, "r", $"{string.Join(',', vparts)} {string.Join(',', vfiles)}", "flash-verify", TimeSpan.FromSeconds(3600));
                    if (vr.Success)
                    {
                        bool allOk = true;
                        for (int i = 0; i < plan.Count; i++)
                        {
                            string a = Sha256File(plan[i].File);
                            string b = File.Exists(vfiles[i]) ? Sha256File(vfiles[i]) : "(ausente)";
                            bool ok = a == b;
                            allOk &= ok;
                            Console.WriteLine($"    - {plan[i].Partition}: {(ok ? "OK" : "DIVERGENTE")}");
                        }
                        Console.WriteLine(allOk
                            ? "[+] Verificação completa: todas as partições conferem."
                            : "[!] Houve divergências — investigar antes de considerar o resgate concluído.");
                    }
                    else
                    {
                        Console.WriteLine("[!] A verificação não completou (ver log).");
                    }
                }
                return report;
            }

            default:
                PrintUsage();
                return 1;
        }
    }

    static string? GetArg(string[] args, string name)
    {
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }

    static string PromptPassword(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine()?.Trim() ?? string.Empty;
        }
        var sb = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) { sb.Length--; Console.Write("\b \b"); }
                continue;
            }
            if (!char.IsControl(key.KeyChar)) { sb.Append(key.KeyChar); Console.Write('*'); }
        }
        return sb.ToString();
    }

    static bool HasFlag(string[] args, string name) =>
        args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    static int ParseTimeout(string[] args, int defaultSeconds) =>
        int.TryParse(GetArg(args, "--timeout"), out int t) && t > 0 ? t : defaultSeconds;

    static bool CheckMtkPrereqs(MtkClientRunner mtk)
    {
        if (!mtk.BinaryAvailable)
        {
            Console.WriteLine($"[!] mtkclient não encontrado em '{mtk.MtkBinary}'.");
            Console.WriteLine("    Instale (ex.: python -m venv ~/.venvs/lmsa-tools && ~/.venvs/lmsa-tools/bin/pip install git+https://github.com/bkerler/mtkclient)");
            Console.WriteLine("    ou aponte com: export SOFTWARE_FIX_MTK=/caminho/do/mtk");
            return false;
        }
        if (!mtk.DaAvailable)
        {
            Console.WriteLine($"[!] DA assinado não encontrado em '{mtk.DaFile}'.");
            Console.WriteLine("    Copie o DA do pacote LamuC (ex.: 'DA_A15_lamuc_FORBID_SIGNED.bin') para esse caminho");
            Console.WriteLine("    ou aponte com: export SOFTWARE_FIX_MTK_DA=/caminho/do/DA.bin");
            return false;
        }
        return true;
    }

    static void PrintConnectRitual(string title)
    {
        Console.WriteLine();
        Console.WriteLine("==============================================================");
        Console.WriteLine($" {title}");
        Console.WriteLine();
        Console.WriteLine(" No aparelho (moto g17 / MediaTek):");
        Console.WriteLine("  1) DEIXE-O DESLIGADO (segure Power 20-30s se estiver ligado);");
        Console.WriteLine("  2) Segure VOLUME ↓ e conecte o cabo USB;");
        Console.WriteLine("  3) Mantenha ~10s — o mtkclient assume a conexão.");
        Console.WriteLine("==============================================================");
    }

    static void PrintWedgedHelp()
    {
        Console.WriteLine();
        Console.WriteLine("[!] PreLoader TRAVADO (handshake falhando em loop).");
        Console.WriteLine("    Solução: segure o Power por 20-30s para desligar o aparelho, reconecte");
        Console.WriteLine("    segurando Volume ↓ e rode o comando novamente.");
    }

    static async Task<MtkRunResult> RunMtkStreaming(MtkClientRunner mtk, string command, string cmdArgs, string logName, TimeSpan timeout)
    {
        void Handler(string line) => Console.WriteLine("  | " + line);
        mtk.LogReceived += Handler;
        try
        {
            return await mtk.RunAsync(command, cmdArgs, logName, timeout);
        }
        finally
        {
            mtk.LogReceived -= Handler;
        }
    }

    static int ReportMtkResult(MtkRunResult r, string successMessage)
    {
        if (r.Wedged)
        {
            PrintWedgedHelp();
            Console.WriteLine($"    (log: {r.LogFile})");
            return 2;
        }
        if (r.NeedsReconnect)
        {
            Console.WriteLine("[!] O aparelho apareceu ANTES do mtkclient iniciar (o DaHandler aborta nesse caso).");
            Console.WriteLine("    Desconecte, desligue o aparelho, rode o comando de novo e só então reconecte segurando Volume ↓.");
            Console.WriteLine($"    (log: {r.LogFile})");
            return 2;
        }
        if (r.TimedOut)
        {
            Console.WriteLine($"[-] Tempo esgotado. (log: {r.LogFile})");
            return 1;
        }
        if (r.Success)
        {
            if (!string.IsNullOrEmpty(successMessage)) Console.WriteLine($"[+] {successMessage}");
            Console.WriteLine($"[i] Log: {r.LogFile}");
            return 0;
        }
        Console.WriteLine($"[-] O mtkclient terminou com erro (exit={r.ExitCode}). (log: {r.LogFile})");
        return 1;
    }

    static string Sha256File(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s)).ToLowerInvariant();
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
        Console.WriteLine("  software-fix odm-server --imei <IMEI>  Servidor de assinatura ODM (SLA/MediaTek) em 127.0.0.1:5000");
        Console.WriteLine("  software-fix odm-sign --imei <IMEI>    Testa a assinatura ODM contra o serviço da Motorola");
        Console.WriteLine("  --- Fluxo MediaTek (PreLoader + DA assinado; ver docs/MTK_RESCUE.md) ---");
        Console.WriteLine("  software-fix mtk-status                Mostra configuração do fluxo MTK e dispositivos USB");
        Console.WriteLine("  software-fix mtk-connect               Conecta via PreLoader e lê o GPT (somente leitura)");
        Console.WriteLine("  software-fix mtk-dump [--partitions p1,p2] [--out dir]   Dump (leitura) de partições");
        Console.WriteLine("  software-fix mtk-write-test [--partition mrdump]         Teste de escrita (regrava idêntico + verifica)");
        Console.WriteLine("  software-fix mtk-flash --dir <rom> [--dry-run] [--verify]  Flash das partições via scatter da ROM");
        Console.WriteLine("  software-fix help                  Show this help message\n");
    }
}
