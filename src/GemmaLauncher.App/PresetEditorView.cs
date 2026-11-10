using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace GemmaLauncher.App;

public sealed class PresetEditorView : LauncherPage
{
    private readonly BindingList<GlossaryRow> _rows = [];
    private readonly BindingSource _source = new();
    private readonly DataGridView _grid;
    private bool _syncing;
    private readonly Action<string, int> _validationHandler;
    private readonly ErrorProvider _errors = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
    public PresetEditorView(LauncherViewModel vm) : base(vm)
    {
        _errors.ContainerControl = this;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.White };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 136));
        Controls.Add(root);
        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(20, 14, 20, 8) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var selectorLabel = Ui.Label(); Bindings.Localize(selectorLabel, "ui.library.taskPreset"); selectorLabel.Margin = new Padding(0, 8, 16, 0); top.Controls.Add(selectorLabel, 0, 0);
        var preset = Ui.Combo("PresetSelector");
        Bindings.Choice(preset, () => vm.Presets.Where(value => value.Id != "none").Concat(vm.IsNewPreset ? [vm.EditingPreset!] : []), () => vm.EditingPreset,
            value => { CommitEdits(); vm.EditingPreset = value; }, value => value.Name);
        Bindings.Watch(() => preset.AccessibleName = Core.Localization.T("ui.library.taskPreset"));
        Bindings.Enabled(preset, () => vm.CanChangePreset); preset.Margin = new Padding(0, 5, 14, 0); top.Controls.Add(preset, 1, 0);
        var create = Ui.Button(); Bindings.Localize(create, "ui.library.newPreset"); Bindings.Command(create, vm.NewPresetCommand, before: CommitEdits); top.Controls.Add(create, 2, 0);
        root.Controls.Add(top, 0, 0);
        var editingArea = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        root.Controls.Add(editingArea, 0, 1);
        var expanded = false;
        var body = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 4, Padding = new Padding(20, 8, 20, 8) };
        void FitBody() => body.Height = Math.Max(editingArea.ClientSize.Height, (int)Math.Round((expanded ? 640 : 400) * DeviceDpi / 96f));
        editingArea.Resize += (_, _) => FitBody(); editingArea.Controls.Add(body); FitBody();
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize)); body.RowStyles.Add(new RowStyle(SizeType.AutoSize)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Bindings.Enabled(body, () => vm.CanEditPreset);
        var nameLabel = Ui.Label(); Bindings.Localize(nameLabel, "ui.library.presetName"); nameLabel.Margin = new Padding(0, 0, 0, 6); body.Controls.Add(nameLabel, 0, 0);
        var name = Ui.TextBox(); name.Name = "PresetName"; name.MaxLength = 100; name.Margin = new Padding(0, 0, 0, 12); Bindings.Edit(name, () => vm.PresetName, value => vm.PresetName = value); Bindings.Watch(() => name.AccessibleName = Core.Localization.T("ui.library.presetName")); body.Controls.Add(name, 0, 1);
        var tabs = new TabControl { Name = "PresetEditorTabs", Dock = DockStyle.Fill, Margin = Padding.Empty };
        var promptPage = new TabPage { BackColor = Color.White, Padding = new Padding(10) }; Bindings.Localize(promptPage, "ui.library.prompt");
        var glossaryPage = new TabPage { BackColor = Color.White, Padding = new Padding(10) }; Bindings.Localize(glossaryPage, "ui.library.glossary");
        tabs.TabPages.AddRange([promptPage, glossaryPage]); body.Controls.Add(tabs, 0, 2);
        var prompt = Ui.TextBox(true); prompt.Name = "PresetPrompt"; prompt.Dock = DockStyle.Fill; prompt.BorderStyle = BorderStyle.FixedSingle; prompt.MaxLength = 100000;
        Bindings.Edit(prompt, () => vm.PresetPrompt, value => vm.PresetPrompt = value); Bindings.Watch(() => prompt.AccessibleName = Core.Localization.T("ui.library.prompt")); promptPage.Controls.Add(prompt);
        var glossary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        glossary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        glossary.RowStyles.Add(new RowStyle(SizeType.AutoSize)); glossary.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); glossary.RowStyles.Add(new RowStyle(SizeType.AutoSize)); glossaryPage.Controls.Add(glossary);
        var import = Ui.Button(); import.Anchor = AnchorStyles.Left; Bindings.Localize(import, "ui.library.importGlossary"); Bindings.Command(import, vm.ImportGlossaryCommand, before: CommitEdits); glossary.Controls.Add(import, 0, 0);
        _source.DataSource = _rows;
        _grid = new DataGridView { Name = "GlossaryGrid", Dock = DockStyle.Fill, AutoGenerateColumns = false, DataSource = _source,
            AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = true, RowHeadersWidth = 26, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
            EnableHeadersVisualStyles = false, GridColor = Ui.Line, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Ui.Tint; _grid.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Ink;
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6);
        _grid.DefaultCellStyle.SelectionBackColor = Ui.Green; _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.DefaultCellStyle.Padding = new Padding(5); _grid.RowTemplate.Height = 34;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(GlossaryRow.Source), Name = "Source", MaxInputLength = 500 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(GlossaryRow.Target), Name = "Target", MaxInputLength = 500 });
        Bindings.Watch(() => { _grid.Columns[0].HeaderText = Core.Localization.T("ui.library.source"); _grid.Columns[1].HeaderText = Core.Localization.T("ui.library.target"); _grid.AccessibleName = Core.Localization.T("ui.library.glossary"); });
        glossary.Controls.Add(_grid, 0, 1);
        var glossaryHint = Ui.Label(); Bindings.Localize(glossaryHint, "ui.library.glossaryHint"); glossaryHint.ForeColor = Ui.Muted; glossary.Controls.Add(glossaryHint, 0, 2);
        var generation = Ui.Stack(); generation.Padding = new Padding(0, 10, 0, 0); body.Controls.Add(generation, 0, 3);
        var advancedToggle = new CheckBox { Name = "PresetAdvancedToggle", AutoSize = false, Height = 38, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 3, 10, 3), ForeColor = Ui.Green };
        advancedToggle.FlatAppearance.BorderColor = Ui.Line; Bindings.Localize(advancedToggle, "ui.preset.generation"); Ui.Add(generation, advancedToggle);
        var advanced = Ui.Stack(); advanced.Name = "PresetAdvanced"; advanced.Visible = false; advanced.Padding = new Padding(8); advanced.BackColor = Ui.Paper; Ui.Add(generation, advanced);
        advancedToggle.CheckedChanged += (_, _) => { expanded = advancedToggle.Checked; advanced.Visible = expanded; FitBody(); };
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        for (var i = 0; i < 3; i++) fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        void NumberField(int column, string field, string labelKey, Func<string> get, Action<string> set)
        {
            var group = Ui.Stack(); group.Padding = new Padding(4);
            var title = Ui.Label(); Bindings.Localize(title, labelKey); Ui.Add(group, title);
            var box = Ui.TextBox(); box.Name = field; box.Width = 100; Bindings.Edit(box, get, set); Ui.Add(group, box); fields.Controls.Add(group, column, 0);
        }
        NumberField(0, "PresetTemperature", "ui.library.temperature", () => vm.PresetTemperature, value => vm.PresetTemperature = value);
        NumberField(1, "PresetTopP", "ui.library.topP", () => vm.PresetTopP, value => vm.PresetTopP = value);
        NumberField(2, "PresetMaxTokens", "ui.library.maxTokens", () => vm.PresetMaxTokens, value => vm.PresetMaxTokens = value);
        Ui.Add(advanced, fields); Heading(advanced, "ui.library.clientWins");
        var footer = Ui.Stack(); footer.AutoSize = false; footer.Dock = DockStyle.Fill; footer.Padding = new Padding(20, 8, 20, 12); root.Controls.Add(footer, 0, 2);
        var scope = Value(footer, () => vm.PresetScopeLabel); scope.ForeColor = Ui.Muted; Ui.SetFont(scope, 9);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        var save = Ui.Button(); Ui.Primary(save); Bindings.Localize(save, "ui.preset.saveApply"); Bindings.Command(save, vm.SavePresetCommand, before: CommitEdits); actions.Controls.Add(save);
        var duplicate = Ui.Button(); Bindings.Localize(duplicate, "ui.preset.duplicate"); Bindings.Command(duplicate, vm.DuplicatePresetCommand, before: CommitEdits); Bindings.Visible(duplicate, () => vm.IsPresetShared); actions.Controls.Add(duplicate);
        var cancel = Ui.Button(); Bindings.Localize(cancel, "ui.navigation.backToModels"); cancel.Click += (_, _) => { CommitEdits(); vm.PageIndex = 0; }; actions.Controls.Add(cancel);
        var delete = Ui.Button(); delete.ForeColor = Color.Maroon; Bindings.Localize(delete, "ui.library.deletePreset"); Bindings.Command(delete, vm.DeletePresetCommand, before: CommitEdits); Bindings.Visible(delete, () => !vm.IsNewPreset && vm.EditingPreset is not null); actions.Controls.Add(delete);
        Ui.Add(footer, actions);
        var note = Value(footer, () => vm.PresetValidationMessage.Length > 0 ? vm.PresetValidationMessage : vm.PresetDirtyLabel); note.ForeColor = Ui.Muted;
        note.AutoSize = false; note.Height = 36;
        Bindings.Watch(() => { note.ForeColor = vm.PresetValidationMessage.Length > 0 ? Color.Maroon : Ui.Muted; if (vm.PresetValidationMessage.Length == 0) _errors.Clear(); });
        _validationHandler = (field, row) =>
        {
            _errors.Clear(); if (field.Length == 0) return;
            var control = field switch { "GlossaryGrid" => (Control)_grid, "PresetName" => name, "PresetPrompt" => prompt, _ => fields.Controls.Find(field, true).FirstOrDefault() };
            if (control is not null) _errors.SetError(control, vm.PresetValidationMessage);
            if (field == "GlossaryGrid") { tabs.SelectedTab = glossaryPage; if (row >= 0 && row < _grid.Rows.Count) _grid.CurrentCell = _grid.Rows[row].Cells[0]; _grid.Focus(); }
            else if (field == "PresetName") name.Focus();
            else if (field == "PresetPrompt") { tabs.SelectedTab = promptPage; prompt.Focus(); }
            else { advancedToggle.Checked = true; var target = fields.Controls.Find(field, true).FirstOrDefault(); target?.Focus(); }
        };
        vm.PresetValidationFailed += _validationHandler;
        vm.Glossary.CollectionChanged += GlossaryChanged; _rows.ListChanged += RowsChanged;
        SyncRows(); StartBindings();
    }
    public void CommitEdits() { if (_grid.IsCurrentCellInEditMode) _grid.EndEdit(); _source.EndEdit(); }
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
        if (disposing) { ViewModel.PresetValidationFailed -= _validationHandler; ViewModel.Glossary.CollectionChanged -= GlossaryChanged; _rows.ListChanged -= RowsChanged; _source.Dispose(); _errors.Dispose(); }
        base.Dispose(disposing);
    }
}
