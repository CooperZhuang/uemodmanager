using UEManager.Core.Games;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>
/// A download made for one game names it in Data\serve.json: the picker shows it in the middle of the highlights, the
/// other two dimmed and still a choice.
/// </summary>
public class ServedGamePickerTests
{
    private static GamePickerViewModel Picker(string? servedId) =>
        new(GameProfile.Offered(), null, _ => null, _ => { }, null, servedId: servedId);

    [Theory]
    [InlineData("phantom-blade-zero", "Black Myth: Wukong", "Phantom Blade Zero", "Stellar Blade: Blood Rain")]
    [InlineData("black-myth-wukong", "Phantom Blade Zero", "Black Myth: Wukong", "Stellar Blade: Blood Rain")]
    [InlineData("STELLAR-blade-blood-rain", "Black Myth: Wukong", "Stellar Blade: Blood Rain", "Phantom Blade Zero")]
    // No highlight: it takes the place of the third highlight, which stays in All games.
    [InlineData("wuchang-fallen-feathers", "Black Myth: Wukong", "WUCHANG: Fallen Feathers", "Phantom Blade Zero")]
    public void The_served_game_is_in_the_middle_and_the_others_are_dimmed(string servedId, string left, string middle, string right)
    {
        var picker = Picker(servedId);

        Assert.True(picker.IsServing);
        Assert.Equal([left, middle, right], picker.Cards.Select(card => card.Name));
        Assert.Equal([false, true, false], picker.Cards.Select(card => card.IsServed));
        Assert.Equal([true, false, true], picker.Cards.Select(card => card.IsDimmed));
        // Every game is still a choice, and All games keeps its order by name.
        Assert.All(picker.Cards, card => Assert.True(picker.ChooseCommand.CanExecute(card)));
        Assert.Equal(Picker(null).AllCards.Select(card => card.Name), picker.AllCards.Select(card => card.Name));
        Assert.Single(picker.AllCards, card => card.IsServed);
        Assert.True(picker.HasMoreGames);
    }

    [Theory]
    [InlineData(null)]
    // A game the picker does not offer: as usual.
    [InlineData("no-such-game")]
    public void Without_a_served_game_the_picker_is_as_usual(string? servedId)
    {
        var picker = Picker(servedId);

        Assert.False(picker.IsServing);
        Assert.Equal(["Black Myth: Wukong", "Phantom Blade Zero", "Stellar Blade: Blood Rain"], picker.Cards.Select(card => card.Name));
        Assert.All(picker.AllCards, card => Assert.False(card.IsServed || card.IsDimmed));
    }
}
