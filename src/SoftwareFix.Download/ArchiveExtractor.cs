using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace SoftwareFix.Download;

public static class ArchiveExtractor
{
    public static async Task ExtractArchiveAsync(string archivePath, string destinationDir, Action<double>? progressCallback = null)
    {
        Directory.CreateDirectory(destinationDir);

        await Task.Run(() =>
        {
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(archivePath);
                int count = archive.Entries.Count;
                int current = 0;

                foreach (var entry in archive.Entries)
                {
                    string target = Path.Combine(destinationDir, entry.FullName);
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(target);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, overwrite: true);
                    }
                    current++;
                    progressCallback?.Invoke((double)current / count * 100.0);
                }
            }
            else if (archivePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            {
                TarFile.ExtractToDirectory(archivePath, destinationDir, overwriteFiles: true);
            }
            else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            {
                using var fileStream = File.OpenRead(archivePath);
                using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzipStream, destinationDir, overwriteFiles: true);
            }
        });
    }
}
