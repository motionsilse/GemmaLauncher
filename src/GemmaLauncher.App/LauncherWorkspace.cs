using System.Windows.Forms;

namespace GemmaLauncher.App;

public sealed class LauncherWorkspace : LauncherPage
{
    public LauncherWorkspace(LauncherViewModel vm) : base(vm)
    {
        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize)); stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(stack);
        var empty = Ui.Stack(); Heading(empty, "ui.library.emptyTitle"); Heading(empty, "ui.library.emptyHint");
        Action(empty, "ui.model.browse", vm.ManageModelsCommand); Bindings.Visible(empty, () => vm.ShowRunEmpty); stack.Controls.Add(empty, 0, 0);
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var settings = Ui.Stack(); var connection = Ui.Stack();
        columns.Controls.Add(Ui.Scroll(settings), 0, 0); columns.Controls.Add(Ui.Scroll(connection), 1, 0);
        Bindings.Visible(columns, () => vm.HasSelectedModel); stack.Controls.Add(columns, 0, 1);
        var models = Choice(settings, "RunModelSelector", "ui.model.choose", () => vm.LibraryModels,
            () => vm.SelectedPresentation, card => vm.SelectedModel = card.Definition, card => card.DisplayName + " · " + card.FileStateLabel);
        Bindings.Enabled(models, () => vm.CanSelectModel);
        Action(settings, "ui.model.browse", vm.ManageModelsCommand);
        var presets = Choice(settings, "RunPresetSelector", "ui.library.taskPreset", () => vm.Presets, () => vm.SelectedPreset, value => vm.SelectedPreset = value, value => value.Name);
        Bindings.Enabled(presets, () => vm.CanChangePreset); Heading(settings, "ui.library.presetScope");
        var contexts = Choice(settings, "ContextSelector", "ui.context.label", () => vm.ContextChoices, () => vm.SelectedContext, value => vm.SelectedContext = value, value => value.Label);
        Bindings.Enabled(contexts, () => vm.CanChangeContext); Value(settings, () => vm.ContextHint);
        var restart = Action(settings, "ui.context.restart", vm.RestartCommand); Bindings.Visible(restart, () => vm.HasPendingContext);

        var advancedToggle = new CheckBox { AutoSize = true, Appearance = Appearance.Button, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
        Bindings.Localize(advancedToggle, "ui.library.advanced"); Ui.Add(settings, advancedToggle);
        var advanced = Ui.Stack(); advanced.Visible = false; Ui.Add(settings, advanced);
        advancedToggle.CheckedChanged += (_, _) => advanced.Visible = advancedToggle.Checked;
        Bindings.Enabled(advanced, () => vm.CanSelectModel);
        Choice(advanced, "GpuLayers", "ui.library.gpuLayers", () => vm.GpuLayerChoices, () => vm.GpuLayers, value => vm.GpuLayers = value, value => value);
        Choice(advanced, "CacheType", "ui.library.cache", () => vm.CacheChoices, () => vm.CacheType, value => vm.CacheType = value, value => value);
        Ui.Add(advanced, Ui.Label("Flash Attention")); var flash = Ui.Combo("FlashAttention"); flash.AccessibleName = "Flash Attention";
        Bindings.Choice(flash, () => vm.ToggleChoices, () => vm.FlashAttention, value => vm.FlashAttention = value, value => value); Ui.Add(advanced, flash);
        Choice(advanced, "Reasoning", "ui.library.reasoning", () => vm.ToggleChoices, () => vm.Reasoning, value => vm.Reasoning = value, value => value);
        Value(advanced, () => vm.MtpLabel);
        var useMtp = new CheckBox { AutoSize = true }; Bindings.Localize(useMtp, "ui.library.useMtp"); Bindings.Check(useMtp, () => vm.UseMtp, value => vm.UseMtp = value); Bindings.Enabled(useMtp, () => vm.HasMtp); Ui.Add(advanced, useMtp);
        Heading(advanced, "ui.library.draftTokens"); var tokens = new NumericUpDown { Minimum = 1, Maximum = 16, Name = "DraftTokens" };
        Bindings.Number(tokens, () => vm.DraftTokens, value => vm.DraftTokens = value); Bindings.Enabled(tokens, () => vm.HasMtp); Ui.Add(advanced, tokens);
        Action(advanced, "ui.library.connectMtp", vm.ConnectMtpCommand);
        Value(advanced, () => vm.TemplateHint);
        Edit(advanced, "ui.library.template", () => vm.ChatTemplate, value => vm.ChatTemplate = value, true).MaxLength = 14000;
        Action(advanced, "ui.library.resetOptions", vm.ResetLoadOptionsCommand);

        Heading(connection, "ui.connection.title"); Value(connection, () => vm.ConnectionLabel);
        ReadOnly(connection, "ui.connection.api", () => vm.ApiAddress);
        Action(connection, "ui.connection.copy", vm.CopyApiCommand);
        ReadOnly(connection, "ui.connection.model", () => vm.ApiModelId);
        Action(connection, "ui.connection.web", vm.OpenWebUiCommand); Heading(connection, "ui.connection.hint");
        Value(connection, () => vm.MemoryUsageLabel);
        var memory = Ui.Label(); Bindings.MemoryText(memory); Ui.Add(connection, memory); Heading(connection, "ui.memory.hint");
        Action(connection, "ui.files.logs", vm.OpenLogsCommand); Action(connection, "ui.files.folder", vm.OpenFolderCommand);
        Action(connection, "ui.files.info", vm.OpenModelSourceCommand);
        StartBindings();
    }
    private void ReadOnly(TableLayoutPanel stack, string key, Func<string> get)
    {
        Heading(stack, key); var text = Ui.TextBox(); text.ReadOnly = true; Bindings.Text(text, get); Ui.Add(stack, text);
    }
}
