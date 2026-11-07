using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>The files in ~mods that no mod in the list placed: the box below the list counts them, and a window lists them.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Those files as a tree of their folders, while the window is open; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnmanagedOpen))]
    private IReadOnlyList<ModTreeRow>? unmanagedRows;

    public bool IsUnmanagedOpen => UnmanagedRows is not null;

    [RelayCommand]
    private void ShowUnmanaged()
    {
        if (manager is null || Game is null)
            return;
        var root = Game.RootPath;
        try
        {
            UnmanagedRows = UnmanagedTree(manager.UnmanagedFiles(), Game.ModsPath, root,
                folder => OpenPath(ModDetailsViewModel.NearestFolder(folder, root)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast(L.F($"The mods folder could not be read: {ex.Message}"), isError: true);
        }
    }

    [RelayCommand]
    private void CloseUnmanaged() => UnmanagedRows = null;

    /// <summary>The files, by their path below ~mods, in the tree the mod details show: each with its size, each folder with Open folder.</summary>
    internal static IReadOnlyList<ModTreeRow> UnmanagedTree(IEnumerable<string> files, string modsPath, string gameRoot, Action<string>? open = null) =>
        ModDetailsViewModel.Tree(files.Select(relative =>
        {
            var full = Path.Combine(modsPath, relative);
            var info = new FileInfo(full);
            return new ModFileItem(Path.GetRelativePath(gameRoot, full), full, info.Exists ? Format.Size(info.Length) : "", "", "");
        }), gameRoot, open);
}
