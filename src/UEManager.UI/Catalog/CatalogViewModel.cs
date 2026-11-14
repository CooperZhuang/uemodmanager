using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core;
using UEManager.Core.Catalog;
using UEManager.Core.Mods;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;

namespace UEManager.UI.Catalog;

/// <summary>
/// The openmods page: the game's mods listed on openmods.nexus, each with a one-click install.
/// The lists are read when the page opens, at most every 10 minutes, as openmods asks.
/// </summary>
public sealed partial class CatalogViewModel : LocalizedObservableObject
{
    public const string SiteUrl = "https://openmods.nexus/";

    /// <summary>The site's full address on its link button, so the player sees where it goes.</summary>
    public string SiteText => SiteUrl.TrimEnd('/');

    /// <summary>The site's name, "openmods.nexus": the link to a mod's page there on the Mods page.</summary>
    public static string SiteName { get; } = new Uri(SiteUrl).Host;

    private readonly MainViewModel owner;
    private readonly HttpClient http;
    private readonly RemoteImages images;
    private readonly string gamesUrl;
    private readonly string? steamAppId;
    private readonly bool isTest;
    private ModException? failure;
    private DateTimeOffset loadedAt;
    private bool loaded;
    private IReadOnlyList<ModRecord> installed = [];

    /// <param name="gamesUrl">The openmods games list.</param>
    /// <param name="isTest">The list was given on the command line, so the page says that it is not the real one.</param>
    public CatalogViewModel(MainViewModel owner, HttpClient http, string gamesUrl, string? steamAppId, bool isTest,
        CatalogSortField sortField, bool sortDescending)
    {
        this.owner = owner;
        this.http = http;
        this.gamesUrl = gamesUrl;
        this.steamAppId = steamAppId;
        images = new RemoteImages(http);
        this.isTest = isTest;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = item => item is CatalogItemViewModel mod && mod.IsTool == ShowTools && mod.HasType(ShownType)
            && mod.Matches(SearchText);
        this.sortField = sortField;
        this.sortDescending = sortDescending;
        ApplySort();
    }

    public ObservableCollection<CatalogItemViewModel> Items { get; } = [];

    /// <summary>The cards as shown: filtered by the search and sorted.</summary>
    public ICollectionView ItemsView { get; }

    public string? TestCatalogText => isTest
        ? L.F($"Test list: {gamesUrl}. Start the app without --catalog to see the real one.") : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    private bool isLoading;

    public string? Error => failure is null ? null : L.Message(failure);

