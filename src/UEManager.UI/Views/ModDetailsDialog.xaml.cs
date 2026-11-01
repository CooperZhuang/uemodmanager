using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;
using UEManager.UI.ViewModels;

namespace UEManager.UI.Views;

public partial class ModDetailsDialog : UserControl
{
    public ModDetailsDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ModDetailsViewModel old)
                old.PropertyChanged -= OnDetailsChanged;
            if (e.NewValue is ModDetailsViewModel details)
                details.PropertyChanged += OnDetailsChanged;
        };
    }

    /// <summary>Renaming starts in the name box, with the name selected to type over.</summary>
    private void OnDetailsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModDetailsViewModel.IsRenaming) && sender is ModDetailsViewModel { IsRenaming: true })
            Dispatcher.BeginInvoke(() =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }, DispatcherPriority.Input);
    }
}
