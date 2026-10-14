using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Forms;

namespace GemmaLauncher.App;

public sealed class PresetEditorView : LauncherPage
{
    private readonly BindingList<GlossaryRow> _rows = [];
    private readonly BindingSource _source = new();
    private readonly DataGridView _grid;
    private bool _syncing;
    public PresetEditorView(LauncherViewModel vm) : base(vm)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root); var stack = Ui.Stack(); root.Controls.Add(stack, 0, 0);
        Heading(stack, "ui.library.presetsHint");
        Action(stack, "ui.library.newPreset", vm.NewPresetCommand);
        var preset = Choice(stack, "PresetSelector", "ui.library.taskPreset", () => vm.Presets, () => vm.SelectedPreset,
            value => { CommitEdits(); vm.SelectedPreset = value; }, value => value.Name);
        Bindings.Enabled(preset, () => vm.CanChangePreset);
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Bindings.Enabled(columns, () => vm.CanEditPreset); root.Controls.Add(columns, 0, 1);
        var editor = Ui.Stack(); var glossary = Ui.Stack(); columns.Controls.Add(Ui.Scroll(editor), 0, 0); columns.Controls.Add(Ui.Scroll(glossary), 1, 0);
        Edit(editor, "ui.library.presetName", () => vm.PresetName, value => vm.PresetName = value).Name = "PresetName";
        Edit(editor, "ui.library.prompt", () => vm.PresetPrompt, value => vm.PresetPrompt = value, true).Name = "PresetPrompt";
        Heading(editor, "ui.library.clientWins");
        Edit(editor, "ui.library.temperature", () => vm.PresetTemperature, value => vm.PresetTemperature = value);
        Ui.Add(editor, Ui.Label("Top P")); var topP = Ui.TextBox(); Bindings.Edit(topP, () => vm.PresetTopP, value => vm.PresetTopP = value); Ui.Add(editor, topP);
        Edit(editor, "ui.library.maxTokens", () => vm.PresetMaxTokens, value => vm.PresetMaxTokens = value);
        Heading(glossary, "ui.library.glossary"); Action(glossary, "ui.library.importGlossary", vm.ImportGlossaryCommand);
        _source.DataSource = _rows;
        _grid = new DataGridView { Name = "GlossaryGrid", Height = 240, Dock = DockStyle.Top, AutoGenerateColumns = false, DataSource = _source,
            AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = true, BackgroundColor = Ui.Paper,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(GlossaryRow.Source), Name = "Source" });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(GlossaryRow.Target), Name = "Target" });
        Bindings.Watch(() => { _grid.Columns[0].HeaderText = Core.Localization.T("ui.library.source"); _grid.Columns[1].HeaderText = Core.Localization.T("ui.library.target"); _grid.AccessibleName = Core.Localization.T("ui.library.glossary"); });
        Ui.Add(glossary, _grid); Heading(glossary, "ui.library.glossaryHint");
        var footer = Ui.Stack(); root.Controls.Add(footer, 0, 2);
        var save = Ui.Button(); Bindings.Localize(save, "ui.library.save"); Bindings.Command(save, vm.SavePresetCommand, before: CommitEdits); Ui.Add(footer, save);
        Action(footer, "ui.library.deletePreset", vm.DeletePresetCommand); Heading(footer, "ui.library.presetScope");
        vm.Glossary.CollectionChanged += GlossaryChanged; _rows.ListChanged += RowsChanged;
        SyncRows(); StartBindings();
    }
    public void CommitEdits() { _grid.EndEdit(); _source.EndEdit(); }
    private void GlossaryChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncRows();
    private void SyncRows()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            _rows.RaiseListChangedEvents = false;
            _rows.Clear(); foreach (var row in ViewModel.Glossary) _rows.Add(row);
            _rows.RaiseListChangedEvents = true; _rows.ResetBindings();
        }
        finally { _syncing = false; }
    }
    private void RowsChanged(object? sender, ListChangedEventArgs e)
    {
        if (_syncing || e.ListChangedType == ListChangedType.ItemChanged) return;
        _syncing = true;
        try { ViewModel.Glossary.Clear(); foreach (var row in _rows) ViewModel.Glossary.Add(row); }
        finally { _syncing = false; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { ViewModel.Glossary.CollectionChanged -= GlossaryChanged; _rows.ListChanged -= RowsChanged; _source.Dispose(); }
        base.Dispose(disposing);
    }
}