    [ObservableProperty]
    private string searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortGlyph), nameof(UpdatedSortGlyph))]
    private CatalogSortField sortField;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameSortGlyph), nameof(UpdatedSortGlyph))]
    private bool sortDescending;

    public string NameSortGlyph => SortGlyph(CatalogSortField.Name);

    public string UpdatedSortGlyph => SortGlyph(CatalogSortField.Updated);

    public bool HasItems => Items.Count > 0;

    /// <summary>The cards shown, of those in the part of the list that is open: the mods or the tools.</summary>
    public string CountText => L.F($"{ItemsView.Cast<object>().Count()} of {(ShowTools ? ToolCount : ModCount)} shown");

    /// <summary>
    /// The widest count either side can show, kept as room behind the count, so the sort box keeps its width and the
    /// switch stays in place when the player switches sides.
    /// </summary>
    public string CountRoomText
    {
        get
        {
            var most = Math.Max(ModCount, ToolCount);
            return L.F($"{most} of {most} shown");
        }
    }

    /// <summary>The lists were read, and openmods lists nothing for this game.</summary>
    public bool ShowEmpty => loaded && !IsLoading && Error is null && Items.Count == 0;

    public string EmptyText => L.F($"No mods for {owner.GameDisplayName} are listed on openmods yet.");

    /// <summary>
    /// The page lists either the mods or the tools, such as UE4SS and this app, which the app offers in other places
    /// too. It opens on the mods at every start, by the user's choice.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMods))]
    private bool showTools;

    /// <summary>
    /// The other side of the switch, for its first button. Read only: the switch's buttons only show the side and set
    /// it through <see cref="ShowModsSideCommand"/> and <see cref="ShowToolsSideCommand"/>. Bound both ways to these two
    /// properties, a radio button that unchecked its neighbor wrote back into the other property, which checked the
    /// first button again, until the stack overflowed at the start on some PCs (2026-11-13).
    /// </summary>
    public bool ShowMods => !ShowTools;

    [RelayCommand]
    private void ShowModsSide() => ShowTools = false;

    [RelayCommand]
    private void ShowToolsSide() => ShowTools = true;

    public int ModCount => Items.Count(item => !item.IsTool);

    public int ToolCount => Items.Count(item => item.IsTool);

    /// <summary>The switch shows only when the list has tools.</summary>
    public bool HasTools => ToolCount > 0;

    public string ModsText => L.F($"Mods ({ModCount})");

    public string ToolsText => L.F($"Tools ({ToolCount})");

    /// <summary>The type filter's order: as in the Mod type info view.</summary>
    private static readonly string[] TypeOrder =
        ["UE4SS Lua", "UE4SS DLL", ModTypes.Ue4ssFiles, "PAK", "IoStore", "Blueprint", ModTypes.ModLoader, ModTypes.GameFiles, ModTypes.ExeFiles, ModTypes.Other];

    /// <summary>
    /// The type filter: All, then each type on the open side of the switch, with its number of cards. One type at a
    /// time; a mod with UE4SS scripts and a DLL is under both. The page opens on All, and so does each side.
    /// </summary>
    public ObservableCollection<CatalogTypeChoice> TypeChoices { get; } = [];

    /// <summary>The type the page shows; null for all types.</summary>
    public string? ShownType { get; private set; }

    /// <summary>The filter shows only when a type would leave out some cards of this side.</summary>
    public bool HasTypeChoices => TypeChoices.Skip(1).Any(choice => choice.Count < TypeChoices[0].Count);

    [RelayCommand]
    private void ShowAllTypes() => ShowType(null);

    /// <summary>Mods are listed, but none is shown: none matches the search here, or this part of the list is empty.</summary>
    public bool ShowNoMatch => Items.Count > 0 && ItemsView.IsEmpty;

    /// <summary>The cards on the other side of the switch that the search matches; all of them while the search is empty.</summary>
    private int OtherSideMatches => Items.Count(item => item.IsTool != ShowTools && item.Matches(SearchText));

    /// <summary>The cards of other types on this side that the search matches; none while all types are shown.</summary>
    private int OtherTypeMatches => ShownType is null
        ? 0
        : Items.Count(item => item.IsTool == ShowTools && !item.HasType(ShownType) && item.Matches(SearchText));

    /// <summary>Nothing of this type matches the search, but cards of other types here do: the page offers all types.</summary>
    public bool ShowAllTypesHint => ShowNoMatch && OtherTypeMatches > 0;

    /// <summary>Nothing is shown here, and something would be on the other side: the page offers to switch.</summary>
    public bool ShowSwitchHint => ShowNoMatch && !ShowAllTypesHint && OtherSideMatches > 0;

    public string SwitchText => ShowTools ? L.T("Show mods") : L.T("Show tools");

    /// <summary>Without a search, the mods side is empty only when the game's list holds tools alone.</summary>
    public string NoMatchTitle => string.IsNullOrWhiteSpace(SearchText)
        ? L.T("Only tools are listed for this game")
        : ShowTools ? L.T("No tools match the search") : L.T("No mods match the search");

    /// <summary>
    /// What the other types here hold for the search, else what the other side holds; nothing more to say when the
    /// search is empty.
    /// </summary>
    public string? NoMatchText => OtherTypeMatches is var others and > 0
        ? ShowTools
            ? L.Plural(others, "{0} tool of another type matches it.", "{0} tools of other types match it.")
            : L.Plural(others, "{0} mod of another type matches it.", "{0} mods of other types match it.")
        : OtherSideMatches is var count and > 0
        ? string.IsNullOrWhiteSpace(SearchText)
            ? null
            : ShowTools
                ? L.Plural(count, "{0} mod matches it.", "{0} mods match it.")
                : L.Plural(count, "{0} tool matches it.", "{0} tools match it.")
        : L.T("Try another name, author or tag.");

    /// <summary>Reads the lists unless they were read less than 10 minutes ago.</summary>
    /// <param name="force">The user asked for it: says so when the list is still fresh.</param>
    public async Task LoadAsync(bool force = false)
    {
        if (IsLoading)
            return;
        if (loaded && Error is null && DateTimeOffset.Now - loadedAt < OpenMods.RefreshInterval)
        {
            if (force)
                owner.ShowToast(L.T("The list is up to date. openmods refreshes it every 10 minutes."));
            return;
        }
        IsLoading = true;
        failure = null;
        OnPropertyChanged(nameof(Error));
        try
        {
            var list = await OpenMods.FetchAsync(http, gamesUrl, steamAppId);
            loadedAt = DateTimeOffset.Now;
            loaded = true;
            Show(list);
            owner.ShowCatalogList(list);
        }
        catch (ModException ex)
        {
            Log.Warn(ex.Message);
            failure = ex;
            OnPropertyChanged(nameof(Error));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Updates every card after the installed mods or UE4SS changed.</summary>
    public void UpdateInstalled(IReadOnlyList<ModRecord> mods)
    {
        installed = mods;
        foreach (var item in Items)
            item.UpdateState(mods);
    }

    [RelayCommand]
    private Task Reload() => LoadAsync(force: true);

    [RelayCommand]
    /// <summary>The main page of openmods, not the game's page there (the user's choice).</summary>
    private void OpenSite() => owner.OpenPath(SiteUrl);

    /// <summary>
    /// A click on the field the page is already sorted by reverses the order. A new field
    /// starts with names A to Z, or with the newest release first.
    /// </summary>
    [RelayCommand]
    private void SortBy(CatalogSortField field)
    {
        if (field == SortField)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortField = field;
            SortDescending = field == CatalogSortField.Updated;
        }
        ApplySort();
        owner.SaveCatalogSort(SortField, SortDescending);
    }

    [RelayCommand]
    private void SwitchSide() => ShowTools = !ShowTools;

    /// <summary>What openmods is, over the whole window: free for players, open to every author, and the same for all.</summary>
    [ObservableProperty]
    private bool isAboutOpen;

    [RelayCommand]
    private void ShowAbout() => IsAboutOpen = true;

    [RelayCommand]
    private void CloseAbout() => IsAboutOpen = false;

    /// <summary>
    /// The full view: the openmods page as a grid of cards in place of the menu and the page, at the window's size.
    /// Off at every start.
    /// </summary>
    [ObservableProperty]
    private bool isFullView;

    [RelayCommand]
    private void EnterFullView() => IsFullView = true;

    [RelayCommand]
    private void ExitFullView() => IsFullView = false;

    /// <summary>What the type boxes in front of the mods' names mean, over the whole window.</summary>
    [ObservableProperty]
    private bool isTypesInfoOpen;

    [RelayCommand]
    private void ShowTypesInfo() => IsTypesInfoOpen = true;

    [RelayCommand]
    private void CloseTypesInfo() => IsTypesInfoOpen = false;

    partial void OnSearchTextChanged(string value)
    {
        ItemsView.Refresh();
        ShownChanged();
    }

    partial void OnShowToolsChanged(bool value)
    {
        // Each side has its own types, so the other side starts with all of them.
        ShownType = null;
        OnPropertyChanged(nameof(ShownType));
        BuildTypeChoices();
        ItemsView.Refresh();
        ShownChanged();
    }

    private void ShowType(string? type)
    {
        if (type == ShownType)
            return;
        ShownType = type;
        OnPropertyChanged(nameof(ShownType));
        MarkShownType();
        ItemsView.Refresh();
        ShownChanged();
    }

    /// <summary>The filter's buttons for the open side; the shown type stays while that side still has it.</summary>
    private void BuildTypeChoices()
    {
        var side = Items.Where(item => item.IsTool == ShowTools).ToList();
        if (ShownType is not null && !side.Any(item => item.HasType(ShownType)))
        {
            ShownType = null;
            OnPropertyChanged(nameof(ShownType));
        }
        TypeChoices.Clear();
        TypeChoices.Add(new CatalogTypeChoice(null, side.Count, ShowType));
        foreach (var type in side.SelectMany(item => item.FilterTypes).GroupBy(type => type)
            .OrderBy(type => Array.IndexOf(TypeOrder, type.Key) is var index and >= 0 ? index : TypeOrder.Length)
            .ThenBy(type => type.Key, StringComparer.Ordinal))
        {
            TypeChoices.Add(new CatalogTypeChoice(type.Key, type.Count(), ShowType));
        }
        MarkShownType();
        OnPropertyChanged(nameof(HasTypeChoices));
    }

    private void MarkShownType()
    {
        foreach (var choice in TypeChoices)
            choice.IsActive = choice.Type == ShownType;
    }

    private void Show(CatalogList? list)
    {
        Items.Clear();
        foreach (var mod in list?.Mods ?? [])
        {
            var item = new CatalogItemViewModel(owner, mod);
            item.UpdateState(installed);
            Items.Add(item);
        }
        // A new list without tools hides the switch, so the page must not stay on the tools.
        if (!HasTools)
            ShowTools = false;
        BuildTypeChoices();
        ItemsView.Refresh();
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ModCount));
        OnPropertyChanged(nameof(ToolCount));
        OnPropertyChanged(nameof(HasTools));
        OnPropertyChanged(nameof(ModsText));
        OnPropertyChanged(nameof(ToolsText));
        OnPropertyChanged(nameof(CountRoomText));
        ShownChanged();
        _ = LoadCoversAsync(Items.ToList(), list?.Game.Art);
    }

    private void ShownChanged()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ShowNoMatch));
        OnPropertyChanged(nameof(ShowSwitchHint));
        OnPropertyChanged(nameof(ShowAllTypesHint));
        OnPropertyChanged(nameof(SwitchText));
        OnPropertyChanged(nameof(NoMatchTitle));
        OnPropertyChanged(nameof(NoMatchText));
    }

    private void ApplySort() => ((ListCollectionView)ItemsView).CustomSort = new CatalogOrder(SortField, SortDescending);

    /// <summary>An arrow on the field the page is sorted by; a quiet dash on the other one, so it reads as sortable too.</summary>
    private string SortGlyph(CatalogSortField field) =>
        field != SortField ? "" : SortDescending ? "" : "";

    /// <summary>A mod without a cover shows the game's picture, as openmods does.</summary>
    private Task LoadCoversAsync(IReadOnlyList<CatalogItemViewModel> items, string? gameArt) =>
        Task.WhenAll(items.Select(async item => item.Cover = await images.LoadAsync(item.Mod.Cover) ?? await images.LoadAsync(gameArt)));
}

