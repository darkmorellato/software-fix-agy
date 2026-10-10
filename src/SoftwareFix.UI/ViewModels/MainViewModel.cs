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

    // Navigation
    [ObservableProperty]
    private int _selectedSectionIndex = 0;

    public bool IsSectionRescue => SelectedSectionIndex == 0;
    public bool IsSectionDiagnostics => SelectedSectionIndex == 1;
    public bool IsSectionDownloads => SelectedSectionIndex == 2;
    public bool IsSectionSettings => SelectedSectionIndex == 3;

    partial void OnSelectedSectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSectionRescue));
        OnPropertyChanged(nameof(IsSectionDiagnostics));
        OnPropertyChanged(nameof(IsSectionDownloads));
        OnPropertyChanged(nameof(IsSectionSettings));
    }

    [RelayCommand]
    public void SelectSection(string indexStr)
    {
        if (int.TryParse(indexStr, out int idx))
        {
            SelectedSectionIndex = idx;
        }
    }

    [ObservableProperty]
    private string _connectionStatus = "Aguardando dispositivo...";

    [ObservableProperty]
    private string _connectionLedColor = "#6B7280";

    [ObservableProperty]
    private bool _isDeviceConnected;

    [ObservableProperty]
    private string _deviceMode = "Desconectado";

    [ObservableProperty]
    private string _deviceBrand = "Motorola";

    [ObservableProperty]
    private string _deviceModel = "Nenhum aparelho";

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

    [ObservableProperty]
    private string _deviceChannelId = "-";

    [ObservableProperty]
    private string _deviceCid = "-";

    [ObservableProperty]
    private string _deviceRam = "-";

    [ObservableProperty]
    private string _deviceSecureState = "-";

    // ROM Matching
    [ObservableProperty]
    private string _romStatus = "Conecte o aparelho para buscar a ROM oficial";

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

    // Progress & Telemetry
    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private string _speedText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Pronto para operação";

    [ObservableProperty]
    private string _currentXmlPath = "";

    [ObservableProperty]
    private bool _showLogsDrawer = false;

    public ObservableCollection<string> Logs { get; } = new();

    public MainViewModel()
    {
        _monitor.DeviceConnected += OnDeviceConnected;
        _monitor.DeviceDisconnected += OnDeviceDisconnected;
        _monitor.StartMonitoring();

        _downloader.ProgressChanged += OnDownloadProgress;
        _flashEngine.ProgressChanged += OnFlashProgress;
        _flashEngine.LogReceived += AddLog;

        AddLog("[Sistema] Software Fix Linux Edition iniciado com sucesso.");
        AddLog($"[Sistema] Diretório de ROMs: {Configurations.RomPath}");

        Task.Run(async () =>
        {
            AddLog("[Nuvem] Conectando aos servidores oficiais da Lenovo (lsa.lenovo.com)...");
            bool ok = await _api.InitializeAsync();
            if (ok)
            {
                AddLog("[Nuvem] Autenticado com sucesso na API Lenovo (Sessão Segura RSA/JWT).");
            }
            else
            {
                AddLog("[Nuvem] Modo offline ativo ou conexão aguardando restabelecimento.");
            }
        });
    }

    [RelayCommand]
    public void ToggleLogsDrawer()
    {
        ShowLogsDrawer = !ShowLogsDrawer;
    }

    [RelayCommand]
    public void ClearLogs()
    {
        Logs.Clear();
        AddLog("[Sistema] Histórico de logs limpo.");
    }

    private void OnDeviceConnected(DeviceRescueInfo device, DeviceMode mode)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsDeviceConnected = true;
            DeviceMode = mode.ToString().ToUpperInvariant();
            DeviceSerial = device.SerialNumber;
            DeviceModel = string.IsNullOrEmpty(device.Sku) ? (string.IsNullOrEmpty(device.ModelName) ? "Motorola Device" : device.ModelName) : device.Sku;
            DeviceBrand = string.IsNullOrEmpty(device.Brand) ? "Motorola" : device.Brand;
            DeviceImei = string.IsNullOrEmpty(device.Imei) ? "Não disponível" : device.Imei;
            DeviceCarrier = string.IsNullOrEmpty(device.RoCarrier) ? "-" : device.RoCarrier;
            DeviceBaseband = string.IsNullOrEmpty(device.BasebandVersion) ? "-" : device.BasebandVersion;
            DeviceFingerprint = string.IsNullOrEmpty(device.Fingerprint) ? "-" : device.Fingerprint;
            DeviceChannelId = string.IsNullOrEmpty(device.ChannelId) ? "-" : device.ChannelId;
            DeviceCid = string.IsNullOrEmpty(device.Cid) ? "-" : device.Cid;
            DeviceRam = string.IsNullOrEmpty(device.Ram) ? "-" : device.Ram;
            DeviceSecureState = string.IsNullOrEmpty(device.SecureState) ? "Locked" : device.SecureState;

            ConnectionLedColor = mode == SoftwareFix.Device.DeviceMode.Fastboot ? "#00A3FF" : "#10B981";
            ConnectionStatus = $"{DeviceBrand} Conectado ({DeviceMode})";

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
            DeviceMode = "Desconectado";
            DeviceModel = "Nenhum aparelho";
            DeviceSerial = "-";
            DeviceImei = "-";
            DeviceCarrier = "-";
            DeviceBaseband = "-";
            DeviceFingerprint = "-";
            DeviceChannelId = "-";
            DeviceCid = "-";
            DeviceRam = "-";
            DeviceSecureState = "-";
            ConnectionLedColor = "#6B7280";
            ConnectionStatus = "Aguardando conexão USB...";
            AddLog("[USB] Dispositivo desconectado da porta USB.");
        });
    }

    [RelayCommand]
    public async Task SearchRomAsync()
    {
        if (_monitor.CurrentDeviceInfo == null)
        {
            AddLog("[Busca] Conecte o aparelho em modo Fastboot ou ADB para consultar a ROM oficial.");
            return;
        }

        IsBusy = true;
        StatusMessage = "Consultando catálogo de ROMs na Lenovo...";
        AddLog($"[Busca] Consultando ROM para {DeviceModel} (Canal: {DeviceCarrier})...");

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
                RomStatus = "Firmware Oficial Identificado!";
                AddLog($"[Busca] Firmware oficial localizado: {RomName} ({RomSize})");
            }
            else
            {
                RomStatus = "Nenhum firmware correspondente no servidor Lenovo.";
                AddLog("[Busca] Servidor Lenovo não retornou ROM para estes parâmetros.");
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
        StatusMessage = "Baixando firmware oficial...";
        string dest = Path.Combine(Configurations.RomPath, Path.GetFileName(new Uri(RomDownloadUrl).LocalPath));
        AddLog($"[Download] Iniciando download multithread: {dest}...");

        try
        {
            await _downloader.DownloadFileAsync(RomDownloadUrl, dest);
            AddLog("[Download] Download concluído com sucesso!");

            // Extract archive
            string extractDir = Path.Combine(Configurations.RomPath, Path.GetFileNameWithoutExtension(dest));
            AddLog($"[Extração] Descompactando pacote oficial em {extractDir}...");
            StatusMessage = "Descompactando pacote...";
            await ArchiveExtractor.ExtractArchiveAsync(dest, extractDir);
            AddLog("[Extração] Pacote descompactado com sucesso!");

            // Locate flashfile.xml
            string[] xmls = Directory.GetFiles(extractDir, "flashfile.xml", SearchOption.AllDirectories);
            if (xmls.Length > 0)
            {
                CurrentXmlPath = xmls[0];
                AddLog($"[Flash] flashfile.xml oficial localizado: {CurrentXmlPath}");
                StatusMessage = "Firmware descompactado e pronto para flash!";
            }
            else
            {
                AddLog("[Flash] Aviso: flashfile.xml não foi encontrado na raiz da pasta extraída.");
            }
        }
        catch (Exception ex)
        {
            AddLog($"[Erro] Falha no download ou descompactação: {ex.Message}");
            StatusMessage = "Falha no download/extração";
        }
        finally
        {
            IsBusy = false;
            SpeedText = "";
        }
    }

    [RelayCommand]
    public async Task StartFlashAsync()
    {
        if (string.IsNullOrEmpty(CurrentXmlPath) || !File.Exists(CurrentXmlPath))
        {
            AddLog("[Flash] Nenhum arquivo flashfile.xml carregado. Baixe o firmware primeiro.");
            return;
        }

        if (DeviceMode != "FASTBOOT")
        {
            AddLog("[Flash] O dispositivo precisa estar no modo FASTBOOT para iniciar o resgate.");
            return;
        }

        IsBusy = true;
        StatusMessage = "Gravando firmware (Resgate em andamento)...";
        ShowLogsDrawer = true;
        AddLog("[Flash] Preparando sequência de gravação oficial Motorola...");

        try
        {
            var commands = FlashFileParser.ParseXml(CurrentXmlPath);
            AddLog($"[Flash] {commands.Count} comandos identificados no flashfile.xml. Iniciando gravação...");

            bool ok = await _flashEngine.FlashDeviceAsync(_monitor.CurrentSerial, commands);
            if (ok)
            {
                AddLog("[Flash] ===========================================================");
                AddLog("[Flash] RESGATE CONCLUÍDO COM SUCESSO! SEU APARELHO ESTÁ RESTAURADO.");
                AddLog("[Flash] ===========================================================");
                StatusMessage = "Resgate concluído com sucesso!";
            }
            else
            {
                AddLog("[Flash] FALHA DURANTE A GRAVAÇÃO. Inspecione os erros acima.");
                StatusMessage = "Falha na gravação do firmware!";
            }
        }
        catch (Exception ex)
        {
            AddLog($"[Erro] Exceção durante o flash: {ex.Message}");
            StatusMessage = "Erro durante o flash";
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
        AddLog("[Controle] Reiniciando aparelho em modo Fastboot via ADB...");
        await _adb.RebootToBootloaderAsync(DeviceSerial);
    }

    private void OnDownloadProgress(object? sender, DownloadProgressEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ProgressValue = e.ProgressPercentage;
            SpeedText = $"{e.SpeedMbps:F1} Mbps";
            ProgressText = $"{e.ProgressPercentage:F1}% ({e.BytesDownloaded / 1024 / 1024} MB / {e.TotalBytes / 1024 / 1024} MB)";
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
