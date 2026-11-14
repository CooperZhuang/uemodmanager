using System.Windows;
using System.Windows.Controls;

namespace UEManager.UI.Catalog;

/// <summary>
/// The openmods page as a grid of cards over the whole window below the title bar, at the window's size
/// (<see cref="CatalogViewModel.IsFullView"/>). It leaves the window's size alone: until 2026-11-14 it maximized the
/// window and gave its size back when it went, and fades were tried to hide the flicker of that; the user chose a view
/// that does neither.
/// </summary>
public partial class CatalogFullView : UserControl
{
    /// <summary>
    /// The narrowest a card gets: the grid has as many columns as fit cards of at least this width. 300 puts four in a
    /// row at the start size of the window, 1360 wide (the user's wish; with 320 there were three, too big).
    /// </summary>
    private const double CardMinWidth = 300;

    /// <summary>The space right of and below each card.</summary>
    private const double CardGap = 16;

    /// <summary>A card's margin, as the view's XAML takes it, so the gap is set in one place.</summary>
    public static Thickness CardMargin { get; } = new(0, 0, CardGap, CardGap);

    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(nameof(Columns), typeof(int), typeof(CatalogFullView), new PropertyMetadata(3));

    /// <summary>How many cards fit next to each other.</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public CatalogFullView()
    {
        InitializeComponent();
        Cards.SizeChanged += (_, e) => Columns = ColumnsFor(e.NewSize.Width);
        // Esc leaves the view: its key binding needs the focus inside.
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
                Dispatcher.BeginInvoke(() => Focus());
        };
    }

    /// <summary>
    /// How many cards fit next to each other in the width the card list has: each needs <see cref="CardMinWidth"/> and
    /// its gap, and the last one's gap may hang over the edge.
    /// </summary>
    internal static int ColumnsFor(double width) => Math.Max(1, (int)((width + CardGap) / (CardMinWidth + CardGap)));
}
