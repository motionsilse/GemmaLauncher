using static GemmaLauncher.Core.Localization;
namespace GemmaLauncher.App;

public sealed class AccountConnectionView : UserControl
{
    private readonly UiBindings bindings;
    private readonly AccountConnectionViewModel vm;
    private readonly Panel scroll;
    private readonly TableLayoutPanel columns, account, server;
    private readonly Control accountTitle;
    private bool compact, arranging;
    public AccountConnectionView(AccountConnectionViewModel vm)
    {
        this.vm = vm;
        Dock = DockStyle.Fill; BackColor = Ui.Paper; Ui.SetFont(this, 12);
        bindings = new(this, vm);
        var content = Ui.Stack(); content.Padding = new(24, 20, 24, 16); content.BackColor = Ui.Paper;
        scroll = Ui.Scroll(content); scroll.BackColor = Ui.Paper; Controls.Add(scroll);
        var pageHead = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        pageHead.ColumnStyles.Add(new(SizeType.Percent,100)); pageHead.ColumnStyles.Add(new(SizeType.AutoSize)); pageHead.RowStyles.Add(new(SizeType.AutoSize));
        var heading = Ui.Label(); bindings.Localize(heading,"ui.loader.chat"); Ui.SetFont(heading,20,FontStyle.Bold); heading.Margin = new(0, 0, 0, 6); pageHead.Controls.Add(heading,0,0);
        var help = Action(pageHead,"ui.account.help",null,"AccountHelp"); help.FlatAppearance.BorderSize = 0; help.Anchor = AnchorStyles.Top | AnchorStyles.Right; help.Click += (_,_) => ShowHelp(); Ui.Add(content,pageHead);
        var intro = Label(content, "ui.account.description"); intro.ForeColor = Ui.Muted; intro.Margin = new(0, 0, 0, 20);
        columns = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        columns.ColumnStyles.Add(new(SizeType.Absolute, 280)); columns.ColumnStyles.Add(new(SizeType.Percent, 100));
        account = Ui.Stack(); account.Name = "AccountPanel"; account.Padding = new(20); account.Margin = new(0, 0, 20, 0);
        server = Ui.Stack(); server.Padding = new(20); server.Margin = Padding.Empty;
        accountTitle = Label(account, "ui.account.accountHeading", 15);
        var identity = Ui.Label(); Ui.SetFont(identity, 12, FontStyle.Bold); bindings.Text(identity, () => vm.AccountText); Ui.Add(account, identity);
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = Padding.Empty };
        var login = Action(actions, "ui.account.login", vm.LoginCommand, "AccountLogin", () => vm.LoginLabel); Ui.Primary(login);
        bindings.Visible(login, () => vm.Account.State == Core.Accounts.SavedAccountState.Missing || vm.IsLoggingIn);
        var reopen = Action(actions, "ui.account.reopen", vm.ReopenCommand, "AccountReopen"); bindings.Visible(reopen, () => vm.CanReopen);
        actions.SizeChanged += (_, _) => { foreach (Button button in actions.Controls) button.MaximumSize = new(Math.Max(92, actions.ClientSize.Width - 6), 0); };
        var logout = Action(actions, "ui.account.signOut", vm.SignOutCommand, "AccountSignOut"); bindings.Visible(logout, () => vm.Account.State == Core.Accounts.SavedAccountState.Available);
        var compactUsage = Action(actions, "ui.account.usage", null, "AccountCompactUsage"); compactUsage.Click += (_, _) => ShowUsage();
        bindings.Visible(compactUsage, () => compact && vm.Account.State == Core.Accounts.SavedAccountState.Available);
        Ui.Add(account, actions);
        var accountHint = Ui.Label(); accountHint.ForeColor = Ui.Muted; Ui.SetFont(accountHint, Ui.SecondaryFontSize); bindings.Text(accountHint, () => vm.AccountHint); Ui.Add(account, accountHint);
        bindings.Visible(accountHint, () => !compact || vm.Account.State != Core.Accounts.SavedAccountState.Available || vm.NeedsAuthentication);
        var recovery = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
        Action(recovery, "ui.account.recheck", null).Click += (_, _) => vm.RecheckSavedState();
        Action(recovery, "ui.account.openFolder", null).Click += (_, _) => OpenStateFolder?.Invoke();
        bindings.Visible(recovery, () => vm.NeedsStorageRecovery); Ui.Add(account, recovery);
        var usage = Ui.Stack(); usage.Padding = new(0, 20, 0, 0);
        Label(usage, "ui.account.usageHeading", 12);
        var usageText = Ui.Label(); Ui.SetFont(usageText, Ui.SecondaryFontSize); bindings.Text(usageText, () => vm.UsageText); Ui.Add(usage, usageText);
        var usageButtons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top };
        Action(usageButtons, "ui.account.usage", vm.UsageCommand);
        var cancelUsage = Action(usageButtons, "ui.account.cancelQuery", vm.CancelQueryCommand); bindings.Visible(cancelUsage, () => vm.IsQueryBusy);
        Ui.Add(usage, usageButtons); Ui.Add(account, usage); bindings.Visible(usage, () => !compact && vm.Account.State == Core.Accounts.SavedAccountState.Available);
        var serverHead = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        serverHead.ColumnStyles.Add(new(SizeType.Percent, 100)); serverHead.ColumnStyles.Add(new(SizeType.AutoSize));
        var serverTitles = Ui.Stack(); serverTitles.Padding = Padding.Empty;
        Label(serverTitles, "ui.account.serverHeading", 15);
        var status = Ui.Label(); status.ForeColor = Ui.Green; bindings.Text(status, () => vm.ServerStatus); Ui.Add(serverTitles, status);
        var notice = Ui.Label(); notice.ForeColor = Ui.Muted; Ui.SetFont(notice,Ui.SecondaryFontSize); bindings.Localize(notice,"ui.account.usageNotice"); Ui.Add(serverTitles,notice);
        serverHead.Controls.Add(serverTitles, 0, 0);
        var toggle = Ui.Button(); toggle.Name = "AccountPrimary"; bindings.Text(toggle, () => vm.ServerLabel); bindings.Command(toggle, vm.ServerCommand);
        toggle.Anchor = AnchorStyles.Top | AnchorStyles.Right; toggle.Margin = new(12, 0, 0, 0);
        bindings.Visible(toggle, () => vm.Account.State == Core.Accounts.SavedAccountState.Available);
        serverHead.Controls.Add(toggle, 1, 0); Ui.Add(server, serverHead);
        var setup = Ui.Stack(); setup.Padding = new(0, 12, 0, 8);
        Label(setup, "ui.account.setupHeading", 12); Label(setup, "ui.account.steps");
        bindings.Visible(setup, () => vm.Account.State != Core.Accounts.SavedAccountState.Available); Ui.Add(server, setup);
        var off = Ui.Label(); bindings.Text(off, () => vm.ServerHint); bindings.Visible(off, () => !vm.IsConnected && vm.Account.State == Core.Accounts.SavedAccountState.Available); Ui.Add(server, off);
        var values = Ui.Stack(); values.Padding = new(0, 16, 0, 0); values.Name = "AccountConnectionValues";
        Label(values, "ui.account.inputHint");
        var copied = Ui.Label(); copied.ForeColor = Ui.Green; copied.Name = "AccountCopyResult";
        Field("ui.account.address", () => vm.ApiAddress, () => vm.Copy(vm.ApiAddress));
        Field("ui.account.key", () => vm.ApiKeyText, () => vm.Copy(vm.CopyableKey ?? ""), true);
        Field("ui.account.model", () => vm.ApiModelId, () => vm.Copy(vm.ApiModelId));
        Action(values, "ui.account.copyAll", vm.CopyAllCommand).Click += (_, _) => copied.Text = T("ui.account.copied");
        Ui.Add(values, copied); bindings.Visible(values, () => vm.IsConnected); Ui.Add(server, values);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new(0, 12, 0, 0) };
        Action(tools, "ui.account.details", null).Click += (_, _) => ShowDiagnostics(); Ui.Add(server, tools);
        columns.Controls.Add(account, 0, 0); columns.Controls.Add(server, 1, 0); Ui.Add(content, columns);
        var previousBusy = vm.IsBusy; var previousAccount = vm.Account.State;
        bindings.Watch(() =>
        {
            if (ContainsFocus && (previousBusy && !vm.IsBusy || previousAccount != vm.Account.State))
                (vm.Account.State == Core.Accounts.SavedAccountState.Missing ? login : toggle).Focus();
            if (!vm.IsConnected) copied.Text = "";
            previousBusy = vm.IsBusy; previousAccount = vm.Account.State;
        });
        bindings.Watch(() =>
        {
            if (compact)
            {
                var width = Math.Max(1, account.ClientSize.Width - account.Padding.Horizontal);
                account.Height = account.Padding.Vertical + account.Controls.Cast<Control>().Where(c => c.Visible)
                    .Sum(c => c.GetPreferredSize(new Size(width - c.Margin.Horizontal, 0)).Height + c.Margin.Vertical);
            }
        });
        scroll.ClientSizeChanged += (_, _) => Arrange();
        bindings.Start(); Arrange();
        void Field(string key, Func<string> text, Action copy, bool keyField = false)
        {
            var title = Label(values, key); title.ForeColor = Ui.Muted; Ui.SetFont(title, Ui.SecondaryFontSize); title.Margin = new(0, 14, 0, 3);
            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
            row.ColumnStyles.Add(new(SizeType.Percent, 100)); row.ColumnStyles.Add(new(SizeType.AutoSize)); row.ColumnStyles.Add(new(SizeType.AutoSize));
            var value = new TextBox { ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = Color.White, Margin = new(0, 10, 10, 0) };
            bindings.Text(value, text); bindings.Watch(() => value.AccessibleName = T(key)); row.Controls.Add(value, 0, 0);
            if (keyField) Action(row, "ui.account.showKey", vm.KeyCommand, text: () => vm.KeyLabel);
            var button = Action(row, "ui.account.copy", null); bindings.Watch(() => button.AccessibleName = T(key) + " · " + T("ui.account.copy")); button.Click += (_, _) => { copy(); copied.Text = T("ui.account.copied"); }; Ui.Add(values, row);
        }
    }
    private void Arrange()
    {
        if (arranging || IsDisposed) return; arranging = true;
        try
        {
            var next = scroll.ClientSize.Width < 850 * DeviceDpi / 96;
            if (next != compact)
            {
                compact = next; columns.SuspendLayout();
                columns.Controls.Remove(account); columns.Controls.Remove(server); columns.ColumnStyles.Clear(); columns.RowStyles.Clear();
                columns.ColumnCount = compact ? 1 : 2; columns.RowCount = compact ? 2 : 1;
                columns.ColumnStyles.Add(compact ? new(SizeType.Percent, 100) : new(SizeType.Absolute, 280 * DeviceDpi / 96));
                if (!compact) columns.ColumnStyles.Add(new(SizeType.Percent, 100));
                columns.Controls.Add(account, 0, 0); columns.Controls.Add(server, compact ? 0 : 1, compact ? 1 : 0);
                account.Margin = compact ? new(0, 0, 0, 16) : new(0, 0, 20, 0);
                account.Padding = compact ? new(16, 10, 16, 10) : new(20); accountTitle.Visible = !compact;
                account.AutoSize = !compact;
                columns.ResumeLayout(true); bindings.Refresh();
            }
        }
        finally { arranging = false; }
    }
    private Label Label(TableLayoutPanel stack, string key, float size = 0)
    {
        var label = Ui.Label(); bindings.Localize(label, key); if (size > 0) Ui.SetFont(label, size, FontStyle.Bold); Ui.Add(stack, label); return label;
    }
    private Button Action(Control parent, string key, System.Windows.Input.ICommand? command, string name = "", Func<string>? text = null)
    {
        var button = Ui.Button(); button.Name = name;
        if (text is null) bindings.Localize(button, key); else { button.UseMnemonic = false; bindings.ActionText(button,text); }
        if (command is not null) bindings.Command(button, command);
        if (parent is TableLayoutPanel table && table.ColumnCount == 1) Ui.Add(table, button); else parent.Controls.Add(button); return button;
    }
    private void ShowHelp() => ShowDialog("ui.account.help", (body, b) =>
    {
        var steps = Ui.Label(); b.Localize(steps, "ui.account.steps"); Ui.Add(body, steps);
        var hint = Ui.Label(); b.Localize(hint, "ui.account.keepLogin"); Ui.Add(body, hint);
    });
    private void ShowUsage() => ShowDialog("ui.account.usageHeading", (body, b) =>
    {
        var text = Ui.Label(); b.Text(text, () => vm.UsageText); Ui.Add(body, text);
        var refresh = Ui.Button(); b.Localize(refresh, "ui.account.usage"); b.Command(refresh, vm.UsageCommand); Ui.Add(body, refresh);
    });
    private void ShowDiagnostics() => ShowDialog("ui.account.details", (body, b) =>
    {
        var text = Ui.Label(); b.Localize(text, "ui.account.testHint"); Ui.Add(body, text);
        foreach (var item in new[] { ("ui.account.refresh", vm.RefreshCommand), ("ui.account.test", vm.TestCommand) })
        { var button = Ui.Button(); b.Localize(button, item.Item1); b.Command(button, item.Item2); Ui.Add(body, button); }
        var result = Ui.Label(); result.Name = "AccountDiagnosticsResult"; b.Text(result, () => vm.DiagnosticsText); Ui.Add(body, result);
    });
    private void ShowDialog(string title, Action<TableLayoutPanel, UiBindings> build)
    {
        using var dialog = new Form { Text = T(title), Size = new(620, 460), MinimumSize = new(420, 340), StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false, AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new(96, 96), BackColor = Ui.Paper, Padding = new(20), Font = Font };
        using var b = new UiBindings(dialog, vm);
        var body = Ui.Stack(); dialog.Controls.Add(Ui.Scroll(body)); build(body, b);
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Bottom, WrapContents = true };
        var stop = Ui.Button(); b.Text(stop, () => vm.IsLoggingIn ? vm.LoginLabel : vm.ServerLabel); b.Visible(stop, () => vm.IsBusy || vm.IsRunning); b.Command(stop, vm.PrimaryCommand); actions.Controls.Add(stop);
        var cancel = Ui.Button(); b.Localize(cancel, "ui.account.cancelQuery"); b.Command(cancel, vm.CancelQueryCommand); b.Visible(cancel, () => vm.IsQueryBusy); actions.Controls.Add(cancel);
        var close = Ui.Button(); b.Localize(close, "ui.close"); close.DialogResult = DialogResult.Cancel; actions.Controls.Add(close); dialog.CancelButton = close;
        dialog.Controls.Add(actions); b.Start(); dialog.ShowDialog(FindForm());
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action? OpenStateFolder { get; set; }
    protected override void Dispose(bool disposing) { if (disposing) bindings.Dispose(); base.Dispose(disposing); }
}
