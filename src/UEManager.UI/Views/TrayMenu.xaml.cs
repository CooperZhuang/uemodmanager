using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using UEManager.UI.ViewModels;

namespace UEManager.UI.Views;

/// <summary>
/// The tray icon's menu, in the app's look instead of the Windows menu. It opens next to the cursor, inside the screen's
/// work area, and closes after a click, on Esc, or when the player clicks anywhere else.
/// </summary>
public partial class TrayMenu : Window
{
    private bool closing;

    public TrayMenu(TrayMenuViewModel menu)
    {
        InitializeComponent();
        DataContext = menu;
        menu.Finished += Shut;
        Deactivated += (_, _) => Shut();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Shut();
        };
        Closing += (_, _) => closing = true;
    }

    private void Shut()
    {
        if (closing)
            return;
        closing = true;
        Close();
    }

    /// <summary>
    /// Opens the menu at the right edge of the screen the cursor is on, as Windows' own panels by the clock: above the
    /// taskbar when it is below, under it when it is on top.
    /// </summary>
    public void ShowAtCursor()
    {
        GetCursorPos(out var cursor);
        // First near the cursor, so the menu opens on that screen at its scale; then exactly, in pixels, once its size is known.
        var scale = GetDpiForSystem() / 96.0;
        Left = cursor.X / scale - 200;
        Top = cursor.Y / scale - 200;
        Opacity = 0;
        Show();
        var handle = new WindowInteropHelper(this).Handle;
        GetWindowRect(handle, out var bounds);
        var screen = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromPoint(cursor, NearestScreen), ref screen);
        var work = screen.Work;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var x = Math.Max(work.Left, work.Right - width);
        var y = Math.Clamp(cursor.Y - height, work.Top, Math.Max(work.Top, work.Bottom - height));
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, KeepSize | KeepOrder | NoActivate);
        Opacity = 1;
        Activate();
        // The menu must be in front, or a click elsewhere would not close it.
        SetForegroundWindow(handle);
    }

    private const uint NearestScreen = 2;
    private const uint KeepSize = 0x1, KeepOrder = 0x4, NoActivate = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct Spot
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Box
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Box Monitor;
        public Box Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Spot point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Box box);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Spot point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
