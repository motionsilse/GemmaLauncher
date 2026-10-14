using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using GemmaLauncher.Core;

namespace GemmaLauncher.Tests;

public static class InstallationTests
{
    public static async Task RunAsync()
    {
        await MatchesRenamedModelFileAgainstPinnedMainArtifacts();
        await RejectsMissingOrUnmatchedModelFiles();
        await ReusesMappedOriginalAndColocatedMtpWithoutDownloading();
        await DownloadsOnlyMissingMtpForMappedMain();
        await RejectsUnverifiedMappedOriginalAndColocatedMtp();
        await CancellingModelFileMatchLeavesOriginalUntouched();
        await ReusesVerifiedOriginalWithoutDownloading();
        await ReusesOriginalWhenVerificationCacheCannotBeSaved();
        await PublishesCompletedPartialWhenVerificationCacheCannotBeSaved();
        await RehashesOriginalWithMalformedVerificationStamp();
        await ResumesPartialAndIgnoresServerRangeResetSafely();
        await RestartsAfterRangeNotSatisfiable();
        await RejectsWrongRangeAndCorruptDownload();
        await CancellationKeepsPartialUnpublished();
        await DownloadTimeoutIsNotUserCancellation();
        await ThrottlesDownloadProgressAndPreservesCompletion();
        await InstallsAndRevalidatesRuntime();
        await RejectsArchiveTraversalAndSymlinks();
        await RejectsUntrustedCatalogPathsAndHttp();
    }

    private static async Task MatchesRenamedModelFileAgainstPinnedMainArtifacts()
    {
        using var fixture = new Fixture();
        byte[] bytes = [1, 2, 3, 4];
        var model = Model(bytes);
        model = model with { Artifacts = [model.Artifacts[0], new() { Role = "mtp", Filename = "draft.gguf", Url = "https://example.test/draft.gguf", Bytes = 2, Sha256 = Hash([5, 6]) }] };
        var original = Path.Combine(fixture.Root, "my renamed model.GGUF");
        await File.WriteAllBytesAsync(original, bytes);
        using var client = Client((_, _) => throw new Exception("Matching a local file must not download."));
        using var service = new InstallationService(fixture.Paths, client);
        ModelDefinition[] candidates = [Model([1, 2, 3]) with { Id = "different-size" }, Model([4, 3, 2, 1]) with { Id = "different-hash" }, model];
        var reports = new List<InstallationProgress>();
        Equal(model, await service.MatchModelFileAsync(original, candidates, new SynchronousProgress(reports.Add)), "Matching must compare the main artifact's bytes and SHA256, independent of filename or missing MTP.");
        True(reports.Any(report => report.Stage == "verifying"), "Matching must report verification progress.");
        reports.Clear();
        Equal(model, await service.MatchModelFileAsync(original, [model], new SynchronousProgress(reports.Add)), "A previously verified selection must remain reusable.");
        Equal(0, reports.Count, "Matching must share the existing verification cache.");
        True((await File.ReadAllBytesAsync(original)).SequenceEqual(bytes), "Matching must preserve original bytes.");
        True(!Directory.Exists(fixture.Paths.Models), "Matching must not copy the model into managed storage.");
    }

    private static async Task RejectsMissingOrUnmatchedModelFiles()
    {
        using var fixture = new Fixture();
        var model = Model([1, 2, 3, 4]);
        using var client = Client((_, _) => throw new Exception("Unmatched local files must not trigger network access."));
        using var service = new InstallationService(fixture.Paths, client);
        Equal<ModelDefinition?>(null, await service.MatchModelFileAsync(Path.Combine(fixture.Root, "missing.gguf"), [model]), "A missing file must not match.");
        foreach (var (filename, bytes) in new (string, byte[])[] { ("model.json", [1, 2, 3, 4]), ("wrong-size.gguf", [1, 2, 3]), ("wrong-hash.gguf", [4, 3, 2, 1]) })
        {
            var file = Path.Combine(fixture.Root, filename);
            await File.WriteAllBytesAsync(file, bytes);
            Equal<ModelDefinition?>(null, await service.MatchModelFileAsync(file, [model]), "Only a GGUF matching a pinned main artifact may be selected.");
            True((await File.ReadAllBytesAsync(file)).SequenceEqual(bytes), "Rejected files must remain untouched.");
        }
    }

