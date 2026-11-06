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
            {
                details.PropertyChanged += OnDetailsChanged;
                // The mod's row opens the window with the form on.
                if (details.IsRenaming)
                    FocusForm(details);
            }
        };
    }

    private void OnDetailsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModDetailsViewModel.IsRenaming) && sender is ModDetailsViewModel { IsRenaming: true } details)
            FocusForm(details);
    }

    /// <summary>Renaming starts in the name box, with the name selected to type over; adding a tag in the tags box, after the ones the mod has.</summary>
    private void FocusForm(ModDetailsViewModel details) => Dispatcher.BeginInvoke(() =>
    {
        if (details.AddingTag)
        {
            TagsBox.Focus();
            TagsBox.CaretIndex = TagsBox.Text.Length;
        }
        else
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }
    }, DispatcherPriority.Input);
}
