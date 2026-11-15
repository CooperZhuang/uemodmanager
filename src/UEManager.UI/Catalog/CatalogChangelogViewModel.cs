using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Catalog;
using UEManager.Core.Mods;

namespace UEManager.UI.Catalog;

/// <summary>A part of a release text, as the changelog shows it: a heading, a list item, a paragraph or code.</summary>
public sealed class CatalogNoteBlockViewModel(NoteBlock block)
{
    public string Text => block.Text;

    public string Marker => block.Marker;

    public bool IsHeading => block.Kind == NoteBlockKind.Heading;

    public bool IsBullet => block.Kind == NoteBlockKind.Bullet;

    public bool IsParagraph => block.Kind == NoteBlockKind.Paragraph;

    public bool IsCode => block.Kind == NoteBlockKind.Code;

    /// <summary>A list item further in for each level; a heading a little apart from what was above it.</summary>
    public Thickness Margin => block.Kind switch
    {
        NoteBlockKind.Bullet => new Thickness(block.Level * 18, 2, 0, 0),
        NoteBlockKind.Heading => new Thickness(0, 10, 0, 2),
        _ => new Thickness(0, 6, 0, 0),
    };

    /// <summary>A first-level heading a little larger than the others.</summary>
    public double FontSize => block.Kind == NoteBlockKind.Heading && block.Level <= 2 ? 14 : 13;
}

/// <summary>One release of the mod in the changelog: its version and date, and its text as blocks.</summary>
public sealed class CatalogReleaseViewModel(ReleaseNote note)
{
    public string Version => note.Version;

    public string DateText => note.Date?.LocalDateTime.ToString(L.T("d MMM yyyy"), CultureInfo.CurrentCulture) ?? "";

    public bool IsPreRelease => note.IsPreRelease;

    public IReadOnlyList<CatalogNoteBlockViewModel> Blocks { get; } =
        ModReleases.Blocks(note.Body).Select(block => new CatalogNoteBlockViewModel(block)).ToList();

    /// <summary>The author wrote nothing for this release.</summary>
    public bool HasNoText => Blocks.Count == 0;
}

/// <summary>
/// The changelog of an openmods mod over the whole window: the texts of its newest releases, read from GitHub on the
/// click that opened it (see <see cref="ModReleases"/>). An answer is kept for the run; a failed one can be asked again.
/// </summary>
public sealed partial class CatalogChangelogViewModel : LocalizedObservableObject
{
    private readonly Func<Task<IReadOnlyList<ReleaseNote>>> fetch;
    private readonly Action<string> open;
    private readonly Action close;
    private ModException? failure;

    /// <param name="name">The mod's name, as the title.</param>
    /// <param name="repo">"owner/repo" of the mod's source on GitHub.</param>
    /// <param name="fetch">Reads the releases, or gives the run's earlier answer.</param>
    /// <param name="open">Opens an address in the browser.</param>
    public CatalogChangelogViewModel(string name, string repo, Func<Task<IReadOnlyList<ReleaseNote>>> fetch, Action<string> open, Action close)
    {
        Name = name;
        Repo = repo;
        this.fetch = fetch;
        this.open = open;
        this.close = close;
        Loaded = LoadAsync();
    }

    public string Name { get; }

    public string Repo { get; }

    /// <summary>The newest releases, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<CatalogReleaseViewModel> releases = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool isLoading;

    /// <summary>Why there is no changelog: GitHub could not be reached, refused, or has no releases page.</summary>
    public string? Error => failure is null ? null : L.Message(failure);

    public bool HasError => failure is not null;

    /// <summary>GitHub answered, and the mod has no release yet.</summary>
    public bool IsEmpty => !IsLoading && failure is null && Releases.Count == 0;

    /// <summary>The read of this opening. Tests wait for it.</summary>
    internal Task Loaded { get; private set; }

    private async Task LoadAsync()
    {
        IsLoading = true;
        SetFailure(null);
        try
        {
            Releases = (await fetch()).Select(note => new CatalogReleaseViewModel(note)).ToList();
        }
        catch (ModException ex)
        {
            SetFailure(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetFailure(ModException? value)
    {
        failure = value;
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void Retry()
    {
        if (!IsLoading)
            Loaded = LoadAsync();
    }

    [RelayCommand]
    private void OpenAllReleases() => open(ModReleases.ReleasesPage(Repo));

    [RelayCommand]
    private void Close() => close();
}
