using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace SoftwareFix.Flash;

public class FlashCommand
{
    public int Index { get; set; }
    public string Operation { get; set; } = string.Empty; // flash, erase, oem, getvar, reboot
    public string Partition { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public long FileSize { get; set; }
    public int TimeoutMs { get; set; } = 60000;

    public string ToFastbootArgument()
    {
        if (!string.IsNullOrEmpty(FilePath))
        {
            return $"{Operation} {Partition} \"{FilePath}\"".Trim();
        }
        return $"{Operation} {Partition}".Trim();
    }
}

public static class FlashFileParser
{
    public static List<FlashCommand> ParseXml(string xmlFilePath)
    {
        var commands = new List<FlashCommand>();
        if (!File.Exists(xmlFilePath)) return commands;

        string baseDir = Path.GetDirectoryName(xmlFilePath)!;
        var doc = new XmlDocument();
        doc.Load(xmlFilePath);

        var stepNodes = doc.SelectNodes("/flashing/steps/step");
        if (stepNodes == null) return commands;

        int index = 1;
        foreach (XmlNode node in stepNodes)
        {
            string op = node.Attributes?["operation"]?.Value?.Trim() ?? string.Empty;
            string part = node.Attributes?["partition"]?.Value?.Trim() ?? string.Empty;
            string filename = node.Attributes?["filename"]?.Value?.Trim() ?? string.Empty;
            string var = node.Attributes?["var"]?.Value?.Trim() ?? string.Empty;

            string? fullFilePath = null;
            long size = 0;

            if (!string.IsNullOrEmpty(filename))
            {
                fullFilePath = Path.Combine(baseDir, filename);
                if (File.Exists(fullFilePath))
                {
                    size = new FileInfo(fullFilePath).Length;
                }
            }

            int timeout = op.ToLowerInvariant() switch
            {
                "flash" => 300000,
                "flashall" => 600000,
                "erase" => 60000,
                "format" => 60000,
                "oem" => 60000,
                "getvar" => 20000,
                "reboot" => 20000,
                "reboot-bootloader" => 10000,
                "continue" => 10000,
                _ => 120000
            };

            var cmd = new FlashCommand
            {
                Index = index++,
                Operation = op,
                Partition = !string.IsNullOrEmpty(var) ? var : part,
                FilePath = fullFilePath,
                FileSize = size,
                TimeoutMs = timeout
            };

            commands.Add(cmd);
        }

        return commands;
    }
}
