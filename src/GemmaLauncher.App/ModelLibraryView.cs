using System.Drawing;
using System.Windows.Forms;

namespace GemmaLauncher.App;

public sealed class ModelLibraryView : LauncherPage
{
    public ModelLibraryView(LauncherViewModel vm) : base(vm)
    {
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(columns);
        var imports = Ui.Stack(); var local = Ui.Stack();
        var left = Ui.Scroll(imports); left.Margin = new Padding(0, 0, 16, 0); columns.Controls.Add(left, 0, 0); columns.Controls.Add(Ui.Scroll(local), 1, 0);
        var title = Heading(imports, "ui.library.localGguf"); Ui.SetFont(title, 16, FontStyle.Bold);
        Heading(imports, "ui.library.localHint");
        var connect = Action(imports, "ui.files.connect", vm.ConnectModelFileCommand); connect.Anchor = AnchorStyles.Left;
        var hf = Ui.Label("Hugging Face"); Ui.SetFont(hf, 16, FontStyle.Bold); hf.Margin = new Padding(0, 24, 0, 8); Ui.Add(imports, hf);
        var address = Edit(imports, "ui.library.hfHint", () => vm.HuggingFaceAddress, value => vm.HuggingFaceAddress = value); address.AccessibleName = "Hugging Face URL";
        Bindings.Enabled(address, () => vm.CanSelectModel);
        var find = Action(imports, "ui.library.findFiles", vm.FindHuggingFaceCommand); find.Anchor = AnchorStyles.Left;
        var files = Choice(imports, "HuggingFaceFiles", "ui.library.downloadFile", () => vm.HuggingFaceFiles, () => vm.SelectedHuggingFaceFile,
            value => vm.SelectedHuggingFaceFile = value, value => value.DisplayName);
        Bindings.Enabled(files, () => vm.CanSelectModel);
        var add = Action(imports, "ui.library.register", vm.AddHuggingFaceCommand); Ui.Primary(add); add.Anchor = AnchorStyles.Left;
        var mine = Heading(local, "ui.library.mine"); Ui.SetFont(mine, 16, FontStyle.Bold);
        Heading(local, "ui.library.mineHint");
        ModelPresentation? selected = null;
        var list = Choice(local, "LibraryModelSelector", "ui.library.userModel", () => vm.LibraryModels.Where(card => card.IsUserModel),
            () => selected is not null && vm.LibraryModels.Contains(selected) ? selected : vm.LibraryModels.FirstOrDefault(card => card.IsUserModel),
            value => { selected = value; Bindings.RequestRefresh(); }, value => value.DisplayName);
        Value(local, () => (list.SelectedItem as ModelPresentation)?.Definition.Artifacts[0].Filename ?? "");
        var use = Action(local, "ui.library.use", vm.UseLibraryModelCommand, () => list.SelectedItem); use.Anchor = AnchorStyles.Left;
        var remove = Action(local, "ui.library.remove", vm.RemoveLibraryModelCommand, () => list.SelectedItem); remove.Anchor = AnchorStyles.Left;
        Heading(local, "ui.files.advanced");
        Action(local, "ui.files.find", vm.ChooseModelFolderCommand);
        Action(local, "ui.files.catalog", vm.ImportCatalogCommand);
        StartBindings();
    }
}
