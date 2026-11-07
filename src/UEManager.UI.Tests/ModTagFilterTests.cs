using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The buttons above the mod list, the search by tag, and the X of a search box.</summary>
public class ModTagFilterTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-tags-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private void WithMods(Action<MainViewModel> check) => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero" }, paths, new ArtSet(null, null));
        Add(model, "Sword", "Combat", "Maps");
        Add(model, "Shield", "combat");
        Add(model, "Map", "Maps");
        Add(model, "Plain");
        model.RefreshTagFilters();
        check(model);
    });

    private void Add(MainViewModel model, string name, params string[] tags) => model.Mods.Add(new ModItemViewModel(model,
        new ModRecord { Id = name, Name = name, Tags = tags.Length == 0 ? null : [.. tags] }, Path.Combine(folder, "library")));

    private static string[] Shown(MainViewModel model) =>
        model.ModsView.Cast<ModItemViewModel>().Select(mod => mod.Name).Order(StringComparer.Ordinal).ToArray();

    private static TagFilterItem Filter(MainViewModel model, string label) => model.TagFilters.Single(filter => filter.Label == label);

    [Fact]
    public void The_buttons_name_all_mods_and_each_tag_with_its_count() => WithMods(model =>
    {
        Assert.True(model.HasTagFilters);
        Assert.Equal(["All tags", "Combat (2)", "Maps (2)"], model.TagFilters.Select(filter => filter.Label));
        Assert.Equal([true, false, false], model.TagFilters.Select(filter => filter.IsActive));
        Assert.Equal(4, Shown(model).Length);
    });

    [Fact]
    public void A_tag_shows_only_its_mods_and_all_shows_every_mod() => WithMods(model =>
    {
        Filter(model, "Combat (2)").Select.Execute(null);

        Assert.Equal(["Shield", "Sword"], Shown(model));
        Assert.True(Filter(model, "Combat (2)").IsActive);
        Assert.False(Filter(model, "All tags").IsActive);
        Assert.Equal("2 of 4 shown", model.ModsCountText);

        Filter(model, "All tags").Select.Execute(null);

        Assert.Equal(4, Shown(model).Length);
        Assert.True(Filter(model, "All tags").IsActive);
    });

    [Fact]
    public void One_tag_is_active_at_a_time_and_a_second_click_shows_all() => WithMods(model =>
    {
        Filter(model, "Combat (2)").Select.Execute(null);
        Filter(model, "Maps (2)").Select.Execute(null);

        Assert.Equal(["Map", "Sword"], Shown(model));

        Filter(model, "Maps (2)").Select.Execute(null);

        Assert.Null(model.SelectedTag);
        Assert.Equal(4, Shown(model).Length);
    });

    [Fact]
    public void The_search_finds_a_tag_and_narrows_the_tags_filter() => WithMods(model =>
    {
        model.SearchText = "maps";
        Assert.Equal(["Map", "Sword"], Shown(model));

        Filter(model, "Combat (2)").Select.Execute(null);
        Assert.Equal(["Sword"], Shown(model));
    });

    [Fact]
    public void A_tag_no_mod_has_any_more_shows_all_again() => WithMods(model =>
    {
        Filter(model, "Combat (2)").Select.Execute(null);

        foreach (var mod in model.Mods.Where(mod => mod.Name is "Sword" or "Shield").ToList())
            model.Mods.Remove(mod);
        model.RefreshTagFilters();

        Assert.Null(model.SelectedTag);
        Assert.Equal(["All tags", "Maps (1)"], model.TagFilters.Select(filter => filter.Label));
        Assert.Equal(["Map", "Plain"], Shown(model));
    });

    [Fact]
    public void Without_tags_there_are_no_buttons() => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero" }, paths, new ArtSet(null, null));
        Add(model, "Plain");

        model.RefreshTagFilters();

        Assert.False(model.HasTagFilters);
        Assert.Empty(model.TagFilters);
    });

    [Fact]
    public void A_row_shows_its_tags_on_a_line_of_their_own_and_none_without_tags() => WithMods(model =>
    {
        Assert.Equal("Combat, Maps", model.Mods.Single(mod => mod.Name == "Sword").TagsText);
        Assert.Null(model.Mods.Single(mod => mod.Name == "Plain").TagsText);
        Assert.DoesNotContain("Combat", model.Mods.Single(mod => mod.Name == "Sword").Detail);
    });

    [Fact]
    public void The_details_show_the_tags_and_the_edit_form_takes_them_over() => WithMods(model =>
    {
        var details = new ModDetailsViewModel(model, model.Mods.Single(mod => mod.Name == "Sword").Record, [], [], folder);
        Assert.Equal("Combat, Maps", details.TagsText);

        details.RenameCommand.Execute(null);

        Assert.Equal("Combat, Maps", details.NewTags);
        Assert.Null(details.TagsText);

        details.CancelRenameCommand.Execute(null);

        Assert.Equal("Combat, Maps", details.TagsText);
    });

    [Fact]
    public void Add_tag_keeps_the_tags_and_makes_room_for_the_next() => WithMods(model =>
    {
        var details = new ModDetailsViewModel(model, model.Mods.Single(mod => mod.Name == "Sword").Record, [], [], folder);

        details.AddTagCommand.Execute(null);

        Assert.True(details.IsRenaming);
        Assert.True(details.AddingTag);
        Assert.Equal("Combat, Maps, ", details.NewTags);
        Assert.Equal(["Combat", "Maps"], Core.Mods.ModTags.Parse(details.NewTags));
    });

    [Fact]
    public void Add_tag_on_a_mod_without_tags_starts_empty() => WithMods(model =>
    {
        var details = new ModDetailsViewModel(model, model.Mods.Single(mod => mod.Name == "Plain").Record, [], [], folder);

        details.AddTagCommand.Execute(null);

        Assert.Equal("", details.NewTags);
    });

    [Fact]
    public void From_the_row_the_window_closes_with_the_form_and_from_the_details_it_stays() => WithMods(model =>
    {
        var record = model.Mods.Single(mod => mod.Name == "Sword").Record;
        var fromRow = new ModDetailsViewModel(model, record, [], [], folder);
        model.ModDetails = fromRow;
        fromRow.OpenToAddTag();
        fromRow.CancelRenameCommand.Execute(null);
        Assert.Null(model.ModDetails);

        var fromDetails = new ModDetailsViewModel(model, record, [], [], folder);
        model.ModDetails = fromDetails;
        fromDetails.AddTagCommand.Execute(null);
        fromDetails.CancelRenameCommand.Execute(null);
        Assert.Same(fromDetails, model.ModDetails);
        Assert.False(fromDetails.IsRenaming);
    });

    [Fact]
    public void The_mods_on_the_card_have_no_tags_to_add() => WithMods(model =>
    {
        var card = new ModRecord { Id = "card", Name = "Card", Recommended = true };

        Assert.False(new ModItemViewModel(model, card, folder).CanTag);
        Assert.False(new ModDetailsViewModel(model, card, [], [], folder).CanTag);
        Assert.True(model.Mods[0].CanTag);
    });

    [Fact]
    public void The_x_of_a_search_box_clears_it() => UiThread.Run(() =>
    {
        var box = new TextBox { Text = "sword" };

        SearchField.Clear.Execute(box);

        Assert.Equal("", box.Text);
    });

    [Fact]
    public void Esc_clears_a_search_box_and_passes_on_when_it_is_empty() => UiThread.Run(() =>
    {
        var box = new TextBox { Text = "sword" };
        using var source = new HwndSource(new HwndSourceParameters("key test"));
        KeyEventArgs Escape() => new(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = UIElement.PreviewKeyDownEvent };

        var first = Escape();
        SearchField.OnPreviewKeyDown(box, first);
        Assert.Equal("", box.Text);
        Assert.True(first.Handled);

        var second = Escape();
        SearchField.OnPreviewKeyDown(box, second);
        Assert.False(second.Handled);
    });
}
