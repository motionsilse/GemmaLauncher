using System.Windows.Forms;

namespace GemmaLauncher.App;

public sealed class AppSettingsView : LauncherPage
{
    public AppSettingsView(LauncherViewModel vm) : base(vm)
    {
        var stack = Ui.Stack(); Controls.Add(Ui.Scroll(stack));
        var language = Choice(stack, "LanguageSelector", "ui.language.label", () => vm.LanguageOptions.Select(option => option.Code), () => vm.LanguageSelection,
            value => vm.LanguageSelection = value, code => vm.LanguageOptions.First(option => option.Code == code).Name);
        var closeToTray = new CheckBox { AutoSize = true, Name = "CloseToTray" };
        Bindings.Localize(closeToTray, "ui.settings.closeToTray"); Bindings.Check(closeToTray, () => vm.CloseToTray, value => vm.CloseToTray = value); Ui.Add(stack, closeToTray);
        Value(stack, () => Core.Localization.T(vm.CloseToTray ? "ui.footer.exit" : "ui.settings.closeExits"));
        Heading(stack, "ui.settings.autoSaved"); StartBindings();
    }
}
