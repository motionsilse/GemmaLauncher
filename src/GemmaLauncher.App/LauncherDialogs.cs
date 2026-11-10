using System.Drawing;
using System.Windows.Forms;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;

// Every owned dialog keeps control of the same server session, including while an editor draft is open.
public sealed class LauncherDialog : Form
{
    private readonly UiBindings _bindings;
    public Panel Content { get; } = new() { Dock = DockStyle.Fill, AutoScroll = true };
    public FlowLayoutPanel Actions { get; } = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true, Padding = new Padding(0, 8, 0, 0) };
    public LauncherDialog(LauncherViewModel vm, string title, Size size)
    {
        Text = title; Size = size; MinimumSize = new Size(360, 240); StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); Ui.SetFont(this, 10.5f);
        BackColor = Color.White; ForeColor = Ui.Ink; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        _bindings = new(this, vm);
        void WrapMessages()
        {
            foreach (var label in Content.Controls.OfType<Label>().Where(label => label.AutoSize))
                label.MaximumSize = new Size(Math.Max(1, Content.ClientSize.Width - label.Margin.Horizontal), 0);
        }
        Content.SizeChanged += (_, _) => WrapMessages(); Content.ControlAdded += (_, _) => WrapMessages();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var running = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Ui.Tint, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 14) };
        running.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); running.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var status = Ui.Label(); _bindings.Text(status, () => vm.SelectedModel?.DisplayName + "\n" + vm.StatusLabel); running.Controls.Add(status, 0, 0);
        var stop = Ui.Button(); stop.Name = "DialogSessionAction"; _bindings.Text(stop, () => vm.PrimaryLabel); _bindings.Command(stop, vm.PrimaryCommand); running.Controls.Add(stop, 1, 0);
        _bindings.Visible(running, () => vm.IsBusy || vm.IsServerRunning);
        root.Controls.Add(running, 0, 0); root.Controls.Add(Content, 0, 1); root.Controls.Add(Actions, 0, 2); Controls.Add(root); _bindings.Start();
        if (vm.BackgroundOperationsFactory is not null) { running.Visible = false; root.Controls.Remove(running); Disposed += (_, _) => running.Dispose(); root.Controls.Add(vm.BackgroundOperationsFactory(), 0, 0); }
    }
    public Button AddAction(string text, DialogResult result, bool primary = false)
    {
        var button = Ui.Button(); button.Text = text; button.DialogResult = result;
        if (primary) Ui.Primary(button); Actions.Controls.Add(button);
        if (result == DialogResult.Cancel) CancelButton = button;
        return button;
    }
    protected override void OnShown(EventArgs e) { WindowLayout.FitToWorkArea(this, Screen.FromControl(this).WorkingArea); base.OnShown(e); }
    protected override void Dispose(bool disposing) { if (disposing) _bindings.Dispose(); base.Dispose(disposing); }
}

internal static class LauncherDialogs
{
    public static Task<AnisAccessInput?> AnisAccess(LauncherViewModel vm, IWin32Window owner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var dialog = new LauncherDialog(vm, T("ui.anis.firstUse"), new Size(640, 480));
        var stack = Ui.Stack(); stack.Padding = Padding.Empty; dialog.Content.Controls.Add(Ui.Scroll(stack));
        Ui.Add(stack, Ui.Label(T("ui.anis.accessHint")));
        var source = new LinkLabel { AutoSize = true, Text = T("ui.anis.officialAccess"), LinkColor = Ui.Green };
        source.LinkClicked += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Core.AnisBackend.ModelPage) { UseShellExecute = true }); Ui.Add(stack, source);
        Ui.Add(stack, Ui.Label(T("ui.anis.token")));
        var credential = Ui.TextBox(); credential.Name = "AnisDownloadToken"; credential.UseSystemPasswordChar = true; credential.MaxLength = 4096; credential.AccessibleName = T("ui.anis.token"); Ui.Add(stack, credential);
        Ui.Add(stack, Ui.Label(T("ui.anis.tokenHint")));
        var validation = Ui.Label(T("engine.anis.accessRequired")); validation.ForeColor = Color.Maroon; validation.Visible = false; Ui.Add(stack, validation);
        AnisAccessInput? access = null;
        var local = Ui.Button(); local.Text = T("ui.anis.connectFolder"); local.Anchor = AnchorStyles.Left;
        local.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = T("ui.anis.connectFolder") };
            if (picker.ShowDialog(dialog) != DialogResult.OK) return;
            access = new() { ModelFolder = picker.SelectedPath }; dialog.DialogResult = DialogResult.Retry;
        };
        Ui.Add(stack, local);
        dialog.AddAction(T("ui.anis.download"), DialogResult.OK, true);
        dialog.AddAction(T("ui.preset.cancel"), DialogResult.Cancel);
        dialog.FormClosing += (_, e) =>
        {
            if (dialog.DialogResult != DialogResult.OK) return;
            var value = credential.Text.Trim();
            if (value.Length == 0 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')))
            { e.Cancel = true; validation.Visible = true; credential.Focus(); return; }
            access = new() { Token = value };
        };
        _ = dialog.Handle;
        using var cancellation = token.Register(() =>
        {
            try { if (!dialog.IsDisposed) dialog.BeginInvoke(() => { if (!dialog.IsDisposed) { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); } }); }
            catch (InvalidOperationException) when (dialog.IsDisposed || !dialog.IsHandleCreated) { }
        });
        var result = dialog.ShowDialog(owner);
        credential.Clear(); token.ThrowIfCancellationRequested();
        return Task.FromResult(result is DialogResult.OK or DialogResult.Retry ? access : null);
    }
    public static PresetEditDecision ResolvePreset(LauncherViewModel vm, IWin32Window owner)
    {
        using var dialog = new LauncherDialog(vm, T("ui.preset.changesTitle"), new Size(580, 320));
        var message = Ui.Label(T("ui.preset.changesHint") + (vm.IsPresetShared ? "\n\n" + vm.PresetScopeLabel : "")); dialog.Content.Controls.Add(message);
        dialog.AddAction(T("ui.library.save"), DialogResult.Yes, true);
        dialog.AddAction(T("ui.preset.discard"), DialogResult.No);
        dialog.AddAction(T("ui.preset.keepEditing"), DialogResult.Cancel);
        return dialog.ShowDialog(owner) switch { DialogResult.Yes => PresetEditDecision.Save, DialogResult.No => PresetEditDecision.Discard, _ => PresetEditDecision.Cancel };
    }
    public static bool DeletePreset(LauncherViewModel vm, IWin32Window owner, string name)
    {
        using var dialog = new LauncherDialog(vm, T("ui.preset.deleteTitle", name), new Size(580, 300));
        dialog.Content.Controls.Add(Ui.Label(T("ui.preset.deleteTitle", name) + "\n\n" + T("ui.preset.deleteHint") + (vm.IsPresetShared ? "\n\n" + vm.PresetScopeLabel : "")));
        dialog.AddAction(T("ui.library.deletePreset"), DialogResult.Yes);
        dialog.AddAction(T("ui.preset.cancel"), DialogResult.Cancel);
        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }
}
