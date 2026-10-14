using System.Windows;
using System.Windows.Controls;
namespace GemmaLauncher.App;
public partial class PresetEditorView : System.Windows.Controls.UserControl
{
    public PresetEditorView() => InitializeComponent();
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        GlossaryGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        GlossaryGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }
}
