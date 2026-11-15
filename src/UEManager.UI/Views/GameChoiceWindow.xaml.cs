using System.Windows;
using System.Windows.Media;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;

namespace UEManager.UI.Views;

/// <summary>
/// The window at a start without a chosen game: the very first start, and once for everyone who used the app while
/// it knew only one game. It has the place and the size of the app's window, and holds only the game picker.
/// </summary>
public partial class GameChoiceWindow : Window
{
    /// <summary>How much darker than the dialog the plain background is, as the strength of a black veil.</summary>
    private const double PlainShade = 0.25;

    /// <param name="backdrop">
    /// The picture behind the picker, blurred; null for none, as for a served game (see <see cref="ServedGame"/>): then a
    /// plain dark anthracite in the game's look, and the picker shows the game's logo instead (the user's choice, 2026-11-15).
    /// </param>
    /// <param name="placement">Where the app's window was when it last closed; null opens this one centred.</param>
    /// <param name="servedArt">
    /// A served game's art on the plain background: its cover art top right, as on the game's pages, and its logo top left.
    /// Null for none.
    /// </param>
    public GameChoiceWindow(GamePickerViewModel picker, ImageSource? backdrop, WindowPlacement? placement, ArtSet? servedArt = null)
    {
        InitializeComponent();
        WindowFrame.Attach(this);
        Title = AppInfo.Name;
        AppName.Text = AppInfo.Name;
        DataContext = picker;
        if (backdrop is null)
        {
            // A tiny bit darker than the picker's dialog (the user's word, 2026-11-15): the dialog's own colour of the
            // game's look under a light black veil.
            Stage.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Brush.PanelSolid");
            Veil.Fill = Brushes.Black;
            Veil.Opacity = PlainShade;
            if (servedArt?.Background is { } cover)
            {
                ServedArtImage.Source = cover;
                ServedArt.Visibility = Visibility.Visible;
            }
            if (servedArt?.Logo is { } logo)
            {
                ServedLogo.Source = logo;
                ServedLogo.Visibility = Visibility.Visible;
            }
        }
        else
            Backdrop.Source = backdrop;
        // As the app's window: the start size is also the smallest.
        var smallest = MainWindow.Smallest(new Size(Width, Height), SystemParameters.WorkArea);
        MinWidth = Width = smallest.Width;
        MinHeight = Height = smallest.Height;
        if (placement is not null)
            MainWindow.Place(this, placement);
        OnWindowStateChanged();
        StateChanged += (_, _) => OnWindowStateChanged();
    }

    private void OnWindowStateChanged()
    {
        // A maximized chromeless window reaches past the screen edge by the resize border.
        var maximized = WindowState == WindowState.Maximized;
        Root.Margin = maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? L.T("Restore") : L.T("Maximize");
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
