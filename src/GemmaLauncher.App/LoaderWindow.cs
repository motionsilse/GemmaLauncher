using GemmaLauncher.Core;
using GemmaLauncher.Core.Accounts;
using static GemmaLauncher.Core.Localization;
namespace GemmaLauncher.App;

public sealed class LoaderWindow : Form
{
    private readonly LoaderShellViewModel shell;
    private readonly UiBindings bindings;
    private readonly Panel workspace = new() { Dock = DockStyle.Fill };
    private readonly Panel operationHost = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly uint activateMessage, legacyMessage;
    private MainWindow? localWindow;
    private AccountConnectionView? accountView;
    private LoaderFeature? shownFeature;
    private readonly Font ownedFont = new("Segoe UI", 10.5f);
    public event EventHandler? ActivationRequested;
    public event EventHandler? ExitRequested;
    public LoaderWindow(LoaderShellViewModel shell, uint activateMessage = 0, uint legacyActivateMessage = 0)
    {
        this.shell = shell; this.activateMessage = activateMessage; legacyMessage = legacyActivateMessage;
        Text = "Cat Loader"; Font = ownedFont; BackColor = Ui.Paper; ForeColor = Ui.Ink;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96,96); Size = new(1040,840); MinimumSize = new(720,560); StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        bindings = new(this,shell);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new(24,16,24,12) };
        root.ColumnStyles.Add(new(SizeType.Percent,100)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent,100)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        var brand = Ui.Label("Cat Loader"); Ui.SetFont(brand,19,FontStyle.Bold); brand.Margin = new(0,2,20,8); header.Controls.Add(brand);
        Tab("ui.loader.local",LoaderFeature.LocalModels); Tab("ui.loader.chat",LoaderFeature.ChatGpt);
        var settings = Ui.Button(); settings.Name = "LoaderSettings"; bindings.Localize(settings,"ui.settings.title"); settings.Click += (_,_) => { using var dialog = SettingsDialog(); dialog.ShowDialog(this); }; header.Controls.Add(settings);
        root.Controls.Add(header,0,0); root.Controls.Add(workspace,0,1); root.Controls.Add(operationHost,0,2); root.Controls.Add(new BackgroundOperationsView(shell),0,3); Controls.Add(root);
        bindings.Visible(operationHost, () => shell.SelectedFeature == LoaderFeature.LocalModels && shell.Local?.ShowOperation == true);
        bindings.Watch(ShowFeature); bindings.Start();
        _ = shell.SelectFeatureAsync(shell.SelectedFeature,default);
        VisibleChanged += (_,_) => { if (!Visible) shell.Account?.MaskKey(); UpdateMetrics(); };
        Resize += (_,_) => UpdateMetrics();
        void Tab(string key,LoaderFeature feature)
        {
            var button = Ui.Button(); button.Name = "Feature" + feature; button.AccessibleRole = AccessibleRole.PageTab;
            bindings.Localize(button,key); bindings.Watch(() => { button.BackColor = shell.SelectedFeature == feature ? Ui.Tint : Color.White; button.FlatAppearance.BorderColor = shell.SelectedFeature == feature ? Ui.Green : Ui.Line; });
            button.Click += async (_,_) => { ValidateChildren(); if (await shell.SelectFeatureAsync(feature,default)) workspace.SelectNextControl(null,true,true,true,false); };
            header.Controls.Add(button);
        }
    }
    private void ShowFeature()
    {
        if (shownFeature == shell.SelectedFeature) { UpdateMetrics(); return; }
        if (shell.SelectedFeature == LoaderFeature.LocalModels && shell.Local is { } local)
        {
            if (localWindow is null)
            {
                local.BackgroundOperationsFactory = () => new BackgroundOperationsView(shell,true);
                localWindow = new(local, embedded: true) { TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill };
                localWindow.ExitRequested += (_,_) => ExitRequested?.Invoke(this,EventArgs.Empty); workspace.Controls.Add(localWindow);
                localWindow.AttachOperationPanel(operationHost);
            }
            if (accountView is not null) accountView.Hide(); localWindow.Show(); localWindow.BringToFront();
            shownFeature = LoaderFeature.LocalModels;
        }
        else if (shell.SelectedFeature == LoaderFeature.ChatGpt && shell.Account is { } account)
        {
            if (accountView is null)
            {
                account.ConfirmSignOut = () => { using var dialog = new LoaderDialog(shell,T("ui.account.signOut")); var label = Ui.Label(T("ui.account.signOutHint")); dialog.Body.Controls.Add(label); dialog.Accept(T("ui.account.signOut")); return dialog.ShowDialog(this) == DialogResult.OK; };
                accountView = new(account) { OpenStateFolder = () => { if (Directory.Exists(AccountStateDirectory)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AccountStateDirectory) { UseShellExecute = true }); } };
                workspace.Controls.Add(accountView);
            }
            localWindow?.Hide(); accountView.Show(); accountView.BringToFront();
            shownFeature = LoaderFeature.ChatGpt;
        }
        UpdateMetrics();
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string AccountStateDirectory { get; set; } = AccountConnection.DefaultDirectory;
    private void UpdateMetrics() => shell.Local?.SetMetricsVisible(Visible && WindowState != FormWindowState.Minimized && shell.SelectedFeature == LoaderFeature.LocalModels && shell.Local.PageIndex == 0);
    private LoaderDialog SettingsDialog()
    {
        var dialog = new LoaderDialog(shell,T("ui.settings.title"));
        var stack = Ui.Stack(); dialog.Body.Controls.Add(Ui.Scroll(stack));
        var text = Ui.Label(T("ui.loader.closeHint")); Ui.Add(stack,text);
        var close = new CheckBox { Text = T("ui.settings.closeToTray"), AutoSize = true, Checked = shell.CloseToTray }; close.CheckedChanged += (_,_) => shell.CloseToTray = close.Checked; Ui.Add(stack,close);
        var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name", ValueMember = "Code", Width = 240 };
        language.Items.Add(new LanguageOption("auto",T("ui.language.auto"))); language.Items.AddRange(Localization.Current.Languages.ToArray()); language.SelectedItem = language.Items.Cast<LanguageOption>().FirstOrDefault(l => l.Code == shell.LanguageSelection);
        language.SelectedIndexChanged += (_,_) => { if (language.SelectedItem is LanguageOption option) shell.LanguageSelection = option.Code; }; Ui.Add(stack,language);
        dialog.Accept(T("ui.close")); return dialog;
    }
    protected override void OnShown(EventArgs e) { WindowLayout.FitToWorkArea(this,Screen.FromControl(this).WorkingArea); base.OnShown(e); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) { if (keyData == (Keys.Control | Keys.Q)) { ExitRequested?.Invoke(this,EventArgs.Empty); return true; } return base.ProcessCmdKey(ref msg,keyData); }
    protected override void WndProc(ref Message m) { if (m.Msg != 0 && ((uint)m.Msg == activateMessage || (uint)m.Msg == legacyMessage)) { ActivationRequested?.Invoke(this,EventArgs.Empty); return; } base.WndProc(ref m); }
    protected override void Dispose(bool disposing) { if (disposing) { bindings.Dispose(); shell.Account?.MaskKey(); if (shell.Local is not null) shell.Local.BackgroundOperationsFactory = null; Icon?.Dispose(); } base.Dispose(disposing); if (disposing) ownedFont.Dispose(); }
}

internal sealed class LoaderDialog : Form
{
    public Panel Body { get; } = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
    public LoaderDialog(LoaderShellViewModel shell,string title)
    {
        Text = title; Size = new(620,420); MinimumSize = new(400,300); StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; Padding = new(20);
        ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false; BackColor = Ui.Paper;
        Controls.Add(Body); Controls.Add(actions); Controls.Add(new BackgroundOperationsView(shell,true));
        var cancel = Ui.Button(); cancel.Text = T("ui.account.cancel"); cancel.DialogResult = DialogResult.Cancel; actions.Controls.Add(cancel); CancelButton = cancel;
    }
    public void Accept(string label) { var button = Ui.Button(); button.Text = label; button.DialogResult = DialogResult.OK; actions.Controls.Add(button); AcceptButton = button; }
}
