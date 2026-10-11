using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftwareFix.Core;
using SoftwareFix.Device;

namespace SoftwareFix.UI.ViewModels;

/// <summary>ROM pronta para o assistente MTK (pasta extraída com scatter).</summary>
public sealed class MtkRomEntry
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public bool Ready { get; init; }
    public string SizeText { get; init; } = "";
    public string Display => $"{Name} — {SizeText}" + (Ready ? "  [PRONTA]" : "  [precisa extrair]");
}

/// <summary>
/// Assistente MTK (MediaTek) do Software Fix Linux: fluxo guiado em PT-BR para
/// atualizar/resgatar Motos MediaTek (PreLoader + DA assinado), com flash + correção
/// de boot numa única sessão. Mesma receita validada nos comandos mtk-* do CLI.
/// </summary>
public partial class MainViewModel
{
    private readonly MtkClientRunner _mtk = new();
    private DispatcherTimer? _mtkTimer;
    private bool _mtkPrereqOk;

    // ============================ ESTADO DO ASSISTENTE ============================
    [ObservableProperty] private bool _isMtkWizardOpen;
    [ObservableProperty] private string _mtkDeviceStatus = "Verificando USB...";
    [ObservableProperty] private string _mtkDeviceHint = "";
    [ObservableProperty] private string _mtkPrereqText = "";
    [ObservableProperty] private string _mtkStatusText = "";
    [ObservableProperty] private string _mtkResultText = "";
    [ObservableProperty] private string _mtkMiscDiag = "";
    [ObservableProperty] private bool _mtkRunning;
    [ObservableProperty] private bool _mtkCanStart;
    [ObservableProperty] private bool _mtkFixBoot = true;
    [ObservableProperty] private bool _mtkFixBootEnabled = true;
    [ObservableProperty] private int _mtkModeIndex; // 0 = resgate completo (apaga); 1 = manter dados do cliente
    [ObservableProperty] private double _mtkProgress;
    [ObservableProperty] private MtkRomEntry? _mtkSelectedRom;

    public ObservableCollection<MtkRomEntry> MtkRoms { get; } = new();

    partial void OnMtkSelectedRomChanged(MtkRomEntry? value)
    {
        UpdateMtkCanStart();
        UpdateRescuePlanPreview(value);
    }

    // ---- Plano de resgate AUTOMÁTICO (flashall.sh / flashfile.xml / EDL / Unisoc) ----
    [ObservableProperty] private string _mtkPlanPreview = "";
    [ObservableProperty] private bool _mtkHasPlan;

    /// <summary>
    /// Analisa o pacote assim que a ROM é escolhida (ou o modo muda) e monta o
    /// plano de resgate real — mesmo motor dos comandos rescue-plan/rescue-run.
    /// </summary>
    private void UpdateRescuePlanPreview(MtkRomEntry? rom)
    {
        try
        {
            if (rom is not { Ready: true } || string.IsNullOrEmpty(rom.Path))
            {
                MtkPlanPreview = "";
                MtkHasPlan = false;
                return;
            }
            bool keepData = MtkModeIndex == 1;
            var plan = SoftwareFix.Device.Rescue.RescuePlanBuilder.BuildFromRomDir(rom.Path, eraseUserdata: !keepData);
            if (plan == null)
            {
                MtkPlanPreview = "Pacote não reconhecido pelo planejador (sem flashall.sh / flashfile.xml / rawprogram / .pac).";
                MtkHasPlan = true;
                return;
            }
            MtkPlanPreview = plan.RenderPlan();
            MtkHasPlan = true;
            AddLog($"[Resgate] Plano automático: {plan.MethodDisplay} — {plan.Commands.Count} passos ({plan.FlashCount} flashes) — {plan.SourceFile}");
        }
        catch (Exception ex)
        {
            MtkPlanPreview = "Falha ao analisar o pacote: " + ex.Message;
            MtkHasPlan = true;
        }
    }

    partial void OnMtkModeIndexChanged(int value)
    {
        bool keep = value == 1;
        if (keep) MtkFixBoot = false;
        MtkFixBootEnabled = !keep;
        AddLog(keep
            ? "[Assistente MTK] Modo: MANTER DADOS do cliente (userdata preservado; correção de boot desativada)."
            : "[Assistente MTK] Modo: resgate completo (apaga os dados do aparelho).");
        UpdateRescuePlanPreview(MtkSelectedRom);
    }

