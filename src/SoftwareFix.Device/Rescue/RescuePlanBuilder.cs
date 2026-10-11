using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SoftwareFix.Device.Rescue;

/// <summary>
/// Monta o plano de resgate a partir de um pacote de firmware Motorola extraído.
///
/// Porte fiel dos planners do LenovoMotoFirmwareDownloader para C#, mantendo a
/// mesma ordem de preferência e as mesmas regras:
///   fastboot-xml (flashfile/servicefile) — QC e MTK com XML;
///   mediatek-script (flashall.sh/bat)    — MTK Tinno (lamuc/G17, lagos);
///   edl-firehose (rawprogram + prog)     — Snapdragon via qdl;
///   unisoc-pac (.pac)                    — Unisoc via spd-tool.
///
/// Modo "manter dados" (dataReset = no): usa servicefile.xml quando existir
/// (e nunca gera erase de userdata/metadata); modo normal usa flashfile.xml.
/// </summary>
public static class RescuePlanBuilder
{
    // ---------------------------------------------------------------- entry

    public static RescuePlan? BuildFromRomDir(string romDir, bool eraseUserdata)
    {
        if (!Directory.Exists(romDir)) return null;
        var files = Directory.GetFiles(romDir, "*", SearchOption.AllDirectories);

        // 1) Scripts de flash (MTK Tinno — flashall.sh é o fluxo oficial do G17/G85)
        var script = FindScript(files);
        if (script != null) return ParseFlashallScript(script, eraseUserdata);

        // 2) XML do fabricante (flashfile.xml / servicefile.xml)
        var xml = PickFastbootXml(files, eraseUserdata);
        if (xml != null) return ParseFastbootXml(xml, eraseUserdata);

        // 3) EDL/Firehose (Snapdragon sem fastboot)
        var edl = BuildEdlPlan(files, romDir);
        if (edl != null) return edl;

        // 4) Unisoc PAC
        var pac = files.FirstOrDefault(f => f.ToLowerInvariant().EndsWith(".pac"));
        if (pac != null) return BuildUnisocPlan(pac, romDir);

        return null;
    }

    /// <summary>Nomes conhecidos dos scripts MTK (espelho de KNOWN_MEDIATEK_SCRIPT_NAMES).</summary>
    private static readonly string[] ScriptPriority = { "flashall.sh", "flashall.bat", "cfc_flash.bat", "fastboot_all.sh" };

    private static string? FindScript(string[] files)
    {
        foreach (var name in ScriptPriority)
        {
            var hit = files.FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>Escolhe o XML (rank: modo normal → flashfile; manter dados → servicefile).</summary>
    private static string? PickFastbootXml(string[] files, bool eraseUserdata)
    {
        string? find(string name) => files.FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));

        if (eraseUserdata)
        {
            return find("flashfile.xml") ?? find("servicefile.xml") ?? find("softwareupgrade.xml");
        }
        return find("servicefile.xml") ?? find("flashfile.xml") ?? find("softwareupgrade.xml");
    }

    // ---------------------------------------------------------------- fastboot XML

    private static readonly Regex StepRegex = new(@"<step\b([^>]*)/?>", RegexOptions.IgnoreCase);
    private static readonly Regex AttrRegex = new(@"([A-Za-z_][\w.:-]*)\s*=\s*(""([^""]*)""|'([^']*)')", RegexOptions.Compiled);

