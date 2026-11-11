using System.Windows;
using System.Windows.Controls;
using UEManager.UI.ViewModels;

namespace UEManager.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnManageSaveBackups(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel model)
            return;
        model.RefreshSaveBackups();
        new SaveBackupsWindow(model) { Owner = Window.GetWindow(this) }.ShowDialog();
        model.RefreshSaveBackups();
    }
}
