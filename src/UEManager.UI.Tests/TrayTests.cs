using System.IO;
using System.Windows.Media;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The tray icon: its three settings, its menu and its picture.</summary>
public sealed class TrayTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-tray-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private static readonly GameProfile Pbz = new() { Id = "phantom-blade-zero", DisplayName = "Phantom Blade Zero" };

    private void WithApp(Action<MainViewModel, AppPaths> check) => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        check(new MainViewModel(Pbz, paths, new ArtSet(null, null)) { GameCard = _ => null, LookUpGame = _ => null }, paths);
    });

    [Fact]
    public void The_icon_shows_by_default_and_minimize_and_the_X_work_as_before_until_the_player_changes_them()
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        // A settings file of an older version, without the tray's settings.
        File.WriteAllText(paths.SettingsFile, "{ \"language\": \"en\", \"game\": \"phantom-blade-zero\" }");

        var settings = AppSettings.Load(paths);

        Assert.True(settings.ShowTrayIcon);
        Assert.False(settings.MinimizeToTray);
        Assert.False(settings.CloseToTray);
    }

    [Fact]
    public void The_X_hides_the_app_only_with_the_icon_and_minimize_to_tray_on() => WithApp((model, paths) =>
    {
        Assert.False(model.MinimizesToTray);
        Assert.False(model.CanCloseToTray);
        model.CloseToTray = true;
        // Minimize to tray is still off.
        Assert.False(model.ClosesToTray);

        model.MinimizeToTray = true;
        Assert.True(model.CanCloseToTray);
        Assert.True(model.MinimizesToTray);
        Assert.True(model.ClosesToTray);

        model.ShowTrayIcon = false;
        Assert.False(model.CanCloseToTray);
        Assert.False(model.MinimizesToTray);
        Assert.False(model.ClosesToTray);

        var saved = AppSettings.Load(paths);
        Assert.False(saved.ShowTrayIcon);
        Assert.True(saved.MinimizeToTray);
        Assert.True(saved.CloseToTray);
    });

    [Fact]
    public void The_menu_lists_the_games_marks_this_one_and_switches_with_one_click() => WithApp((model, paths) =>
    {
        string? restartedWith = null;
        model.RestartForGame = game =>
        {
            restartedWith = game;
            return true;
        };
        var (opened, exited, finished) = (0, 0, 0);
        var menu = model.TrayMenu(() => opened++, () => exited++);
        menu.Finished += () => finished++;

        Assert.Equal(["Black Myth: Wukong", "Phantom Blade Zero", "Stellar Blade: Blood Rain"], menu.Cards.Select(card => card.Name));
        Assert.Equal([false, true, false], menu.Cards.Select(card => card.IsCurrent));
        Assert.Equal([false, false, true], menu.Cards.Select(card => card.IsComingSoon));
        Assert.True(menu.CanSwitch);
        // A game that is not out yet is only dimmed: it can be chosen, as in the picker.
        Assert.Equal(AppInfo.CanChooseComingGames, menu.ChooseCommand.CanExecute(menu.Cards[^1]));

        // The game of this run opens the window.
        menu.ChooseCommand.Execute(menu.Cards.Single(card => card.IsCurrent));
        Assert.Equal(1, opened);
        Assert.Null(restartedWith);

        menu.ChooseCommand.Execute(menu.Cards.Single(card => card.Name == "Black Myth: Wukong"));
        Assert.Equal("black-myth-wukong", restartedWith);
        Assert.Equal("black-myth-wukong", AppSettings.Load(paths).Game);

        menu.OpenCommand.Execute(null);
        menu.ExitCommand.Execute(null);
        Assert.Equal(2, opened);
        Assert.Equal(1, exited);
        Assert.Equal(4, finished);
    });

    [Fact]
    public void While_a_dialog_is_open_the_menu_does_not_switch() => WithApp((model, _) =>
    {
        // The picker is a dialog over the app.
        model.SwitchGameCommand.Execute(null);

        var menu = model.TrayMenu(() => { }, () => { });

        Assert.False(menu.CanSwitch);
        Assert.All(menu.Cards, card => Assert.False(menu.ChooseCommand.CanExecute(card)));
    });

    [Fact]
    public async Task A_row_shows_the_mods_of_its_game()
    {
        var games = new GameProfile[] { new() { Id = "a", DisplayName = "A" }, new() { Id = "b", DisplayName = "B" } };
        var menu = new TrayMenuViewModel(games, "a", _ => null, game => new GameOnPc(GameStore.Steam, game.Id == "a" ? 3 : 0),
            true, _ => { }, () => { }, () => { }, () => { });

        await menu.LookedUp.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["3 mods", ""], menu.Cards.Select(card => card.ModsText));
    }

    [Fact]
    public void The_tray_picture_is_the_mod_stack_in_the_colours_of_the_game() => UiThread.Run(() =>
    {
        var brushes = new Dictionary<string, Brush>
        {
            ["Brush.PanelSolid"] = Brushes.Black,
            ["Brush.LineStrong"] = Brushes.Gray,
            ["Brush.Accent"] = Brushes.Red,
            ["Brush.TextMuted"] = Brushes.White,
            ["Brush.TextDim"] = Brushes.Blue,
        };

        var picture = TrayIcon.Draw(32, key => brushes[key]);

        Assert.Equal(32, picture.PixelWidth);
        Assert.Equal(32, picture.PixelHeight);
        var pixels = new byte[32 * 32 * 4];
        picture.CopyPixels(pixels, 32 * 4, 0);
        // Blue, green, red and alpha of the pixel at x, y.
        byte[] At(int x, int y) => pixels[((y * 32 + x) * 4)..((y * 32 + x) * 4 + 4)];
        // The plates from the top: the game's accent, then the two text colours; the tile around them.
        Assert.Equal([0, 0, 255, 255], At(16, 8));
        Assert.Equal([255, 255, 255, 255], At(16, 15));
        Assert.Equal([255, 0, 0, 255], At(16, 22));
        Assert.Equal([0, 0, 0, 255], At(16, 3));
    });
}
