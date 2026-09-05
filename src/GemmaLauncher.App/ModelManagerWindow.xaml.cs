using System.Windows;
using System.Windows.Input;

namespace GemmaLauncher.App;

public partial class ModelManagerWindow : Window
{
    public ModelManagerWindow(object viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowLayout.FitToWorkArea(this, SystemParameters.WorkArea);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SelectModel_Click(object sender, RoutedEventArgs e)
    {
        // Button commands run after Click handlers. Close after the selection command.
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
            DragMove();
    }
}
