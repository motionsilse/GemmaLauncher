using System.Windows;

namespace GemmaLauncher.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // These commands must work without creating WPF windows, loading settings or acquiring the instance mutex.
        var isUtilityCommand = BundledResources.IsUtilityCommand(args);
        try
        {
            if (isUtilityCommand) return BundledResources.RunUtilityCommand(args);
            var application = new App();
            application.InitializeComponent();
            return application.Run();
        }
        catch (Exception exception)
        {
            // Keep failures out of Windows' unhandled CLR-exception dialog, including failures before App.OnStartup.
            if (!isUtilityCommand)
            {
                try
                {
                    System.Windows.MessageBox.Show(exception.Message, "Gemma Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception) { }
            }
            return 1;
        }
    }
}
