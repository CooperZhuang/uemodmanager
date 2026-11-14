namespace UEManager.Core.Storage;

/// <summary>
/// One program at a time changes a data folder: the app for as long as it runs, the command line tool for one command.
/// Two would each keep their own copy of the mod list and overwrite each other's changes, and the app empties the
/// staging folder at its start. The lock is a file that only its holder may open; Windows closes it, and deletes it,
/// when the holder ends, even after a crash.
/// </summary>
public static class DataFolderLock
{
    /// <summary>Takes the data folder's lock, or returns null while another program holds it.</summary>
    /// <returns>The lock; disposing it, or the end of the process, gives the folder free.</returns>
    public static FileStream? TryTake(AppPaths paths)
    {
        try
        {
            return new FileStream(paths.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