/// <summary>One mod on the openmods page.</summary>
public sealed partial class CatalogItemViewModel(MainViewModel owner, CatalogMod mod) : LocalizedObservableObject
{
    private enum CardAction
    {
        None,
        Install,
        InstallUe4ss,
        UpdateUe4ss,
    }

    private CardAction action;

    /// <summary>Read once, with the game's folders: the scanner looks at every path of the zip.</summary>
    private readonly string? typeLabel = mod.TypeLabelIn(owner.CatalogScanFolders.ProjectFolder, owner.CatalogScanFolders.ExeFolder);

    public CatalogMod Mod => mod;

    public string Name => mod.Name;

    public string? Summary => mod.Summary;

    public bool IsTool => mod.IsTool;

    /// <summary>The mod's type, as the Mods page names it after the install; null when it is not known.</summary>
    public string? KindText => typeLabel is null ? null : Format.Kind(typeLabel);

    /// <summary>
    /// The types the page's type filter shows the card under: a UE4SS mod with scripts and a DLL under both, so a player
    /// who looks for Lua mods finds it too; none while the type is not known.
    /// </summary>
    internal IReadOnlyList<string> FilterTypes => typeLabel switch
    {
        null => [],
        "UE4SS Lua + DLL" => ["UE4SS Lua", "UE4SS DLL"],
        var label => [label],
    };

