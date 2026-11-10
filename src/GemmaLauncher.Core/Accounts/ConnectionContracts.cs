namespace GemmaLauncher.Core.Accounts;

public enum SavedAccountState { Missing, Available, Unreadable, Multiple }
public sealed record AccountSummary(SavedAccountState State, string? Email = null, string? Plan = null);
public enum AccountPreparationStage { CheckingRuntime, DownloadingRuntime, ExtractingRuntime, StartingProxy, AwaitingLogin, CheckingAccount, ReadingModels, Ready, Stopping }
public sealed record ConnectionProgress(AccountPreparationStage Stage, long CompletedBytes = 0, long? TotalBytes = null);
public sealed record UsageWindow(string Bucket, double? UsedPercent, long? DurationSeconds, DateTimeOffset? ResetsAt);
public sealed record AccountUsage(DateTimeOffset CheckedAt, IReadOnlyList<UsageWindow> Windows);
public sealed record ConnectionProbeResult(bool TextMatched, string ReportedModel, double ElapsedMs);
public sealed class ConnectionFailure(string code, int? httpStatus = null) : Exception(code)
{
    public string Code { get; } = code;
    public int? HttpStatus { get; } = httpStatus;
}
public interface IAccountConnection : IAsyncDisposable
{
    string ApiAddress { get; }
    string? LocalApiKey { get; }
    bool IsRunning { get; }
    bool IsReady { get; }
    // The last asynchronous lifecycle failure; intentional stop/restart clears it.
    string? FailureCode => null;
    event EventHandler? StatusChanged;
    AccountSummary ReadSavedAccount();
    Task ConnectAsync(IProgress<ConnectionProgress>? progress, CancellationToken ct);
    // Saves authentication and closes its temporary helper; does not start the user API.
    Task LoginAsync(Action<Uri> openBrowser, string uiLanguage, IProgress<ConnectionProgress>? progress, CancellationToken ct);
    Task<IReadOnlyList<string>> ModelsAsync(CancellationToken ct);
    Task<AccountUsage> UsageAsync(CancellationToken ct);
    Task<ConnectionProbeResult> TestAsync(string model, CancellationToken ct);
    Task SignOutAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
