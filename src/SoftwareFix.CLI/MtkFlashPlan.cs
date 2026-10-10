using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SoftwareFix.CLI;

/// <summary>
/// Constrói o plano de flash (partição → imagem) a partir de uma ROM extraída com
/// scatter do SP Flash Tool (ex.: MT6768_Android_scatter.txt).
/// Formato do scatter: blocos com "partition_index:" / "partition_name:" /
/// "file_name:" / "is_download:".
/// </summary>
internal static class MtkFlashPlan
{
    public static List<(string Partition, string File)> Build(string dir, string? onlyCsv, bool slotted)
    {
        var scatter = Directory.EnumerateFiles(dir, "*scatter*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f.Length).FirstOrDefault();
        if (scatter == null) return new List<(string, string)>();

        var pairs = ParseScatter(scatter, dir);
        var only = onlyCsv?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<(string, string)>();
        foreach (var (part, file) in pairs)
        {
            // Segurança v1: não regravar tabela de partições nem preloader via DA
            // (preloader fica de fora desta versão; entra com flag explícita no futuro).
            if (part.StartsWith("PGPT", StringComparison.OrdinalIgnoreCase)) continue;
            if (part.Equals("preloader", StringComparison.OrdinalIgnoreCase)) continue;
            if (only != null && only.Length > 0 &&
                !only.Any(o => part.Equals(o, StringComparison.OrdinalIgnoreCase))) continue;

            if (!slotted || part.EndsWith("_a") || part.EndsWith("_b"))
            {
                result.Add((part, file));
            }
            else
            {
                result.Add((part + "_a", file));
                result.Add((part + "_b", file));
            }
        }
        return result;
    }

    static List<(string Part, string File)> ParseScatter(string scatterPath, string dir)
    {
        var result = new List<(string, string)>();
        string? part = null;
        string? file = null;
        string? dl = null;

        void Flush()
        {
            if (part != null && file != null && dl != "false")
            {
                string full = Path.Combine(dir, file);
                if (File.Exists(full)) result.Add((part, full));
            }
            part = null; file = null; dl = null;
        }

        foreach (var raw in File.ReadAllLines(scatterPath))
        {
            var line = raw.Trim();
            // Blocos começam com "- partition_index: SYSn" (com traço) no scatter do SP Flash Tool
            if (line.StartsWith("- partition_index:") || line.StartsWith("partition_index:")) { Flush(); continue; }
            if (line.StartsWith("partition_name:", StringComparison.OrdinalIgnoreCase))
                part = line["partition_name:".Length..].Trim();
            else if (line.StartsWith("file_name:", StringComparison.OrdinalIgnoreCase))
                file = line["file_name:".Length..].Trim();
            else if (line.StartsWith("is_download:", StringComparison.OrdinalIgnoreCase))
                dl = line["is_download:".Length..].Trim();
        }
        Flush();
        return result;
    }
}
