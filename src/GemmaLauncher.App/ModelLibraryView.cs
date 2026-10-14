using System.Windows.Forms;

namespace GemmaLauncher.App;

public sealed class ModelLibraryView : LauncherPage
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Name = "ModelTabs" };
    private readonly bool[] _created = new bool[3];
    private bool _switching;
    public ModelLibraryView(LauncherViewModel vm) : base(vm)
    {
        foreach (var key in new[] { "ui.library.recommended", "ui.library.mine", "ui.files.add" })
        {
            var tab = new TabPage { BackColor = Ui.Paper }; Bindings.Localize(tab, key); _tabs.TabPages.Add(tab);
        }
        Controls.Add(_tabs);
        _tabs.SelectedIndex = Math.Clamp(vm.ModelPageIndex, 0, 2);
        _tabs.SelectedIndexChanged += (_, _) => { if (!_switching) vm.ModelPageIndex = _tabs.SelectedIndex; CreatePage(); };
        // Create before refreshing bindings: a newly created page registers its own updates.
        vm.PropertyChanged += PageChanged;
        CreatePage(); StartBindings();
    }
    private void PageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LauncherViewModel.ModelPageIndex))
        {
            _switching = true; try { _tabs.SelectedIndex = Math.Clamp(ViewModel.ModelPageIndex, 0, 2); CreatePage(); } finally { _switching = false; }
        }
    }
    private void CreatePage()
    {
        var index = _tabs.SelectedIndex;
        if (index < 0 || _created[index]) return;
        _created[index] = true;
        var stack = Ui.Stack(); _tabs.TabPages[index].Controls.Add(Ui.Scroll(stack));
        if (index == 0) Recommended(stack);
        else if (index == 1) Mine(stack);
        else AddModel(stack);
        Bindings.RequestRefresh();
    }
    private void Recommended(TableLayoutPanel stack)
    {
        Heading(stack, "ui.library.headline"); Heading(stack, "ui.library.description");
        foreach (var family in ViewModel.Families)
        {
            var group = Ui.Stack(); group.BorderStyle = BorderStyle.FixedSingle; Ui.Add(stack, group);
            Value(group, () => family.Name);
            var variant = Choice(group, "VariantSelector", "ui.model.choose", () => family.Variants, () => family.Selected,
                card => { family.Selected = card; Bindings.RequestRefresh(); }, card => card.Variant);
            Bindings.Visible(variant, () => family.HasVariants);
            Value(group, () => family.Selected.Credit ?? ""); Value(group, () => family.Selected.Headline);
            Value(group, () => family.Selected.Description);
            var detailsToggle = new CheckBox { AutoSize = true, Appearance = Appearance.Button };
            Bindings.Localize(detailsToggle, "ui.files.info"); Ui.Add(group, detailsToggle);
            var details = Ui.Stack(); details.Visible = false;
            Value(details, () => family.Selected.HardwareGuide + "\n" + family.Selected.MemoryGuide);
            Value(details, () => string.Join("\n", family.Selected.Benefits)); Ui.Add(group, details);
            detailsToggle.CheckedChanged += (_, _) => details.Visible = detailsToggle.Checked;
            Value(group, () => Core.Localization.T("ui.model.download") + " · " + family.Selected.DownloadLabel);
            Action(group, "ui.library.use", ViewModel.UseLibraryModelCommand, () => family.Selected);
        }
        Action(stack, "ui.files.add", ViewModel.AddModelCommand);
    }
    private void Mine(TableLayoutPanel stack)
    {
        var vm = ViewModel;
        Heading(stack, "ui.library.mineHint");
        var empty = Heading(stack, "ui.library.emptyHint"); Bindings.Visible(empty, () => vm.ShowLibraryEmpty);
        ModelPresentation? selected = null;
        var list = Choice(stack, "LibraryModelSelector", "ui.library.mine", () => vm.LibraryModels,
            () => selected is not null && vm.LibraryModels.Contains(selected) ? selected : vm.LibraryModels.FirstOrDefault(),
            value => { selected = value; Bindings.RequestRefresh(); }, value => value.DisplayName + " · " + value.FileStateLabel);
        Value(stack, () => (list.SelectedItem as ModelPresentation)?.OriginLabel ?? "");
        Value(stack, () => (list.SelectedItem as ModelPresentation)?.Definition.Artifacts[0].Filename ?? "");
        Action(stack, "ui.library.use", vm.UseLibraryModelCommand, () => list.SelectedItem);
        Action(stack, "ui.library.remove", vm.RemoveLibraryModelCommand, () => list.SelectedItem);
        Action(stack, "ui.files.find", vm.ChooseModelFolderCommand); Action(stack, "ui.files.folder", vm.OpenFolderCommand);
        Action(stack, "ui.files.add", vm.AddModelCommand);
    }
    private void AddModel(TableLayoutPanel stack)
    {
        var vm = ViewModel;
        Heading(stack, "ui.library.localGguf"); Heading(stack, "ui.library.localHint"); Action(stack, "ui.files.connect", vm.ConnectModelFileCommand);
        Heading(stack, "ui.library.hfHint");
        var address = Edit(stack, "ui.library.hfHint", () => vm.HuggingFaceAddress, value => vm.HuggingFaceAddress = value); address.AccessibleName = "Hugging Face URL";
        Bindings.Enabled(address, () => vm.CanSelectModel); Action(stack, "ui.library.findFiles", vm.FindHuggingFaceCommand);
        var files = Choice(stack, "HuggingFaceFiles", "ui.library.downloadFile", () => vm.HuggingFaceFiles, () => vm.SelectedHuggingFaceFile,
            value => vm.SelectedHuggingFaceFile = value, value => value.DisplayName);
        Bindings.Enabled(files, () => vm.CanSelectModel); Action(stack, "ui.library.register", vm.AddHuggingFaceCommand);
        Heading(stack, "ui.files.advanced"); Action(stack, "ui.files.catalog", vm.ImportCatalogCommand);
    }
    protected override void Dispose(bool disposing) { if (disposing) ViewModel.PropertyChanged -= PageChanged; base.Dispose(disposing); }
}
