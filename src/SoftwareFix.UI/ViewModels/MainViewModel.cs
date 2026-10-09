using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftwareFix.Core;
using SoftwareFix.Core.Models;
using SoftwareFix.Core.Services;
using SoftwareFix.Device;
using SoftwareFix.Download;
using SoftwareFix.Flash;

namespace SoftwareFix.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly DeviceMonitor _monitor = new();
    private readonly LenovoApiClient _api = new();
    private readonly FastbootRunner _fastboot = new();
    private readonly AdbRunner _adb = new();
    private readonly RomDownloader _downloader = new();
    private readonly FlashEngine _flashEngine = new();

    [ObservableProperty]
    private string _connectionStatus = "Aguardando dispositivo...";

    [ObservableProperty]
    private bool _isDeviceConnected;

    [ObservableProperty]
    private string _deviceMode = "Nenhum";

    [ObservableProperty]
    private string _deviceModel = "-";

    [ObservableProperty]
    private string _deviceSerial = "-";

    [ObservableProperty]
    private string _deviceImei = "-";

    [ObservableProperty]
    private string _deviceCarrier = "-";

    [ObservableProperty]
    private string _deviceBaseband = "-";

    [ObservableProperty]
    private string _deviceFingerprint = "-";

    // ROM Matching
    [ObservableProperty]
    private string _romStatus = "Nenhum firmware selecionado";

    [ObservableProperty]
    private string _romName = "-";

    [ObservableProperty]
    private string _romVersion = "-";

    [ObservableProperty]
    private string _romSize = "-";

    [ObservableProperty]
    private string _romDownloadUrl = "";

    [ObservableProperty]
    private bool _isRomReady;

    // Progress & Logs
    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Pronto";

    [ObservableProperty]
    private string _currentXmlPath = "";

    public ObservableCollection<string> Logs { get; } = new();

    public MainViewModel()
    {
        _monitor.DeviceConnected += OnDeviceConnected;
        _monitor.DeviceDisconnected += OnDeviceDisconnected;
        _monitor.StartMonitoring();

        _downloader.ProgressChanged += OnDownloadProgress;
        _flashEngine.ProgressChanged += OnFlashProgress;
        _flashEngine.LogReceived += AddLog;

        AddLog("[Sistema] Software Fix Linux Edition iniciado.");
        AddLog($"[Sistema] Diretório de ROMs: {Configurations.RomPath}");

        Task.Run(async () =>
        {
            AddLog("[Nuvem] Conectando aos servidores da Lenovo...");
            bool ok = await _api.InitializeAsync();
            if (ok)
            {
                AddLog("[Nuvem] Autenticado com sucesso na API Lenovo (lsa.lenovo.com).");
            }
            else
            {
                AddLog("[Nuvem] Modo offline/anônimo ativo.");
            }
        });
    }

    private void OnDeviceConnected(DeviceRescueInfo device, DeviceMode mode)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsDeviceConnected = true;
            DeviceMode = mode.ToString().ToUpperInvariant();
            DeviceSerial = device.SerialNumber;
            DeviceModel = string.IsNullOrEmpty(device.Sku) ? device.ModelName : device.Sku;
            DeviceImei = string.IsNullOrEmpty(device.Imei) ? "Não disponível" : device.Imei;
            DeviceCarrier = string.IsNullOrEmpty(device.RoCarrier) ? "-" : device.RoCarrier;
            DeviceBaseband = string.IsNullOrEmpty(device.BasebandVersion) ? "-" : device.BasebandVersion;
            DeviceFingerprint = string.IsNullOrEmpty(device.Fingerprint) ? "-" : device.Fingerprint;

            ConnectionStatus = $"Dispositivo Conectado ({DeviceMode})";
            AddLog($"[USB] Dispositivo detectado: {DeviceModel} ({DeviceSerial}) em modo {DeviceMode}");

            // Auto-search ROM
            SearchRomCommand.Execute(null);
        });
    }

    private void OnDeviceDisconnected()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsDeviceConnected = false;
            DeviceMode = "Nenhum";
            ConnectionStatus = "Aguardando dispositivo...";
            AddLog("[USB] Dispositivo desconectado.");
        });
    }

    [RelayCommand]
    public async Task SearchRomAsync()
    {
        if (_monitor.CurrentDeviceInfo == null)
        {
            AddLog("[Busca] Nenhum dispositivo conectado para buscar ROM.");
            return;
        }

        IsBusy = true;
        StatusMessage = "Buscando firmware oficial na Lenovo...";
        AddLog($"[Busca] Consultando ROM para {DeviceModel} (Carrier: {DeviceCarrier})...");

        try
        {
            var rom = await _api.FindRomByDeviceAsync(_monitor.CurrentDeviceInfo);
            if (rom != null)
            {
                RomName = rom.RomName;
                RomVersion = rom.RomVersion;
                RomSize = $"{rom.FileSize / (1024 * 1024)} MB";
                RomDownloadUrl = rom.DownloadUrl;
                IsRomReady = true;
                RomStatus = "Firmware Oficial Disponível!";
                AddLog($"[Busca] Firmware encontrado: {RomName} ({RomSize})");
            }
            else
            {
                RomStatus = "Nenhum firmware correspondente encontrado.";
                AddLog("[Busca] Nenhum firmware encontrado para os parâmetros deste aparelho.");
            }
        }
        catch (Exception ex)
        {
            AddLog($"[Erro] Falha ao consultar ROM: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            StatusMessage = "Pronto";
        }
    }

    [RelayCommand]
    public async Task DownloadRomAsync()
    {
        if (string.IsNullOrEmpty(RomDownloadUrl))
        {
            AddLog("[Download] URL de download não disponível.");
            return;
        }

        IsBusy = true;
        string dest = Path.Combine(Configurations.RomPath, Path.GetFileName(new Uri(RomDownloadUrl).LocalPath));
        AddLog($"[Download] Iniciando download: {dest}...");

        try
        {
            await _downloader.DownloadFileAsync(RomDownloadUrl, dest);
            AddLog($"[Download] Download concluído com sucesso!");

            // Extract archive
            string extractDir = Path.Combine(Configurations.RomPath, Path.GetFileNameWithoutExtension(dest));
            AddLog($"[Extração] Extraindo pacote para {extractDir}...");
            await ArchiveExtractor.ExtractArchiveAsync(dest, extractDir);
            AddLog($"[Extração] Pacote extraído com sucesso!");

            // Locate flashfile.xml
            string[] xmls = Directory.GetFiles(extractDir, "flashfile.xml", SearchOption.AllDirectories);
            if (xmls.Length > 0)
            {
                CurrentXmlPath = xmls[0];
                AddLog($"[Flash] flashfile.xml localizado: {CurrentXmlPath}");
            }
        }
        catch (Exception ex)
        {
            AddLog($"[Erro] Falha no download/extração: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task StartFlashAsync()
    {
        if (string.IsNullOrEmpty(CurrentXmlPath) || !File.Exists(CurrentXmlPath))
        {
            AddLog("[Flash] Nenhum arquivo flashfile.xml carregado.");
            return;
        }

        if (DeviceMode != "FASTBOOT")
        {
            AddLog("[Flash] O dispositivo precisa estar em modo Fastboot para iniciar a gravação.");
            return;
        }

        IsBusy = true;
        StatusMessage = "Gravando firmware...";
        AddLog("[Flash] Lendo comandos de gravação...");

        try
        {
            var commands = FlashFileParser.ParseXml(CurrentXmlPath);
            AddLog($"[Flash] {commands.Count} comandos preparados. Iniciando gravação...");

            bool ok = await _flashEngine.FlashDeviceAsync(_monitor.CurrentSerial, commands);
            if (ok)
            {
                AddLog("[Flash] GRAVAÇÃO FINALIZADA COM SUCESSO! O dispositivo pode ser reiniciado.");
                StatusMessage = "Gravação concluída com sucesso!";
            }
            else
            {
                AddLog("[Flash] FALHA DURANTE A GRAVAÇÃO. Verifique os logs.");
                StatusMessage = "Erro na gravação!";
            }
        }
        catch (Exception ex)
        {
            AddLog($"[Erro] Exceção durante o flash: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RebootToBootloaderAsync()
    {
        if (string.IsNullOrEmpty(DeviceSerial) || DeviceMode != "ADB") return;
        AddLog("[Controle] Enviando comando 'reboot bootloader'...");
        await _adb.RebootToBootloaderAsync(DeviceSerial);
    }

    private void OnDownloadProgress(object? sender, DownloadProgressEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ProgressValue = e.ProgressPercentage;
            ProgressText = $"{e.ProgressPercentage:F1}% ({e.BytesDownloaded / 1024 / 1024} MB / {e.TotalBytes / 1024 / 1024} MB) - {e.SpeedMbps:F2} Mbps";
        });
    }

    private void OnFlashProgress(object? sender, FlashProgressEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ProgressValue = e.ProgressPercentage;
            ProgressText = $"Passo {e.CurrentStep}/{e.TotalSteps} ({e.ProgressPercentage:F1}%): {e.CurrentOperation} {e.CurrentPartition}";
        });
    }

    private void AddLog(string msg)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Logs.Add(line);
            if (Logs.Count > 500) Logs.RemoveAt(0);
        });
    }
}
