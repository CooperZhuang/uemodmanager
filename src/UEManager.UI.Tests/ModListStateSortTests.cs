using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The All, Enabled and Disabled buttons above the mod list, and the sort by status, type and integrity.</summary>
public sealed class ModListStateSortTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-state-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private AppPaths Paths()
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        return paths;
    }

    private static MainViewModel Model(AppPaths paths) =>
        new(new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero" }, paths, new ArtSet(null, null));

    /// <summary>
    /// Alpha: on, a pak in place. Bravo: off, IoStore. Charlie: on, its pak is gone. Delta: off, a game file.
    /// Alpha and Bravo have the tag Combat.
    /// </summary>
    private void Fill(MainViewModel model)
    {
        var pak = Path.Combine(folder, "a.pak");
        File.WriteAllText(pak, "abc");
        Add(model, new ModRecord { Id = "a", Name = "Alpha", Enabled = true, Tags = ["Combat"], InstalledAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            Files = [new ModFileRecord { Target = DeployTarget.Mods, FileName = "a.pak", Size = 3, DeployedPath = pak }] });
        Add(model, new ModRecord { Id = "b", Name = "Bravo", Tags = ["Combat"], InstalledAt = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero),
            Files = [new ModFileRecord { Target = DeployTarget.Mods, FileName = "b.utoc" }] });
        Add(model, new ModRecord { Id = "c", Name = "Charlie", Enabled = true, InstalledAt = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero),
            Files = [new ModFileRecord { Target = DeployTarget.Mods, FileName = "c.pak", Size = 3, DeployedPath = Path.Combine(folder, "gone.pak") }] });
        Add(model, new ModRecord { Id = "d", Name = "Delta", InstalledAt = new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero),
            Files = [new ModFileRecord { Target = DeployTarget.InstallRoot, FileName = "TestGame/Saved/Keys.sav" }] });
        model.RefreshTagFilters();
    }

    private void Add(MainViewModel model, ModRecord record) => model.Mods.Add(new ModItemViewModel(model, record, Path.Combine(folder, "library")));

    private void With(Action<MainViewModel, AppPaths> check) => UiThread.Run(() =>
    {
        var paths = Paths();
        var model = Model(paths);
        Fill(model);
        check(model, paths);
    });

    private static string[] Shown(MainViewModel model) => model.ModsView.Cast<ModItemViewModel>().Select(mod => mod.Name).ToArray();

    private static TagFilterItem State(MainViewModel model, string start) => model.StateFilters.Single(filter => filter.Label.StartsWith(start, StringComparison.Ordinal));

    [Fact]
    public void The_state_buttons_count_the_mods_and_all_is_active_at_first() => With((model, _) =>
    {
        Assert.True(model.HasFilterRow);
        Assert.Equal(["All (4)", "Enabled (2)", "Disabled (2)"], model.StateFilters.Select(filter => filter.Label));
        Assert.Equal([true, false, false], model.StateFilters.Select(filter => filter.IsActive));
        Assert.Equal(4, Shown(model).Length);
    });

    [Fact]
    public void Enabled_hides_the_disabled_mods_and_disabled_the_enabled_ones() => With((model, _) =>
    {
        State(model, "Enabled").Select.Execute(null);
        Assert.Equal(["Alpha", "Charlie"], Shown(model));
        Assert.True(State(model, "Enabled").IsActive);
        Assert.Equal("2 of 4 shown", model.ModsCountText);

        State(model, "Disabled").Select.Execute(null);
        Assert.Equal(["Bravo", "Delta"], Shown(model));

        State(model, "Disabled").Select.Execute(null);
        Assert.Equal(ModStateFilter.All, model.StateFilter);
        Assert.Equal(4, Shown(model).Length);
    });

    [Fact]
    public void The_state_works_with_a_tag_and_the_search() => With((model, _) =>
    {
        State(model, "Enabled").Select.Execute(null);
        model.TagFilters.Single(filter => filter.Label == "Combat (2)").Select.Execute(null);
        Assert.Equal(["Alpha"], Shown(model));

        model.SearchText = "bravo";
        Assert.Empty(Shown(model));
        Assert.Equal("All tags", model.TagFilters[0].Label);
    });

    [Fact]
    public void The_state_stays_after_a_restart() => With((model, paths) =>
    {
        State(model, "Enabled").Select.Execute(null);
        Assert.Equal("Enabled", AppSettings.Load(paths).ModsState);

        var again = Model(paths);
        Fill(again);
        Assert.Equal(ModStateFilter.Enabled, again.StateFilter);
        Assert.Equal(["Alpha", "Charlie"], Shown(again));

        State(again, "All").Select.Execute(null);
        Assert.Null(AppSettings.Load(paths).ModsState);
    });

    [Fact]
    public void Without_mods_there_is_no_filter_row() => UiThread.Run(() =>
        Assert.False(Model(Paths()).HasFilterRow));

    [Fact]
    public void Status_puts_enabled_mods_first_and_a_second_click_disabled_ones() => With((model, paths) =>
    {
        model.SortByCommand.Execute(ModSortField.Status);
        Assert.Equal(["Alpha", "Charlie", "Bravo", "Delta"], Shown(model));
        Assert.NotEqual(model.NameSortGlyph, model.StatusSortGlyph);
        Assert.Equal("Status", AppSettings.Load(paths).ModsSort);

        model.SortByCommand.Execute(ModSortField.Status);
        Assert.Equal(["Bravo", "Delta", "Alpha", "Charlie"], Shown(model));
    });

    [Fact]
    public void Type_goes_by_the_type_name() => With((model, _) =>
    {
        model.SortByCommand.Execute(ModSortField.Type);

        Assert.Equal(["Delta", "Bravo", "Alpha", "Charlie"], Shown(model));
        Assert.Equal(["Game files", "IoStore", "PAK", "PAK"], model.ModsView.Cast<ModItemViewModel>().Select(mod => mod.KindText));
    });

    [Fact]
    public void Integrity_puts_problems_first() => With((model, _) =>
    {
        model.SortByCommand.Execute(ModSortField.Integrity);

        Assert.Equal(["Charlie", "Alpha", "Bravo", "Delta"], Shown(model));
    });

    [Fact]
    public void Installed_still_starts_with_the_newest() => With((model, _) =>
    {
        model.SortByCommand.Execute(ModSortField.Installed);

        Assert.Equal(["Delta", "Bravo", "Charlie", "Alpha"], Shown(model));
    });

    [Fact]
    public void Problems_rank_before_mods_in_place_and_mods_that_are_off() =>
        Assert.Equal([ModIntegrity.FilesChanged, ModIntegrity.FilesMissing, ModIntegrity.Intact, ModIntegrity.NotDeployed],
            Enum.GetValues<ModIntegrity>().OrderBy(ModOrder.IntegrityRank));
}
