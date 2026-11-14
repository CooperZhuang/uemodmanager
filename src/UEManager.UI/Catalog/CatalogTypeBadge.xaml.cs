using System.Windows;
using System.Windows.Controls;

namespace UEManager.UI.Catalog;

/// <summary>A mod's type box on the openmods page and in its full view.</summary>
public partial class CatalogTypeBadge : UserControl
{
    public static readonly DependencyProperty KeepsRoomProperty =
        DependencyProperty.Register(nameof(KeepsRoom), typeof(bool), typeof(CatalogTypeBadge), new PropertyMetadata(false));

    public CatalogTypeBadge() => InitializeComponent();

    /// <summary>
    /// Without a type, the box keeps its room, empty, instead of taking none: in the full view's cards the lines below
    /// it then start at the same height in a row (the user's choice).
    /// </summary>
    public bool KeepsRoom
    {
        get => (bool)GetValue(KeepsRoomProperty);
        set => SetValue(KeepsRoomProperty, value);
    }
}
