using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Mods;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>A file of a mod in its details: where it goes in the game, how big it is, and its state there.</summary>
/// <param name="Tone">"good", "warn" or "bad" for the colour of the state; empty while the mod is off.</param>
public sealed record ModFileItem(string Place, string FullPath, string Size, string State, string Tone);

/// <summary>A line of a mod's files as a tree of their folders in the game: a folder, or a file with its size and state.</summary>
/// <param name="Depth">How far the line is indented: 0 for the top folders.</param>
/// <param name="File">The file; null for a folder.</param>
public sealed record ModTreeRow(int Depth, string Name, ModFileItem? File)
{
    public bool IsFolder => File is null;

    public double Indent => Depth * 18;

    public string Glyph => IsFolder ? "" : "";
}

/// <summary>
/// Everything about one installed mod: where it came from, when it was installed, and each file it placed in the
/// game with its state, as the player or a helper needs it to check a mod by hand.
/// </summary>
/// <param name="changedFiles">The files changed outside the manager, as the player knows them, such as Scripts/main.lua.</param>
public sealed partial class ModDetailsViewModel(MainViewModel owner, ModRecord mod, IReadOnlyList<ModFileItem> files,
    IReadOnlyList<string> changedFiles) : LocalizedObservableObject
{
    /// <summary>
    /// Files up to this size are compared by checksum here, so a change that keeps the size shows too; bigger ones
    /// only by size, so the details open at once.
    /// </summary>
    internal const long ChecksumUpTo = 1024 * 1024;

    public string Name => mod.Name;

    public string Summary => string.Join("  ·  ", new[]
    {
        Format.Kind(mod.TypeLabel),
        mod.Version is null ? null : L.F($"Version {mod.Version}"),
        mod.Variant is null ? null : L.F($"Variant {mod.Variant}"),
        mod.Enabled ? L.T("Enabled") : L.T("Disabled"),
    }.Where(part => part is not null));

    public string InstalledText => mod.InstalledAt.LocalDateTime.ToString("f", CultureInfo.CurrentCulture);

    /// <summary>The download or folder it was installed from, as the player knows it.</summary>
    public string SourceText => mod.SourceFileName ?? "—";

    public string? CatalogId => mod.CatalogId;

    public string SizeText => $"{Format.Size(mod.TotalSize)}  ·  {L.Plural(files.Count, "{0} file", "{0} files")}";

    public IReadOnlyList<ModFileItem> Files => files;

    /// <summary>The files in the tree of their folders in the game.</summary>
    public IReadOnlyList<ModTreeRow> Rows { get; } = Tree(files);

    [RelayCommand]
    private void Close() => owner.CloseModDetails();

    /// <summary>Files changed outside the manager stop a disable, an update and an uninstall, until the player keeps them.</summary>
    public bool HasChangedFiles => changedFiles.Count > 0;

    public string ChangedText => L.F($"Changed outside the manager: {string.Join(", ", changedFiles)}. Until you keep your version, the mod cannot be turned off, updated or uninstalled.");

    [RelayCommand]
    private Task KeepChangedFiles() => owner.KeepChangedFilesAsync([mod.Id], mod);

    /// <summary>The name and version can be changed in place of the title; the bottom row then saves or cancels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowing))]
    private bool isRenaming;

    public bool IsShowing => !IsRenaming;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRenameCommand))]
    private string newName = "";

    [ObservableProperty]
    private string newVersion = "";

    [RelayCommand]
    private void Rename()
    {
        NewName = mod.Name;
        NewVersion = mod.Version ?? "";
        IsRenaming = true;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    private bool CanSaveRename() => !string.IsNullOrWhiteSpace(NewName);

    [RelayCommand(CanExecute = nameof(CanSaveRename))]
    private async Task SaveRename()
    {
        if (await owner.RenameModAsync(mod, NewName, NewVersion))
        {
            IsRenaming = false;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>
    /// The files as a tree of their folders, by <see cref="ModFileItem.Place"/>: folders first, each by name. A folder
    /// that holds only one folder shares its line with it, as "platform\steam\ue4ss\Mods", so the tree stays short.
    /// </summary>
    internal static IReadOnlyList<ModTreeRow> Tree(IEnumerable<ModFileItem> files)
    {
        var root = new TreeFolder("");
        foreach (var file in files)
        {
            var parts = file.Place.Split('\\', '/');
            var folder = root;
            foreach (var part in parts[..^1])
                folder = folder.Child(part);
            folder.Files.Add((parts[^1], file));
        }
        var rows = new List<ModTreeRow>();
        AddRows(root, 0, rows);
        return rows;
    }

    private static void AddRows(TreeFolder folder, int depth, List<ModTreeRow> rows)
    {
        foreach (var child in folder.Folders.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var shown = child;
            var name = child.Name;
            while (shown.Files.Count == 0 && shown.Folders.Count == 1)
            {
                shown = shown.Folders[0];
                name += Path.DirectorySeparatorChar + shown.Name;
            }
            rows.Add(new ModTreeRow(depth, name, null));
            AddRows(shown, depth + 1, rows);
        }
        foreach (var (name, file) in folder.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            rows.Add(new ModTreeRow(depth, name, file));
    }

    private sealed class TreeFolder(string name)
    {
        public string Name => name;

        public List<TreeFolder> Folders { get; } = [];

        public List<(string Name, ModFileItem File)> Files { get; } = [];

        public TreeFolder Child(string part)
        {
            var child = Folders.FirstOrDefault(f => string.Equals(f.Name, part, StringComparison.OrdinalIgnoreCase));
            if (child is null)
                Folders.Add(child = new TreeFolder(part));
            return child;
        }
    }

    /// <summary>
    /// The state of a mod's file in the game, with the tones of <see cref="ModManager.Integrity"/>. <paramref name="changed"/>
    /// is the file's place in <see cref="ModManager.ChangedFiles"/>; otherwise a file counts as changed by its size.
    /// </summary>
    internal static (string State, string Tone) StateOf(ModRecord mod, ModFileRecord file, bool changed = false)
    {
        if (!mod.Enabled)
            return (L.T("Not in the game"), "");
        var info = file.DeployedPath is null ? null : new FileInfo(file.DeployedPath);
        if (info is null || !info.Exists)
            return (L.T("Missing"), "warn");
        if (file.IsSettingsFile)
            return (L.T("In the game: settings you may change"), "good");
        if (file.Kept)
            return (L.T("In the game: your version, kept"), "good");
        // A borrowed shared file belongs to whoever put it there, such as UE4SS, which may update it.
        return changed || (info.Length != file.Size && !file.Borrowed)
            ? (L.T("Changed outside the manager"), "bad")
            : (L.T("In the game"), "good");
    }
}