    /// <param name="type">A type from <see cref="FilterTypes"/>; null for all types.</param>
    internal bool HasType(string? type) => type is null || FilterTypes.Contains(type);

    public string AuthorText => L.F($"by {mod.Author ?? L.T("an unknown author")}");

    /// <summary>Set when openmods lists another author's project.</summary>
    public string? ListedByText => mod.ListedBy is null ? null : L.F($"listed by {mod.ListedBy}");

    public IReadOnlyList<string> Tags => mod.Tags;

    public string MetaText => string.Join("  ·  ", new[]
    {
        mod.Release is null ? L.T("No release yet") : L.F($"Version {mod.Release.Version}"),
        mod.Updated is { } updated ? L.F($"Updated {updated.LocalDateTime.ToString(L.T("d MMM yyyy"), CultureInfo.CurrentCulture)}") : null,
        mod.License,
    }.Where(part => part is not null));

    public bool HasPage => mod.Page is not null;

    public bool HasSource => mod.Source is not null;

    [ObservableProperty]
    private ImageSource? cover;

    [ObservableProperty]
    private string? actionText;

    [ObservableProperty]
    private bool hasAction;

    /// <summary>What is installed, or why there is nothing to install.</summary>
    [ObservableProperty]
    private string? statusText;

    [ObservableProperty]
    private string statusTone = "muted";

