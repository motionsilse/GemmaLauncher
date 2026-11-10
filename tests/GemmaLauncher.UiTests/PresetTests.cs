using System.Drawing;
using System.Windows.Forms;
using GemmaLauncher.App;

namespace GemmaLauncher.UiTests;

public static partial class Program
{
    private static async Task TestPresetDraftLifecycleAsync()
    {
        await using var fixture = new Fixture(); var vm = fixture.ViewModel;
        await UntilAsync(() => !vm.IsBusy);
        var translation = vm.Presets.Single(p => p.Id == "translation"); var creative = vm.Presets.Single(p => p.Id == "creative");
        vm.SelectedPreset = creative; vm.PageIndex = 2; vm.EditingPreset = translation; vm.PresetPrompt = "Saved separately";
        Assert(vm.SelectedPreset == creative && fixture.Store.Load().Presets.Single(p => p.Id == "translation").Prompt != vm.PresetPrompt, "Editor changes must not apply or save before submission.");
        Assert(vm.SavePresetDraft() && vm.SelectedPreset == creative, "Saving an editor draft must preserve the model's current preset assignment.");
        var count = vm.Presets.Count;
        vm.NewPresetCommand.Execute(null); vm.PresetName = "Discarded draft";
        vm.ResolvePresetEdits = () => PresetEditDecision.Discard; vm.PageIndex = 0;
        Assert(vm.Presets.Count == count && fixture.Store.Load().Presets.All(p => p.Name != "Discarded draft"), "Discarding a new draft must leave no stored or registered preset.");
        vm.SelectedPreset = translation;
        var originalModel = vm.SelectedModel!; var otherModel = vm.Models.First(model => model.Id.StartsWith("hy-mt2"));
        vm.SelectedModel = otherModel; await UntilAsync(() => !vm.IsBusy); vm.SelectedPreset = translation;
        vm.SelectedModel = originalModel; await UntilAsync(() => !vm.IsBusy); vm.PageIndex = 2;
        Assert(vm.IsPresetShared && vm.PresetScopeLabel.Contains(otherModel.DisplayName), "Editing shared settings must identify the other affected model.");
        vm.DuplicatePresetCommand.Execute(null); vm.PresetName = "Independent copy"; vm.PresetPrompt = "Only the current model";
        Assert(vm.IsNewPreset && vm.Presets.Count == count, "Duplication must create an independent unsaved draft.");
        vm.SavePresetCommand.Execute(null); var copy = vm.SelectedPreset!;
        Assert(copy.Id != translation.Id && fixture.Store.Load().ModelPresets[otherModel.Id] == translation.Id && translation.Preset.Prompt == "Saved separately", "Saving a copy must leave other models and the original preset unchanged.");
        vm.PageIndex = 2; vm.EditingPreset = translation; vm.DeletePresetCommand.Execute(null);
        Assert(vm.SelectedPreset == copy && fixture.Store.Load().ModelPresets[originalModel.Id] == copy.Id && !fixture.Store.Load().ModelPresets.ContainsKey(otherModel.Id), "Deleting an inactive preset must clear only its users and preserve the active setting.");
        vm.PageIndex = 2; vm.PresetName = "Retry after failure";
        Directory.CreateDirectory(fixture.Paths.SettingsFile + ".tmp");
        try { Assert(!vm.SavePresetDraft() && vm.HasPresetChanges && fixture.Store.Load().Presets.Single(p => p.Id == copy.Id).Name == "Independent copy", "A failed save must preserve the draft and every saved setting."); }
        finally { Directory.Delete(fixture.Paths.SettingsFile + ".tmp"); }
        Assert(vm.SavePresetDraft() && !vm.HasPresetChanges, "The same draft must be saveable after the write failure is resolved.");
        vm.PresetName = "  Trimmed name  "; vm.PresetTemperature = ".4";
        Assert(vm.SavePresetDraft() && !vm.HasPresetChanges && vm.PresetName == "Trimmed name" && vm.PresetTemperature == "0.4", "Successful saves must normalize the editor and not trigger another save dialog.");
    }

    private static async Task TestDialogStopPreservesDraftAsync()
    {
        await using var fixture = new Fixture(); var vm = fixture.ViewModel; await UntilAsync(() => !vm.IsBusy);
        vm.PrimaryCommand.Execute(null); await UntilAsync(() => vm.IsServerRunning && !vm.IsBusy);
        vm.PageIndex = 2; vm.PresetPrompt = "An unfinished editor draft";
        using var dialog = new LauncherDialog(vm, "Fixture", new Size(580, 320));
        dialog.Show(); await Task.Delay(30);
        var stop = Controls(dialog).OfType<Button>().Single(button => button.Name == "DialogSessionAction");
        Assert(stop.Visible && stop.Enabled, "Owned dialogs must retain direct control of the running session.");
        stop.PerformClick(); await UntilAsync(() => !vm.IsServerRunning && !vm.IsBusy);
        Assert(vm.HasPresetChanges && vm.PresetPrompt == "An unfinished editor draft" && fixture.Store.Load().Presets.All(p => p.Prompt != vm.PresetPrompt), "Stopping from a dialog must neither save nor discard the editor draft.");
        dialog.Close();
    }
}
