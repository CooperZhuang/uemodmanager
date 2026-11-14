using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.UI.Services;

namespace UEManager.UI.Catalog;

/// <summary>
/// One button of the openmods page's type filter: all types, or one type, with the number of cards on the open side of
/// the switch that it shows.
/// </summary>
/// <param name="type">The type as the scanner names it (see <see cref="Core.Mods.ModTypes"/>); null for all types.</param>
/// <param name="select">Shows the cards of this type; the page then marks this button.</param>
public sealed partial class CatalogTypeChoice(string? type, int count, Action<string?> select) : LocalizedObservableObject
{
    public string? Type => type;

    public int Count => count;

    public string Text => type is null ? L.F($"All ({count})") : L.F($"{Format.Kind(type)} ({count})");

    /// <summary>The page shows this type. Only the view model sets it; the button shows it and never writes it back.</summary>
    [ObservableProperty]
    private bool isActive;

    [RelayCommand]
    private void Select() => select(type);
}
