using System.Drawing;
using System.Windows.Forms;

namespace GemmaLauncher.App;

public sealed class RunOperationPanel : UserControl
{
    private readonly UiBindings _bindings;
    public RunOperationPanel(LauncherViewModel vm, Action commit)
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Top;
        BackColor = Ui.Paper; Margin = Padding.Empty; _bindings = new(this, vm);
        var root = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(14), BackColor = Ui.Paper };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var summary = Ui.Stack(); summary.Padding = Padding.Empty; summary.Margin = new Padding(0, 6, 16, 0); summary.BackColor = Ui.Paper;
        var model = Ui.Label(); model.Name = "OperationModel"; Ui.SetFont(model, Ui.BodyFontSize, FontStyle.Bold);
        _bindings.Text(model, () => vm.SelectedPresentation?.DisplayName ?? ""); _bindings.Visible(model, () => vm.ShowOperationModel); Ui.Add(summary, model);
        var text = Ui.Label(); _bindings.Text(text, () => vm.StatusTitle + "\n" + vm.StatusDescription); Ui.Add(summary, text); root.Controls.Add(summary, 0, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var prerequisite = Ui.Button(); _bindings.Localize(prerequisite, "ui.runtime.install"); _bindings.Command(prerequisite, vm.InstallPrerequisiteCommand); _bindings.Visible(prerequisite, () => vm.NeedsRuntimePrerequisite); actions.Controls.Add(prerequisite);
        var primary = Ui.Button(); Ui.Primary(primary); primary.Name = "PrimaryAction"; _bindings.Text(primary, () => vm.PrimaryLabel); _bindings.Command(primary, vm.PrimaryCommand, before: commit); _bindings.Visible(primary, () => vm.ShowPrimaryAction); actions.Controls.Add(primary);
        root.Controls.Add(actions, 1, 0);
        var details = Ui.Button(); details.Name = "ErrorDetails"; _bindings.Localize(details, "ui.files.logs"); _bindings.Command(details, vm.OpenLogsCommand); _bindings.Visible(details, () => vm.HasError); actions.Controls.Add(details);
        var progress = new ProgressBar { Name = "OperationProgress", Height = 7, Dock = DockStyle.Top, Margin = new Padding(0, 10, 0, 0) };
        _bindings.Watch(() => { progress.Style = vm.IsIndeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous; progress.Value = (int)Math.Clamp(vm.ProgressPercent, 0, 100); });
        _bindings.Visible(progress, () => vm.IsProgressVisible); root.Controls.Add(progress, 0, 1); root.SetColumnSpan(progress, 2);
        _bindings.Visible(this, () => vm.ShowOperation); Controls.Add(root); _bindings.Start();
    }
    protected override void Dispose(bool disposing) { if (disposing) _bindings.Dispose(); base.Dispose(disposing); }
}
