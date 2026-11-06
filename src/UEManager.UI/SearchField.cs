using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace UEManager.UI;

/// <summary>What makes a text box a search box: its X clears the text, and so does Esc. See the TextBox.Search style.</summary>
public static class SearchField
{
    /// <summary>The X in the box. The box keeps the focus, to type the next search.</summary>
    public static ICommand Clear { get; } = new RelayCommand<TextBox>(box =>
    {
        box?.SetCurrentValue(TextBox.TextProperty, "");
        box?.Focus();
    });

    public static readonly DependencyProperty ClearsOnEscapeProperty = DependencyProperty.RegisterAttached(
        "ClearsOnEscape", typeof(bool), typeof(SearchField), new PropertyMetadata(false, OnClearsOnEscapeChanged));

    public static bool GetClearsOnEscape(DependencyObject element) => (bool)element.GetValue(ClearsOnEscapeProperty);

    public static void SetClearsOnEscape(DependencyObject element, bool value) => element.SetValue(ClearsOnEscapeProperty, value);

    private static void OnClearsOnEscapeChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBox box)
            return;
        box.PreviewKeyDown -= OnPreviewKeyDown;
        if ((bool)e.NewValue)
            box.PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>An empty box lets Esc pass, as it does anywhere else.</summary>
    internal static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not TextBox { Text.Length: > 0 } box)
            return;
        box.SetCurrentValue(TextBox.TextProperty, "");
        e.Handled = true;
    }
}
