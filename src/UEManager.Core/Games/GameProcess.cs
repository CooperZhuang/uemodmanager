using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UEManager.Core.Games;

/// <summary>Finds the running game, so that no file is changed while the game uses it.</summary>
public static class GameProcess
{
    /// <summary>Rights to read a process's exe path, which Windows also gives for a process started as administrator.</summary>
    private const uint QueryLimitedInformation = 0x1000;

    /// <summary>The error OpenProcess gives for a process that has ended.</summary>
    private const int InvalidParameter = 87;

    /// <summary>Whether an executable from the game's exe folder is running.</summary>
    public static bool IsRunning(GameInstall game)
    {
        string[] executables;
        try
        {
            executables = Directory.Exists(game.BinariesPath) ? Directory.GetFiles(game.BinariesPath, "*.exe") : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        foreach (var executable in executables)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                using (process)
                {
                    var path = PathOf(process.Id, out var error);
                    // Another program of the same name, or one that has just ended.
                    if (path is not null ? !SamePath(path, executable) : error == InvalidParameter)
                        continue;
                    // A path Windows does not tell even so counts as the game, as a change while it runs could break it.
                    Log.Info(path is not null
                        ? $"{game.Profile.DisplayName} is running: {Path.GetFileName(executable)}, process {process.Id} ({path})."
                        : $"{game.Profile.DisplayName} is running: {Path.GetFileName(executable)}, process {process.Id} (its path cannot be read, Windows error {error}, so it counts as the game).");
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// The full path of a process's exe, read with the query Windows allows for most processes, also for one started
    /// as administrator, which the path of its main module does not tell. Null when even that is refused.
    /// </summary>
    internal static string? PathOf(int processId, out int error)
    {
        using var handle = OpenProcess(QueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            error = Marshal.GetLastWin32Error();
            return null;
        }
        var name = new char[32768];
        var size = name.Length;
        if (!QueryFullProcessImageName(handle, 0, name, ref size))
        {
            error = Marshal.GetLastWin32Error();
            return null;
        }
        error = 0;
        return new string(name, 0, size);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] name, ref int size);
}
