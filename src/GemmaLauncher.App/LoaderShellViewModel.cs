using GemmaLauncher.Core;
namespace GemmaLauncher.App;
public enum LoaderFeature { LocalModels, ChatGpt }
public sealed class LoaderShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly LauncherSettings settings;
    private readonly SettingsStore store;
    private readonly Func<LauncherViewModel> localFactory;
    private readonly Func<AccountConnectionViewModel> accountFactory;
    private readonly TimeSpan cleanupTimeout;
    private bool disposed;
    public LauncherViewModel? Local { get; private set; }
    public AccountConnectionViewModel? Account { get; private set; }
    public LoaderFeature SelectedFeature { get; private set; }
    public bool CloseToTray { get => settings.CloseToTray; set { settings.CloseToTray = value; if (Local is not null) Local.CloseToTray = value; Save(); } }
    public string LanguageSelection { get => Localization.Current.Selection; set => Localization.Current.SetLanguage(value); }
    public LoaderShellViewModel(LauncherSettings settings, SettingsStore store, Func<LauncherViewModel> localFactory, Func<AccountConnectionViewModel> accountFactory, TimeSpan? cleanupTimeout = null)
    {
        this.settings = settings; this.store = store; this.localFactory = localFactory; this.accountFactory = accountFactory;
        this.cleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(30);
        SelectedFeature = settings.ActiveFeature == "chatgpt" ? LoaderFeature.ChatGpt : LoaderFeature.LocalModels;
        Localization.Current.LanguageChanged += LanguageChanged;
    }
    private void LanguageChanged(object? s, EventArgs e) { settings.LanguagePreference = Localization.Current.Selection; Save(); }
    private void Save() { store.Save(settings); OnPropertyChanged(null); }
    private void Changed(object? s, System.ComponentModel.PropertyChangedEventArgs e) { if (!disposed) OnPropertyChanged(null); }
    public bool ResolvePendingEdits() => Local?.ResolvePendingPreset() ?? true;
    public async Task<bool> SelectFeatureAsync(LoaderFeature feature, CancellationToken ct)
    {
        if (disposed) return false;
        ct.ThrowIfCancellationRequested();
        if (feature != SelectedFeature && !ResolvePendingEdits()) return false;
        if (feature == LoaderFeature.LocalModels && Local is null) { Local = localFactory(); Local.PropertyChanged += Changed; }
        if (feature == LoaderFeature.ChatGpt)
        {
            if (Account is null) { Account = accountFactory(); Account.PropertyChanged += Changed; }
            await Account.ActivateAsync(ct);
        }
        Account?.MaskKey(); SelectedFeature = feature;
        settings.ActiveFeature = feature == LoaderFeature.ChatGpt ? "chatgpt" : "local";
        Save(); return true;
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        Local?.CancelPending(); Account?.CancelPending();
        Localization.Current.LanguageChanged -= LanguageChanged;
        var cleanup = new List<Task>();
        if (Local is not null) { Local.PropertyChanged -= Changed; cleanup.Add(Clean("local", () => Local.DisposeAsync())); }
        if (Account is not null) { Account.PropertyChanged -= Changed; cleanup.Add(Clean("chatgpt", () => Account.DisposeAsync())); }
        await Task.WhenAll(cleanup);
        async Task Clean(string feature, Func<ValueTask> dispose)
        {
            Task? pending = null;
            try { pending = dispose().AsTask(); await pending.WaitAsync(cleanupTimeout); }
            catch
            {
                if (pending is not null) _ = pending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw new InvalidOperationException(Localization.T("ui.loader.cleanupFailed", feature));
            }
        }
    }
}
