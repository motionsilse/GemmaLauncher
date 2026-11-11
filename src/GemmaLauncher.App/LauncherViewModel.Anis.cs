using System.Windows.Forms;
using System.Reflection;
using GemmaLauncher.Core;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;

public sealed partial class LauncherViewModel
{
    public bool IsAnis => SelectedModel?.Engine == "anis";
    public bool CloseToTray { get => _settings.CloseToTray; set { _settings.CloseToTray = value; SaveSettings(); OnPropertyChanged(); } }
    public void OpenModelLicense() { if (SelectedModel is not null) OpenLink(SelectedModel.LicenseUrl); }
    public void OpenAnisProgramSource() => OpenLink(AnisBackend.SourcePage);
    public Func<CancellationToken, Task<string?>>? RequestAnisAccess { get; set; }
    public Action<Uri> OpenAnisDownload { get; set; } = uri =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    internal string AnisManagedFolder => _anis.ManagedModelDirectory;
    internal Task<IReadOnlyList<AnisArtifact>> ImportAnisDownloadsAsync(string directory, CancellationToken token) =>
        _anis.ImportBrowserDownloadsAsync(directory, token);
    internal static string? HuggingFaceClientId { get; } = typeof(LauncherViewModel).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "HuggingFaceClientId")?.Value;
    private HuggingFaceAuthorization? _anisAuthorization;
    internal bool HasAnisAuthorization => _anisAuthorization?.IsAuthorized == true;
    internal void ClearAnisAuthorization() => _anisAuthorization?.ClearAuthorization();

    internal async Task DownloadAnisWithLoginAsync(string clientId, IProgress<InstallationProgress> progress, CancellationToken token)
    {
        _anisAuthorization ??= new HuggingFaceAuthorization(clientId);
        var credential = await _anisAuthorization.SignInAsync(OpenAnisDownload, token);
        await _anis.EnsureModelAsync(SelectedModel!, credential, progress, token);
        ClearAnisAuthorization();
    }

    public string[] AnisDevices { get; } = ["auto", "cpu", "cuda"];
    public string AnisDevice
    {
        get => _settings.AnisDevice is "cpu" or "cuda" ? _settings.AnisDevice : "auto";
        set
        {
            if (!CanSelectModel || !AnisDevices.Contains(value)) return;
            _settings.AnisDevice = value; SaveSettings(); OnPropertyChanged();
        }
    }
    public string AnisDeviceLabel(string device) => device switch { "auto" => T("ui.option.auto"), "cpu" => "CPU", "cuda" => "NVIDIA GPU (CUDA)", _ => device };
    private void ChooseAnisFolder()
    {
        using var picker = new FolderBrowserDialog { Description = T("ui.anis.connectFolder") };
        if (picker.ShowDialog() == DialogResult.OK) _ = ConnectAnisFolderAsync(picker.SelectedPath);
    }
    public Task ConnectAnisFolderAsync(string folder)
    {
        if (!CanSelectModel || !IsAnis) return Task.CompletedTask;
        BeginOperation(async token =>
        {
            var installed = await _anis.InspectFolderAsync(SelectedModel!, folder, token) ?? throw new InvalidDataException(T("engine.anis.invalidFolder"));
            var previous = _settings.AnisModelFolder;
            _settings.AnisModelFolder = Path.GetDirectoryName(Path.GetDirectoryName(installed.ModelPath));
            try { _store.Save(_settings); } catch { _settings.AnisModelFolder = previous; throw; }
            _installed = installed; SelectedPresentation!.FileState = ModelFileState.Ready;
        }, false);
        return _operation ?? Task.CompletedTask;
    }
}