    // ============================ COMANDOS ============================

    [RelayCommand]
    private void OpenMtkWizard()
    {
        IsMtkWizardOpen = true;
        MtkResultText = "";
        MtkMiscDiag = "";
        MtkProgress = 0;
        MtkStatusText = "Passo 1 — siga as instruções na tela. O assistente detecta o aparelho sozinho.";

        var t = new MtkClientRunner();
        var issues = new List<string>();
        if (!t.BinaryAvailable) issues.Add($"mtkclient ausente ({t.MtkBinary})");
        if (!t.DaAvailable) issues.Add($"DA assinado ausente ({t.DaFile})");
        _mtkPrereqOk = issues.Count == 0;
        MtkPrereqText = _mtkPrereqOk
            ? "Pré-requisitos OK (mtkclient + DA assinado)."
            : "PENDÊNCIA: " + string.Join("; ", issues) + " — veja mtk-status no terminal.";

        RefreshMtkRoms();

        if (_mtkTimer is null)
        {
            _mtkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _mtkTimer.Tick += (_, _) => UpdateMtkDeviceStatus();
        }
        _mtkTimer.Start();
        UpdateMtkDeviceStatus();
        AddLog("[Assistente MTK] Aberto.");
    }

    [RelayCommand]
    private void CloseMtkWizard()
    {
        IsMtkWizardOpen = false;
        _mtkTimer?.Stop();
        AddLog("[Assistente MTK] Fechado.");
    }

