using System.Collections.Generic;
using System.Linq;

namespace SoftwareFix.Device.Rescue;

/// <summary>
/// Tipo de fluxo de resgate — espelha os "planners" do projeto
/// LenovoMotoFirmwareDownloader (fastboot-xml, mediatek-script, edl-firehose,
/// unisoc-pac), traduzidos para o fluxo do Software Fix Linux.
/// </summary>
public enum RescueMethod
{
    /// <summary>flashfile.xml / servicefile.xml — todos os Moto QC/MTK com XML.</summary>
    FastbootXml,

    /// <summary>flashall.sh / flashall.bat — MTK Tinno (lamuc/g17, lagos).</summary>
    FastbootScript,

    /// <summary>rawprogram*.xml + prog_*.mbn — Snapdragon via qdl/EDL (9008).</summary>
    EdlFirehose,

    /// <summary>*.pac — Unisoc via spd-tool.</summary>
    UnisocPac,
}

/// <summary>Um comando do plano de resgate.</summary>
public class RescueCommand
{
    public string Tool { get; init; } = "fastboot";
    public List<string> Args { get; init; } = new();
    public string Label { get; init; } = "";

    /// <summary>Falha tolerada (getvar/chamadas informativas).</summary>
    public bool SoftFail { get; init; }

    /// <summary>flash | erase | oem | getvar | reboot | set_active | other</summary>
    public string Kind { get; init; } = "other";

    public string Render(string? serial = null)
    {
        var parts = new List<string> { Tool };
        if (Tool == "fastboot" && !string.IsNullOrEmpty(serial))
        {
            parts.Add("-s");
            parts.Add(serial);
        }
        parts.AddRange(Args);
        return string.Join(' ', parts);
    }
}

/// <summary>Plano completo de resgate para um pacote de firmware.</summary>
public class RescuePlan
{
    public RescueMethod Method { get; init; }
    public string SourceFile { get; init; } = "";
    public string Codename { get; set; } = "";
    public string SoftwareVersion { get; set; } = "";
    public bool ErasesUserdata { get; init; }
    public List<RescueCommand> Commands { get; } = new();
    public List<string> Warnings { get; } = new();

    public int FlashCount => Commands.Count(c => c.Kind == "flash");
    public int EraseCount => Commands.Count(c => c.Kind == "erase");

    public string MethodDisplay => Method switch
    {
        RescueMethod.FastbootXml => "Fastboot (XML do fabricante)",
        RescueMethod.FastbootScript => "Fastboot (script flashall)",
        RescueMethod.EdlFirehose => "EDL/Firehose — Snapdragon (qdl)",
        RescueMethod.UnisocPac => "SPD/Unisoc (arquivo PAC)",
        _ => Method.ToString(),
    };

    /// <summary>Pré-visualização do plano (dry-run) em texto.</summary>
    public string RenderPlan(string? serial = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"  Método ..........: {MethodDisplay}");
        sb.AppendLine($"  Fonte ...........: {SourceFile}");
        if (!string.IsNullOrEmpty(Codename)) sb.AppendLine($"  Codename ........: {Codename}");
        if (!string.IsNullOrEmpty(SoftwareVersion)) sb.AppendLine($"  Versão ..........: {SoftwareVersion}");
        sb.AppendLine($"  Apaga dados? ....: {(ErasesUserdata ? "SIM (flashfile / -eu)" : "não (mantém dados do cliente)")}");
        sb.AppendLine($"  Comandos ........: {Commands.Count} ({FlashCount} flashes, {EraseCount} erases)");
        if (Warnings.Count > 0)
            foreach (var w in Warnings) sb.AppendLine($"  ⚠ {w}");
        sb.AppendLine();
        int i = 1;
        foreach (var c in Commands)
        {
            var mark = c.SoftFail ? "~" : " ";
            sb.AppendLine($"  {mark}{i++,3}. {c.Render(serial)}");
        }
        return sb.ToString();
    }
}
