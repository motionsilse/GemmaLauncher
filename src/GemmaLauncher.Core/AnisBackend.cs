using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GemmaLauncher.Core;

public sealed class AnisAccessRequiredException() : IOException(Localization.T("engine.anis.accessRequired"));
public sealed record AnisRuntimeInstallation(string PythonExecutable, string SourceDirectory);
public sealed record AnisArtifact(string Path, long Bytes, string Hash, bool GitBlob = false);

/// <summary>Obtains the unmodified upstream program and weights directly from their official channels.</summary>
public sealed class AnisBackend : IDisposable
{
    public const string ModelRevision = "c99c0367790cb82e76bb4edc948b5d6353e29454";
    public const string SourceRevision = "1165f0cda25a9afdd0c8e0491038e949d9446e6e";
    public const string ModelPage = "https://huggingface.co/jintigers/anis-trans";
    public const string SourcePage = "https://github.com/jintigers/anis-trans";
    public static RuntimePackage BootstrapPackage { get; } = new()
    {
        Version = "uv-0.13.7", Url = "https://github.com/astral-sh/uv/releases/download/0.13.7/uv-x86_64-pc-windows-msvc.zip",
        Sha256 = "0cdc5b38fe5a8168ed6a25814f5ba43c21d313dd3187314fccb37b7cec46e021", Bytes = 15236317, ExecutableRelativePath = "uv.exe"
    };
    public static IReadOnlyList<AnisArtifact> ModelFiles { get; } = Array.AsReadOnly<AnisArtifact>([
        new("LICENSE-ko.md",7171,"e648a4356644ab14098e5851ee2b850d8ff72e8e",true),
        new("LICENSE-en.md",6637,"8c14c3d364413f38f45b45fc518b9e6a7f40faeb",true),
        new("README.md",6921,"9e7e379fdb6d43ae68dca14dae363f9fd68a532b",true),
        new("release.json",434,"7afdc3b569e3c022595e6e07f540cf7e486fecb4",true),
        new("ct2/config.json",224,"f678488b1c0b4a1eaa7ef31547e7592e383ef06d",true),
        new("ct2/shared_vocabulary.json",354853,"f4ec5d30e7f20c2a993b50b1b88c304a76719e4c",true),
        new("tokenizer.model",275719,"32a11bc0c1c45a477d4a4cdbc0dbc04935063b849e25a7c5d94f1294a1e3a15c"),
        new("ct2/model.bin",300265575,"07212fcad6655f632de0345713981d8e171d8891b71039289125d79c3ae8bcde")
    ]);
    public static IReadOnlyList<AnisArtifact> RuntimeModelFiles { get; } = Array.AsReadOnly(
        ModelFiles.Where(file => file.Path.StartsWith("ct2/", StringComparison.Ordinal) || file.Path == "tokenizer.model").ToArray());
    private const string SourceHash = "ef664aa5d77ff53bb8c0d57c2d4188b474c86ace0f61a6c5d4ea88c1027b19f9";
    private const string PythonVersion = "3.12.12";
    private const string Requirements = "ctranslate2==4.8.5 --hash=sha256:16b1478f9dccd3c6eedb7a1b2e23cb2a41f03976266b71afa1bd9019df7c7014\nsentencepiece==0.2.3 --hash=sha256:972af959c63cde446210a8935524806820c253afa4b0a97d83c206b857085f44\nnumpy==2.4.3 --hash=sha256:65f3c2455188f09678355f5cae1f959a06b778bc66d535da07bf2ef20cd319d5\nPyYAML==6.0.3 --hash=sha256:5fcd34e47f6e0b794d17de1b4ff496c00986e1c83f7ab2fb8fcfe9616ff7477b\n";
    private const string Probe = "import sys,ctranslate2,sentencepiece,numpy,yaml;assert sys.version_info[:3]==(3,12,12);assert ctranslate2.__version__=='4.8.5';assert sentencepiece.__version__=='0.2.3';assert numpy.__version__=='2.4.3';assert yaml.__version__=='6.0.3'";
    private readonly LauncherPaths paths;
    private readonly Func<string?> externalFolder;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, (long Bytes, long WriteTicks, bool Matches)> verified = new(StringComparer.OrdinalIgnoreCase);
    public string ManagedModelDirectory => Path.Combine(paths.Models, "anis-trans", ModelRevision);

