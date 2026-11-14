using System.Globalization;
using System.IO;
using System.Windows.Input;
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
/// <param name="FolderPath">For a folder in the mod details, the folder in the game; null elsewhere.</param>
/// <param name="OpenFolder">Opens <paramref name="FolderPath"/> in Explorer; null where the row has none.</param>
public sealed record ModTreeRow(int Depth, string Name, ModFileItem? File, string? FolderPath = null, ICommand? OpenFolder = null)
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
/// <param name="gameRoot">The game's install folder, which <see cref="ModFileItem.Place"/> is relative to.</param>
public sealed partial class ModDetailsViewModel(MainViewModel owner, ModRecord mod, IReadOnlyList<ModFileItem> files,
    IReadOnlyList<string> changedFiles, string gameRoot) : LocalizedObservableObject
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

    /// <summary>The mod's page, on openmods or on the mod site; see <see cref="MainViewModel.ModPageOf"/>.</summary>
    public string? ModPageUrl { get; } = owner.ModPageOf(mod);

    /// <summary>The page is on openmods: the link reads "openmods.nexus", in orange, as in the mod's row.</summary>
    public bool IsOpenModsPage { get; } = owner.IsOpenModsPage(mod);

    public string ModPageText => MainViewModel.ModPageText(IsOpenModsPage);

    private ModUpdateStatus? Update => ModPageUrl is null ? null : owner.ModUpdateOf(mod);

    /// <summary>The mod page has a newer version; see <see cref="ModItemViewModel.IsUpdateAvailable"/>.</summary>
    public bool IsUpdateAvailable => Update?.IsAvailable == true;

    public string UpdateBoxText => MainViewModel.UpdateBoxText(mod);

    public bool IsUpToDate => Update is { IsAvailable: false };

    public bool IsNotTracked => ModPageUrl is not null && mod.NotTracked;

    /// <summary>The check asks for this mod's page version; off keeps the installed version without a note on newer ones.</summary>
    public bool TrackUpdates
    {
        get => !mod.NotTracked;
        set
        {
            if (value == !mod.NotTracked)
                return;
            owner.SetModTracked(mod, value);
            OnPropertyChanged(string.Empty);
        }
    }

    public string? UpdateHint => MainViewModel.ModUpdateHint(mod, Update);

    /// <summary>The blue box; an update from openmods replaces this mod, so the details close first.</summary>
    [RelayCommand]
    private Task OpenModFiles()
    {
        if (owner.CatalogModOf(mod) is { InstallFile: not null })
            owner.CloseModDetails();
        return owner.OpenModUpdateAsync(mod);
    }

    [RelayCommand]
    private void OpenModPage()
    {
        if (ModPageUrl is not null)
            owner.OpenPath(ModPageUrl);
    }

    public string? CatalogId => mod.CatalogId;

    /// <summary>The mods on the card stand apart from the list that tags filter, so they have no tags to add.</summary>
    public bool CanTag => !mod.Recommended;

    /// <summary>The player's tags; null with none and while they are edited, so the row stays away.</summary>
    public string? TagsText => mod.Tags is { Count: > 0 } && !IsRenaming ? ModTags.Join(mod.Tags) : null;

    public string SizeText => $"{Format.Size(mod.TotalSize)}  ·  {L.Plural(files.Count, "{0} file", "{0} files")}";

    public IReadOnlyList<ModFileItem> Files => files;

    /// <summary>The files in the tree of their folders in the game; each folder opens in Explorer.</summary>
    public IReadOnlyList<ModTreeRow> Rows { get; } = Tree(files, gameRoot, folder => owner.OpenPath(NearestFolder(folder, gameRoot)));

    /// <summary>
    /// The folder, or the nearest one above it that is there: a disabled mod's own folder is gone from the game.
    /// Never above the game's folder.
    /// </summary>
    internal static string NearestFolder(string folder, string gameRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
        for (var place = Path.GetFullPath(folder); place.Length > root.Length; place = Path.GetDirectoryName(place)!)
        {
            if (Directory.Exists(place))
                return place;
        }
        return root;
    }

    [RelayCommand]
    private void Close() => owner.CloseModDetails();

    /// <summary>Files changed outside the manager stop a disable, an update and an uninstall, until the player keeps them.</summary>
    public bool HasChangedFiles => changedFiles.Count > 0;

    public string ChangedText => L.F($"Changed outside the manager: {string.Join(", ", changedFiles)}. Until you keep your version, the mod cannot be turned off, updated or uninstalled.");

    [RelayCommand]
    private Task KeepChangedFiles() => owner.KeepChangedFilesAsync([mod.Id], mod);

    /// <summary>The name, version and tags can be changed in place of the title; the bottom row then saves or cancels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowing))]
    [NotifyPropertyChangedFor(nameof(TagsText))]
    private bool isRenaming;

    public bool IsShowing => !IsRenaming;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRenameCommand))]
    private string newName = "";

    [ObservableProperty]
    private string newVersion = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Suggestions), nameof(HasSuggestions))]
    [NotifyCanExecuteChangedFor(nameof(AcceptSuggestionCommand))]
    private string newTags = "";

    /// <summary>Tags to pick under the box; see <see cref="TagHints"/>.</summary>
    public IReadOnlyList<string> Suggestions => TagHints.For(NewTags, TagHints.Own(owner.Mods.Select(item => item.Record)));

    public bool HasSuggestions => Suggestions.Count > 0;

    [RelayCommand]
    private void AddSuggestion(string tag) => NewTags = TagHints.Apply(NewTags, tag);

    private bool CanAcceptSuggestion() => TagHints.IsTyping(NewTags) && Suggestions.Count > 0;

    /// <summary>Tab completes the tag being typed with the first one offered.</summary>
    [RelayCommand(CanExecute = nameof(CanAcceptSuggestion))]
    private void AcceptSuggestion() => AddSuggestion(Suggestions[0]);

    /// <summary>The form opened to add a tag, so the cursor starts in the tags box, after the tags the mod has.</summary>
    internal bool AddingTag { get; private set; }

    /// <summary>The mod's row opened the form: when it is done, so is the window.</summary>
    private bool closeWhenDone;

    [RelayCommand]
    private void Rename() => OpenForm(addingTag: false);

    [RelayCommand]
    private void AddTag() => OpenForm(addingTag: true);

    /// <summary>The button in the mod's row: straight to its tags, and back to the list after them.</summary>
    internal void OpenToAddTag()
    {
        closeWhenDone = true;
        OpenForm(addingTag: true);
    }

    private void OpenForm(bool addingTag)
    {
        NewName = mod.Name;
        NewVersion = mod.Version ?? "";
        NewTags = ModTags.Join(mod.Tags);
        if (addingTag && NewTags.Length > 0)
            NewTags += ", ";
        AddingTag = addingTag;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsRenaming = false;
        if (closeWhenDone)
            owner.CloseModDetails();
    }

    private bool CanSaveRename() => !string.IsNullOrWhiteSpace(NewName);

    [RelayCommand(CanExecute = nameof(CanSaveRename))]
    private async Task SaveRename()
    {
        if (await owner.RenameModAsync(mod, NewName, NewVersion, NewTags))
        {
            IsRenaming = false;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Summary));
            if (closeWhenDone)
                owner.CloseModDetails();
        }
    }

    /// <summary>
    /// The files as a tree of their folders, by <see cref="ModFileItem.Place"/>: folders first, each by name. A folder
    /// that holds only one folder shares its line with it, as "platform\steam\ue4ss\Mods", so the tree stays short.
    /// With <paramref name="gameRoot"/> and <paramref name="open"/>, each folder line can open its folder in the game.
    /// </summary>
    internal static IReadOnlyList<ModTreeRow> Tree(IEnumerable<ModFileItem> files, string? gameRoot = null, Action<string>? open = null)
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
        AddRows(root, 0, rows, "", gameRoot, open);
        return rows;
    }

    private static void AddRows(TreeFolder folder, int depth, List<ModTreeRow> rows, string place, string? gameRoot, Action<string>? open)
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
            var path = Path.Combine(place, name);
            var inGame = gameRoot is null || open is null ? null : Path.Combine(gameRoot, path);
            rows.Add(new ModTreeRow(depth, name, null, inGame, inGame is null ? null : new RelayCommand(() => open!(inGame))));
            AddRows(shown, depth + 1, rows, path, gameRoot, open);
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
