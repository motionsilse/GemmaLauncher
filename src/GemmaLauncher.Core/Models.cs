namespace GemmaLauncher.Core;

public sealed record ModelArtifact
{
    public required string Role { get; init; }
    public required string Filename { get; init; }
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public long Bytes { get; init; }
}

public sealed record ModelProfile
{
    public int DefaultContext { get; init; } = 8192;
    public int[] ContextSizes { get; init; } = [4096, 8192, 16384];
    public string Lazy { get; init; } = "on";
    public int MtpDraftMax { get; init; } = 2;
    public double Temperature { get; init; }
    public double TopP { get; init; } = .95;
    public int TopK { get; init; } = 64;
    public double MinP { get; init; } = .05;
}

public sealed record ModelDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string Credit { get; init; } = "";
    public required string ApiModelId { get; init; }
    public required string Category { get; init; }
    public required string Headline { get; init; }
    public required string Description { get; init; }
    public required string HardwareGuide { get; init; }
    public required string MemoryGuide { get; init; }
    public string[] Benefits { get; init; } = [];
    public required string SourceUrl { get; init; }
    public required string LicenseUrl { get; init; }
    public required ModelProfile Profile { get; init; }
    public required ModelArtifact[] Artifacts { get; init; }
    public long DownloadBytes => Artifacts.Sum(a => a.Bytes);
    public string DownloadLabel => $"{DownloadBytes / 1_000_000_000d:0.00} GB";
}

public sealed record RuntimePackage
{
    public required string Version { get; init; }
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public long Bytes { get; init; }
    public string ExecutableRelativePath { get; init; } = "llama-server.exe";
}

public sealed record ModelCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public required RuntimePackage Runtime { get; init; }
    public required ModelDefinition[] Models { get; init; }
}

public sealed record InstalledModel(ModelDefinition Definition, string ModelPath, string? MtpPath);
public sealed record InstallationProgress(string Stage, string Message, long CompletedBytes = 0, long? TotalBytes = null)
{
    public double? Percent => TotalBytes > 0 ? Math.Clamp(CompletedBytes * 100d / TotalBytes.Value, 0, 100) : null;
}

public sealed class LauncherPaths
{
    public string Root { get; }
    public string Models => Path.Combine(Root, "models");
    public string Runtime => Path.Combine(Root, "runtime");
    public string Logs => Path.Combine(Root, "logs");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public LauncherPaths(string? root = null) => Root = Path.GetFullPath(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GemmaLauncher"));
}

public interface IInstallationService
{
    Task<InstalledModel?> FindAsync(ModelDefinition model, CancellationToken cancellationToken = default);
    Task<InstalledModel> EnsureModelAsync(ModelDefinition model, IProgress<InstallationProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<string> EnsureRuntimeAsync(RuntimePackage runtime, IProgress<InstallationProgress>? progress = null, CancellationToken cancellationToken = default);
}

public enum ServerState { Stopped, Starting, Running, Stopping, Faulted }
public sealed record SessionStatus(ServerState State, string Message, int? ProcessId = null, string? ModelId = null);
public sealed record LaunchConfiguration(InstalledModel Model, string RuntimeExecutable, int Context, string LogDirectory, int Port = 8080);

public interface IServerSession : IAsyncDisposable
{
    SessionStatus Status { get; }
    event EventHandler<SessionStatus>? StatusChanged;
    Task StartAsync(LaunchConfiguration configuration, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
