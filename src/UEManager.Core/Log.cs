namespace UEManager.Core;

/// <summary>A line of the log as a watcher gets it: when, how serious (INFO, WARN, ERROR), and what.</summary>
public sealed record LogLine(DateTime Time, string Level, string Message)
{
    public override string ToString() => $"{Time:HH:mm:ss}  {Level,-5}  {Message}";
}

/// <summary>Plain text log, one file per day in the data folder's logs directory.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly AsyncLocal<Action<LogLine>?> Watcher = new();
    private static string? logsDir;

    public static void Initialize(string directory)
    {
        Directory.CreateDirectory(directory);
        logsDir = directory;
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    /// <summary>
    /// A detail of the work, such as each file a change moves. Written only while someone watches the work (see
    /// <see cref="Watch"/>): then into the log file as well, so the list is there after the watcher is gone. Without a
    /// watcher, a change of a mod with a thousand files would add a thousand lines each time.
    /// </summary>
    public static void Step(string message)
    {
        if (Watcher.Value is not null)
            Write("INFO", message);
    }

    /// <summary>
    /// Sends every line that this flow of work writes, and the work it starts, to <paramref name="watcher"/> as well, until
    /// the result is disposed. Lines that other work writes at the same time, such as an update check, do not reach it.
    /// The watcher is called on the thread that writes the line.
    /// </summary>
    public static IDisposable Watch(Action<LogLine> watcher)
    {
        var before = Watcher.Value;
        Watcher.Value = watcher;
        return new Unwatch(before);
    }

    private sealed class Unwatch(Action<LogLine>? before) : IDisposable
    {
        public void Dispose() => Watcher.Value = before;
    }

    private static void Write(string level, string message)
    {
        var now = DateTime.Now;
        if (logsDir is not null)
        {
            var line = $"{now:yyyy-MM-dd HH:mm:ss} {level,-5} {message}{Environment.NewLine}";
            lock (Gate)
            {
                try
                {
                    File.AppendAllText(Path.Combine(logsDir, $"app-{now:yyyyMMdd}.log"), line);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Logging must never take the application down.
                }
            }
        }
        Watcher.Value?.Invoke(new LogLine(now, level, message));
    }
}
