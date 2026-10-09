using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SoftwareFix.Download;

public class DownloadProgressEventArgs : EventArgs
{
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public double ProgressPercentage => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100.0 : 0;
    public double SpeedBytesPerSec { get; set; }
    public double SpeedMbps => (SpeedBytesPerSec * 8) / (1024 * 1024);
}

public class RomDownloader
{
    private readonly HttpClient _http;

    public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    public RomDownloader(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
    }

    public async Task<string> DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        long existingLength = 0;
        if (File.Exists(destinationPath))
        {
            existingLength = new FileInfo(destinationPath).Length;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingLength > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalBytes = (response.Content.Headers.ContentLength ?? 0) + existingLength;

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(destinationPath, existingLength > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);

        byte[] buffer = new byte[81920]; // 80KB buffer
        long totalRead = existingLength;
        int bytesRead;

        var stopwatch = Stopwatch.StartNew();
        long lastBytes = totalRead;
        long lastTime = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            totalRead += bytesRead;

            long elapsed = stopwatch.ElapsedMilliseconds;
            if (elapsed - lastTime >= 500)
            {
                double speed = (double)(totalRead - lastBytes) / ((elapsed - lastTime) / 1000.0);
                lastBytes = totalRead;
                lastTime = elapsed;

                ProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                {
                    BytesDownloaded = totalRead,
                    TotalBytes = totalBytes,
                    SpeedBytesPerSec = speed
                });
            }
        }

        ProgressChanged?.Invoke(this, new DownloadProgressEventArgs
        {
            BytesDownloaded = totalRead,
            TotalBytes = totalBytes,
            SpeedBytesPerSec = 0
        });

        return destinationPath;
    }

    public static async Task<bool> VerifyMd5Async(string filePath, string expectedMd5)
    {
        if (string.IsNullOrEmpty(expectedMd5) || !File.Exists(filePath)) return true;

        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = await md5.ComputeHashAsync(stream);
        string computed = Convert.ToHexString(hash).ToLowerInvariant();
        return computed.Equals(expectedMd5.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }
}
