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
    private readonly Font ownedFont = new("Segoe UI", Ui.BodyFontSize);
    private readonly System.Windows.Forms.Timer? updateTimer;
    private readonly CancellationTokenSource updateLifetime = new();
    public event EventHandler? ActivationRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? HiddenToTray;
    public event EventHandler<ReleaseUpdate>? UpdateFound;
    public LoaderWindow(LoaderShellViewModel shell, uint activateMessage = 0, uint legacyActivateMessage = 0,
        Func<CancellationToken, Task<ReleaseUpdate?>>? checkUpdate = null,
        Func<ReleaseUpdate, IProgress<int>, CancellationToken, Task>? installUpdate = null, Action<Uri>? openRelease = null)
    {
        this.shell = shell; this.activateMessage = activateMessage; legacyMessage = legacyActivateMessage;
        Text = "Cat Loader"; Font = ownedFont; BackColor = Ui.Paper; ForeColor = Ui.Ink;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96,96); Size = new(1040,840); MinimumSize = new(720,560); StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        bindings = new(this,shell);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new(SizeType.Percent,100)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent,100)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        var headerRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 80, ColumnCount = 2, RowCount = 1, Padding = new(24,12,24,0), BackColor = Color.White };
        headerRow.RowStyles.Add(new(SizeType.AutoSize));
        headerRow.ColumnStyles.Add(new(SizeType.Percent,100)); headerRow.ColumnStyles.Add(new(SizeType.AutoSize));
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        var brand = Ui.Label("Cat Loader"); Ui.SetFont(brand,17,FontStyle.Bold); brand.Margin = new(0,8,20,8); header.Controls.Add(brand);
        Tab("ui.loader.local",LoaderFeature.LocalModels); Tab("ui.loader.chat",LoaderFeature.ChatGpt);
        var settings = Ui.Button(); settings.FlatAppearance.BorderSize = 0; settings.Name = "LoaderSettings"; bindings.Localize(settings,"ui.settings.title"); settings.Click += (_,_) => { using var dialog = SettingsDialog(); dialog.ShowDialog(this); }; headerRow.Controls.Add(header,0,0); headerRow.Controls.Add(settings,1,0);
        root.Controls.Add(headerRow,0,0); root.Controls.Add(workspace,0,3); root.Controls.Add(operationHost,0,4); root.Controls.Add(new LoaderStatusBar(shell),0,5); Controls.Add(root);
        var saveError = Ui.Label();
        saveError.Name = "SettingsSaveError";
        saveError.ForeColor = Color.Firebrick;
        saveError.Margin = new(24, 6, 24, 6);
        bindings.Text(saveError, () => shell.SettingsSaveError);
        bindings.Visible(saveError, () => shell.SettingsSaveError.Length > 0);
        root.SizeChanged += (_, _) => saveError.MaximumSize = new(Math.Max(1, root.ClientSize.Width - saveError.Margin.Horizontal), 0);
        root.Controls.Add(saveError, 0, 2);
        if (checkUpdate is not null)
        {
            var notice = Ui.Stack();
            notice.Padding = Padding.Empty;
            notice.BackColor = Ui.Paper;
            notice.Margin = new Padding(24, 6, 24, 0);
            var update = Ui.Button();
            update.Name = "UpdateNotice";
            update.BackColor = Ui.Tint;
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
            actions.Controls.Add(update);
            var releasePage = Ui.Button();
            releasePage.Name = "UpdateReleasePage";
            bindings.Localize(releasePage, "ui.update.releasePage");
            actions.Controls.Add(releasePage);
            Ui.Add(notice, actions);
            var errorLabel = Ui.Label();
            errorLabel.Name = "UpdateError";
            errorLabel.ForeColor = Color.Firebrick;
            notice.SizeChanged += (_, _) => errorLabel.MaximumSize = new(Math.Max(1, notice.ClientSize.Width), 0);
            Ui.Add(notice, errorLabel);
            root.Controls.Add(notice, 0, 1);
            ReleaseUpdate? available = null;
            Version? lastNotified = null;
            var checking = false;
            var installing = false;
            var percent = 0;
            string? installError = null;
            bool CanInstall() => installUpdate is not null && available?.Package is not null;
            bindings.ActionText(update, () => available is null ? "" : installing
                ? percent >= 100 ? T("ui.update.applying") : T("ui.update.progress", percent)
                : T(CanInstall() ? "ui.update.install" : "ui.update.available", available.Version.ToString(3)));
            bindings.Visible(notice, () => available is not null);
            bindings.Enabled(update, () => !installing);
            bindings.Visible(releasePage, () => installError is not null && CanInstall());
            bindings.Text(errorLabel, () => installError is null ? "" : T("ui.update.failed", installError));
            bindings.Visible(errorLabel, () => installError is not null);
            releasePage.Click += (_, _) => { if (available is not null) OpenRelease(available); };
            update.Click += async (_, _) =>
            {
                if (available is null || installing) return;
                if (!CanInstall()) { OpenRelease(available); return; }
                var token = updateLifetime.Token;
                installing = true;
                installError = null;
                percent = 0;
                bindings.Refresh();
                try
                {
                    var progress = new Progress<int>(value => { percent = value; bindings.RequestRefresh(); });
                    await installUpdate!(available, progress, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or HttpRequestException or OperationCanceledException or System.Security.SecurityException)
                {
                    installError = error is OperationCanceledException ? T("engine.install.timedOut") : error.Message;
                }
                finally
                {
                    installing = false;
                    if (!token.IsCancellationRequested) bindings.RequestRefresh();
                }
            };
            updateTimer = new() { Interval = (int)TimeSpan.FromHours(1).TotalMilliseconds };
            HandleCreated += async (_, _) => { if (!updateTimer.Enabled) { updateTimer.Start(); await RefreshUpdateAsync(); } };
            updateTimer.Tick += async (_, _) => await RefreshUpdateAsync();

            void OpenRelease(ReleaseUpdate release)
            {
                try
                {
                    if (openRelease is not null) openRelease(release.DownloadPage);
                    else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(release.DownloadPage.AbsoluteUri) { UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception ex) { MessageBox.Show(this, T("ui.note.browserFailed", ex.Message), Text); }
            }

            async Task RefreshUpdateAsync()
            {
                if (checking || installing) return;
                checking = true;
                try
                {
                    var token = updateLifetime.Token;
                    var result = await checkUpdate(token);
                    if (token.IsCancellationRequested || installing) return;
                    if (available?.Version != result?.Version) installError = null;
                    available = result;
                    bindings.RequestRefresh();
                    if (result is not null && lastNotified != result.Version)
                    {
                        lastNotified = result.Version;
                        UpdateFound?.Invoke(this, result);
                    }
                }
                catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException) { }
                finally { checking = false; }
            }
        }
        // FlowLayout's unconstrained preferred width wraps every tab and inflates
        // an AutoSize table row. Measure against its actual available width.
        void FitHeader()
        {
            brand.Visible = headerRow.Width >= 850 * DeviceDpi / 96;
            if (header.Width > 0) headerRow.Height = header.GetPreferredSize(new Size(header.Width, 0)).Height + headerRow.Padding.Vertical + header.Margin.Vertical;
        }
        header.SizeChanged += (_, _) => FitHeader(); bindings.Watch(FitHeader);
        bindings.Visible(operationHost, () => shell.SelectedFeature == LoaderFeature.LocalModels && shell.Local?.ShowOperation == true);
        bindings.Watch(ShowFeature); bindings.Start();
        _ = shell.SelectFeatureAsync(shell.SelectedFeature,default);
        VisibleChanged += (_,_) => { if (!Visible) shell.Account?.MaskKey(); UpdateMetrics(); };
        Resize += (_,_) => UpdateMetrics();
        void Tab(string key,LoaderFeature feature)
        {
            var button = new FeatureTab(); button.Name = "Feature" + feature; button.AccessibleRole = AccessibleRole.PageTab;
            bindings.Localize(button,key); bindings.Watch(() => { button.Selected = shell.SelectedFeature == feature; });
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
                account.ConfirmSignOut = () => { using var dialog = new LoaderDialog(shell,T("ui.account.signOut")); var label = Ui.Label(account.AccountText + "\n\n" + T("ui.account.signOutHint")); label.MaximumSize = new(520,0); dialog.Body.Controls.Add(label); dialog.Accept(T("ui.account.signOut")); dialog.AcceptButton = dialog.CancelButton; return dialog.ShowDialog(this) == DialogResult.OK; };
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
        var dialog = new LoaderDialog(shell,T("ui.settings.title"), confirmation: false);
        var settingsBindings = new UiBindings(dialog,shell);
        dialog.Disposed += (_,_) => settingsBindings.Dispose();
        settingsBindings.Localize(dialog,"ui.settings.title");
        var stack = Ui.Stack(); dialog.Body.Controls.Add(Ui.Scroll(stack));
        var text = Ui.Label(); settingsBindings.Localize(text,"ui.loader.closeHint"); Ui.Add(stack,text);
        var close = new CheckBox { AutoSize = true, Name = "CloseToTray" }; settingsBindings.Localize(close,"ui.settings.closeToTray"); settingsBindings.Check(close,() => shell.CloseToTray,value => shell.CloseToTray = value); Ui.Add(stack,close);
        var startup = new CheckBox { AutoSize = true, Name = "StartWithWindows" };
        settingsBindings.Localize(startup, "ui.settings.startWithWindows");
        settingsBindings.Check(startup, () => shell.StartWithWindows, value => shell.StartWithWindows = value);
        Ui.Add(stack, startup);
        var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name", ValueMember = "Code", Width = 240 };
        IEnumerable<LanguageOption> Languages() => new[] { new LanguageOption("auto",T("ui.language.auto")) }.Concat(Localization.Current.Languages);
        settingsBindings.Choice(language,Languages,() => Languages().FirstOrDefault(option => option.Code == shell.LanguageSelection),option => shell.LanguageSelection = option.Code,option => option.Name); Ui.Add(stack,language);
        var saved = Ui.Label(); settingsBindings.Text(saved, () => shell.SettingsSaveError.Length > 0 ? shell.SettingsSaveError : T("ui.settings.autoSaved")); Ui.Add(stack,saved);
        settingsBindings.Start(); return dialog;
    }
    protected override void OnShown(EventArgs e) { WindowLayout.FitToWorkArea(this,Screen.FromControl(this).WorkingArea); base.OnShown(e); }
    internal void SendToTray()
    {
        if (!Visible) return;
        Hide();
        HiddenToTray?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && shell is { CloseToTray: true }) SendToTray();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) { if (keyData == (Keys.Control | Keys.Q)) { ExitRequested?.Invoke(this,EventArgs.Empty); return true; } return base.ProcessCmdKey(ref msg,keyData); }
    protected override void WndProc(ref Message m) { if (m.Msg != 0 && ((uint)m.Msg == activateMessage || (uint)m.Msg == legacyMessage)) { ActivationRequested?.Invoke(this,EventArgs.Empty); return; } base.WndProc(ref m); }
    protected override void Dispose(bool disposing)
    {
        var releaseResources = disposing && !IsDisposed;
        if (releaseResources)
        {
            updateTimer?.Dispose();
            updateLifetime.Cancel();
            updateLifetime.Dispose();
            bindings.Dispose();
            shell.Account?.MaskKey();
            if (shell.Local is not null) shell.Local.BackgroundOperationsFactory = null;
            Icon?.Dispose();
        }
        base.Dispose(disposing);
        if (releaseResources) ownedFont.Dispose();
    }
}

internal sealed class LoaderDialog : Form
{
    private readonly UiBindings bindings;
    public Panel Body { get; } = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
    public LoaderDialog(LoaderShellViewModel shell,string title,bool confirmation = true)
    {
        bindings = new(this,shell);
        Text = title; Size = new(620,420); MinimumSize = new(400,300); StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96,96); Ui.SetFont(this,Ui.BodyFontSize); Padding = new(20);
        ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false; BackColor = Ui.Paper;
        Controls.Add(Body); Controls.Add(actions); Controls.Add(new BackgroundOperationsView(shell,true));
        var close = Ui.Button(); bindings.Localize(close,confirmation ? "ui.account.cancel" : "ui.close"); close.DialogResult = DialogResult.Cancel; actions.Controls.Add(close); CancelButton = close;
        if (!confirmation) AcceptButton = close;
        bindings.Start();
    }
    public void Accept(string label) { var button = Ui.Button(); button.Text = label; button.ForeColor = Color.FromArgb(158,49,42); button.DialogResult = DialogResult.OK; actions.Controls.Add(button); AcceptButton = button; }
    protected override void Dispose(bool disposing) { if (disposing) bindings.Dispose(); base.Dispose(disposing); }
}
