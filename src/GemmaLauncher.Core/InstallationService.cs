using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace GemmaLauncher.Core;

/// <summary>Installs only catalog-pinned artifacts; discovered model originals remain read-only.</summary>
public sealed class InstallationService : IInstallationService, IDisposable
{
    private readonly LauncherPaths paths;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly string[] searchDirectories;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, VerifiedFile> verified = new(StringComparer.OrdinalIgnoreCase);
    private bool cacheLoaded;
    private string CachePath => Path.Combine(paths.Root, "verified-files.json");
    private sealed record VerifiedFile(long Bytes, long LastWriteTicks, string Sha256);
    private sealed record RuntimeReceipt(string PackageSha256, Dictionary<string, string> Files);

    public InstallationService(LauncherPaths paths, HttpClient? httpClient = null, IEnumerable<string>? searchDirectories = null)
    {
        this.paths = paths;
        ownsClient = httpClient is null;
        client = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        this.searchDirectories = (searchDirectories ?? []).Select(Path.GetFullPath)
            .Concat(FindWinGetModelDirectories()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<InstalledModel?> FindAsync(ModelDefinition model, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await FindModelAsync(model, null, cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task<InstalledModel> EnsureModelAsync(ModelDefinition model, IProgress<InstallationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ValidateModel(model);
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var artifact in model.Artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var existing = await FindArtifactAsync(model, artifact, progress, cancellationToken);
                if (existing is not null) { files.Add(artifact.Role, existing); continue; }
                var destination = ArtifactPath(model, artifact);
                await DownloadVerifiedAsync(artifact.Url, artifact.Bytes, artifact.Sha256, destination, progress, cancellationToken);
                files.Add(artifact.Role, destination);
            }
            progress?.Report(new("ready", Localization.T("engine.install.modelReady")));
            return new(model, files["model"], files.GetValueOrDefault("mtp"));
        }
        finally { gate.Release(); }
    }

    public async Task<string> EnsureRuntimeAsync(RuntimePackage runtime, IProgress<InstallationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ValidateSegment(runtime.Version);
            ValidateArtifact(runtime.Url, runtime.Bytes, runtime.Sha256);
            var versionDirectory = ManagedPath(paths.Runtime, runtime.Version + "-" + runtime.Sha256[..12]);
            var executable = ContainedPath(versionDirectory, runtime.ExecutableRelativePath);
            if (await RuntimeIsReadyAsync(versionDirectory, runtime, cancellationToken)) return executable;

            var archivePath = ManagedPath(paths.Runtime, "downloads", runtime.Sha256 + ".zip");
            await DownloadVerifiedAsync(runtime.Url, runtime.Bytes, runtime.Sha256, archivePath, progress, cancellationToken);
            var staging = ManagedPath(paths.Runtime, ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                progress?.Report(new("extracting", Localization.T("engine.install.extracting")));
                var hashes = await ExtractRuntimeAsync(archivePath, staging, cancellationToken);
                ValidateRuntimeFiles(staging, runtime.ExecutableRelativePath);
                await File.WriteAllTextAsync(Path.Combine(staging, "ready.json"), JsonSerializer.Serialize(new RuntimeReceipt(runtime.Sha256, hashes)), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (Directory.Exists(versionDirectory)) Directory.Delete(ManagedPath(versionDirectory), true);
                Directory.Move(staging, versionDirectory);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(ManagedPath(staging), true); }
            progress?.Report(new("ready", Localization.T("engine.install.runtimeReady")));
            return executable;
        }
        finally { gate.Release(); }
    }

    private async Task<InstalledModel?> FindModelAsync(ModelDefinition model, IProgress<InstallationProgress>? progress, CancellationToken token)
    {
        ValidateModel(model);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in model.Artifacts)
        {
            var file = await FindArtifactAsync(model, artifact, progress, token);
            if (file is null) return null;
            files.Add(artifact.Role, file);
        }
        return new(model, files["model"], files.GetValueOrDefault("mtp"));
    }

    private async Task<string?> FindArtifactAsync(ModelDefinition model, ModelArtifact artifact, IProgress<InstallationProgress>? progress, CancellationToken token)
    {
        var candidates = new[] { ArtifactPath(model, artifact) }.Concat(searchDirectories.SelectMany(directory => new[]
        {
            Path.Combine(directory, artifact.Filename), Path.Combine(directory, model.Id, artifact.Filename)
        }));
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (await VerifyAsync(candidate, artifact.Bytes, artifact.Sha256, progress, token)) return candidate;
        }
        return null;
    }

    private string ArtifactPath(ModelDefinition model, ModelArtifact artifact) => ManagedPath(paths.Models, model.Id, artifact.Sha256[..12], artifact.Filename);

