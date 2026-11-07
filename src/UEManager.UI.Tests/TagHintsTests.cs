using System.IO;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

public sealed class TagHintsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-hints-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void An_empty_box_offers_the_players_own_tags_first_then_common_ones() =>
        Assert.Equal(["Hard mode", "Combat", "Quality of life", "Appearance", "Outfits", "Weapons", "Gameplay", "Difficulty"],
            TagHints.For("", ["Hard mode", "Combat"]));

    [Fact]
    public void Typing_narrows_them_starts_first_then_the_ones_that_hold_it() =>
        Assert.Equal(["Characters", "Cheats", "Utilities"], TagHints.For("Combat, Ch", []).Concat(TagHints.For("Combat, liti", [])));

    [Fact]
    public void A_tag_in_the_box_is_not_offered_again_whatever_its_case() =>
        Assert.DoesNotContain("Combat", TagHints.For("combat, ", []));

    [Fact]
    public void Nothing_fits_and_nothing_is_offered() => Assert.Empty(TagHints.For("Zzz", []));

    [Theory]
    [InlineData("", "UI", "UI, ")]
    [InlineData("we", "Weapons", "Weapons, ")]
    [InlineData("Combat, we", "Weapons", "Combat, Weapons, ")]
    [InlineData("Combat,we", "Weapons", "Combat, Weapons, ")]
    [InlineData("Combat, ", "UI", "Combat, UI, ")]
    [InlineData("战斗，界", "界面", "战斗，界面, ")]
    public void A_picked_tag_takes_the_place_of_the_one_being_typed(string text, string tag, string result) =>
        Assert.Equal(result, TagHints.Apply(text, tag));

    [Theory]
    [InlineData("", false)]
    [InlineData("Combat, ", false)]
    [InlineData("Combat, w", true)]
    [InlineData("w", true)]
    public void Tab_completes_only_while_a_tag_is_being_typed(string text, bool typing) =>
        Assert.Equal(typing, TagHints.IsTyping(text));

    [Fact]
    public void The_players_tags_count_by_how_many_mods_have_them() =>
        Assert.Equal(["Maps", "Combat"], TagHints.Own([new ModRecord { Tags = ["Combat", "Maps"] }, new ModRecord { Tags = ["maps"] }]));

    private ModDetailsViewModel Details()
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero" }, paths, new ArtSet(null, null));
        var record = new ModRecord { Id = "a", Name = "Alpha", Tags = ["Combat"] };
        model.Mods.Add(new ModItemViewModel(model, record, folder));
        model.Mods.Add(new ModItemViewModel(model, new ModRecord { Id = "b", Name = "Bravo", Tags = ["Hard mode", "Combat"] }, folder));
        return new ModDetailsViewModel(model, record, [], [], folder);
    }

    [Fact]
    public void The_edit_form_offers_tags_and_a_click_or_tab_takes_one() => UiThread.Run(() =>
    {
        var details = Details();
        details.AddTagCommand.Execute(null);

        Assert.Equal("Combat, ", details.NewTags);
        Assert.Equal("Hard mode", details.Suggestions[0]);
        Assert.DoesNotContain("Combat", details.Suggestions);
        Assert.False(details.AcceptSuggestionCommand.CanExecute(null));

        details.AddSuggestionCommand.Execute("Hard mode");
        Assert.Equal("Combat, Hard mode, ", details.NewTags);

        details.NewTags = "Combat, Hard mode, we";
        Assert.Equal(["Weapons"], details.Suggestions);
        Assert.True(details.AcceptSuggestionCommand.CanExecute(null));
        details.AcceptSuggestionCommand.Execute(null);
        Assert.Equal("Combat, Hard mode, Weapons, ", details.NewTags);
    });
}
