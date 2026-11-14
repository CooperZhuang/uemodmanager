using System.Runtime.InteropServices;

namespace UEManager.UI.Services;

/// <summary>
/// Output for "UEModManager.exe --cli": the exe is a window program, so Windows gives it no console. Output that a
/// script, a pipe or a file takes keeps going there; else the program writes to the terminal it was started from.
/// </summary>
internal static class ParentConsole
{
    private const int AttachParentProcess = -1;

    public static void Attach()
    {
        if (!Console.IsOutputRedirected)
            AttachConsole(AttachParentProcess);
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
