using System.Windows;
using System.Windows.Controls;

namespace UEManager.UI.Catalog;

/// <summary>The search, sort and switch row of the openmods page and of its full view.</summary>
public partial class CatalogToolbar : UserControl
{
    private const double MinWideWidth = 860;
    private bool isNarrow;

    public CatalogToolbar() => InitializeComponent();

    private void OnToolbarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < MinWideWidth;
        if (narrow == isNarrow)
            return;
        isNarrow = narrow;
        if (narrow)
        {
            Grid.SetRow(ControlsBox, 1);
            Grid.SetColumn(ControlsBox, 0);
            Grid.SetColumnSpan(ControlsBox, 3);
            ControlsBox.Margin = new Thickness(0, 8, 0, 0);
            ControlsBox.Padding = new Thickness(16, 7, 16, 7);
            ControlsBox.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            Grid.SetRow(ControlsBox, 0);
            Grid.SetColumn(ControlsBox, 1);
            Grid.SetColumnSpan(ControlsBox, 1);
            ControlsBox.Margin = new Thickness(10, 0, 0, 0);
            ControlsBox.Padding = new Thickness(16, 0, 16, 0);
            ControlsBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }
}