    [RelayCommand]
    private void RefreshMtkRoms()
    {
        MtkRoms.Clear();
        string romDir = Configurations.RomPath;
        try
        {
            if (Directory.Exists(romDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(romDir).OrderBy(d => d))
                {
                    try
                    {
                        var dInf = new DirectoryInfo(dir);
                        var files = dInf.EnumerateFiles("*", SearchOption.AllDirectories).ToList();
                        long sz = files.Sum(f => f.Length);
                        bool ready = files.Any(f =>
                            f.Name.Contains("scatter", StringComparison.OrdinalIgnoreCase) &&
                            f.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
                        if (ready)
                        {
                            MtkRoms.Add(new MtkRomEntry
                            {
                                Name = dInf.Name,
                                Path = dir,
                                Ready = true,
                                SizeText = $"{sz / (1024L * 1024 * 1024)} GB",
                            });
                        }
                    }
                    catch { /* pasta inacessível: ignora */ }
                }
            }
            MtkSelectedRom = MtkRoms.FirstOrDefault();
            AddLog($"[Assistente MTK] {MtkRoms.Count} ROM(s) prontas em {romDir}");
        }
        finally
        {
            UpdateMtkCanStart();
        }
    }

    private void UpdateMtkDeviceStatus()
    {
        var devs = MtkClientRunner.GetMtkUsbDevices();
        bool preloader = devs.Any(d => d.Vid == "0e8d");
        bool moto = devs.Any(d => d.Vid == "22b8");
        if (preloader)
        {
            MtkDeviceStatus = "PreLoader DETECTADO — o assistente vai capturar o aparelho.";
            MtkDeviceHint = "Deixe conectado. Se não avançar: desconecte, segure Power 20-30s e reconecte segurando Volume ↓.";
        }
        else if (moto)
        {
            MtkDeviceStatus = "Aparelho Motorola em outro modo (Android/fastboot).";
            MtkDeviceHint = "Para o resgate: desconecte e reconecte SEGURANDO Volume ↓ (modo PreLoader).";
        }
        else
        {
            MtkDeviceStatus = "Nenhum aparelho no USB.";
            MtkDeviceHint = MtkRunning ? "Aguardando o aparelho (veja o passo atual)." : "Conecte seguindo as instruções desta tela.";
        }
    }

    private void UpdateMtkCanStart() =>
        MtkCanStart = !MtkRunning && _mtkPrereqOk && MtkSelectedRom is { Ready: true };

    [RelayCommand]
    private async Task StartMtkRescueAsync()
    {
        if (MtkRunning) return;
        if (!_mtkPrereqOk) { MtkResultText = "Pré-requisitos pendentes — resolva antes de iniciar."; return; }
        var rom = MtkSelectedRom;
        if (rom is null || !rom.Ready) { MtkResultText = "Selecione uma ROM PRONTA (pasta extraída com scatter)."; return; }

        MtkRunning = true;
        MtkCanStart = false;
        MtkResultText = "";
        MtkMiscDiag = "";
        MtkProgress = 0;
        try
        {
            bool keepData = MtkModeIndex == 1;
            var plan = MtkFlashPlan.Build(rom.Path, null, false, keepData);
            if (plan.Count == 0)
            {
                MtkResultText = keepData
                    ? "ROM sem o scatter *_reserve_userdata* (necessário para manter os dados do cliente)."
                    : "ROM sem plano válido (scatter não encontrado ou vazio).";
                AddLog("[Assistente MTK] " + MtkResultText);
                return;
            }
            long totalBytes = 0;
            foreach (var p in plan) totalBytes += new FileInfo(p.File).Length;
            AddLog($"[Assistente MTK] Modo: {(keepData ? "MANTER DADOS do cliente" : "resgate completo")}. Plano: {plan.Count} partições, {totalBytes / (1024L * 1024 * 1024.0):F2} GB — ROM {rom.Name}");
            if (MtkFixBoot && !keepData) AddLog("[Assistente MTK] Correção de boot ATIVA (lê misc + apaga metadata/misc + reinicia).");

            // Passo 1: aguardar a desconexão (se houver algo no USB)
            var t0 = DateTime.Now;
            while (MtkClientRunner.PreloaderPresentNow())
            {
                MtkStatusText = "Passo 1 — DESCONECTE o cabo agora. O assistente arranca sozinho quando o USB liberar...";
                if (DateTime.Now - t0 > TimeSpan.FromMinutes(10))
                {
                    MtkResultText = "Tempo esgotado aguardando a desconexão do cabo.";
                    return;
                }
                await Task.Delay(700);
            }

            // Passo 2: preparar script e iniciar (o mtkclient espera o aparelho aparecer)
            MtkStatusText = "Passo 2 — iniciando o mtkclient. AGORA: segure Power 20-30s e reconecte SEGURANDO Volume ↓.";
            string runDir = Path.Combine(Configurations.MtkLogDir, "ui-mtk-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(runDir);
            string miscPath = Path.Combine(runDir, "misc.img");
            string script = Path.Combine(runDir, "rescue.script");

            var lines = plan.Select(p => $"w {p.Partition} {p.File}").ToList();
            if (MtkFixBoot && !keepData)
            {
                lines.Add($"r misc {miscPath}");
                lines.Add("e metadata");
                lines.Add("e misc");
            }
            lines.Add("reset");
            File.WriteAllLines(script, lines);

            int done = 0;
            int totalSteps = plan.Count + (MtkFixBoot ? 3 : 1);
            void Handler(string line)
            {
                AddLog("[MTK] " + line);
                if (line.Contains("Downloading \""))
                {
                    var m = Regex.Match(line, "Downloading \"([^\"]+)\"");
                    if (m.Success)
                        Dispatcher.UIThread.Post(() =>
                            MtkStatusText = $"Passo 3 — gravando {m.Groups[1].Value} ({Math.Min(done + 1, plan.Count)}/{plan.Count})...");
                }
                if (line.Contains("to partition"))
                {
                    done++;
                    Dispatcher.UIThread.Post(() =>
                    {
                        MtkProgress = Math.Min(95, 5 + 90.0 * done / Math.Max(1, totalSteps));
                        if (done <= plan.Count) MtkStatusText = $"Passo 3 — gravado {done}/{plan.Count} partições...";
                    });
                }
                if (line.Contains("SLA Signature was accepted"))
                    Dispatcher.UIThread.Post(() => MtkStatusText = "Conexão segura aceita (SLA). Gravando...");
                if (line.Contains("Successsfully formatted") || line.Contains("Successfully formatted"))
                    Dispatcher.UIThread.Post(() => MtkStatusText = "Passo 4 — corrigindo estado de boot (metadata/misc)...");
                if (line.Contains("Reset command was sent"))
                    Dispatcher.UIThread.Post(() =>
                    {
                        MtkProgress = 98;
                        MtkStatusText = "Passo 5 — reiniciando o aparelho no sistema...";
                    });
            }

            _mtk.LogReceived += Handler;
            MtkRunResult r;
            try
            {
                r = await _mtk.RunAsync(
                    "script", $"\"{script}\"", "ui-mtk-rescue", TimeSpan.FromSeconds(7200),
                    new Dictionary<string, string> { ["SOFTWARE_FIX_MTK_BOOTMODE"] = "1" });
            }
            finally
            {
                _mtk.LogReceived -= Handler;
            }

            if (r.Success)
            {
                MtkProgress = 100;
                MtkStatusText = "Concluído — o aparelho está reiniciando no sistema.";
                MtkResultText = keepData
                    ? "SUCESSO! Atualização concluída com os DADOS DO CLIENTE PRESERVADOS. Aguarde o boot."
                    : "SUCESSO! Resgate/atualização concluído. Aguarde o boot (a primeira inicialização pode demorar alguns minutos).";
                AddLog("[Assistente MTK] " + MtkResultText);
                if (MtkFixBoot && !keepData && File.Exists(miscPath)) MtkMiscDiag = DiagnoseMiscFile(miscPath);
            }
            else if (r.Wedged)
            {
                MtkStatusText = "Falhou — PreLoader travado no handshake.";
                MtkResultText = "O PreLoader travou: desconecte, segure Power 20-30s, e inicie de novo.";
            }
            else if (r.NeedsReconnect)
            {
                MtkStatusText = "Falhou — o aparelho apareceu cedo demais.";
                MtkResultText = "O aparelho apareceu antes do mtkclient iniciar. Desconecte, desligue (Power 20-30s) e use Iniciar de novo.";
            }
            else
            {
                MtkStatusText = "Falhou.";
                MtkResultText = $"O processo terminou com erro (exit={r.ExitCode}). Veja o console de logs. (log: {r.LogFile})";
            }
        }
        catch (Exception ex)
        {
            MtkResultText = "Erro inesperado: " + ex.Message;
            AddLog("[Assistente MTK] Exceção: " + ex);
        }
        finally
        {
            MtkRunning = false;
            _mtkTimer?.Start();
            UpdateMtkCanStart();
        }
    }

    /// <summary>Diagnóstico do bloco de slot (BCAB) no dump do misc — texto PT-BR.</summary>
    private static string DiagnoseMiscFile(string miscPath)
    {
        try
        {
            byte[] d = File.ReadAllBytes(miscPath);
            int i0 = -1;
            for (int i = 0; i + 24 <= d.Length; i++)
                if (d[i] == 0x42 && d[i + 1] == 0x43 && d[i + 2] == 0x41 && d[i + 3] == 0x42) { i0 = i; break; }
            if (i0 < 0) return "Bloco de slot: vazio/limpo (estado normal após a correção).";
            uint version = BitConverter.ToUInt32(d, i0 + 4);
            string slot = System.Text.Encoding.ASCII.GetString(d, i0 + 8, 4).TrimEnd('\0');
            bool ok = slot is "a" or "b" or "_a" or "_b";
            var sb = new System.Text.StringBuilder();
            sb.Append($"Bloco de slot: versão 0x{version:X8}; sufixo ativo: {(ok ? "'" + slot + "'" : "(ILEGÍVEL — estava corrompido!)")}");
            for (int s = 0; s < 2; s++)
            {
                int b = i0 + 16 + s * 4;
                if (b + 4 > d.Length) break;
                int v = BitConverter.ToInt32(d, b);
                sb.Append($"\nslot {s}: prioridade={v & 0xF} tentativas={(v >> 4) & 0x7} bem-sucedido={(v >> 7) & 1}");
            }
            sb.Append(ok
                ? "\n=> Bloco legível; as limpezas fazem o aparelho voltar ao slot padrão de qualquer forma."
                : "\n=> Bloco CORROMPIDO: era a causa provável do travamento — corrigido pelas limpezas.");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return "Falha ao analisar o misc: " + ex.Message;
        }
    }
}