    private static async Task ReusesMappedOriginalAndColocatedMtpWithoutDownloading()
    {
        using var fixture = new Fixture();
        byte[] bytes = [1, 2, 3, 4];
        byte[] mtpBytes = [5, 6];
        var model = Model(bytes);
        var mtpArtifact = new ModelArtifact { Role = "mtp", Filename = "draft.gguf", Url = "https://example.test/draft.gguf", Bytes = mtpBytes.Length, Sha256 = Hash(mtpBytes) };
        model = model with { Artifacts = [model.Artifacts[0], mtpArtifact] };
        var originalDirectory = Path.Combine(fixture.Root, "originals");
        Directory.CreateDirectory(originalDirectory);
        var original = Path.Combine(originalDirectory, "renamed main.gguf");
        var mtp = Path.Combine(originalDirectory, mtpArtifact.Filename);
        await File.WriteAllBytesAsync(original, bytes);
        await File.WriteAllBytesAsync(mtp, mtpBytes);
        var modelFiles = new Dictionary<string, string>();
        using var client = Client((_, _) => throw new Exception("Mapped originals and colocated MTP must not download."));
        using var service = new InstallationService(fixture.Paths, client, modelFiles: modelFiles);
        Equal<InstalledModel?>(null, await service.FindAsync(model), "Unconfigured external files must not be discovered by accident.");
        modelFiles[model.Artifacts[0].Sha256] = original;
        // A saved user selection takes priority even when a managed copy also exists.
        var managed = ArtifactDestination(fixture.Paths, model);
        Directory.CreateDirectory(Path.GetDirectoryName(managed)!);
        await File.WriteAllBytesAsync(managed, bytes);
        var found = await service.FindAsync(model);
        Equal(original, found?.ModelPath, "The service must observe mappings added after construction and prefer the exact path.");
        Equal(mtp, found?.MtpPath, "The pinned MTP must be discovered beside the selected main file.");
        var installed = await service.EnsureModelAsync(model);
        Equal(original, installed.ModelPath, "Ensure must preserve the selected original path.");
        Equal(mtp, installed.MtpPath, "Ensure must reuse the verified colocated MTP.");
        True((await File.ReadAllBytesAsync(original)).SequenceEqual(bytes), "The mapped main original must remain untouched.");
        True((await File.ReadAllBytesAsync(mtp)).SequenceEqual(mtpBytes), "The colocated MTP original must remain untouched.");
    }

