using System.Windows.Controls;
using System.Windows.Media;
using UEManager.UI.Services;

namespace UEManager.UI.Views;

public partial class ResetModdingView : UserControl
{
    public ResetModdingView()
    {
        InitializeComponent();
        // As the folder report: the game's colours for the text and its selection, no links.
        var area = Viewer.TextArea;
        area.SelectionBrush = (Brush)FindResource("Brush.Selection");
        area.SelectionBorder = null;
        area.SelectionForeground = null;
        area.Caret.CaretBrush = (Brush)FindResource("Brush.Text");
        Viewer.Options.EnableHyperlinks = false;
        Viewer.Options.EnableEmailHyperlinks = false;
        Viewer.SyntaxHighlighting = Syntax.Log;
        // A live log: each new line comes into sight, so the last one is the step at work, or the one that stopped it.
        Viewer.DocumentChanged += (_, _) =>
        {
            if (Viewer.Document is { } document)
                document.TextChanged += (_, _) => Viewer.ScrollToEnd();
        };
    }
}
