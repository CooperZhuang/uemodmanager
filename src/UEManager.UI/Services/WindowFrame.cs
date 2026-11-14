using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace UEManager.UI.Services;

/// <summary>
/// Gives a chromeless window its Windows shadow and dark frame through the Desktop Window Manager, while keeping the
/// sharp corners of the app's look, and shows the window only once it is ready. Before its first drawing Windows
/// showed the frame's see-through material, and the main window then its loading state ("Game not found", "Reading
/// the mod list…"): two flashes at every start (2026-11-14). Now the window stays cloaked, so Windows shows nothing of
/// it, until it is drawn and, for the main window, until its first load is done.
/// </summary>
internal static class WindowFrame
{
    /// <summary>DWMWA_NCRENDERING_POLICY: turns non-client rendering, and with it the window shadow, on.</summary>
    private const int NonClientRenderingPolicy = 2;
    private const int RenderingEnabled = 2;

    /// <summary>
    /// DWMWA_CLOAK: the window exists and draws, but Windows shows nothing of it until it is uncloaked. (14 is
    /// DWMWA_CLOAKED, which only reads the state: setting it fails.)
    /// </summary>
    private const int CloakAttribute = 13;

    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE: dark frame and heavier shadow on Windows 10 (2004+) and Windows 11.</summary>
    private const int ImmersiveDarkMode = 20;

    /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE: keeps sharp corners on Windows 11 while the shadow stays on.</summary>
    private const int WindowCornerPreference = 33;
    private const int DoNotRound = 1;

    /// <summary>The longest a window stays hidden at its start: a slow first load shows its loading state after it.</summary>
    internal static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(3);

    /// <summary>Sets up the window's frame and its first showing.</summary>
    /// <param name="ready">
    /// What the window waits for besides its first drawing, such as the main window's first load; null for none. It is
    /// asked once the window is drawn, so it may read the window's data context.
    /// </param>
    public static void Attach(Window window, Func<Task>? ready = null)
    {
        var handle = IntPtr.Zero;
        var shown = true;
        void Show()
        {
            if (shown)
                return;
            shown = true;
            SetCloak(handle, false);
        }

        window.SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;
            Apply(handle);
            // Without cloaking (older Windows), the window shows at once, as it did before.
            shown = !SetCloak(handle, true);
            if (shown)
                return;
            var timer = new DispatcherTimer { Interval = LongestWait };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Show();
            };
            timer.Start();
        };

        window.ContentRendered += async (_, _) =>
        {
            try
            {
                if (ready?.Invoke() is { } waiting)
                    await waiting;
                // What the load changed is drawn first, so the window shows in its final state.
                await Frames(2);
            }
            finally
            {
                Show();
            }
        };
    }

    internal static bool Apply(IntPtr handle)
    {
        var policy = RenderingEnabled;
        var policyResult = DwmSetWindowAttribute(handle, NonClientRenderingPolicy, ref policy, sizeof(int));

        var dark = 1;
        _ = DwmSetWindowAttribute(handle, ImmersiveDarkMode, ref dark, sizeof(int));

        var corners = DoNotRound;
        _ = DwmSetWindowAttribute(handle, WindowCornerPreference, ref corners, sizeof(int));

        var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        var frameResult = DwmExtendFrameIntoClientArea(handle, ref margins);
        return policyResult == 0 && frameResult == 0;
    }

    /// <summary>Hides the window from view, or shows it again; false when Windows cannot.</summary>
    internal static bool SetCloak(IntPtr handle, bool cloaked)
    {
        var value = cloaked ? 1 : 0;
        return DwmSetWindowAttribute(handle, CloakAttribute, ref value, sizeof(int)) == 0;
    }

    /// <summary>Done after WPF has drawn this many more frames.</summary>
    private static Task Frames(int count)
    {
        var done = new TaskCompletionSource();
        void OnRendering(object? sender, EventArgs e)
        {
            if (--count > 0)
                return;
            CompositionTarget.Rendering -= OnRendering;
            done.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        return done.Task;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
