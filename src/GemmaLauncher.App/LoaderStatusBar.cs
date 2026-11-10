using static GemmaLauncher.Core.Localization;
namespace GemmaLauncher.App;

internal sealed class FeatureTab : Button
{
    private bool selected;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get => selected; set { if (selected == value) return; selected = value; Invalidate(); } }
    public FeatureTab()
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0; BackColor = Color.White; ForeColor = Ui.Green;
        Padding = new(12, 10, 12, 14); Cursor = Cursors.Hand; UseMnemonic = false;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Selected) { using var brush = new SolidBrush(Ui.Green); var height = Math.Max(2, 2 * DeviceDpi / 96); e.Graphics.FillRectangle(brush, 0, Height - height, Width, height); }
    }
}

internal sealed class LoaderStatusBar : UserControl
{
    private readonly UiBindings bindings;
    public LoaderStatusBar(LoaderShellViewModel shell)
    {
        Dock = DockStyle.Top; BackColor = Color.White;
        bindings = new(this, shell);
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(16, 4, 16, 4), WrapContents = true };
        var local = Ui.Label(); local.Margin = new(0, 10, 14, 0); Ui.SetFont(local, Ui.SecondaryFontSize);
        bindings.Text(local, () => T("ui.loader.local") + " · " + T(shell.Local?.IsServerRunning == true ? "ui.account.serverRunning" : shell.Local?.IsBusy == true ? "ui.account.preparing" : "ui.account.serverOff")); row.Controls.Add(local);
        var localStop = Ui.Button(); bindings.Text(localStop, () => shell.Local?.PrimaryLabel ?? ""); bindings.Visible(localStop, () => shell.Local is { } vm && (vm.IsBusy || vm.IsServerRunning)); bindings.Enabled(localStop, () => shell.Local?.PrimaryCommand.CanExecute(null) == true); localStop.Click += (_, _) => shell.Local?.PrimaryCommand.Execute(null); row.Controls.Add(localStop);
        var account = Ui.Label(); account.Margin = new(8, 10, 12, 0); Ui.SetFont(account, Ui.SecondaryFontSize);
        bindings.Text(account, () => T("ui.loader.chat") + " · " + (shell.Account?.IsLoggingIn == true ? shell.Account.StatusDescription : shell.Account?.ServerStatus ?? T("ui.account.serverOff"))); row.Controls.Add(account);
        var stop = Ui.Button(); stop.Name = "GlobalAccountStop";
        bindings.Text(stop, () => T("ui.loader.chat") + " · " + (shell.Account?.IsLoggingIn == true ? shell.Account.LoginLabel : shell.Account?.ServerLabel));
        bindings.Visible(stop, () => shell.Account is { } vm && (vm.IsBusy || vm.IsRunning || vm.IsStopping));
        bindings.Enabled(stop, () => shell.Account?.PrimaryCommand.CanExecute(null) == true); stop.Click += (_, _) => shell.Account?.PrimaryCommand.Execute(null); row.Controls.Add(stop);
        var fitting = false;
        void FitHeight()
        {
            if (fitting || row.ClientSize.Width <= 0) return;
            fitting = true;
            try { Height = row.GetPreferredSize(new Size(row.ClientSize.Width,0)).Height; }
            finally { fitting = false; }
        }
        Controls.Add(row); row.SizeChanged += (_,_) => FitHeight(); bindings.Watch(FitHeight); bindings.Start();
    }
    protected override void Dispose(bool disposing) { if (disposing) bindings.Dispose(); base.Dispose(disposing); }
}
