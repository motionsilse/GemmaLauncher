using static GemmaLauncher.Core.Localization;
namespace GemmaLauncher.App;
public sealed class BackgroundOperationsView : UserControl
{
    private readonly UiBindings bindings;
    public BackgroundOperationsView(LoaderShellViewModel shell, bool all = false)
    {
        Dock = DockStyle.Top; AutoSize = true; bindings = new(this,shell);
        var stack = Ui.Stack(); stack.Padding = new(8); Controls.Add(stack);
        Add(() => shell.Local is { } vm && (vm.IsBusy || vm.IsServerRunning) && (all || shell.SelectedFeature != LoaderFeature.LocalModels), () => T("ui.loader.local") + " · " + shell.Local?.PrimaryLabel, () => shell.Local?.PrimaryCommand.Execute(null), () => shell.Local?.PrimaryCommand.CanExecute(null) == true);
        Add(() => shell.Account is { } vm && (vm.IsBusy || vm.IsRunning) && (all || shell.SelectedFeature != LoaderFeature.ChatGpt), () => T("ui.loader.chat") + " · " + shell.Account?.PrimaryLabel, () => shell.Account?.PrimaryCommand.Execute(null), () => shell.Account?.PrimaryCommand.CanExecute(null) == true);
        bindings.Visible(this, () => shell.Local is { } local && (local.IsBusy || local.IsServerRunning) && (all || shell.SelectedFeature != LoaderFeature.LocalModels) || shell.Account is { } account && (account.IsBusy || account.IsRunning) && (all || shell.SelectedFeature != LoaderFeature.ChatGpt)); bindings.Start();
        void Add(Func<bool> visible,Func<string> text,Action action,Func<bool> enabled) { var button = Ui.Button(); bindings.Text(button,text); bindings.Visible(button,visible); bindings.Enabled(button,enabled); button.Click += (_,_) => action(); Ui.Add(stack,button); }
    }
    protected override void Dispose(bool disposing) { if (disposing) bindings.Dispose(); base.Dispose(disposing); }
}
