using System.Windows.Forms;
using GemmaLauncher.Core;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;

public sealed class AnisAccessInput
{
    public string? Token { get; init; }
    public string? ModelFolder { get; init; }
    public override string ToString() => "ANIS access input";
}

public sealed partial class LauncherViewModel
{
    public bool IsAnis => SelectedModel?.Engine == "anis";
    public bool CloseToTray { get => _settings.CloseToTray; set { _settings.CloseToTray = value; SaveSettings(); OnPropertyChanged(); } }
    public void OpenModelLicense() { if (SelectedModel is not null) OpenLink(SelectedModel.LicenseUrl); }
    public void OpenAnisProgramSource() => OpenLink(AnisBackend.SourcePage);
    public Func<CancellationToken, Task<AnisAccessInput?>>? RequestAnisAccess { get; set; }
    public string[] AnisDirections { get; } = ["auto", "ja-ko", "ko-ja"];
    public string[] AnisDevices { get; } = ["auto", "cpu", "cuda"];
    public string AnisDirection
    {
        get => _settings.AnisDirection is "ja-ko" or "ko-ja" ? _settings.AnisDirection : "auto";
        set
        {
            if (!CanSelectModel || !AnisDirections.Contains(value) || value == AnisDirection) return;
            _settings.AnisDirection = value; SaveSettings(); OnPropertyChanged(); OnPropertyChanged(nameof(ApiModelId));
        }
    }
    public string AnisDevice
    {
        get => _settings.AnisDevice is "cpu" or "cuda" ? _settings.AnisDevice : "auto";
        set
        {
            if (!CanSelectModel || !AnisDevices.Contains(value)) return;
            _settings.AnisDevice = value; SaveSettings(); OnPropertyChanged();
        }
    }
    public string AnisDirectionLabel(string direction) => T("ui.anis.direction." + direction);
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
