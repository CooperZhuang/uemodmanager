using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Mods;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>A button above the mod list that shows only the mods with one tag, or all mods.</summary>
public sealed record TagFilterItem(string Label, bool IsActive, ICommand Select);

/// <summary>One row in the mod list. Rows are rebuilt after every change, so they hold no state of their own.</summary>
public sealed partial class ModItemViewModel : LocalizedObservableObject
{
    private readonly MainViewModel owner;
    private readonly ModRecord record;
    private readonly string libraryFolder;

    public ModItemViewModel(MainViewModel owner, ModRecord record, string libraryFolder)
    {
        this.owner = owner;
        this.record = record;
        this.libraryFolder = libraryFolder;
        Integrity = ModManager.Integrity(record);
        HasUe4ssErrors = owner.HadUe4ssErrors(record);
        ModPageUrl = record.Recommended ? null : owner.Profile.ModPage(record.SourceFileName);
    }

    /// <summary>
    /// The mod's page on the mod site, from the number in its download's file name; null without one, and for the
    /// mods on the card, which have their own notes and links.
    /// </summary>
    public string? ModPageUrl { get; }

    /// <summary>"No mod page" in the row: a mod of the list whose download's name has no mod number. The card's mods show neither.</summary>
    public bool ShowsNoModPage => !record.Recommended && ModPageUrl is null;

    internal ModRecord Record => record;

    /// <summary>UE4SS reported errors in this mod at the last game start.</summary>
    public bool HasUe4ssErrors { get; }

    [RelayCommand]
    private void ShowUe4ssErrors() => owner.ShowUe4ssErrors(record);

    public string Id => record.Id;

    public string Name => record.Name;

    public bool Enabled => record.Enabled;

    public string EnabledText => Enabled ? L.T("Enabled") : L.T("Disabled");

    public string Detail => string.Join("  ·  ", new[]
    {
        record.Version is null ? null : L.F($"Version {record.Version}"),
        record.Variant is null ? null : L.F($"Variant {record.Variant}"),
        L.Plural(record.Files.Count, "{0} file", "{0} files"),
        Format.Size(record.TotalSize),
    }.Where(part => part is not null));

    /// <summary>The player's tags, on a line of their own below <see cref="Detail"/>; null with none, so the line stays away.</summary>
    public string? TagsText => record.Tags is { Count: > 0 } ? ModTags.Join(record.Tags) : null;

    /// <summary>The mods on the card stand apart from the list that tags filter, so they have no tags to add.</summary>
    public bool CanTag => !record.Recommended;

    /// <summary>A Lua, C++ or Blueprint mod, which only runs with UE4SS.</summary>
    public bool NeedsUe4ss => record.NeedsUe4ss;

    /// <summary>The mod has a settings file the player may change, such as Scripts/config.lua.</summary>
    public bool HasSettings => record.Files.Any(f => f.IsSettingsFile);

    /// <summary>The mod has a settings file or one whose changed version the player kept: Reset settings puts back the mod's own.</summary>
    public bool CanResetSettings => record.Files.Any(f => f.IsPlayerFile && !f.Made);

    /// <summary>The settings files in the game, for the settings editor; none while the mod is disabled.</summary>
    public IReadOnlyList<SettingsFileItem> DeployedSettingsFiles =>
        record.Files.Where(f => f.IsSettingsFile && f.DeployedPath is not null)
            .Select(f => new SettingsFileItem(f.DeployedPath!, SettingsFiles.Name(f.FileName)))
            .ToList();

    public string EditSettingsHint => Enabled
        ? L.Join(L.F($"Edit this mod's settings: {string.Join(", ", record.Files.Where(f => f.IsSettingsFile).Select(f => SettingsFiles.Name(f.FileName)))}."),
            L.T("Most mods read them when the game starts."))
        : L.T("Enable the mod to edit its settings");

    public string KindText => Format.Kind(record.TypeLabel);

    /// <summary>A mod of game files or exe files puts files beside the game's own, so its row has a shade of its own.</summary>
    public bool IsBesideGameFiles => record.TypeLabel is ModTypes.GameFiles or ModTypes.ExeFiles;

    public DateTimeOffset InstalledAt => record.InstalledAt;

    public string InstalledText => record.InstalledAt.LocalDateTime.ToString(L.T("d MMM yyyy"), CultureInfo.CurrentCulture);

    public string InstalledFullText => record.InstalledAt.LocalDateTime.ToString("f", CultureInfo.CurrentCulture);

    public string FilesText => string.Join(Environment.NewLine, record.Files.Select(f => f.FileName));

    public ModIntegrity Integrity { get; }

    /// <summary>Empty for a disabled mod: the Status column already says so.</summary>
    public string IntegrityText => Integrity switch
    {
        ModIntegrity.Intact => L.T("Deployed"),
        ModIntegrity.FilesMissing => L.T("Files missing"),
        ModIntegrity.FilesChanged => L.T("Files changed"),
        _ => "",
    };

    public string IntegrityTone => Integrity switch
    {
        ModIntegrity.Intact => "good",
        ModIntegrity.FilesMissing => "warn",
        _ => "bad",
    };

    public string IntegrityGlyph => Integrity switch
    {
        ModIntegrity.Intact => "\uE73E",
        ModIntegrity.NotDeployed => "",
        _ => "\uE7BA",
    };

    public string? IntegrityHint => Integrity switch
    {
        ModIntegrity.Intact => FilesText,
        ModIntegrity.FilesMissing => L.T("Some deployed files are gone. Disable and enable the mod to put them back."),
        ModIntegrity.FilesChanged => L.T("A file was changed outside the manager, so the manager will not remove it. Open the mod's details to keep your version."),
        _ => null,
    };

    public bool Matches(string search) =>
        string.IsNullOrWhiteSpace(search)
        || Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (record.Version?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
        || record.TypeLabel.Contains(search, StringComparison.OrdinalIgnoreCase)
        || KindText.Contains(search, StringComparison.OrdinalIgnoreCase)
        || record.Files.Any(f => f.FileName.Contains(search, StringComparison.OrdinalIgnoreCase))
        || record.Tags?.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase)) == true;

    [RelayCommand]
    private Task Toggle() => owner.SetEnabledAsync(this, !Enabled);

    [RelayCommand]
    private Task Uninstall() => owner.UninstallAsync(this);

    [RelayCommand(CanExecute = nameof(Enabled))]
    private void EditSettings() => owner.EditSettings(this);

    [RelayCommand]
    private Task ResetSettings() => owner.ResetSettingsAsync(this);

    [RelayCommand]
    private void OpenFolder() => owner.OpenPath(libraryFolder);

    [RelayCommand]
    private void OpenModPage()
    {
        if (ModPageUrl is not null)
            owner.OpenPath(ModPageUrl);
    }

    [RelayCommand]
    private void ShowDetails() => owner.ShowModDetails(record);

    [RelayCommand]
    private void AddTag() => owner.ShowModDetails(record, addTag: true);
}