    private static Dictionary<string, string> ParseAttributes(string attrSource)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttrRegex.Matches(attrSource))
        {
            var key = m.Groups[1].Value.Trim().ToLowerInvariant();
            var value = (m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value).Trim();
            if (key.Length > 0) attrs[key] = value;
        }
        return attrs;
    }

    /// <summary>Partições que nunca devem ser apagadas no modo "manter dados".</summary>
    private static readonly HashSet<string> DataPartitions = new(StringComparer.OrdinalIgnoreCase)
    {
        "userdata", "metadata", "debug_token", "carrier",
    };

    private static RescuePlan ParseFastbootXml(string xmlPath, bool eraseUserdata)
    {
        var text = File.ReadAllText(xmlPath);
        var plan = new RescuePlan
        {
            Method = RescueMethod.FastbootXml,
            SourceFile = Path.GetFileName(xmlPath),
            ErasesUserdata = eraseUserdata,
        };

        // header: <phone_model model="..."/> / <software_version version="..."/>
        var modelMatch = Regex.Match(text, @"<phone_model[^>]*model\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
        if (modelMatch.Success) plan.Codename = modelMatch.Groups[1].Value;
        var verMatch = Regex.Match(text, @"<software_version[^>]*version\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
        if (verMatch.Success) plan.SoftwareVersion = verMatch.Groups[1].Value;

        int skippedErases = 0;
        foreach (Match step in StepRegex.Matches(text))
        {
            var attrs = ParseAttributes(step.Groups[1].Value);
            var op = attrs.GetValueOrDefault("operation", "").ToLowerInvariant();
            if (op.Length == 0) continue;

            switch (op)
            {
                case "flash":
                {
                    var partition = attrs.GetValueOrDefault("partition", "");
                    var filename = attrs.GetValueOrDefault("filename", "");
                    if (partition.Length == 0 || filename.Length == 0) continue;
                    plan.Commands.Add(new RescueCommand
                    {
                        Kind = "flash",
                        Args = { "flash", partition, filename },
                        Label = $"Flash {partition} ← {filename}",
                    });
                    break;
                }
                case "erase":
                {
                    var partition = attrs.GetValueOrDefault("partition", "");
                    if (partition.Length == 0) continue;
                    if (!eraseUserdata && DataPartitions.Contains(partition)) { skippedErases++; continue; }
                    plan.Commands.Add(new RescueCommand
                    {
                        Kind = "erase",
                        Args = { "erase", partition },
                        Label = $"Apagar {partition}",
                    });
                    break;
                }
                case "oem":
                {
                    var cmd = attrs.GetValueOrDefault("var", attrs.GetValueOrDefault("command", ""));
                    if (cmd.Length == 0) continue;
                    if (!eraseUserdata && cmd.StartsWith("config unset", StringComparison.OrdinalIgnoreCase)) continue;
                    var args = new List<string> { "oem", cmd };
                    plan.Commands.Add(new RescueCommand { Kind = "oem", Args = args, Label = $"oem {cmd}", SoftFail = true });
                    break;
                }
                case "getvar":
                {
                    var v = attrs.GetValueOrDefault("var", "");
                    if (v.Length == 0) continue;
                    plan.Commands.Add(new RescueCommand { Kind = "getvar", Args = { "getvar", v }, Label = $"Checar {v}", SoftFail = true });
                    break;
                }
                case "reboot":
                case "reboot-bootloader":
                case "continue":
                case "set_active":
                case "set-active":
                case "boot":
                case "format":
                {
                    var args = new List<string> { op.Replace("set-active", "set_active") };
                    var extra = attrs.GetValueOrDefault("partition", attrs.GetValueOrDefault("var", ""));
                    if (extra.Length > 0) args.Add(extra);
                    plan.Commands.Add(new RescueCommand { Kind = op.StartsWith("reboot") ? "reboot" : "other", Args = args, Label = string.Join(' ', args) });
                    break;
                }
            }
        }

        // Reboot final (o XML não traz; o fluxo oficial reinicia após o clear)
        if (!plan.Commands.Any(c => c.Kind == "reboot"))
            plan.Commands.Add(new RescueCommand { Kind = "reboot", Args = { "reboot" }, Label = "Reiniciar aparelho" });

        if (!eraseUserdata && skippedErases > 0)
            plan.Warnings.Add($"{skippedErases} apagamento(s) de dados ignorado(s) — modo manter dados.");

        return plan;
    }

    // ---------------------------------------------------------------- flashall.sh / .bat

    private static readonly Regex VarRegex = new(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*""?([^""#\r\n]*)""?\s*$", RegexOptions.Compiled);
    private static readonly Regex FlashImageRegex = new(@"^\s*flash_image\s+(\S+)\s+(\S+)", RegexOptions.Compiled);
    private static readonly Regex FastbootLineRegex = new(@"^\s*(\$FASTBOOT|%FASTBOOT%|\""?%FASTBOOT%\""?|fastboot|\.\/fastboot)\s+(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Porte do mediatek-script-planner: lê o flashall.sh (script Tinno) e monta a
    /// sequência fastboot real. Também aceita flashall.bat (mesma estrutura).
    /// </summary>
    private static RescuePlan ParseFlashallScript(string scriptPath, bool eraseUserdata)
    {
        bool isBat = scriptPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        var plan = new RescuePlan
        {
            Method = RescueMethod.FastbootScript,
            SourceFile = Path.GetFileName(scriptPath),
            ErasesUserdata = eraseUserdata,
        };

        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(scriptPath);

        RescueCommand? lastCommand = null;
        int skippedErases = 0;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("::")) continue;

            // variáveis (FASTBOOT=fastboot / RAWIMAGE=vbmeta.img / PRODUCT_NAME="null")
            var vmatch = VarRegex.Match(line);
            if (vmatch.Success)
            {
                var name = vmatch.Groups[1].Value;
                var value = vmatch.Groups[2].Value.Trim().Trim('"');
                // valores dinâmicos ($(...) command substitution) não são resolvíveis — ignore
                if (value.Length > 0 && !value.StartsWith("$(")) vars[name] = value;
                continue;
            }

            // comparação de codename: PRODUCT_NAME == "lamuc"
            var prodCmp = Regex.Match(line, @"PRODUCT_NAME""?\s*==\s*""([^""]+)""");
            if (prodCmp.Success) { vars["__PRODUCT_MATCH"] = prodCmp.Groups[1].Value; continue; }

            // flash_image <part> <img> (função interna do script)
            var fi = FlashImageRegex.Match(line);
            if (fi.Success)
            {
                var part = fi.Groups[1].Value;
                var img = fi.Groups[2].Value;
                // o script tem variantes condicionais (vendor_boot-debug.img) — ficamos com a normal
                if (img.Contains("-debug.img", StringComparison.OrdinalIgnoreCase)) continue;
                lastCommand = new RescueCommand { Kind = "flash", Args = { "flash", part, img }, Label = $"Flash {part} ← {img}" };
                plan.Commands.Add(lastCommand);
                continue;
            }

            // linha $FASTBOOT ... (com -s "$SERIAL_NUMBER" etc.)
            var fb = FastbootLineRegex.Match(line);
            if (fb.Success)
            {
                // pula a linha-modelo da função interna (flash "$part" "$img") e expansões
                if (line.Contains("$part") || line.Contains("$img")) continue;

                var rest = fb.Groups[2].Value.Trim();
                var args = TokenizeScriptArgs(rest, vars);
                if (args.Count == 0) continue;

                // remove pares "-s <valor dinâmico>" e lixo de expansão de shell
                for (int i = args.Count - 2; i >= 0; i--)
                {
                    if (args[i] == "-s" &&
                        (args[i + 1].Contains("$(") || args[i + 1].Contains("awk") || args[i + 1].Contains("devices") || args[i + 1].Contains("SERIAL")))
                    {
                        args.RemoveRange(i, 2);
                    }
                }
                args.RemoveAll(a => a.Contains("$(") || a.Contains("awk") || a.Contains("SERIAL_NUMBER"));
                while (args.Count > 0 && args[0] == "-s") args.RemoveAt(0);

                if (args.Count == 0) continue;
                var head = args[0].ToLowerInvariant();

                // filtro de apagamento no modo manter-dados
                if (head == "erase" && args.Count >= 2 && !eraseUserdata && DataPartitions.Contains(args[1])) { skippedErases++; continue; }

                // checks de preparação ficam como soft (getvar efuse/product/security_patch)
                var cmd = new RescueCommand
                {
                    Kind = head switch
                    {
                        "flash" => "flash",
                        "erase" => "erase",
                        "oem" => "oem",
                        "getvar" => "getvar",
                        "reboot" => "reboot",
                        "set_active" => "set_active",
                        _ => "other",
                    },
                    Args = args,
                    Label = string.Join(' ', args),
                    SoftFail = head is "getvar" or "oem",
                };
                plan.Commands.Add(cmd);
                continue;
            }
        }

        // Codename: os scripts Tinno comparam PRODUCT_NAME com "lamuc"/"lamu" etc.
        var matchVar = vars.GetValueOrDefault("__PRODUCT_MATCH", "");
        if (matchVar.Length > 0) plan.Codename = matchVar;

        // Garantir reboot final (o script tem NEED_REBOOT default=1)
        if (!plan.Commands.Any(c => c.Kind == "reboot"))
            plan.Commands.Add(new RescueCommand { Kind = "reboot", Args = { "reboot" }, Label = "Reiniciar aparelho" });

        if (!eraseUserdata && skippedErases > 0)
            plan.Warnings.Add($"{skippedErases} apagamento(s) de dados ignorado(s) — modo manter dados.");

        if (plan.Commands.Count(c => c.Kind == "flash") == 0)
            plan.Warnings.Add("Nenhum comando flash_image encontrado no script — verifique o pacote.");

        return plan;
    }

    private static RescuePlan CloneWithCodename(RescuePlan src, string codename)
    {
        var p = new RescuePlan
        {
            Method = src.Method,
            SourceFile = src.SourceFile,
            ErasesUserdata = src.ErasesUserdata,
            Codename = codename,
            SoftwareVersion = src.SoftwareVersion,
        };
        p.Commands.AddRange(src.Commands);
        p.Warnings.AddRange(src.Warnings);
        return p;
    }

    private static List<string> TokenizeScriptArgs(string raw, Dictionary<string, string> vars)
    {
        var args = new List<string>();
        var expanded = raw;

        // expande ${VAR} e $VAR (e %VAR% nos .bat) com o que conhecemos
        expanded = Regex.Replace(expanded, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", m => vars.GetValueOrDefault(m.Groups[1].Value, m.Value));
        expanded = Regex.Replace(expanded, @"\$([A-Za-z_][A-Za-z0-9_]*)", m => vars.GetValueOrDefault(m.Groups[1].Value, m.Value));
        expanded = Regex.Replace(expanded, @"%([A-Za-z_][A-Za-z0-9_]*)%", m => vars.GetValueOrDefault(m.Groups[1].Value, m.Value));

        foreach (Match m in Regex.Matches(expanded, @"""([^""]*)""|(\S+)"))
        {
            var tok = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            tok = tok.Trim();
            if (tok.Length == 0) continue;
            if (tok.StartsWith("2>") || tok.StartsWith("|") || tok.StartsWith("&&") || tok.StartsWith(";")) continue;
            if (tok is "||" or "true" or "false" or "exit" or "fi" or "then" or "else" or "if" or "do" or "done" or "shift") continue;
            if (tok.Contains("$(") || tok.Contains("awk")) continue;
            args.Add(tok);
        }
        return args;
    }

    // ---------------------------------------------------------------- EDL / Firehose (Snapdragon)

    private static readonly Regex RawProgramRegex = new(@"rawprogram[^/\\]*\.xml$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static RescuePlan? BuildEdlPlan(string[] files, string romDir)
    {
        // programmer: prog_firehose_ddr*.mbn / *firehose*.mbn/.elf (rank: ddr > nome)
        var programmers = files
            .Where(f => Regex.IsMatch(f, @"(prog[_\-].*|.*firehose.*)\.(mbn|elf|bin)$", RegexOptions.IgnoreCase))
            .OrderByDescending(f => f.ToLowerInvariant().Contains("firehose") ? 2 : 0)
            .ThenByDescending(f => f.ToLowerInvariant().Split('\\', '/').Last().Length)
            .ToArray();
        if (programmers.Length == 0) return null;

        var rawprograms = files.Where(f => RawProgramRegex.IsMatch(f)).ToArray();
        if (rawprograms.Length == 0) return null;

        int RankRaw(string path)
        {
            var n = Path.GetFileName(path).ToLowerInvariant();
            int s = 0;
            if (n.Contains("unsparse_clean_carrier")) s += 60;
            else if (n.Contains("unsparse")) s += 55;
            else if (n.Contains("clean_carrier")) s += 50;
            else if (n.StartsWith("rawprogram")) s += 40;
            if (n.Contains("loadinfo")) s -= 60;
            if (n.EndsWith(".xml")) s += 10;
            return s;
        }

        var raw = rawprograms.OrderByDescending(RankRaw).ThenBy(f => Path.GetFileName(f).Length).First();
        var patch = files.FirstOrDefault(f => Regex.IsMatch(Path.GetFileName(f), @"^patch.*\.xml$", RegexOptions.IgnoreCase));

        var plan = new RescuePlan
        {
            Method = RescueMethod.EdlFirehose,
            SourceFile = Path.GetFileName(programmers[0]),
            ErasesUserdata = true,
        };

        // storage hint: UFS quando o programmer/rawprogram indicam
        var storage = (Path.GetFileName(raw) + Path.GetFileName(programmers[0])).ToLowerInvariant().Contains("ufs") ? "ufs" : "emmc";

        var args = new List<string> { "--storage", storage };
        // include dirs (relativos) de todas as pastas relevantes
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddDir(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var d = Path.GetDirectoryName(Path.GetRelativePath(romDir, path));
            if (!string.IsNullOrEmpty(d) && d != ".") dirs.Add(d.Replace('\\', '/'));
        }
        AddDir(programmers[0]); AddDir(raw); AddDir(patch);
        var rawText = File.ReadAllText(raw);
        foreach (Match m in Regex.Matches(rawText, @"filename\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase))
        {
            var img = m.Groups[1].Value.Replace('\\', '/').Split('/').Last();
            var found = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), img, StringComparison.OrdinalIgnoreCase));
            AddDir(found);
        }
        foreach (var d in dirs) { args.Add("--include"); args.Add(d); }

        args.Add(Path.GetRelativePath(romDir, programmers[0]).Replace('\\', '/'));
        args.Add(Path.GetRelativePath(romDir, raw).Replace('\\', '/'));
        if (patch != null) args.Add(Path.GetRelativePath(romDir, patch).Replace('\\', '/'));

        plan.Commands.Add(new RescueCommand
        {
            Tool = "qdl",
            Kind = "other",
            Args = args,
            Label = $"qdl ({storage}) {Path.GetFileName(programmers[0])} {Path.GetFileName(raw)}",
            SoftFail = false,
        });
        plan.Warnings.Add("EDL exige o aparelho em modo 9008 (lsusb 05c6:9008) e a ferramenta 'qdl' instalada (linux-msm/qdl).");
        return plan;
    }

    // ---------------------------------------------------------------- Unisoc

    private static RescuePlan BuildUnisocPlan(string pacPath, string romDir)
    {
        var plan = new RescuePlan
        {
            Method = RescueMethod.UnisocPac,
            SourceFile = Path.GetFileName(pacPath),
            ErasesUserdata = false,
        };
        plan.Commands.Add(new RescueCommand
        {
            Tool = "spd-tool",
            Kind = "other",
            Args = { "flush", Path.GetRelativePath(romDir, pacPath).Replace('\\', '/') },
            Label = "spd-tool flush (Unisoc)",
        });
        plan.Warnings.Add("Unisoc requer 'spd-tool' no PATH (github:enigma550/spd-tool-bun) e o aparelho em modo BROM/SPD.");
        return plan;
    }
}
