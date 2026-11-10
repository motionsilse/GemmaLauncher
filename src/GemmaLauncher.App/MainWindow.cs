using System.Drawing;
using System.Windows.Forms;
using static GemmaLauncher.Core.Localization;

namespace GemmaLauncher.App;

public sealed class MainWindow : Form
{
    private readonly LauncherViewModel _viewModel;
    private readonly UiBindings _bindings;
    private readonly Font _uiFont = new("Segoe UI", Ui.BodyFontSize);
    private readonly Panel _workspace = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 12) };
    private readonly LauncherPage?[] _pages = new LauncherPage?[3];
    private int _shownPage = -1;
    private readonly Panel _secondaryOperationHost = new() { Dock = DockStyle.Top, AutoSize = true, Margin = Padding.Empty };
    private readonly RunOperationPanel _operationPanel;
    private Panel? _externalOperationHost;
    private readonly uint _activateMessage;
    private readonly uint _legacyActivateMessage;
    private readonly Func<PresetEditDecision>? _previousPresetResolver;
    private readonly Func<string, bool>? _previousDeleteConfirmation;
    private readonly Action? _previousCommit;
    private readonly Func<CancellationToken, Task<AnisAccessInput?>>? _previousAnisAccess;
    public event EventHandler? ActivationRequested;
    public event EventHandler? ExitRequested;
    public int CreatedPageCount => _pages.Count(page => page is not null);

    public MainWindow(LauncherViewModel viewModel, uint activateMessage = 0, uint legacyActivateMessage = 0, bool embedded = false)
    {
        _activateMessage = activateMessage; _legacyActivateMessage = legacyActivateMessage;
        _viewModel = viewModel; _bindings = new(this, viewModel); _operationPanel = new(viewModel, CommitEditors);
        _previousPresetResolver = viewModel.ResolvePresetEdits; _previousDeleteConfirmation = viewModel.ConfirmPresetDeletion; _previousCommit = viewModel.CommitPresetEdits;
        viewModel.ResolvePresetEdits ??= () => LauncherDialogs.ResolvePreset(viewModel, this);
        viewModel.ConfirmPresetDeletion ??= name => LauncherDialogs.DeletePreset(viewModel, this, name);
        viewModel.CommitPresetEdits = CommitEditors;
        _previousAnisAccess = viewModel.RequestAnisAccess;
        viewModel.RequestAnisAccess ??= token => LauncherDialogs.AnisAccess(viewModel, this, token);
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        Font = _uiFont; BackColor = Ui.Paper; ForeColor = Ui.Ink;
        Size = new Size(1040, 840); MinimumSize = embedded ? Size.Empty : new Size(720, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = embedded ? new Padding(0, 8, 0, 0) : new Padding(24, 18, 24, 12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var brand = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var back = Ui.Button(); back.Name = "BackToLauncher"; _bindings.Localize(back, "ui.navigation.backToModels");
        back.Click += (_, _) => { CommitEditors(); viewModel.PageIndex = 0; };
        _bindings.Visible(back, () => viewModel.PageIndex != 0); brand.Controls.Add(back);
        var title = Ui.Label(); Ui.SetFont(title, 19, FontStyle.Bold); title.Margin = new Padding(0, 2, 16, 4);
        _bindings.Text(title, () => viewModel.PageIndex switch { 1 => T("ui.files.add"), 2 => T("ui.library.presets"), _ => T(embedded ? "ui.loader.local" : "ui.app.title") });
        brand.Controls.Add(title); header.Controls.Add(brand, 0, 0);
        var settings = Ui.Button(); settings.Name = "AppSettings"; _bindings.Localize(settings, "ui.settings.title");
        settings.Click += (_, _) => { using var dialog = CreateSettingsDialog(); dialog.ShowDialog(this); };
        header.Controls.Add(settings, 1, 0); root.Controls.Add(header, 0, 0);
        if (embedded) settings.Visible = false;
        _bindings.Watch(ShowPage); root.Controls.Add(_workspace, 0, 1);
        _bindings.Visible(_secondaryOperationHost, () => _externalOperationHost is null && (viewModel.PageIndex != 0 || UseCompactLayout) && viewModel.ShowOperation);
        root.Controls.Add(_secondaryOperationHost, 0, 2);
        var footer = Ui.Label(); footer.ForeColor = Ui.Muted; Ui.SetFont(footer, Ui.SecondaryFontSize); footer.Margin = new Padding(0, 10, 0, 0);
        _bindings.Text(footer, () => viewModel.StatusNote + (viewModel.StatusNote.Length > 0 ? "\n" : "") + "by Drugged Cat   ·   " + T(viewModel.IsServerRunning && viewModel.CloseToTray ? "ui.footer.background" : "ui.connection.localOnly"));
        root.Controls.Add(footer, 0, 3); Controls.Add(root);
        _bindings.Text(this, () => T("ui.app.title"));
        VisibleChanged += (_, _) => UpdateTelemetry(); Resize += (_, _) => { PlaceOperationPanel(); _bindings.RequestRefresh(); UpdateTelemetry(); };
        _bindings.Start();
    }
    public LauncherDialog CreateSettingsDialog()
    {
        var dialog = new LauncherDialog(_viewModel, T("ui.settings.title"), new Size(580, 360));
        dialog.Content.Controls.Add(new AppSettingsView(_viewModel));
        dialog.AddAction(T("ui.close"), DialogResult.OK, true); return dialog;
    }
    private void CommitEditors() { ValidateChildren(); if (_pages[2] is PresetEditorView presets) presets.CommitEdits(); }
    private void ShowPage()
    {
        var index = Math.Clamp(_viewModel.PageIndex, 0, 2);
        if (_shownPage == index) return;
        CommitEditors();
        if (_shownPage >= 0) _pages[_shownPage]!.Visible = false;
        if (_pages[index] is null)
        {
            _pages[index] = index switch { 0 => new LauncherWorkspace(_viewModel), 1 => new ModelLibraryView(_viewModel), _ => new PresetEditorView(_viewModel) };
            _workspace.Controls.Add(_pages[index]!);
        }
        _shownPage = index; PlaceOperationPanel(); _pages[index]!.Visible = true; _pages[index]!.BringToFront(); UpdateTelemetry();
    }
    private bool UseCompactLayout => ClientSize.Width * 96f / DeviceDpi < 900 || ClientSize.Height * 96f / DeviceDpi < 650;
    public void AttachOperationPanel(Panel host) { _externalOperationHost = host; PlaceOperationPanel(); _bindings.RequestRefresh(); }
    private void PlaceOperationPanel()
    {
        if (_shownPage < 0) return;
        var host = _externalOperationHost ?? (_shownPage == 0 && !UseCompactLayout ? ((LauncherWorkspace)_pages[0]!).OperationHost : _secondaryOperationHost);
        if (_operationPanel.Parent == host) return;
        _operationPanel.Parent?.Controls.Remove(_operationPanel); host.Controls.Add(_operationPanel);
    }
    private void UpdateTelemetry() => _viewModel.SetMetricsVisible(Visible && WindowState != FormWindowState.Minimized && _viewModel.PageIndex == 0);
    protected override void OnShown(EventArgs e) { if (TopLevel) WindowLayout.FitToWorkArea(this, Screen.FromControl(this).WorkingArea); base.OnShown(e); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Q)) { CommitEditors(); ExitRequested?.Invoke(this, EventArgs.Empty); return true; }
        if (keyData == Keys.Escape && _viewModel.PageIndex != 0) { CommitEditors(); _viewModel.PageIndex = 0; return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg != 0 && ((uint)m.Msg == _activateMessage || (uint)m.Msg == _legacyActivateMessage)) { ActivationRequested?.Invoke(this, EventArgs.Empty); return; }
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _viewModel.SetMetricsVisible(false); _bindings.Dispose(); Icon?.Dispose(); _viewModel.ResolvePresetEdits = _previousPresetResolver; _viewModel.ConfirmPresetDeletion = _previousDeleteConfirmation; _viewModel.CommitPresetEdits = _previousCommit; _viewModel.RequestAnisAccess = _previousAnisAccess; }
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
