using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace UEManager.UI;

/// <summary>
/// Puts the cursor at the end of a text box when its text comes from the view model, such as a picked tag, so the player
/// types on after it. Typing itself never moves it. The binding needs NotifyOnTargetUpdated.
/// </summary>
public static class CaretAtEnd
{
    public static readonly DependencyProperty OnUpdateProperty = DependencyProperty.RegisterAttached(
        "OnUpdate", typeof(bool), typeof(CaretAtEnd), new PropertyMetadata(false, OnChanged));

    public static bool GetOnUpdate(DependencyObject element) => (bool)element.GetValue(OnUpdateProperty);

    public static void SetOnUpdate(DependencyObject element, bool value) => element.SetValue(OnUpdateProperty, value);

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBox box)
            return;
        box.TargetUpdated -= OnTargetUpdated;
        if ((bool)e.NewValue)
            box.TargetUpdated += OnTargetUpdated;
    }

    private static void OnTargetUpdated(object? sender, DataTransferEventArgs e)
    {
        if (sender is TextBox box && e.Property == TextBox.TextProperty)
            box.CaretIndex = box.Text.Length;
    }
}