    public AnisBackend(LauncherPaths paths, Func<string?>? externalFolder = null, HttpClient? client = null)
    {
        this.paths = paths; this.externalFolder = externalFolder ?? (() => null); ownsClient = client is null;
        this.client = client ?? new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(30), UseCookies = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public async Task<InstalledModel?> FindAsync(ModelDefinition model, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { return await FindModelAsync(model, token); }
        finally { gate.Release(); }
    }
    private async Task<InstalledModel?> FindModelAsync(ModelDefinition model, CancellationToken token)
    {
        var folder = externalFolder();
        foreach (var candidate in string.IsNullOrWhiteSpace(folder) ? new[] { ManagedModelDirectory } : new[] { Path.GetFullPath(folder), Path.Combine(Path.GetFullPath(folder), "model"), ManagedModelDirectory })
        {
            var installed = await FindInFolderAsync(model, candidate, token); if (installed is not null) return installed;
        }
        return null;
    }
    private async Task<InstalledModel?> FindInFolderAsync(ModelDefinition model, string folder, CancellationToken token)
    {
        foreach (var file in RuntimeModelFiles)
            if (!await VerifyAsync(Path.Combine(folder, file.Path), file, token)) return null;
        return new(model, Path.Combine(folder, "ct2", "model.bin"), null);
    }
    public async Task<InstalledModel?> InspectFolderAsync(ModelDefinition model, string folder, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            folder = Path.GetFullPath(folder);
            return await FindInFolderAsync(model, folder, token) ?? await FindInFolderAsync(model, Path.Combine(folder, "model"), token);
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<AnisArtifact>> ImportBrowserDownloadsAsync(string directory, CancellationToken token = default)
    {
        directory = Path.GetFullPath(directory);
        await gate.WaitAsync(token);
        try
        {
            var missing = new List<AnisArtifact>();
            foreach (var file in RuntimeModelFiles)
            {
                token.ThrowIfCancellationRequested();
                var destination = ManagedPath(ManagedModelDirectory, file.Path);
                if (await VerifyAsync(destination, file, token)) continue;

                var name = Path.GetFileName(file.Path);
                var copies = Directory.Exists(directory)
                    ? Directory.EnumerateFiles(directory, Path.GetFileNameWithoutExtension(name) + " (*)" + Path.GetExtension(name))
                    : [];
                var candidates = new[] { Path.Combine(directory, name), Path.Combine(directory, file.Path), Path.Combine(directory, "model", file.Path) }
                    .Concat(copies).Distinct(StringComparer.OrdinalIgnoreCase);
                var imported = false;
                foreach (var candidate in candidates)
                {
                    if (!await VerifyAsync(candidate, file, token)) continue;
                    await CopyDownloadAsync(candidate, destination, file, token);
                    imported = true;
                    break;
                }
                if (!imported) missing.Add(file);
            }
            return missing;
        }
        finally { gate.Release(); }
    }

    private async Task CopyDownloadAsync(string source, string destination, AnisArtifact file, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
            {
                if (input.Length != file.Bytes) throw new InvalidDataException(Localization.T("engine.anis.invalidFile", file.Path));
                await input.CopyToAsync(output, token);
            }
            if (!await VerifyAsync(temporary, file, token)) throw new InvalidDataException(Localization.T("engine.anis.invalidFile", file.Path));
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<InstalledModel> EnsureModelAsync(ModelDefinition model, string? accessToken, IProgress<InstallationProgress>? progress, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var existing = await FindModelAsync(model, token); if (existing is not null) return existing;
            var missing = new List<AnisArtifact>();
            foreach (var file in RuntimeModelFiles)
            {
                var destination = ManagedPath(ManagedModelDirectory, file.Path);
                if (!await VerifyAsync(destination, file, token)) missing.Add(file);
            }
            var total = missing.Sum(file => file.Bytes);
            var completed = 0L;
            foreach (var file in missing)
            {
                var destination = ManagedPath(ManagedModelDirectory, file.Path);
                var url = ModelPage + "/resolve/" + ModelRevision + "/" + file.Path;
                await DownloadAsync(url, destination, file, accessToken, progress, token, completed, total);
                completed += file.Bytes;
            }
            return new(model, Path.Combine(ManagedModelDirectory, "ct2", "model.bin"), null);
        }
        finally { gate.Release(); }
    }
    private async Task<bool> VerifyAsync(string file, AnisArtifact expected, CancellationToken token)
    {
        var info = new FileInfo(file); if (!info.Exists || info.Length != expected.Bytes) return false;
        var key = file + "|" + expected.Hash;
        if (verified.TryGetValue(key, out var cached) && cached.Bytes == info.Length && cached.WriteTicks == info.LastWriteTimeUtc.Ticks)
            return cached.Matches;
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] digest;
        if (expected.GitBlob)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            hash.AppendData(Encoding.ASCII.GetBytes($"blob {expected.Bytes}\0"));
            var buffer = new byte[131072]; int count;
            while ((count = await stream.ReadAsync(buffer, token)) > 0) hash.AppendData(buffer, 0, count);
            digest = hash.GetHashAndReset();
        }
        else digest = await SHA256.HashDataAsync(stream, token);
        var matches = Convert.ToHexString(digest).Equals(expected.Hash, StringComparison.OrdinalIgnoreCase);
        verified[key] = (info.Length, info.LastWriteTimeUtc.Ticks, matches);
        return matches;
    }
    private async Task DownloadAsync(string url, string destination, AnisArtifact expected, string? accessToken,
        IProgress<InstallationProgress>? progress, CancellationToken token, long completedBeforeFile = 0, long? totalBytes = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                accessToken = accessToken.Trim();
                if (!HuggingFaceAuthorization.IsValidAccessToken(accessToken)) throw new AnisAccessRequiredException();
            }
            progress?.Report(new("connecting", Localization.T("engine.install.connecting", expected.Path)));
            using var response = await OpenDownloadAsync(new Uri(url), accessToken, token);
            if (new Uri(url).Host == "huggingface.co" && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new AnisAccessRequiredException();
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != expected.Bytes) throw new InvalidDataException(Localization.T("engine.anis.invalidFile", expected.Path));
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
            {
                var buffer = new byte[131072];
                long completed = 0;
                long? lastProgress = null;
                while (true)
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                    var count = await input.ReadAsync(buffer, readTimeout.Token);
                    if (count == 0) break;
                    completed += count; if (completed > expected.Bytes) throw new InvalidDataException(Localization.T("engine.anis.invalidFile", expected.Path));
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    var now = Stopwatch.GetTimestamp();
                    if (lastProgress is not long previous || completed == expected.Bytes || Stopwatch.GetElapsedTime(previous, now) >= TimeSpan.FromMilliseconds(100))
                    {
                        progress?.Report(new("downloading", Localization.T("engine.install.downloading", expected.Path), completedBeforeFile + completed, totalBytes ?? expected.Bytes));
                        lastProgress = now;
                    }
                }
                await output.FlushAsync(token);
            }
            progress?.Report(new("verifying", Localization.T("engine.install.verifying", expected.Path)));
            if (!await VerifyAsync(temporary, expected, token)) throw new InvalidDataException(Localization.T("engine.anis.invalidFile", expected.Path));
            token.ThrowIfCancellationRequested(); File.Move(temporary, destination, true);
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException(Localization.T("engine.anis.downloadTimedOut", expected.Path), error);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<HttpResponseMessage> OpenDownloadAsync(Uri address, string? accessToken, CancellationToken token)
    {
        for (var hop = 0; hop < 8; hop++)
        {
            if (address.Scheme != Uri.UriSchemeHttps || address.UserInfo.Length != 0)
                throw new InvalidDataException(Localization.T("engine.install.insecureConnection"));
            if (address.IdnHost != "huggingface.co" || address.Port != 443) accessToken = null;
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            if (!string.IsNullOrWhiteSpace(accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is not { } location)
                return response;
            response.Dispose();
            address = new Uri(address, location);
        }
        throw new IOException(Localization.T("engine.anis.accessRequired"));
    }
    public async Task<AnisRuntimeInstallation> EnsureRuntimeAsync(IInstallationService installer, IProgress<InstallationProgress>? progress, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var root = ManagedPath(paths.Runtime, "anis-" + SourceRevision[..12]);
            var archive = ManagedPath(root, "official-source.zip");
            var sourceArtifact = new AnisArtifact("official-source.zip", 71071, SourceHash);
            if (!await VerifyAsync(archive, sourceArtifact, token))
                await DownloadAsync("https://codeload.github.com/jintigers/anis-trans/zip/" + SourceRevision, archive, sourceArtifact, null, progress, token);
            var source = ManagedPath(root, "source");
            await ExtractSourceAsync(archive, source, token);
            var python = ManagedPath(root, "environment/Scripts/python.exe");
            var receipt = ManagedPath(root, "ready.receipt");
            if (File.Exists(python) && File.Exists(receipt) && await File.ReadAllTextAsync(receipt, token) == SourceHash + Requirements)
            {
                try { await RunAsync(python, ["-I", "-c", Probe], root, token); return new(python, Path.Combine(source, "src")); }
                catch (IOException) { File.Delete(receipt); }
            }
            var uv = await installer.EnsureRuntimeAsync(BootstrapPackage, progress, token);
            progress?.Report(new("preparing", Localization.T("engine.anis.runtime")));
            await RunAsync(uv, ["--no-config", "venv", "--python", PythonVersion, "--managed-python", "--allow-existing", Path.GetDirectoryName(Path.GetDirectoryName(python))!], root, token);
            var requirements = ManagedPath(root, "requirements.lock");
            await File.WriteAllTextAsync(requirements, Requirements, token);
            await RunAsync(uv, ["--no-config", "pip", "install", "--python", python, "--index-url", "https://pypi.org/simple", "--only-binary", ":all:", "--no-deps", "--require-hashes", "--reinstall", "-r", requirements], root, token);
            await RunAsync(python, ["-I", "-c", Probe], root, token);
            token.ThrowIfCancellationRequested(); await File.WriteAllTextAsync(receipt, SourceHash + Requirements, token);
            return new(python, Path.Combine(source, "src"));
        }
        finally { gate.Release(); }
    }
    private static async Task ExtractSourceAsync(string archive, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(archive); long total = 0;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Length > 2_000_000 || (total += entry.Length) > 20_000_000 || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException(Localization.T("engine.anis.invalidSource"));
            var prefix = "anis-trans-" + SourceRevision + "/";
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException(Localization.T("engine.anis.invalidSource"));
            var relative = entry.FullName[prefix.Length..]; if (relative.Length == 0) continue;
            var target = ManagedPath(destination, relative);
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open(); await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
            await input.CopyToAsync(output, token);
        }
        if (!File.Exists(Path.Combine(destination, "src", "anis_trans", "server.py")) || !File.Exists(Path.Combine(destination, "LICENSE-ko.md")))
            throw new InvalidDataException(Localization.T("engine.anis.invalidSource"));
    }
    private async Task RunAsync(string executable, IEnumerable<string> arguments, string root, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["UV_PYTHON_INSTALL_DIR"] = ManagedPath(root, "python"); info.Environment["UV_CACHE_DIR"] = ManagedPath(root, "cache");
        info.Environment["UV_NO_PROGRESS"] = "1"; info.Environment["UV_PYTHON_BIN_DIR"] = ManagedPath(root, "bin");
        info.Environment.Remove("PYTHONPATH"); info.Environment.Remove("PYTHONHOME");
        using var job = new WindowsJob(); using var process = new Process { StartInfo = info };
        var output = new StringBuilder(); var outputLock = new object();
        async Task ReadAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
                lock (outputLock) { if (output.Length < 60000) output.AppendLine(line.Length > 2000 ? line[..2000] : line); }
        }
        var started = false; var assigned = false; Task? stdout = null, stderr = null;
        try
        {
            token.ThrowIfCancellationRequested(); started = process.Start();
            if (!started) throw new IOException(Localization.T("engine.session.launchFailed"));
            stdout = ReadAsync(process.StandardOutput); stderr = ReadAsync(process.StandardError); job.Assign(process); assigned = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(15));
            await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stdout, stderr);
            if (process.ExitCode != 0)
            {
                Directory.CreateDirectory(paths.Logs); await File.WriteAllTextAsync(Path.Combine(paths.Logs, "anis-prepare.log"), output.ToString(), token);
                throw new IOException(Localization.T("engine.anis.runtimeFailed", process.ExitCode));
            }
        }
        finally
        {
            if (started && !assigned && !process.HasExited)
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
            }
            job.Dispose();
            if (started && !process.HasExited)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(cleanup.Token);
            }
            if (stdout is not null && stderr is not null) await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    private static string ManagedPath(string root, string relative)
    {
        root = Path.GetFullPath(root); var result = Path.GetFullPath(Path.Combine(root, relative));
        if (!result.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Localization.T("engine.anis.invalidSource"));
        if ((File.Exists(result) || Directory.Exists(result)) && (File.GetAttributes(result) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(Localization.T("engine.anis.invalidSource"));
        for (var parent = new DirectoryInfo(Path.GetDirectoryName(result)!); parent is not null && parent.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase); parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException(Localization.T("engine.anis.invalidSource"));
        return result;
    }
    public void Dispose() { if (ownsClient) client.Dispose(); gate.Dispose(); }
}
