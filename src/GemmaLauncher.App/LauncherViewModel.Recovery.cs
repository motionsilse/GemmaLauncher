using System.Net;
using System.Net.Sockets;
using System.Windows.Input;
using GemmaLauncher.Core;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;

public enum FailureRecovery { Retry, Access, Reconnect, LoadSettings, Storage, VerifyPort, Redownload }

public sealed partial class LauncherViewModel
{
    private FailureRecovery _failureRecovery;
    public FailureRecovery Recovery => _failureRecovery;
    public event EventHandler? LoadSettingsRequested;
    public event EventHandler? StorageRecoveryRequested;
    public string StorageInfo
    {
        get
        {
            try { return T("ui.recovery.spaceInfo", (new DriveInfo(Path.GetPathRoot(_paths.Root)!).AvailableFreeSpace / 1_000_000_000d).ToString("0.0"), DownloadLabel); }
            catch (IOException) { return DownloadLabel; }
        }
    }
    public ICommand RetryCommand => _retryCommand ??= Command(_ => BeginOperation(StartAsync), _ => CanSelectModel && HasSelectedModel);
    private RelayCommand? _retryCommand;
    private void ClassifyFailure(Exception exception)
    {
        _failureRecovery = FailureRecovery.Retry;
        for (Exception? error = exception; error is not null; error = error.InnerException)
        {
            if (error is AnisAccessRequiredException) _failureRecovery = FailureRecovery.Access;
            else if (error is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }) _failureRecovery = FailureRecovery.VerifyPort;
            else if (error is ModelLoadMemoryException or OutOfMemoryException || error.Message.Contains("out of memory", StringComparison.OrdinalIgnoreCase)) _failureRecovery = FailureRecovery.LoadSettings;
            else if ((error.HResult & 0xffff) is 39 or 112) _failureRecovery = FailureRecovery.Storage;
            else if (error is FileNotFoundException && (SelectedModel?.Origin == "local" || IsAnis && _settings.AnisModelFolder is not null)) _failureRecovery = FailureRecovery.Reconnect;
            else if (error is InvalidDataException) _failureRecovery = SelectedModel?.Origin == "local" ? FailureRecovery.Reconnect : FailureRecovery.Redownload;
        }
    }
    private string? RecoveryLabel => !HasServerError ? null : T(_failureRecovery switch
    {
        FailureRecovery.Access => "ui.recovery.access", FailureRecovery.Reconnect => IsAnis ? "ui.anis.connectFolder" : "ui.files.connect",
        FailureRecovery.LoadSettings => "ui.recovery.settings", FailureRecovery.Storage => "ui.recovery.storage", FailureRecovery.VerifyPort => "ui.recovery.port",
        FailureRecovery.Redownload => "ui.recovery.redownload", _ => "ui.recovery.retry"
    });
    private bool RecoverBeforeStart()
    {
        if (!HasServerError) return false;
        switch (_failureRecovery)
        {
            case FailureRecovery.LoadSettings: LoadSettingsRequested?.Invoke(this, EventArgs.Empty); return true;
            case FailureRecovery.Reconnect: if (IsAnis) ChooseAnisFolder(); else ChooseModelFile(); return true;
            case FailureRecovery.Storage: StorageRecoveryRequested?.Invoke(this, EventArgs.Empty); return true;
            default: return false;
        }
    }
}
