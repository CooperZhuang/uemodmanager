using System.Windows.Controls;
using System.Windows.Input;

namespace UEManager.UI.Views;

public partial class SaveBackupsView : UserControl
{
    public SaveBackupsView() => InitializeComponent();

    /// <summary>Enter keeps a backup's note, as leaving the field does.</summary>
    private void OnNoteKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box)
            return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Keyboard.ClearFocus();
        e.Handled = true;
    }
}
