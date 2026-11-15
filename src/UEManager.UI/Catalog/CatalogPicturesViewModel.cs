using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.Core.Catalog;

namespace UEManager.UI.Catalog;

/// <summary>One picture in the picture viewer: its thumbnail and the picture itself, each loaded once.</summary>
public sealed partial class CatalogPictureViewModel(CatalogPicture picture) : ObservableObject
{
    public string Url => picture.Url;

    /// <summary>The alt text from the mod's openmods.md, shown under the picture; null without one.</summary>
    public string? Alt => picture.Alt;

    public bool HasAlt => !string.IsNullOrWhiteSpace(Alt);

    [ObservableProperty]
    private ImageSource? thumbnail;

    [ObservableProperty]
    private ImageSource? image;

    /// <summary>The picture is on its way.</summary>
    [ObservableProperty]
    private bool isLoading = true;

    /// <summary>
    /// The picture could not be loaded, for example when its site deleted it or is blocked in the player's country: a
    /// placeholder takes its place, and the other pictures stay.
    /// </summary>
    [ObservableProperty]
    private bool failed;

    /// <summary>The thumbnail could not be loaded: its tile shows a placeholder.</summary>
    [ObservableProperty]
    private bool thumbnailFailed;

    /// <summary>The picture the viewer shows: its thumbnail is marked.</summary>
    [ObservableProperty]
    private bool isCurrent;

    internal Task? Loading { get; set; }
}

/// <summary>
/// The picture viewer over the whole window: a mod's cover and its screenshots from openmods, one at a time, with
/// arrows, a "2 / 5" counter and a strip of thumbnails. The pictures are downloaded only while it is open.
/// </summary>
public sealed partial class CatalogPicturesViewModel : ObservableObject
{
    /// <summary>The thumbnails' width when decoded, in pixels: twice their size on the screen, for sharp tiles.</summary>
    public const int ThumbnailWidth = 240;

    /// <summary>The picture's width when decoded, in pixels: a smaller picture keeps its own width.</summary>
    public const int PictureWidth = 1920;

    private readonly Func<string, int, Task<ImageSource?>> load;
    private readonly Action close;

    /// <param name="name">The mod's name, as the viewer's title.</param>
    /// <param name="pictures">The pictures: the cover first, then the screenshots (see <see cref="CatalogMod.Pictures"/>).</param>
    /// <param name="load">Downloads a picture at a width; null when it cannot be loaded.</param>
    /// <param name="close">Closes the viewer.</param>
    public CatalogPicturesViewModel(string name, IReadOnlyList<CatalogPicture> pictures, Func<string, int, Task<ImageSource?>> load, Action close)
    {
        ArgumentOutOfRangeException.ThrowIfZero(pictures.Count);
        Name = name;
        this.load = load;
        this.close = close;
        Pictures = pictures.Select(picture => new CatalogPictureViewModel(picture)).ToList();
        Show(0);
        Thumbnails = HasMany ? LoadThumbnailsAsync() : Task.CompletedTask;
    }

    /// <summary>How many thumbnails load at once: each is a full download of a few MB the first time.</summary>
    internal const int ThumbnailsAtOnce = 4;

    /// <summary>
    /// How far from the shown picture a large picture stays in memory, on each side: about 8 MB each when decoded, so a
    /// mod with 20 screenshots holds at most 5 of them. A released one comes back from the copy on disk.
    /// </summary>
    internal const int KeptAround = 2;

    public string Name { get; }

    public IReadOnlyList<CatalogPictureViewModel> Pictures { get; }

    /// <summary>The thumbnails' downloads. Tests wait for them.</summary>
    internal Task Thumbnails { get; }

    public int Index { get; private set; }

    public CatalogPictureViewModel Current => Pictures[Index];

    /// <summary>More than one picture: the arrows, the counter and the thumbnails show.</summary>
    public bool HasMany => Pictures.Count > 1;

    /// <summary>"2 / 5": only numbers, as in the image viewers players know.</summary>
    public string CounterText => $"{Index + 1} / {Pictures.Count}";

    [RelayCommand]
    private void Next() => Show((Index + 1) % Pictures.Count);

    [RelayCommand]
    private void Previous() => Show((Index - 1 + Pictures.Count) % Pictures.Count);

    [RelayCommand]
    private void Select(CatalogPictureViewModel? picture)
    {
        for (var i = 0; i < Pictures.Count; i++)
        {
            if (Pictures[i] == picture)
                Show(i);
        }
    }

    [RelayCommand]
    private void Close() => close();

    /// <summary>
    /// Shows a picture, and loads it and its neighbors, so a step to the next one shows it at once. The large pictures
    /// further away than <see cref="KeptAround"/> are released.
    /// </summary>
    private void Show(int index)
    {
        Index = index;
        for (var i = 0; i < Pictures.Count; i++)
        {
            var picture = Pictures[i];
            picture.IsCurrent = i == index;
            if (Distance(i, index) > KeptAround && picture.Loading is { IsCompleted: true })
            {
                picture.Image = null;
                picture.Failed = false;
                picture.IsLoading = true;
                picture.Loading = null;
            }
        }
        foreach (var near in new[] { index, index + 1, index - 1 + Pictures.Count }.Select(i => Pictures[i % Pictures.Count]).Distinct())
            near.Loading ??= LoadPictureAsync(near);
        OnPropertyChanged(nameof(Index));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CounterText));
    }

    /// <summary>The steps between two pictures, either way round.</summary>
    private int Distance(int a, int b)
    {
        var step = Math.Abs(a - b);
        return Math.Min(step, Pictures.Count - step);
    }

    /// <summary>The thumbnails, after the first picture, a few at a time: the picture the player looks at comes first.</summary>
    private async Task LoadThumbnailsAsync()
    {
        await CurrentLoading;
        using var gate = new SemaphoreSlim(ThumbnailsAtOnce);
        await Task.WhenAll(Pictures.Select(async picture =>
        {
            await gate.WaitAsync();
            try
            {
                await LoadThumbnailAsync(picture);
            }
            finally
            {
                gate.Release();
            }
        }));
    }

    /// <summary>The current picture's download. Tests wait for it.</summary>
    internal Task CurrentLoading => Current.Loading ?? Task.CompletedTask;

    private async Task LoadPictureAsync(CatalogPictureViewModel picture)
    {
        picture.Image = await load(picture.Url, PictureWidth);
        picture.Failed = picture.Image is null;
        picture.IsLoading = false;
    }

    private async Task LoadThumbnailAsync(CatalogPictureViewModel picture)
    {
        picture.Thumbnail = await load(picture.Url, ThumbnailWidth);
        picture.ThumbnailFailed = picture.Thumbnail is null;
    }
}
