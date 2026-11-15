using System.Collections;

namespace UEManager.UI.Catalog;

public enum CatalogSortField
{
    Name,
    Updated,
    Downloads,
}

/// <summary>
/// Sorts the openmods page by name, by the date of the latest release or by downloads; equal entries fall back to the
/// name. A mod without a download count counts as fewer than one with 0.
/// </summary>
public sealed class CatalogOrder(CatalogSortField field, bool descending) : IComparer
{
    private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

    public int Compare(object? x, object? y)
    {
        if (x is not CatalogItemViewModel a || y is not CatalogItemViewModel b)
            return 0;
        var result = field switch
        {
            CatalogSortField.Updated => Nullable.Compare(a.Mod.Updated, b.Mod.Updated),
            CatalogSortField.Downloads => Nullable.Compare(a.Mod.Downloads, b.Mod.Downloads),
            _ => Names.Compare(a.Name, b.Name),
        };
        if (result == 0)
            result = Names.Compare(a.Name, b.Name);
        return descending ? -result : result;
    }
}
