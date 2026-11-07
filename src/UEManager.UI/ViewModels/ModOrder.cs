using System.Collections;
using UEManager.Core.Mods;

namespace UEManager.UI.ViewModels;

/// <summary>The columns the mod list can be sorted by.</summary>
public enum ModSortField
{
    Name,
    Installed,
    Status,
    Type,
    Integrity,
}

/// <summary>Which mods the list shows by their state, as the buttons above it choose.</summary>
public enum ModStateFilter
{
    All,
    Enabled,
    Disabled,
}

/// <summary>
/// Sorts the mod list by one column. Enabled mods come first by status, problems first by integrity, and the type goes
/// by its name; mods that are equal there go by their name, A to Z, either way.
/// </summary>
public sealed class ModOrder(ModSortField field, bool descending) : IComparer
{
    private static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

    public int Compare(object? x, object? y)
    {
        if (x is not ModItemViewModel a || y is not ModItemViewModel b)
            return 0;
        var result = field switch
        {
            ModSortField.Installed => a.InstalledAt.CompareTo(b.InstalledAt),
            ModSortField.Status => b.Enabled.CompareTo(a.Enabled),
            ModSortField.Type => Names.Compare(a.KindText, b.KindText),
            ModSortField.Integrity => IntegrityRank(a.Integrity).CompareTo(IntegrityRank(b.Integrity)),
            _ => Names.Compare(a.Name, b.Name),
        };
        if (descending)
            result = -result;
        return result != 0 ? result : Names.Compare(a.Name, b.Name);
    }

    /// <summary>A mod with a problem first: changed files, then missing ones, then a mod in place, then one that is off.</summary>
    internal static int IntegrityRank(ModIntegrity integrity) => integrity switch
    {
        ModIntegrity.FilesChanged => 0,
        ModIntegrity.FilesMissing => 1,
        ModIntegrity.Intact => 2,
        _ => 3,
    };
}
