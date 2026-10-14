using System.Windows.Controls;
namespace GemmaLauncher.App;
public partial class LauncherWorkspace : System.Windows.Controls.UserControl
{
    public LauncherWorkspace() => InitializeComponent();
    public ScrollViewer ExecutionScroll => SettingsScroll;
}