    private async Task DownloadVerifiedAsync(string url, long bytes, string sha256, string destination, IProgress<InstallationProgress>? progress, CancellationToken token)
    {
        ValidateArtifact(url, bytes, sha256);
        destination = ManagedPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (await VerifyAsync(destination, bytes, sha256, progress, token)) return;
        var partial = ManagedPath(destination + ".part");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                if (offset >= bytes)
                {
                    if (offset == bytes && await VerifyAsync(partial, bytes, sha256, progress, token))
                    {
                        PublishPartial(partial, destination);
                        return;
                    }
                    File.Delete(partial);
                    offset = 0;
                }
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("GemmaLauncher/1.0");
                request.Headers.AcceptEncoding.ParseAdd("identity");
                if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                headersTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
                if (response.RequestMessage?.RequestUri?.Scheme is string scheme && scheme != Uri.UriSchemeHttps)
                    throw new InvalidDataException(Localization.T("engine.install.insecureConnection"));
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    File.Delete(partial);
                    throw new IOException(Localization.T("engine.install.resumeChanged"));
                }
                response.EnsureSuccessStatusCode();
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (range is null || range.Unit != "bytes" || range.From != offset || range.Length != bytes || range.To is null || range.To >= bytes)
                        throw new InvalidDataException(Localization.T("engine.install.invalidRange"));
                }
                else if (response.StatusCode == HttpStatusCode.OK) offset = 0;
                else throw new InvalidDataException(Localization.T("engine.install.unsupportedResponse"));
                if (response.Content.Headers.ContentEncoding.Any(encoding => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(Localization.T("engine.install.compressedResponse"));
                if (response.Content.Headers.ContentLength is long length && length > bytes - offset)
                    throw new InvalidDataException(Localization.T("engine.install.sizeMismatch"));

                await using (var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                {
                    var buffer = new byte[128 * 1024];
                    long? lastProgressTimestamp = null;
                    while (true)
                    {
                        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        readTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                        var count = await input.ReadAsync(buffer, readTimeout.Token);
                        if (count == 0) break;
                        if (offset + count > bytes) throw new InvalidDataException(Localization.T("engine.install.sizeExceeded"));
                        await output.WriteAsync(buffer.AsMemory(0, count), token);
                        offset += count;
                        var now = Stopwatch.GetTimestamp();
                        if (lastProgressTimestamp is not long previous || offset == bytes || Stopwatch.GetElapsedTime(previous, now) >= TimeSpan.FromMilliseconds(100))
                        {
                            progress?.Report(new("downloading", Localization.T("engine.install.downloading", Path.GetFileName(destination)), offset, bytes));
                            lastProgressTimestamp = now;
                        }
                    }
                    await output.FlushAsync(token);
                }
                if (offset != bytes) throw new IOException(Localization.T("engine.install.interrupted"));
                if (!await VerifyAsync(partial, bytes, sha256, progress, token))
                {
                    File.Delete(partial);
                    throw new InvalidDataException(Localization.T("engine.install.verificationFailed"));
                }
                PublishPartial(partial, destination);
                return;
            }
            catch (OperationCanceledException exception) when (!token.IsCancellationRequested)
            {
                if (attempt == 2) throw new TimeoutException(Localization.T("engine.install.timedOut"), exception);
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token);
            }
            catch (Exception exception) when (attempt < 2 && exception is IOException or HttpRequestException or InvalidDataException)
            { await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token); }
        }
    }

    private void PublishPartial(string partial, string destination)
    {
        File.Move(ManagedPath(partial), ManagedPath(destination), true);
        if (verified.Remove(partial, out var stamp)) verified[destination] = stamp;
        SaveVerificationCache();
    }

    private async Task<bool> VerifyAsync(string path, long bytes, string sha256, IProgress<InstallationProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != bytes) return false;
        LoadVerificationCache();
        if (verified.TryGetValue(path, out var known) && known.Bytes == info.Length && known.LastWriteTicks == info.LastWriteTimeUtc.Ticks && string.Equals(known.Sha256, sha256, StringComparison.OrdinalIgnoreCase)) return true;
        progress?.Report(new("verifying", Localization.T("engine.install.verifying", info.Name)));
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
            info.Refresh();
            if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase) || info.Length != bytes) return false;
            verified[path] = new(bytes, info.LastWriteTimeUtc.Ticks, sha256);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        SaveVerificationCache();
        return true;
    }

    private void LoadVerificationCache()
    {
        if (cacheLoaded) return;
        cacheLoaded = true;
        try
        {
            if (File.Exists(CachePath))
                foreach (var item in JsonSerializer.Deserialize<Dictionary<string, VerifiedFile>>(File.ReadAllText(CachePath)) ?? [])
                    if (item.Value is not null) verified[item.Key] = item.Value;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { /* A corrupt cache only causes another hash check. */ }
    }

    private void SaveVerificationCache()
    {
        var root = ManagedPath(paths.Root);
        var destination = ManagedPath(CachePath);
        var temporary = ManagedPath(CachePath + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(temporary, JsonSerializer.Serialize(verified));
            File.Move(temporary, ManagedPath(destination), true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { /* Persistence is optional; a valid hash remains valid if the cache cannot be saved. */ }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(ManagedPath(temporary)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { /* A leftover cache temporary file does not affect installed artifacts. */ }
        }
    }

    private static async Task<Dictionary<string, string>> ExtractRuntimeAsync(string archivePath, string staging, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        if (archive.Entries.Count > 5000) throw new InvalidDataException(Localization.T("engine.install.tooManyEntries"));
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var destination = ContainedPath(staging, entry.FullName);
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(Localization.T("engine.install.archiveLink"));
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > 2L * 1024 * 1024 * 1024) throw new InvalidDataException(Localization.T("engine.install.archiveTooLarge"));
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(destination); continue; }
            if (entry.FullName.Equals("ready.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Localization.T("engine.install.reservedFilename"));
            if (!hashes.TryAdd(entry.FullName, "")) throw new InvalidDataException(Localization.T("engine.install.duplicateFile"));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var input = entry.Open())
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
                await input.CopyToAsync(output, token);
            if (new FileInfo(destination).Length != entry.Length) throw new InvalidDataException(Localization.T("engine.install.corruptArchive"));
            await using var file = File.OpenRead(destination);
            hashes[entry.FullName] = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
        }
        return hashes;
    }

    private static async Task<bool> RuntimeIsReadyAsync(string directory, RuntimePackage runtime, CancellationToken token)
    {
        if (!Directory.Exists(directory)) return false;
        try
        {
            RejectReparsePoints(directory);
            var receipt = JsonSerializer.Deserialize<RuntimeReceipt>(await File.ReadAllTextAsync(Path.Combine(directory, "ready.json"), token));
            if (receipt is null || !string.Equals(receipt.PackageSha256, runtime.Sha256, StringComparison.OrdinalIgnoreCase) || receipt.Files is null || receipt.Files.Count == 0) return false;
            foreach (var file in receipt.Files)
            {
                var path = ContainedPath(directory, file.Key);
                RejectReparsePoints(path);
                await using var input = File.OpenRead(path);
                if (!Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(file.Value, StringComparison.OrdinalIgnoreCase)) return false;
            }
            ValidateRuntimeFiles(directory, runtime.ExecutableRelativePath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException) { return false; }
    }

    private static void ValidateRuntimeFiles(string directory, string executableRelativePath)
    {
        var executable = ContainedPath(directory, executableRelativePath);
        var executableDirectory = Path.GetDirectoryName(executable)!;
        foreach (var path in new[] { executable }.Concat(new[] { "llama-server-impl.dll", "llama.dll", "llama-common.dll", "ggml.dll", "ggml-base.dll", "ggml-vulkan.dll" }.Select(name => Path.Combine(executableDirectory, name))))
        {
            using var input = File.OpenRead(path);
            if (input.Length < 64 || input.ReadByte() != 'M' || input.ReadByte() != 'Z') throw new InvalidDataException(Localization.T("engine.install.invalidRuntimeFile"));
        }
    }

    private static void ValidateModel(ModelDefinition model)
    {
        ValidateSegment(model.Id);
        if (model.Artifacts.Count(artifact => artifact.Role == "model") != 1 || model.Artifacts.Any(artifact => artifact.Role is not ("model" or "mtp")) || model.Artifacts.Select(artifact => artifact.Role).Distinct().Count() != model.Artifacts.Length)
            throw new ArgumentException(Localization.T("engine.install.invalidModelComposition"));
        foreach (var artifact in model.Artifacts)
        {
            ValidateSegment(artifact.Filename);
            ValidateArtifact(artifact.Url, artifact.Bytes, artifact.Sha256);
        }
    }

    private static void ValidateArtifact(string url, long bytes, string sha256)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException(Localization.T("engine.install.httpsRequired"));
        if (bytes <= 0 || sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character))) throw new ArgumentException(Localization.T("engine.install.verificationRequired"));
    }

    private static void ValidateSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.EndsWith('.') || segment.EndsWith(' ')) throw new ArgumentException(Localization.T("engine.install.invalidFilename"));
    }

    private string ManagedPath(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(parts));
        var root = Path.TrimEndingDirectorySeparator(paths.Root);
        if (!path.Equals(root, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Localization.T("engine.install.pathOutsideData"));
        RejectReparsePoints(path);
        return path;
    }

    private static string ContainedPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException(Localization.T("engine.install.invalidArchivePath"));
        var segments = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) throw new InvalidDataException(Localization.T("engine.install.unsafeArchivePath"));
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Localization.T("engine.install.archivePathEscape"));
        return path;
    }

    private static void RejectReparsePoints(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException(Localization.T("engine.install.symbolicLink"));
    }

    private static IEnumerable<string> FindWinGetModelDirectories()
    {
        var packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        try
        {
            return Directory.Exists(packages) ? Directory.GetDirectories(packages, "ggml.llamacpp_*").Select(directory => Path.Combine(directory, "models")).Where(Directory.Exists).ToArray() : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }
    }

    public void Dispose()
    {
        if (ownsClient) client.Dispose();
        gate.Dispose();
    }
}
