using System.Windows.Forms;

namespace GemmaLauncher.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Utility commands must not create windows, load settings or acquire the instance mutex.
        var isUtilityCommand = BundledResources.IsUtilityCommand(args);
        try
        {
            if (isUtilityCommand) return BundledResources.RunUtilityCommand(args);
            ApplicationConfiguration.Initialize();
            using var application = new App(args);
            if (application.Ready) Application.Run(application);
            return 0;
        }
        catch (Exception exception)
        {
            // Keep failures out of Windows' unhandled CLR-exception dialog, including failures before App.OnStartup.
            if (!isUtilityCommand)
            {
                try
                {
                    MessageBox.Show(exception.Message, "Gemma Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception) { }
            }
            return 1;
        }
    }
}
