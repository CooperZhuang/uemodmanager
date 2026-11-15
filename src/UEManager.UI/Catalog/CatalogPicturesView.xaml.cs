using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace UEManager.UI.Catalog;

/// <summary>
/// The picture viewer over the whole window; see <see cref="CatalogViewModel.Pictures"/>. It takes the keyboard focus
/// while it is open, for Esc and the arrow keys, and gives it back when it closes, as to the full view below it.
/// </summary>
public partial class CatalogPicturesView : UserControl
{
    private IInputElement? before;

    public CatalogPicturesView()
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

    /// <summary>
    /// Set on a thumbnail while its picture is shown: the strip scrolls it into view, so with many screenshots the
    /// arrows and the keys never leave the marked thumbnail out of sight.
    /// </summary>
    public static readonly DependencyProperty ShownProperty = DependencyProperty.RegisterAttached(
        "Shown", typeof(bool), typeof(CatalogPicturesView), new PropertyMetadata(false, OnShownChanged));

    public static bool GetShown(DependencyObject element) => (bool)element.GetValue(ShownProperty);

    public static void SetShown(DependencyObject element, bool value) => element.SetValue(ShownProperty, value);

    private static void OnShownChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        // After the layout: a thumbnail made for a picture shown at once has no place in the strip yet.
        if (e.NewValue is true && sender is FrameworkElement thumbnail)
            thumbnail.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => thumbnail.BringIntoView());
    }

    /// <summary>The mouse wheel scrolls the strip of thumbnails sideways, its only way.</summary>
    private void Strip_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var strip = (ScrollViewer)sender;
        if (strip.ScrollableWidth <= 0)
            return;
        strip.ScrollToHorizontalOffset(strip.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
}