    private static async Task RejectsUnverifiedMappedOriginalAndColocatedMtp()
    {
        using var fixture = new Fixture();
        var model = Model([1, 2, 3, 4]);
        var mtpArtifact = new ModelArtifact { Role = "mtp", Filename = "draft.gguf", Url = "https://example.test/draft.gguf", Bytes = 2, Sha256 = Hash([5, 6]) };
        model = model with { Artifacts = [model.Artifacts[0], mtpArtifact] };
        var original = Path.Combine(fixture.Root, "renamed main.gguf");
        var mtp = Path.Combine(fixture.Root, mtpArtifact.Filename);
        await File.WriteAllBytesAsync(original, [4, 3, 2, 1]);
        await File.WriteAllBytesAsync(mtp, [5, 6]);
        using var client = Client((_, _) => throw new Exception("Discovery must not download files."));
        using var service = new InstallationService(fixture.Paths, client, modelFiles: new Dictionary<string, string> { [model.Artifacts[0].Sha256] = original });
        Equal<InstalledModel?>(null, await service.FindAsync(model), "A mapped main file with the right size but wrong hash must be rejected.");
        True((await File.ReadAllBytesAsync(original)).SequenceEqual(new byte[] { 4, 3, 2, 1 }), "Rejecting a mapped main file must preserve its bytes.");
        await File.WriteAllBytesAsync(original, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(mtp, [6, 5]);
        Equal<InstalledModel?>(null, await service.FindAsync(model), "Colocated MTP must also match its pinned hash.");
        True((await File.ReadAllBytesAsync(mtp)).SequenceEqual(new byte[] { 6, 5 }), "Rejecting colocated MTP must preserve its bytes.");
    }

    private static async Task DownloadsOnlyMissingMtpForMappedMain()
    {
        using var fixture = new Fixture();
        byte[] bytes = [1, 2, 3, 4];
        byte[] mtpBytes = [5, 6];
        var model = Model(bytes);
        var mtpArtifact = new ModelArtifact { Role = "mtp", Filename = "draft.gguf", Url = "https://example.test/draft.gguf", Bytes = mtpBytes.Length, Sha256 = Hash(mtpBytes) };
        model = model with { Artifacts = [model.Artifacts[0], mtpArtifact] };
        var original = Path.Combine(fixture.Root, "renamed main.gguf");
        await File.WriteAllBytesAsync(original, bytes);
        var requests = 0;
        using var client = Client((request, _) =>
        {
            requests++;
            Equal(mtpArtifact.Url, request.RequestUri?.AbsoluteUri, "A verified mapped main must never be redownloaded when its MTP is missing.");
            return Task.FromResult(Response(mtpBytes));
        });
        using var service = new InstallationService(fixture.Paths, client, modelFiles: new Dictionary<string, string> { [model.Artifacts[0].Sha256] = original });
        var installed = await service.EnsureModelAsync(model);
        Equal(1, requests, "Only the missing MTP must download.");
        Equal(original, installed.ModelPath, "Installing the missing MTP must keep using the selected main file.");
        True((await File.ReadAllBytesAsync(installed.MtpPath!)).SequenceEqual(mtpBytes), "The missing MTP must be verified and published.");
        True((await File.ReadAllBytesAsync(original)).SequenceEqual(bytes), "Installing the missing MTP must leave the main original untouched.");
        True(!File.Exists(ArtifactDestination(fixture.Paths, model)), "A mapped main must not be copied into managed storage.");
    }

    private static async Task CancellingModelFileMatchLeavesOriginalUntouched()
    {
        using var fixture = new Fixture();
        byte[] bytes = [1, 2, 3, 4];
        var model = Model(bytes);
        var original = Path.Combine(fixture.Root, "original.gguf");
        await File.WriteAllBytesAsync(original, bytes);
        var originalWriteTime = File.GetLastWriteTimeUtc(original);
        using var cancellation = new CancellationTokenSource();
        using var client = Client((_, _) => throw new Exception("Cancelling file verification must not download."));
        using var service = new InstallationService(fixture.Paths, client);
        var progress = new SynchronousProgress(value => { if (value.Stage == "verifying") cancellation.Cancel(); });
        await ThrowsAsync<OperationCanceledException>(() => service.MatchModelFileAsync(original, [model], progress, cancellation.Token));
        True((await File.ReadAllBytesAsync(original)).SequenceEqual(bytes), "Cancellation must preserve the selected original bytes.");
        Equal(originalWriteTime, File.GetLastWriteTimeUtc(original), "Cancellation must not write to the original.");
        True(!Directory.Exists(fixture.Paths.Models), "Cancellation must not copy or publish the selected file.");
        Equal(model, await service.MatchModelFileAsync(original, [model]), "A cancelled verification must release the installation gate for the next attempt.");
    }

    private static async Task ReusesVerifiedOriginalWithoutDownloading()
    {
        using var fixture = new Fixture();
        var model = Model([1, 2, 3, 4]);
        var originalDirectory = Path.Combine(fixture.Root, "originals");
        Directory.CreateDirectory(originalDirectory);
        var original = Path.Combine(originalDirectory, model.Artifacts[0].Filename);
        await File.WriteAllBytesAsync(original, [1, 2, 3, 4]);
        using var client = Client((_, _) => throw new Exception("Existing files must not download."));
        using (var service = new InstallationService(fixture.Paths, client, [originalDirectory]))
        {
            var found = await service.FindAsync(model);
            Equal(original, found?.ModelPath, "Find must hash and reuse the original path.");
            var installed = await service.EnsureModelAsync(model);
            Equal(original, installed.ModelPath, "Ensure must preserve the original path.");
        }
        // A cache must not accept a same-size file whose modification time changed.
        await File.WriteAllBytesAsync(original, [4, 3, 2, 1]);
        File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddMinutes(1));
        using var reopened = new InstallationService(fixture.Paths, client, [originalDirectory]);
        Equal<InstalledModel?>(null, await reopened.FindAsync(model), "Changed originals must be rehashed.");
        Equal(4L, new FileInfo(original).Length, "Original must never be removed or overwritten.");
    }

