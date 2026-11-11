using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UEManager.Core;

namespace UEManager.UI.Services;

/// <summary>
/// The app's icon in the notification area, next to the clock. It passes a left and a right click on, and comes back by
/// itself when Explorer starts again. It uses the tray function of Windows directly, without a library.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    /// <summary>The title of the icon's hidden window, by which a test finds it.</summary>
    internal const string WindowTitle = "UEModManager.Tray";

    /// <summary>The message Windows sends the hidden window for a click on the icon: WM_APP + 1.</summary>
    private const int ClickMessage = 0x8001;

    private const int Add = 0, Modify = 1, Delete = 2;
    private const int WithMessage = 0x1, WithIcon = 0x2, WithTip = 0x4;
    private const int LeftButtonUp = 0x0202, RightButtonUp = 0x0205;

    /// <summary>The width and height of a small icon, such as the tray's: 16 at 100 % scale, 32 at 200 %.</summary>
    private const int SmallIconWidth = 49;

    private readonly HwndSource window;

    /// <summary>The message Explorer sends every window when it started again, with an empty tray.</summary>
    private readonly int taskbarCreated = RegisterWindowMessage("TaskbarCreated");

    private readonly IntPtr icon;
    private string tip;
    private bool shown;

    /// <param name="picture">The icon; see <see cref="Draw"/>.</param>
    /// <param name="tip">The text over the icon, such as "UE MM · Mortal Shell II".</param>
    public TrayIcon(BitmapSource picture, string tip)
    {
        this.tip = tip;
        // A hidden window of its own receives the clicks. Not a message-only window: those miss Explorer's restart.
        window = new HwndSource(new HwndSourceParameters(WindowTitle) { Width = 0, Height = 0, WindowStyle = 0 });
        window.AddHook(OnMessage);
        icon = IconOf(picture);
        shown = Notify(Add);
        if (!shown)
            Log.Warn("The tray icon could not be added; it comes when Explorer starts again.");
    }

    /// <summary>A left click on the icon.</summary>
    public event Action? LeftClick;

    /// <summary>A right click on the icon.</summary>
    public event Action? RightClick;

    /// <summary>The size of the tray's icons on this screen, in pixels.</summary>
    public static int Size => Math.Max(16, GetSystemMetrics(SmallIconWidth));

    /// <summary>A new text over the icon, as after a change of language.</summary>
    public void SetTip(string text)
    {
        tip = text;
        if (shown)
            Notify(Modify);
    }

    /// <summary>
    /// The app's mark as the tray shows it: the mod stack on a dark tile, as the app's icon, in the colours of the game the
    /// app runs for, as the mark in the title bar. The brushes come from the game's look.
    /// </summary>
    /// <param name="size">The width and height in pixels; see <see cref="Size"/>.</param>
    /// <param name="brush">A brush of the look by its key, such as "Brush.Accent".</param>
    public static BitmapSource Draw(int size, Func<string, Brush> brush)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            // The tile fills the square, as in the app's icon at small sizes; the plates sit on whole pixels, so they
            // stay sharp. The same measures as the plates of app.ico, on a 240 grid.
            var radius = size * 0.19;
            var edge = new Pen(brush("Brush.LineStrong"), Math.Max(1.0, size / 60.0));
            context.DrawRoundedRectangle(brush("Brush.PanelSolid"), edge, new Rect(0.5, 0.5, size - 1, size - 1), radius, radius);
            var height = Math.Round(size * 38 / 240.0);
            var gap = Math.Max(1, Math.Round(size * 14 / 240.0));
            var top = Math.Round((size - (3 * height + 2 * gap)) / 2);
            var width = size * 124 / 240.0;
            var skew = size * 36 / 240.0;
            var left = (size - (width + skew)) / 2;
            string[] plates = ["Brush.Accent", "Brush.TextMuted", "Brush.TextDim"];
            for (var k = 0; k < plates.Length; k++)
            {
                var y = top + k * (height + gap);
                var plate = new StreamGeometry();
                using (var line = plate.Open())
                {
                    line.BeginFigure(new Point(left + skew, y), isFilled: true, isClosed: true);
                    line.LineTo(new Point(left + skew + width, y), isStroked: false, isSmoothJoin: false);
                    line.LineTo(new Point(left + width, y + height), isStroked: false, isSmoothJoin: false);
                    line.LineTo(new Point(left, y + height), isStroked: false, isSmoothJoin: false);
                }
                plate.Freeze();
                context.DrawGeometry(brush(plates[k]), null, plate);
            }
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private bool Notify(int message)
    {
        var data = new NotifyIconData
        {
            Size = Marshal.SizeOf<NotifyIconData>(),
            Window = window.Handle,
            Id = 1,
            Flags = WithMessage | WithIcon | WithTip,
            CallbackMessage = ClickMessage,
            Icon = icon,
            Tip = tip.Length > 127 ? tip[..127] : tip,
        };
        return Shell_NotifyIcon(message, ref data);
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == ClickMessage)
        {
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case LeftButtonUp:
                    LeftClick?.Invoke();
                    break;
                case RightButtonUp:
                    RightClick?.Invoke();
                    break;
            }
            handled = true;
        }
        else if (message == taskbarCreated)
        {
            shown = Notify(Add);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (shown)
            Notify(Delete);
        shown = false;
        window.RemoveHook(OnMessage);
        window.Dispose();
        if (icon != IntPtr.Zero)
            DestroyIcon(icon);
    }

    /// <summary>A Windows icon of a picture: the picture as a PNG in an icon's data, which Windows reads since Vista.</summary>
    private static IntPtr IconOf(BitmapSource picture)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var data = stream.ToArray();
        return CreateIconFromResourceEx(data, data.Length, true, 0x00030000, picture.PixelWidth, picture.PixelHeight, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr Window;
        public int Id;
        public int Flags;
        public int CallbackMessage;
        public IntPtr Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public int State;
        public int StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public int TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public int InfoFlags;
        public Guid Item;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconFromResourceEx(byte[] data, int size, bool icon, int version, int width, int height, int flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
