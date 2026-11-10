using System.Drawing;
using System.Windows.Forms;

namespace GemmaLauncher.App;

public sealed class LauncherWorkspace : LauncherPage
{
    private readonly EventHandler _loadSettingsHandler;
    private readonly EventHandler _storageHandler;
    public Panel OperationHost { get; } = new() { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 6, 0, 14) };
    public LauncherWorkspace(LauncherViewModel vm) : base(vm)
    {
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(columns);
        var catalog = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.White, Margin = new Padding(0, 0, 16, 0), Padding = new Padding(8) };
        catalog.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        catalog.RowStyles.Add(new RowStyle(SizeType.AutoSize)); catalog.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); catalog.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = Ui.Label(); Bindings.Localize(label, "ui.model.choose"); Ui.SetFont(label, 12, FontStyle.Bold); label.Margin = new Padding(16, 12, 8, 16); catalog.Controls.Add(label, 0, 0);
        var models = new ModelChoiceList(); Bindings.Models(models); Bindings.Enabled(models, () => vm.CanSelectModel);
        catalog.Controls.Add(models, 0, 1); columns.Controls.Add(catalog, 0, 0);
        var add = Ui.Button(); Bindings.Localize(add, "ui.files.add"); Bindings.Command(add, vm.AddModelCommand); add.Margin = new Padding(12, 10, 12, 6); add.Anchor = AnchorStyles.Left; catalog.Controls.Add(add, 0, 2);
        var details = Ui.Stack(); columns.Controls.Add(Ui.Scroll(details), 1, 0);
        var empty = Ui.Stack(); empty.Padding = Padding.Empty;
        Heading(empty, "ui.model.choose"); Heading(empty, "ui.library.emptyHint");
        Ui.Add(details, empty); Bindings.Visible(empty, () => vm.ShowRunEmpty);
        var selected = Ui.Stack(); selected.Padding = Padding.Empty; Ui.Add(details, selected);
        Bindings.Visible(selected, () => vm.HasSelectedModel);
        var category = Value(selected, () => vm.SelectedPresentation?.Category ?? ""); category.ForeColor = Ui.Green; Ui.SetFont(category, 9, FontStyle.Bold);
        var title = Value(selected, () => vm.SelectedPresentation?.DisplayName ?? ""); Ui.SetFont(title, 18, FontStyle.Bold);
        Value(selected, () => vm.SelectedPresentation?.Description ?? "");
        var specs = Ui.Label(); Bindings.MemoryText(specs, () => Core.Localization.T("ui.model.download") + " · " + vm.DownloadLabel + "\n" + vm.MemoryUsage); Ui.Add(selected, specs); specs.ForeColor = Ui.Muted; specs.Margin = new Padding(0, 4, 0, 6);
        var credit = Value(selected, () => vm.SelectedPresentation?.Credit ?? ""); credit.ForeColor = Ui.Muted; Ui.SetFont(credit, 9); Bindings.Visible(credit, () => !string.IsNullOrWhiteSpace(vm.SelectedPresentation?.Credit));
        var source = new LinkLabel { AutoSize = true, UseMnemonic = false, LinkColor = Ui.Green, ActiveLinkColor = Ui.Green, VisitedLinkColor = Ui.Green };
        Bindings.Localize(source, "ui.files.info"); source.LinkClicked += (_, _) => vm.OpenModelSourceCommand.Execute(null);
        var links = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true }; links.Controls.Add(source);
        var license = new LinkLabel { AutoSize = true, LinkColor = Ui.Green, Margin = new Padding(12, 0, 0, 0) }; Bindings.Localize(license, "ui.source.license"); license.LinkClicked += (_, _) => vm.OpenModelLicense(); links.Controls.Add(license);
        var program = new LinkLabel { AutoSize = true, LinkColor = Ui.Green, Margin = new Padding(12, 0, 0, 0) }; Bindings.Localize(program, "ui.source.program"); Bindings.Visible(program, () => vm.IsAnis); program.LinkClicked += (_, _) => vm.OpenAnisProgramSource(); links.Controls.Add(program); Ui.Add(selected, links);
        var divider = new Panel { Height = 1, BackColor = Ui.Line }; Ui.Add(selected, divider);
        var settingsRow = Ui.Stack(); settingsRow.Padding = Padding.Empty;
        var presetRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        presetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); presetRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var presets = Ui.Combo("RunPresetSelector"); Bindings.Choice(presets, () => vm.Presets, () => vm.SelectedPreset, value => vm.SelectedPreset = value, value => value.Name);
        Bindings.Enabled(presets, () => vm.CanChangePreset); presets.Margin = new Padding(0, 6, 10, 0); presetRow.Controls.Add(presets, 0, 0);
        var editPreset = Ui.Button(); Bindings.Localize(editPreset, "ui.preset.edit"); editPreset.Click += (_, _) => vm.PageIndex = 2; presetRow.Controls.Add(editPreset, 1, 0);
        Heading(settingsRow, "ui.library.taskPreset"); Ui.Add(settingsRow, presetRow); Ui.Add(selected, settingsRow);
        Bindings.Visible(settingsRow, () => !vm.IsAnis);
        var direction = Ui.Stack(); direction.Padding = Padding.Empty;
        var directions = Choice(direction, "AnisDirection", "ui.anis.direction", () => vm.AnisDirections, () => vm.AnisDirection, value => vm.AnisDirection = value, vm.AnisDirectionLabel);
        Bindings.Enabled(directions, () => vm.CanSelectModel); Bindings.Visible(direction, () => vm.IsAnis); Ui.Add(selected, direction);
        Ui.Add(selected, OperationHost);
        var connection = Value(selected, () => Core.Localization.T("ui.connection.title") + " · " + vm.ConnectionLabel); Ui.SetFont(connection, 12, FontStyle.Bold);
        CopyField(selected, "ui.connection.api", () => vm.ApiAddress, vm.CopyApiCommand);
        CopyField(selected, "ui.connection.apiModel", () => vm.ApiModelId, null);
        var options = Ui.Stack(); options.Padding = Padding.Empty; Ui.Add(selected, options);
        var advancedToggle = Ui.Button(); advancedToggle.Name = "AdvancedToggle"; advancedToggle.Anchor = AnchorStyles.Left;
        Bindings.Localize(advancedToggle, "ui.library.advanced"); Ui.Add(options, advancedToggle);
        var advanced = Ui.Stack(); advanced.Padding = new Padding(12); advanced.BackColor = Ui.Paper; advanced.Visible = false; Controls.Add(advanced);
        void ShowAdvanced()
        {
            using var dialog = new LauncherDialog(vm, Core.Localization.T("ui.library.advanced"), new Size(620, 660));
            advanced.Parent?.Controls.Remove(advanced); advanced.Visible = true; dialog.Content.Controls.Add(Ui.Scroll(advanced));
            dialog.AddAction(Core.Localization.T("ui.close"), DialogResult.Cancel);
            if (vm.HasServerError)
            {
                var retry = dialog.AddAction(Core.Localization.T("ui.recovery.runAgain"), DialogResult.None, true);
                retry.Click += (_, _) => { dialog.Close(); vm.RetryCommand.Execute(null); };
            }
            try { dialog.ShowDialog(this); }
            finally { advanced.Parent?.Controls.Remove(advanced); advanced.Visible = false; Controls.Add(advanced); }
        }
        advancedToggle.Click += (_, _) => ShowAdvanced();
        _loadSettingsHandler = (_, _) => ShowAdvanced(); vm.LoadSettingsRequested += _loadSettingsHandler;
        _storageHandler = (_, _) =>
        {
            using var dialog = new LauncherDialog(vm, Core.Localization.T("ui.recovery.storage"), new Size(580, 320));
            dialog.Content.Controls.Add(Ui.Label(vm.StorageInfo + "\n\n" + Core.Localization.T("ui.recovery.spaceHint")));
            dialog.AddAction(Core.Localization.T("ui.close"), DialogResult.Cancel);
            var retry = dialog.AddAction(Core.Localization.T("ui.recovery.retry"), DialogResult.None, true);
            retry.Click += (_, _) => { dialog.Close(); vm.RetryCommand.Execute(null); }; dialog.ShowDialog(this);
        };
        vm.StorageRecoveryRequested += _storageHandler;
        Heading(advanced, "ui.settings.loadHint");
        var validation = Value(advanced, () => vm.LoadOptionsFeedback); validation.ForeColor = Color.Maroon;
        Bindings.Visible(validation, () => vm.LoadOptionsFeedback.Length > 0);
        var contexts = Choice(advanced, "ContextSelector", "ui.context.label", () => vm.ContextChoices, () => vm.SelectedContext, value => vm.SelectedContext = value, value => value.Label);
        var device = Choice(advanced, "AnisDevice", "ui.anis.device", () => vm.AnisDevices, () => vm.AnisDevice, value => vm.AnisDevice = value, vm.AnisDeviceLabel);
        Bindings.Visible(device, () => vm.IsAnis); Bindings.Enabled(device, () => vm.CanSelectModel);
        Bindings.Visible((Control)device.Tag!, () => vm.IsAnis);
        Bindings.Visible(contexts, () => !vm.IsAnis);
        Bindings.Visible((Control)contexts.Tag!, () => !vm.IsAnis);
        Bindings.Enabled(contexts, () => vm.CanChangeContext); var contextHint = Value(advanced, () => vm.ContextHint); Bindings.Visible(contextHint, () => !vm.IsAnis);
        var restart = Action(advanced, "ui.context.restart", vm.RestartCommand); Bindings.Visible(restart, () => vm.HasPendingContext);
        var load = Ui.Stack(); load.Padding = Padding.Empty; Ui.Add(advanced, load); Bindings.Enabled(load, () => vm.CanChangeLoadOptions);
        Bindings.Visible(load, () => !vm.IsAnis);
        Choice(load, "GpuLayers", "ui.library.gpuLayers", () => vm.GpuLayerChoices, () => vm.GpuLayers, value => vm.GpuLayers = value, vm.GpuLayerLabel);
        Heading(load, "ui.library.gpuHint");
        Choice(load, "CacheType", "ui.library.cache", () => vm.CacheChoices, () => vm.CacheType, value => vm.CacheType = value, value => value);
        Ui.Add(load, Ui.Label("Flash Attention")); var flash = Ui.Combo("FlashAttention"); flash.AccessibleName = "Flash Attention";
        Bindings.Choice(flash, () => vm.ToggleChoices, () => vm.FlashAttention, value => vm.FlashAttention = value, vm.ToggleLabel); Ui.Add(load, flash);
        Choice(load, "Reasoning", "ui.library.reasoning", () => vm.ToggleChoices, () => vm.Reasoning, value => vm.Reasoning = value, vm.ToggleLabel);
        Value(load, () => vm.MtpLabel);
        var useMtp = new CheckBox { AutoSize = true }; Bindings.Localize(useMtp, "ui.library.useMtp"); Bindings.Check(useMtp, () => vm.UseMtp, value => vm.UseMtp = value); Bindings.Enabled(useMtp, () => vm.HasMtp); Ui.Add(load, useMtp);
        Heading(load, "ui.library.draftTokens"); var tokens = new NumericUpDown { Minimum = 1, Maximum = 16, Name = "DraftTokens" };
        Bindings.Number(tokens, () => vm.DraftTokens, value => vm.DraftTokens = value); Bindings.Enabled(tokens, () => vm.HasMtp); Ui.Add(load, tokens);
        Action(load, "ui.library.connectMtp", vm.ConnectMtpCommand); Value(load, () => vm.TemplateHint);
        Edit(load, "ui.library.template", () => vm.ChatTemplate, value => vm.ChatTemplate = value, true).MaxLength = 14000;
        Heading(load, "ui.library.resetHint");
        Action(load, "ui.library.resetOptions", vm.ResetLoadOptionsCommand);
        var web = Action(advanced, "ui.connection.web", vm.OpenWebUiCommand); Bindings.Visible(web, () => !vm.IsAnis);
        Action(advanced, "ui.files.logs", vm.OpenLogsCommand); Action(advanced, "ui.files.folder", vm.OpenFolderCommand);
        StartBindings();
    }
    private void CopyField(TableLayoutPanel stack, string key, Func<string> get, System.Windows.Input.ICommand? command)
    {
        var label = Heading(stack, key); Ui.SetFont(label, 9); label.ForeColor = Ui.Muted; label.Margin = new Padding(0, 6, 0, 2);
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var text = Ui.TextBox(); text.ReadOnly = true; text.BackColor = Ui.Paper; text.BorderStyle = BorderStyle.FixedSingle; text.Margin = new Padding(0, 7, 8, 0);
        Bindings.Text(text, get); Bindings.Watch(() => text.AccessibleName = Core.Localization.T(key)); row.Controls.Add(text, 0, 0);
        var copy = Ui.Button(); Bindings.Localize(copy, "ui.connection.copyValue");
        if (command is not null) Bindings.Command(copy, command);
        else copy.Click += (_, _) => { try { Clipboard.SetText(get()); } catch (System.Runtime.InteropServices.ExternalException) { System.Media.SystemSounds.Exclamation.Play(); } };
        row.Controls.Add(copy, 1, 0); Ui.Add(stack, row);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { ViewModel.LoadSettingsRequested -= _loadSettingsHandler; ViewModel.StorageRecoveryRequested -= _storageHandler; }
        base.Dispose(disposing);
    }
}
