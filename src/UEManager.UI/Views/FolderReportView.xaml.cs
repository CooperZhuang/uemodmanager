using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using UEManager.UI.Services;

namespace UEManager.UI.Views;

public partial class FolderReportView : UserControl
{
    public FolderReportView()
    {
        InitializeComponent();
        // As the settings editor: the game's colours for the text and its selection, no links.
        var area = Viewer.TextArea;
        area.SelectionBrush = (Brush)FindResource("Brush.Selection");
        area.SelectionBorder = null;
        area.SelectionForeground = null;
        area.Caret.CaretBrush = (Brush)FindResource("Brush.Text");
        Viewer.Options.EnableHyperlinks = false;
        Viewer.Options.EnableEmailHyperlinks = false;
        Viewer.SyntaxHighlighting = Syntax.Report;
        // The text takes the keys as the view shows, so the arrows scroll it and Esc closes it.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                Dispatcher.BeginInvoke(() =>
                {
                    Viewer.ScrollToHome();
                    Viewer.TextArea.Focus();
                }, DispatcherPriority.Input);
        };
    }
}
