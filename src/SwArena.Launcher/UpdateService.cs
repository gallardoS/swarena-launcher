using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace SwArena.Launcher;

public sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(12);
    private const int ManifestAttempts = 2;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(20) };
    private readonly string _clientRoot;
    private readonly Uri _manifestUri;

    public UpdateService(string clientRoot, string manifestUrl)
    {
        _clientRoot = Path.GetFullPath(clientRoot);
        _manifestUri = new Uri(manifestUrl, UriKind.Absolute);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("swArena-Launcher/1.0");
    }

    public async Task<UpdateManifest> UpdateAsync(IProgress<UpdateProgress> progress, CancellationToken cancellationToken)
    {
        var manifest = await DownloadManifestAsync(progress, cancellationToken);

        if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Files is null)
            throw new InvalidDataException("The manifest has an invalid format.");

        var pending = new List<ManifestFile>();
        long downloadBytes = 0;
        for (var i = 0; i < manifest.Files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = manifest.Files[i];
            ValidateManifestFile(file);
            progress.Report(new("Verifying client…", file.Path, manifest.Files.Count == 0 ? 100 : i * 100d / manifest.Files.Count));
            var destination = ResolveSafePath(file.Path);
            if (!await MatchesAsync(destination, file.Size, file.Sha256, cancellationToken))
            {
                pending.Add(file);
                downloadBytes += file.Size;
            }
        }

        long completedBytes = 0;
        foreach (var file in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = ResolveSafePath(file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".swarena-download";
            try
            {
                using var response = await _http.GetAsync(new Uri(_manifestUri, file.Url), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true))
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(1024 * 128);
                    try
                    {
                        int read;
                        long currentFileBytes = 0;
                        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            currentFileBytes += read;
                            var pct = downloadBytes == 0 ? 100 : (completedBytes + currentFileBytes) * 100d / downloadBytes;
                            progress.Report(new("Downloading update…", $"{file.Path}  •  {FormatBytes(completedBytes + currentFileBytes)} / {FormatBytes(downloadBytes)}", pct));
                        }
                    }
                    finally { ArrayPool<byte>.Shared.Return(buffer); }
                }

                if (!await MatchesAsync(temporary, file.Size, file.Sha256, cancellationToken))
                    throw new InvalidDataException($"The downloaded file '{file.Path}' does not match its SHA-256 signature.");

                File.Move(temporary, destination, true);
                completedBytes += file.Size;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        foreach (var relativePath in manifest.Delete ?? Array.Empty<string>())
        {
            var target = ResolveSafePath(relativePath);
            if (File.Exists(target)) File.Delete(target);
        }

        var stateDirectory = Path.Combine(_clientRoot, ".swarena");
        Directory.CreateDirectory(stateDirectory);
        await File.WriteAllTextAsync(Path.Combine(stateDirectory, "version.txt"), manifest.Version, cancellationToken);
        progress.Report(new("Client updated", pending.Count == 0 ? "No pending files" : $"Version {manifest.Version} installed", 100));
        return manifest;
    }

    private async Task<UpdateManifest> DownloadManifestAsync(
        IProgress<UpdateProgress> progress,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= ManifestAttempts; attempt++)
        {
            progress.Report(new(
                "Checking for updates…",
                attempt == 1 ? "Downloading manifest" : $"Retrying manifest ({attempt}/{ManifestAttempts})",
                0));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ManifestTimeout);

            try
            {
                using var response = await _http.GetAsync(
                    _manifestUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentLength is > 2 * 1024 * 1024)
                    throw new InvalidDataException("The update manifest is unexpectedly large.");

                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                return await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, cancellationToken: timeout.Token)
                    ?? throw new InvalidDataException("The update manifest is empty.");
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
            }
            catch (HttpRequestException ex) when (attempt < ManifestAttempts)
            {
                lastError = ex;
            }

            if (attempt < ManifestAttempts)
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new TimeoutException(
            "The update server did not finish sending the manifest. Check your connection and try again.",
            lastError);
    }

    private string ResolveSafePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"Path not allowed in the manifest: '{relativePath}'.");

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_clientRoot, normalized));
        var rootPrefix = _clientRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path outside the client directory: '{relativePath}'.");
        return fullPath;
    }

    private static void ValidateManifestFile(ManifestFile file)
    {
        if (file.Size < 0 || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException($"Invalid metadata for '{file.Path}'.");
        if (!Uri.TryCreate(file.Url, UriKind.RelativeOrAbsolute, out _))
            throw new InvalidDataException($"Invalid URL for '{file.Path}'.");
    }

    private static async Task<bool> MatchesAsync(string path, long expectedSize, string expectedHash, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expectedSize) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatBytes(long value) => value switch
    {
        >= 1_073_741_824 => $"{value / 1_073_741_824d:0.0} GB",
        >= 1_048_576 => $"{value / 1_048_576d:0.0} MB",
        >= 1024 => $"{value / 1024d:0.0} KB",
        _ => $"{value} B"
    };

    public void Dispose() => _http.Dispose();
}