    [ObservableProperty]
    private string statusGlyph = "";

    /// <summary>The search looks at the name, the description, the authors, the tags and the type, in English too.</summary>
    public bool Matches(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;
        var text = search.Trim();
        return new[] { mod.Name, mod.Summary, mod.Author, mod.ListedBy, typeLabel, KindText }.Concat(mod.Tags)
            .Any(field => field?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    internal void UpdateState(IReadOnlyList<ModRecord> installed)
    {
        Status(null, "muted", "");
        if (mod.IsUe4ss)
        {
            // UE4SS has its own install: the manifest names the release made for this game.
            if (owner.IsUe4ssUpdateAvailable)
                Set(CardAction.UpdateUe4ss, owner.Ue4ssUpdateText);
            else if (owner.IsUe4ssInstalled)
                Set(CardAction.None, null);
            else
                Set(CardAction.InstallUe4ss, L.T("Install UE4SS…"));
            if (owner.IsUe4ssInstalled)
                Status(owner.Ue4ssText, "good", "");
            return;
        }
        if (mod.Release is null || mod.InstallFile is null)
        {
            Set(CardAction.None, null);
            Status(mod.Release is null || mod.Release.Files.Count == 0 ? L.T("No download yet") : L.T("Several files: see the mod's page"), "muted", "");
            return;
        }

        var record = installed.FirstOrDefault(m => string.Equals(m.CatalogId, mod.Id, StringComparison.OrdinalIgnoreCase));
        if (record is null)
        {
            Set(CardAction.Install, L.T("Install"));
            return;
        }
        var text = record.Version is null ? L.T("Installed") : L.F($"Installed  ·  {record.Version}");
        Status(record.Enabled ? text : L.F($"{text}  ·  disabled"), "good", "");
        if (ModVersion.IsUpdate(record.Version, mod.Release.Version))
            Set(CardAction.Install, L.F($"Update to {mod.Release.Version}"));
        else
            Set(CardAction.None, null);
    }

    [RelayCommand]
    private Task Act()
    {
        switch (action)
        {
            case CardAction.Install:
                return owner.InstallFromCatalogAsync(mod);
            case CardAction.UpdateUe4ss:
                return owner.DownloadAndInstallUe4ssAsync();
            case CardAction.InstallUe4ss:
                owner.ShowUe4ssGuideCommand.Execute(null);
                break;
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void OpenPage()
    {
        if (mod.Page is not null)
            owner.OpenPath(mod.Page);
    }

    [RelayCommand]
    private void OpenSource()
    {
        if (mod.Source is not null)
            owner.OpenPath(mod.Source);
    }

    private void Set(CardAction next, string? text)
    {
        action = next;
        ActionText = text;
        HasAction = next != CardAction.None;
    }

    private void Status(string? text, string tone, string glyph)
    {
        StatusText = text;
        StatusTone = tone;
        StatusGlyph = glyph;
    }
}
