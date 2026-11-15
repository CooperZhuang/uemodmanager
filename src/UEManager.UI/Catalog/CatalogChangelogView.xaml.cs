using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UEManager.UI.Catalog;

/// <summary>
/// A mod's changelog over the whole window; see <see cref="CatalogViewModel.Changelog"/>. It takes the keyboard focus
/// while it is open, for Esc, and gives it back when it closes, as to the full view below it.
/// </summary>
public partial class CatalogChangelogView : UserControl
{
    private IInputElement? before;

    public CatalogChangelogView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
            {
                before = Keyboard.FocusedElement;
                Dispatcher.BeginInvoke(() => Focus());
            }
            else if (before is UIElement { IsVisible: true } element)
            {
                Dispatcher.BeginInvoke(() => element.Focus());
                before = null;
            }
        };
    }
}
