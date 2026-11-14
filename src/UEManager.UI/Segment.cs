using System.Windows;

namespace UEManager.UI;

/// <summary>
/// One side of a two-way switch drawn as plain buttons with the style Segment, such as the openmods page's
/// Mods | Tools: whether it is the side shown. The view model decides it; a click only runs the button's command.
/// Radio buttons bound both ways to the two sides wrote an unchecked neighbor back into the view model, which checked
/// it again, until the stack overflowed at the start on a PC (2026-11-13); arrow keys and accessibility tools could
/// also check one without a click.
/// </summary>
public static class Segment
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(Segment), new PropertyMetadata(false));

    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);

    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);
}