    private static async Task ReusesOriginalWhenVerificationCacheCannotBeSaved()
    {
        using var fixture = new Fixture();
        byte[] bytes = [1, 2, 3, 4];
        var model = Model(bytes);
        var originalDirectory = Path.Combine(fixture.Root, "originals");
        Directory.CreateDirectory(originalDirectory);
        var original = Path.Combine(originalDirectory, model.Artifacts[0].Filename);
        await File.WriteAllBytesAsync(original, bytes);
        Directory.CreateDirectory(Path.Combine(fixture.Paths.Root, "verified-files.json"));
        using var client = Client((_, _) => throw new Exception("A verified original must not download when its cache cannot be saved."));
        using var service = new InstallationService(fixture.Paths, client, [originalDirectory]);
        Equal(original, (await service.EnsureModelAsync(model)).ModelPath, "Cache write failure must not invalidate a verified original.");
        True((await File.ReadAllBytesAsync(original)).SequenceEqual(bytes), "Cache write failure must leave the original untouched.");
    }

    private static async Task PublishesCompletedPartialWhenVerificationCacheCannotBeSaved()
    {
        using var fixture = new Fixture();
        byte[] bytes = [1, 2, 3, 4];
        var model = Model(bytes);
        var destination = ArtifactDestination(fixture.Paths, model);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination + ".part", bytes);
        Directory.CreateDirectory(Path.Combine(fixture.Paths.Root, "verified-files.json"));
        using var client = Client((_, _) => throw new Exception("A verified completed partial must not download when its cache cannot be saved."));
        using var service = new InstallationService(fixture.Paths, client);
        Equal(destination, (await service.EnsureModelAsync(model)).ModelPath, "Cache write failure must not prevent publication.");
        True((await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "The complete verified bytes must be published.");
        True(!File.Exists(destination + ".part"), "Publication must move the completed partial file.");
        True(!Directory.EnumerateFiles(fixture.Paths.Root, "verified-files.json.*.tmp").Any(), "Failed cache writes must clean up their temporary files.");
    }

    private static async Task RehashesOriginalWithMalformedVerificationStamp()
    {
        foreach (var corruptOriginal in new[] { false, true })
        {
            using var fixture = new Fixture();
            byte[] bytes = [1, 2, 3, 4];
            var model = Model(bytes);
            var originalDirectory = Path.Combine(fixture.Root, "originals");
            Directory.CreateDirectory(originalDirectory);
            var original = Path.Combine(originalDirectory, model.Artifacts[0].Filename);
            await File.WriteAllBytesAsync(original, corruptOriginal ? [4, 3, 2, 1] : bytes);
            Directory.CreateDirectory(fixture.Paths.Root);
            var cache = new Dictionary<string, object>
            {
                [original] = new { Bytes = bytes.Length, LastWriteTicks = File.GetLastWriteTimeUtc(original).Ticks, Sha256 = (string?)null }
            };
            await File.WriteAllTextAsync(Path.Combine(fixture.Paths.Root, "verified-files.json"), JsonSerializer.Serialize(cache));
            using var client = Client((_, _) => throw new Exception("Discovery must not download files."));
            using var service = new InstallationService(fixture.Paths, client, [originalDirectory]);
            var found = await service.FindAsync(model);
            Equal(corruptOriginal ? null : original, found?.ModelPath, "A null cache hash must cause a fresh hash check, not a crash or an unverified reuse.");
        }
    }

