using static GemmaLauncher.Core.Localization;
namespace GemmaLauncher.App;
public sealed class AccountConnectionView : UserControl
{
    private readonly UiBindings bindings;
    public AccountConnectionView(AccountConnectionViewModel vm)
    {
        Dock = DockStyle.Fill; BackColor = Ui.Paper;
        bindings = new(this, vm);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Percent,100)); layout.RowStyles.Add(new(SizeType.AutoSize));
        var stack = Ui.Stack(); stack.Padding = new(20);
        void Label(string key, bool heading = false) { var label = Ui.Label(); bindings.Localize(label,key); if (heading) Ui.SetFont(label, 15, FontStyle.Bold); Ui.Add(stack,label); }
        Label("ui.loader.chat", true); Label("ui.account.description");
        var status = Ui.Label(); Ui.SetFont(status, 15, FontStyle.Bold); bindings.Text(status, () => vm.StatusTitle); Ui.Add(stack,status);
        var account = Ui.Label(); bindings.Text(account, () => vm.AccountText); Ui.Add(stack,account);
        var hint = Ui.Label(); hint.ForeColor = Ui.Muted; bindings.Text(hint, () => vm.StatusDescription); Ui.Add(stack,hint);
        var reopen = Button(stack,"ui.account.reopen",vm.ReopenCommand); bindings.Visible(reopen, () => vm.CanReopen);
        var retry = Ui.Button(); bindings.Localize(retry,"ui.account.recheck"); retry.Click += (_,_) => vm.RecheckSavedState(); bindings.Visible(retry, () => vm.NeedsRecovery); Ui.Add(stack,retry);
        var recoveryFolder = Ui.Button(); bindings.Localize(recoveryFolder,"ui.account.openFolder"); recoveryFolder.Click += (_,_) => OpenStateFolder?.Invoke(); bindings.Visible(recoveryFolder, () => vm.NeedsRecovery); Ui.Add(stack,recoveryFolder);
        Label("ui.account.appConnection",true);
        Row("ui.account.address", () => vm.ApiAddress, () => vm.Copy(vm.ApiAddress));
        Row("ui.account.key", () => vm.ApiKeyText, () => vm.Copy(vm.CopyableKey ?? ""));
        var show = Button(stack,"ui.account.showKey",vm.KeyCommand); bindings.Text(show, () => vm.KeyLabel); show.Anchor = AnchorStyles.Left;
        Row("ui.account.model", () => vm.ApiModelId, () => vm.Copy(vm.ApiModelId));
        var copyAll = Button(stack,"ui.account.copyAll",vm.CopyAllCommand); copyAll.Anchor = AnchorStyles.Left;
        Label("ui.account.usage",true);
        var usage = Ui.Label(); bindings.Text(usage, () => vm.UsageText); Ui.Add(stack,usage);
        Button(stack,"ui.account.usage",vm.UsageCommand);
        var cancelQuery = Button(stack,"ui.account.cancelQuery",vm.CancelQueryCommand); cancelQuery.Anchor = AnchorStyles.Left; bindings.Visible(cancelQuery, () => vm.IsQueryBusy);
        var details = new CheckBox { AutoSize = true, Appearance = Appearance.Button, Margin = new(0,16,0,8) }; bindings.Localize(details,"ui.account.details"); Ui.Add(stack,details);
        var extra = Ui.Stack(); extra.Padding = Padding.Empty; extra.Visible = false; details.CheckedChanged += (_,_) => extra.Visible = details.Checked; Ui.Add(stack,extra);
        Button(extra,"ui.account.refresh",vm.RefreshCommand);
        var testHint = Ui.Label(); bindings.Localize(testHint,"ui.account.testHint"); Ui.Add(extra,testHint);
        Button(extra,"ui.account.test",vm.TestCommand); Button(extra,"ui.account.signOut",vm.SignOutCommand);
        var folder = Ui.Button(); bindings.Localize(folder,"ui.account.openFolder"); folder.Click += (_,_) => OpenStateFolder?.Invoke(); Ui.Add(extra,folder);
        layout.Controls.Add(Ui.Scroll(stack),0,0);
        var primary = Ui.Button(); primary.Name = "AccountPrimary"; primary.Dock = DockStyle.Fill; Ui.Primary(primary); bindings.Text(primary, () => vm.PrimaryLabel); bindings.Command(primary,vm.PrimaryCommand); layout.Controls.Add(primary,0,1);
        Controls.Add(layout); bindings.Start();
        void Row(string key, Func<string> text, Action copy)
        {
            Label(key);
            var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 }; row.ColumnStyles.Add(new(SizeType.Percent,100)); row.ColumnStyles.Add(new(SizeType.AutoSize));
            var value = new TextBox { ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill }; bindings.Text(value,text); bindings.Watch(() => value.AccessibleName = T(key));
            var button = Ui.Button(); bindings.Localize(button,"ui.account.copy"); bindings.Enabled(button, () => vm.IsConnected); bindings.Watch(() => button.AccessibleName = T(key) + " · " + T("ui.account.copy")); button.Click += (_,_) => copy();
            row.Controls.Add(value,0,0); row.Controls.Add(button,1,0); Ui.Add(stack,row);
        }
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action? OpenStateFolder { get; set; }
    private Button Button(TableLayoutPanel parent,string key,System.Windows.Input.ICommand command) { var button = Ui.Button(); bindings.Localize(button,key); bindings.Command(button,command); Ui.Add(parent,button); return button; }
    protected override void Dispose(bool disposing) { if (disposing) bindings.Dispose(); base.Dispose(disposing); }
}
