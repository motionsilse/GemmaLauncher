namespace GemmaLauncher.App;
public sealed partial class LauncherViewModel
{
    public Func<Control>? BackgroundOperationsFactory { get; set; }
    public void CancelPending() { _operationCancellation?.Cancel(); _scanCancellation?.Cancel(); }
}