    private static async Task ResumesPartialAndIgnoresServerRangeResetSafely()
    {
        foreach (var ignoreRange in new[] { false, true })
        {
            using var fixture = new Fixture();
            byte[] bytes = [10, 20, 30, 40, 50, 60];
            var model = Model(bytes);
            var destination = ArtifactDestination(fixture.Paths, model);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination + ".part", bytes[..2]);
            var requests = 0;
            using var client = Client((request, _) =>
            {
                requests++;
                Equal(2L, request.Headers.Range?.Ranges.Single().From, "Resume must request the saved offset.");
                return Task.FromResult(ignoreRange ? Response(bytes) : Response(bytes[2..], 2, bytes.Length));
            });
            using var service = new InstallationService(fixture.Paths, client);
            var result = await service.EnsureModelAsync(model);
            Equal(1, requests, "A valid response must complete in one request.");
            Equal(destination, result.ModelPath, "Destination must be catalog-scoped.");
            True((await File.ReadAllBytesAsync(destination)).SequenceEqual(bytes), "A 200 response must replace, not append to, the partial file.");
            True(!File.Exists(destination + ".part"), "Publication must move the verified partial file.");
        }
    }

    private static async Task RestartsAfterRangeNotSatisfiable()
    {
        using var fixture = new Fixture();
        byte[] bytes = [1, 3, 5, 7];
        var model = Model(bytes);
        var destination = ArtifactDestination(fixture.Paths, model);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination + ".part", bytes[..1]);
        var requests = 0;
        using var client = Client((request, _) =>
        {
            requests++;
            if (requests == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
            True(request.Headers.Range is null, "A rejected range must restart from zero.");
            return Task.FromResult(Response(bytes));
        });
        using var service = new InstallationService(fixture.Paths, client);
        await service.EnsureModelAsync(model);
        Equal(2, requests, "416 recovery must remain bounded.");
    }

    private static async Task RejectsWrongRangeAndCorruptDownload()
    {
        foreach (var wrongRange in new[] { false, true })
        {
            using var fixture = new Fixture();
            byte[] bytes = [1, 3, 5, 7];
            var model = Model(bytes);
            var destination = ArtifactDestination(fixture.Paths, model);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (wrongRange) await File.WriteAllBytesAsync(destination + ".part", bytes[..1]);
            using var client = Client((_, _) => Task.FromResult(wrongRange ? Response(bytes[2..], 2, bytes.Length) : Response([1, 3, 5, 8])));
            using var service = new InstallationService(fixture.Paths, client);
            await ThrowsAsync<InvalidDataException>(() => service.EnsureModelAsync(model));
            True(!File.Exists(destination), "Invalid bytes must never be published as installed.");
            Equal<InstalledModel?>(null, await service.FindAsync(model), "Invalid partial files must not appear installed.");
        }
    }

    private static async Task CancellationKeepsPartialUnpublished()
    {
        using var fixture = new Fixture();
        byte[] bytes = [1, 2, 3, 4];
        var model = Model(bytes);
        using var cancellation = new CancellationTokenSource();
        var stream = new CancelAfterFirstReadStream(bytes[..2], cancellation);
        var requests = 0;
        using var client = Client((_, _) => { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) }); });
        using var service = new InstallationService(fixture.Paths, client);
        var progress = new SynchronousProgress(value => { if (value.Stage == "downloading") cancellation.Cancel(); });
        await ThrowsAsync<OperationCanceledException>(() => service.EnsureModelAsync(model, progress, cancellation.Token));
        var destination = ArtifactDestination(fixture.Paths, model);
        True(!File.Exists(destination), "Cancellation must not publish a completed file.");
        True(File.Exists(destination + ".part"), "Cancellation must preserve the resumable partial file.");
        Equal(2L, new FileInfo(destination + ".part").Length, "The partial file must retain completed writes.");
        Equal(1, requests, "A caller cancellation must not retry the download.");
    }

    private static async Task DownloadTimeoutIsNotUserCancellation()
    {
        using var fixture = new Fixture();
        byte[] bytes = [1, 2, 3, 4];
        var model = Model(bytes);
        var destination = ArtifactDestination(fixture.Paths, model);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination + ".part", bytes[..2]);
        var requests = 0;
        using var client = Client((request, _) =>
        {
            requests++;
            Equal(2L, request.Headers.Range?.Ranges.Single().From, "Timeout retries must retain the existing download offset.");
            throw new OperationCanceledException("Simulated download timeout.");
        });
        using var service = new InstallationService(fixture.Paths, client);
        await ThrowsAsync<TimeoutException>(() => service.EnsureModelAsync(model));
        Equal(3, requests, "Download timeouts must stop after three attempts.");
        True(!File.Exists(destination), "A timed-out download must not be published.");
        True((await File.ReadAllBytesAsync(destination + ".part")).SequenceEqual(bytes[..2]), "Timeout must preserve all resumable bytes.");
    }

    private static async Task InstallsAndRevalidatesRuntime()
    {
        using var fixture = new Fixture();
        var zip = RuntimeZip();
        var runtime = Runtime(zip);
        var requests = 0;
        using var client = Client((_, _) => { requests++; return Task.FromResult(Response(zip)); });
        using var service = new InstallationService(fixture.Paths, client);
        var executable = await service.EnsureRuntimeAsync(runtime);
        True(File.Exists(executable), "Runtime executable must be published after extraction.");
        True(File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "ready.json")), "Runtime requires a completion receipt.");
        Equal(executable, await service.EnsureRuntimeAsync(runtime), "Ready runtime must be reusable.");
        Equal(1, requests, "Ready runtime must not redownload.");
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(executable)!, "ggml-vulkan.dll"), "corrupt");
        await service.EnsureRuntimeAsync(runtime);
        Equal(1, requests, "Repair must reuse the verified archive.");
        Equal(64L, new FileInfo(Path.Combine(Path.GetDirectoryName(executable)!, "ggml-vulkan.dll")).Length, "Runtime dependencies must be revalidated and repaired.");
        True(!Directory.EnumerateDirectories(fixture.Paths.Runtime, ".staging-*").Any(), "Successful install must leave no staging folders.");
    }

    private static async Task ThrottlesDownloadProgressAndPreservesCompletion()
    {
        using var fixture = new Fixture();
        var bytes = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(bytes);
        var model = Model(bytes);
        var reports = new List<InstallationProgress>();
        using var client = Client((_, _) => Task.FromResult(Response(bytes)));
        using var service = new InstallationService(fixture.Paths, client);
        var elapsed = Stopwatch.StartNew();
        await service.EnsureModelAsync(model, new SynchronousProgress(reports.Add));
        elapsed.Stop();
        var downloading = reports.Where(report => report.Stage == "downloading").ToArray();
        True(downloading.Length >= 2, "First chunk and completion must both be reported.");
        True(downloading[0].CompletedBytes > 0 && downloading[0].CompletedBytes < bytes.Length, "Progress must appear before the download finishes.");
        Equal((long)bytes.Length, downloading[^1].CompletedBytes, "Completion must report the full byte count immediately.");
        Equal(100d, downloading[^1].Percent, "Completion must report 100 percent.");
        // The elapsed-time bound tolerates slow test disks without requiring timing-sensitive sleeps.
        True(downloading.Length <= elapsed.ElapsedMilliseconds / 100 + 2, "Progress must be bounded by time, not by the number of read chunks.");
        True(reports.Any(report => report.Stage == "verifying"), "Throttling must preserve verification stage updates.");
        Equal("ready", reports[^1].Stage, "Throttling must preserve the final ready stage.");
    }

    private static async Task RejectsArchiveTraversalAndSymlinks()
    {
        foreach (var malicious in new[] { "../outside.dll", "C:/outside.dll", "sub/../../outside.dll", "symlink" })
        {
            using var fixture = new Fixture();
            var zip = RuntimeZip(malicious);
            using var client = Client((_, _) => Task.FromResult(Response(zip)));
            using var service = new InstallationService(fixture.Paths, client);
            await ThrowsAsync<InvalidDataException>(() => service.EnsureRuntimeAsync(Runtime(zip)));
            True(!File.Exists(Path.Combine(fixture.Paths.Runtime, "outside.dll")), "Archive must not write outside staging.");
            True(!Directory.EnumerateDirectories(fixture.Paths.Runtime, ".staging-*").Any(), "Rejected archive staging must be cleaned.");
            True(!Directory.EnumerateFiles(fixture.Paths.Runtime, "ready.json", SearchOption.AllDirectories).Any(), "Rejected archive must never become ready.");
        }
    }

    private static async Task RejectsUntrustedCatalogPathsAndHttp()
    {
        using var fixture = new Fixture();
        var valid = Model([1, 2]);
        using var client = Client((_, _) => throw new Exception("Invalid catalog must not trigger network access."));
        using var service = new InstallationService(fixture.Paths, client);
        await ThrowsAsync<ArgumentException>(() => service.EnsureModelAsync(valid with { Id = "../outside" }));
        await ThrowsAsync<ArgumentException>(() => service.EnsureModelAsync(valid with { Artifacts = [valid.Artifacts[0] with { Filename = "../outside.gguf" }] }));
        await ThrowsAsync<ArgumentException>(() => service.EnsureModelAsync(valid with { Artifacts = [valid.Artifacts[0] with { Url = "http://example.test/model.gguf" }] }));
    }

    private static ModelDefinition Model(byte[] bytes) => new()
    {
        Id = "test-model", DisplayName = "Test", ApiModelId = "test", Category = "Test", Headline = "Test", Description = "Test",
        HardwareGuide = "Test", MemoryGuide = "Test", SourceUrl = "https://example.test/model", LicenseUrl = "https://example.test/license", Profile = new(),
        Artifacts = [new() { Role = "model", Filename = "test.gguf", Url = "https://example.test/test.gguf", Bytes = bytes.Length, Sha256 = Hash(bytes) }]
    };

    private static RuntimePackage Runtime(byte[] bytes) => new() { Version = "test-runtime", Url = "https://example.test/runtime.zip", Bytes = bytes.Length, Sha256 = Hash(bytes) };
    private static string ArtifactDestination(LauncherPaths paths, ModelDefinition model) => Path.Combine(paths.Models, model.Id, model.Artifacts[0].Sha256[..12], model.Artifacts[0].Filename);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] RuntimeZip(string? malicious = null)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach (var name in new[] { "llama-server.exe", "llama-server-impl.dll", "llama.dll", "llama-common.dll", "ggml.dll", "ggml-base.dll", "ggml-vulkan.dll" })
            {
                using var output = zip.CreateEntry(name).Open();
                byte[] pe = new byte[64];
                pe[0] = (byte)'M'; pe[1] = (byte)'Z';
                output.Write(pe);
            }
            if (malicious is not null)
            {
                var entry = zip.CreateEntry(malicious);
                if (malicious == "symlink") entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                using var output = entry.Open();
                output.Write([1, 2, 3]);
            }
        }
        return buffer.ToArray();
    }

    private static HttpResponseMessage Response(byte[] bytes, long? from = null, long total = 0)
    {
        var response = new HttpResponseMessage(from.HasValue ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        if (from.HasValue) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from.Value, from.Value + bytes.Length - 1, total);
        return response;
    }

    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) => new(new Handler(handle));
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
    }
    private sealed class SynchronousProgress(Action<InstallationProgress> action) : IProgress<InstallationProgress>
    { public void Report(InstallationProgress value) => action(value); }

    private sealed class CancelAfterFirstReadStream(byte[] initial, CancellationTokenSource cancellation) : MemoryStream(initial)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position == Length) { cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested(); }
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "GemmaLauncherInstallationTests", Guid.NewGuid().ToString("N"));
        public LauncherPaths Paths { get; }
        public Fixture() { Paths = new LauncherPaths(Path.Combine(Root, "app")); Directory.CreateDirectory(Root); }
        public void Dispose()
        {
            var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GemmaLauncherInstallationTests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Root).StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path.");
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual, string message) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message} Expected {expected}, actual {actual}."); }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
