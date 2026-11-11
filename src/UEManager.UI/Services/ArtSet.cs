using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UEManager.Core;

namespace UEManager.UI.Services;

/// <summary>
/// A game's optional artwork, compiled into the exe from Theme\Art\&lt;game id&gt;. Every image may be missing.
/// </summary>
public sealed record ArtSet(ImageSource? Background, ImageSource? Logo)
{
    public bool HasBackground => Background is not null;

    public bool HasLogo => Logo is not null;

    /// <summary>The cover art and the emblem of the game the app runs for.</summary>
    public static ArtSet Load(string gameId) => new(LoadImage($"{gameId}/background"), LoadImage($"{gameId}/logo"));

    /// <summary>The game's picture on its card in the game picker, 16:9.</summary>
    public static ImageSource? Card(string gameId) => LoadImage($"{gameId}/card");

    /// <summary>The picture behind the game picker at a start without a game: there is no game's view to show yet.</summary>
    public static ImageSource? Backdrop() => LoadImage("backdrop");

    private static ImageSource? LoadImage(string name)
    {
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg" })
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri($"pack://application:,,,/Theme/Art/{name}{extension}", UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (IOException)
            {
                // Not built into this exe.
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException)
            {
                Log.Warn($"Artwork '{name}{extension}' could not be read: {ex.Message}");
            }
        }
        return null;
    }
}
