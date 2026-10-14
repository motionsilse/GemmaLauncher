using System.Drawing;
using System.Windows.Forms;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;

public sealed class MainWindow : Form
{
    private readonly LauncherViewModel _viewModel;
    private readonly UiBindings _bindings;
    private readonly Font _uiFont = new("Segoe UI", 10);
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Name = "WorkspaceTabs" };
    private readonly LauncherPage?[] _pages = new LauncherPage?[3];
    private bool _switching;
    private readonly uint _activateMessage;
    private readonly uint _legacyActivateMessage;
    public event EventHandler? ActivationRequested;
    public event EventHandler? ExitRequested;
    public int CreatedPageCount => _pages.Count(page => page is not null);

    public MainWindow(LauncherViewModel viewModel, uint activateMessage = 0, uint legacyActivateMessage = 0)
    {
        _activateMessage = activateMessage; _legacyActivateMessage = legacyActivateMessage;
        _viewModel = viewModel; _bindings = new(this, viewModel);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = _uiFont;
        BackColor = Ui.Paper; ForeColor = Ui.Ink;
        Size = new Size(1080, 860); MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        var title = Ui.Label(); _bindings.Text(title, () => T("ui.app.title") + " · by Drugged Cat");
        var language = Ui.Combo("LanguageSelector");
        _bindings.Choice(language, () => viewModel.LanguageOptions.Select(option => option.Code), () => viewModel.LanguageSelection,
            value => { CommitEditors(); viewModel.LanguageSelection = value; }, code => viewModel.LanguageOptions.First(option => option.Code == code).Name);
        _bindings.Watch(() => language.AccessibleName = T("ui.language.label"));
        header.Controls.Add(title, 0, 0); header.Controls.Add(language, 1, 0); root.Controls.Add(header, 0, 0);
        foreach (var key in new[] { "ui.library.run", "ui.library.models", "ui.library.presets" })
        {
            var tab = new TabPage { BackColor = Ui.Paper, Padding = new Padding(4) };
            _bindings.Localize(tab, key); _tabs.TabPages.Add(tab);
        }
        _tabs.Selecting += (_, _) => CommitEditors();
        _tabs.SelectedIndexChanged += (_, _) => { if (!_switching) viewModel.PageIndex = _tabs.SelectedIndex; ShowPage(); };
        _bindings.Watch(() =>
        {
            _switching = true;
            try { _tabs.SelectedIndex = Math.Clamp(viewModel.PageIndex, 0, 2); ShowPage(); }
            finally { _switching = false; }
        });
        root.Controls.Add(_tabs, 0, 1);
        var operation = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        operation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); operation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        var status = Ui.Label(); _bindings.Text(status, () => (viewModel.ShowOperationModel ? viewModel.SelectedModel?.DisplayName + "\n" : "") + viewModel.StatusTitle + "\n" + viewModel.StatusDescription);
        var actions = Ui.Stack();
        var progress = new ProgressBar { Name = "OperationProgress", Height = 16, Dock = DockStyle.Top };
        _bindings.Watch(() => { progress.Style = viewModel.IsIndeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous; progress.Value = (int)Math.Clamp(viewModel.ProgressPercent, 0, 100); });
        _bindings.Visible(progress, () => viewModel.IsProgressVisible); Ui.Add(actions, progress);
        var prerequisite = Ui.Button(); _bindings.Localize(prerequisite, "ui.runtime.install"); _bindings.Command(prerequisite, viewModel.InstallPrerequisiteCommand); _bindings.Visible(prerequisite, () => viewModel.NeedsRuntimePrerequisite); Ui.Add(actions, prerequisite);
        var primary = Ui.Button(); primary.Name = "PrimaryAction"; _bindings.Text(primary, () => viewModel.PrimaryLabel); _bindings.Command(primary, viewModel.PrimaryCommand, before: CommitEditors); _bindings.Visible(primary, () => viewModel.ShowPrimaryAction); Ui.Add(actions, primary);
        operation.Controls.Add(status, 0, 0); operation.Controls.Add(actions, 1, 0); _bindings.Visible(operation, () => viewModel.ShowOperation);
        root.Controls.Add(operation, 0, 2);
        var footer = Ui.Label(); _bindings.Text(footer, () => viewModel.StatusNote + (viewModel.StatusNote.Length > 0 ? "\n" : "") + T("ui.footer.background") + "  " + T("ui.footer.exit"));
        root.Controls.Add(footer, 0, 3); Controls.Add(root);
        _bindings.Text(this, () => T("ui.app.title"));
        VisibleChanged += (_, _) => UpdateTelemetry(); Resize += (_, _) => UpdateTelemetry();
        _bindings.Start();
    }

    private void CommitEditors() { ValidateChildren(); if (_pages[2] is PresetEditorView presets) presets.CommitEdits(); }
    private void ShowPage()
    {
        var index = _tabs.SelectedIndex;
        if (index < 0) return;
        if (_pages[index] is null)
        {
            _pages[index] = index switch { 0 => new LauncherWorkspace(_viewModel), 1 => new ModelLibraryView(_viewModel), _ => new PresetEditorView(_viewModel) };
            _tabs.TabPages[index].Controls.Add(_pages[index]!);
        }
        UpdateTelemetry();
    }
    private void UpdateTelemetry() => _viewModel.SetMetricsVisible(Visible && WindowState != FormWindowState.Minimized && _tabs.SelectedIndex == 0);
    protected override void OnShown(EventArgs e) { WindowLayout.FitToWorkArea(this, Screen.FromControl(this).WorkingArea); base.OnShown(e); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Q)) { CommitEditors(); ExitRequested?.Invoke(this, EventArgs.Empty); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg != 0 && ((uint)m.Msg == _activateMessage || (uint)m.Msg == _legacyActivateMessage)) { ActivationRequested?.Invoke(this, EventArgs.Empty); return; }
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _viewModel.SetMetricsVisible(false); _bindings.Dispose(); Icon?.Dispose(); }
        base.Dispose(disposing);
        if (disposing) _uiFont.Dispose();
    }
}

public static class WindowLayout
{
    public static void FitToWorkArea(Form window, Rectangle available)
    {
        var size = new Size(Math.Min(window.Width, Math.Max(1, available.Width - 24)), Math.Min(window.Height, Math.Max(1, available.Height - 24)));
        window.MinimumSize = new Size(Math.Min(window.MinimumSize.Width, size.Width), Math.Min(window.MinimumSize.Height, size.Height));
        window.Size = size;
        window.Location = new Point(available.Left + (available.Width - size.Width) / 2, available.Top + (available.Height - size.Height) / 2);
    }
}
