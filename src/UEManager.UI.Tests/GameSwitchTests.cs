using System.IO;
using System.Text.RegularExpressions;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>"Switch game" in the title bar, the game picker, and the look of each game.</summary>
public class GameSwitchTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-games-" + Guid.NewGuid().ToString("N"));

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
    public void The_picker_shows_every_game_by_name_and_marks_the_one_of_this_run() => WithApp((model, _) =>
    {
        model.SwitchGameCommand.Execute(null);

        Assert.True(model.IsGamePickerOpen);
        Assert.True(model.HasOverlay);
        var picker = model.GamePicker!;
        Assert.True(picker.CanClose);
        Assert.Equal(["Black Myth: Wukong", "Phantom Blade Zero", "Stellar Blade: Blood Rain"], picker.Cards.Select(card => card.Name));
        Assert.Equal([false, true, false], picker.Cards.Select(card => card.IsCurrent));
        Assert.Equal([false, false, false], picker.Cards.Select(card => card.IsExperimental));
        // The developer's message is closed until the player opens it.
        Assert.False(picker.IsMessageOpen);
        picker.IsMessageOpen = true;
        Assert.True(picker.IsMessageOpen);

        picker.CloseCommand.Execute(null);
        Assert.False(model.IsGamePickerOpen);
    });

    [Fact]
    public void A_game_that_is_not_out_yet_comes_last_and_says_so()
    {
        var games = new GameProfile[]
        {
            new() { Id = "a-coming", DisplayName = "A Coming Game", ComingSoon = true },
            new() { Id = "b-game", DisplayName = "B Game" },
            new() { Id = "c-game", DisplayName = "C Game" },
        };
        var picker = new GamePickerViewModel(games, null, _ => null, _ => { }, null);

        Assert.Equal(["B Game", "C Game", "A Coming Game"], picker.Cards.Select(card => card.Name));
        Assert.Equal([false, false, true], picker.Cards.Select(card => card.IsComingSoon));
        Assert.False(picker.CanClose);
        // While the games are being tested, its card can be chosen, to try the app's look for it.
        Assert.Equal(AppInfo.CanChooseComingGames, picker.ChooseCommand.CanExecute(picker.Cards[^1]));
        Assert.True(picker.ChooseCommand.CanExecute(picker.Cards[0]));
        // Its card invites a click to take a peek, but not while the app shows that game already.
        Assert.Equal([false, false, AppInfo.CanChooseComingGames], picker.Cards.Select(card => card.InvitesPeek));
        Assert.False(new GamePickerViewModel(games, "a-coming", _ => null, _ => { }, () => { }).Cards[^1].InvitesPeek);
    }

    [Fact]
    public async Task The_cards_say_where_each_game_is_and_how_many_mods_it_has()
    {
        var games = new GameProfile[]
        {
            new() { Id = "a-steam", DisplayName = "A Steam Game" },
            new() { Id = "b-epic", DisplayName = "B Epic Game" },
            new() { Id = "c-folder", DisplayName = "C Folder Game" },
            new() { Id = "d-missing", DisplayName = "D Missing Game" },
            new() { Id = "e-coming", DisplayName = "E Coming Game", ComingSoon = true },
        };
        var onPc = new Dictionary<string, GameOnPc>
        {
            ["a-steam"] = new(GameStore.Steam, 3),
            ["b-epic"] = new(GameStore.Epic, 0),
            ["c-folder"] = new(GameStore.Manual, 1),
            ["d-missing"] = new(null, 2),
        };
        var looked = new List<string>();
        var picker = new GamePickerViewModel(games, null, _ => null, _ => { }, null, lookUp: game =>
        {
            lock (looked)
                looked.Add(game.Id);
            return onPc[game.Id];
        });

        await picker.LookedUp.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(["Steam  ·  3 mods", "Epic Games", "Chosen by hand  ·  1 mod", "Game not found  ·  2 mods", ""],
            picker.Cards.Select(card => card.StatusText));
        Assert.Equal([true, true, true, false, false], picker.Cards.Select(card => card.IsFound));
        Assert.Equal([true, true, true, true, false], picker.Cards.Select(card => card.HasStatus));
        // A game that is not out yet is not looked up: its card says Coming soon.
        Assert.DoesNotContain("e-coming", looked);
    }

    [Fact]
    public void A_card_finds_its_game_in_the_folder_chosen_before_and_counts_its_mods()
    {
        var game = Path.Combine(folder, "Game");
        foreach (var file in new[] { @"Engine\Binaries\Win64\placeholder", @"TestGame\Content\Paks\placeholder", @"TestGame\Binaries\Win64\TestGame-Win64-Shipping.exe" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(game, file))!);
            File.WriteAllText(Path.Combine(game, file), file);
        }
        var profile = new GameProfile { Id = "test-game", DisplayName = "Test Game", ProjectFolder = "TestGame" };
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();

        // Neither a folder chosen before nor a store that lists it.
        Assert.Equal(new GameOnPc(null, 0), GameOnPc.Look(profile, null, paths));

        var download = Path.Combine(folder, "Blue");
        Directory.CreateDirectory(download);
        File.WriteAllText(Path.Combine(download, "Blue_P.pak"), "pak");
        var manager = new ModManager(GameLocator.FromFolder(profile, game)!, paths);
        using (var preview = manager.Inspect(download))
            manager.Install(preview, preview.Packages, "Blue", null);
        Assert.Equal(new GameOnPc(GameStore.Manual, 1), GameOnPc.Look(profile, game, paths));

        // A mod list that cannot be read counts no mods, and the picker still opens.
        File.WriteAllText(paths.StateFile(profile.Id), "{ broken");
        Assert.Equal(new GameOnPc(GameStore.Manual, 0), GameOnPc.Look(profile, game, paths));
    }

    [Fact]
    public void A_game_that_is_not_out_yet_says_it_is_a_placeholder_instead_of_not_found() => UiThread.Run(() =>
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var coming = new MainViewModel(new GameProfile { Id = "stellar-blade-blood-rain", DisplayName = "Stellar Blade: Blood Rain", ComingSoon = true },
            paths, new ArtSet(null, null));
        var released = new MainViewModel(Pbz, paths, new ArtSet(null, null));

        Assert.True(coming.IsComingSoon);
        Assert.False(coming.ShowGameNotFound);
        Assert.Equal("Coming soon", coming.GameStatusText);
        Assert.Equal("warn", coming.GameStatusTone);
        Assert.StartsWith("Stellar Blade: Blood Rain is not out yet", coming.ComingSoonNote);

        Assert.False(coming.IsExperimental);
        var experimental = new MainViewModel(new GameProfile { Id = "mortal-shell-2", DisplayName = "Mortal Shell II", Experimental = true },
            paths, new ArtSet(null, null));
        Assert.True(experimental.IsExperimental);
        Assert.StartsWith("Support for Mortal Shell II is new.", experimental.ExperimentalNote);

        Assert.False(released.IsComingSoon);
        Assert.True(released.ShowGameNotFound);
        Assert.Equal("Game not found", released.GameStatusText);
        Assert.Equal("bad", released.GameStatusTone);
    });

    [Fact]
    public void The_game_of_this_run_only_closes_the_picker() => WithApp((model, paths) =>
    {
        var restarts = 0;
        model.RestartForGame = _ => ++restarts > 0;
        model.SwitchGameCommand.Execute(null);

        model.GamePicker!.ChooseCommand.Execute(model.GamePicker.Cards.Single(card => card.IsCurrent));

        Assert.False(model.IsGamePickerOpen);
        Assert.Equal(0, restarts);
        Assert.Null(AppSettings.Load(paths).Game);
    });

    [Fact]
    public void Another_game_is_saved_and_the_app_starts_again_at_once_and_only_once() => WithApp((model, paths) =>
    {
        var restarts = 0;
        string? startedWith = null;
        model.RestartForGame = game => (startedWith = game) is not null && ++restarts > 0;
        model.SwitchGameCommand.Execute(null);
        var card = model.GamePicker!.Cards.Single(card => card.Name == "Black Myth: Wukong");

        model.GamePicker.ChooseCommand.Execute(card);
        model.GamePicker.ChooseCommand.Execute(card);

        Assert.Null(model.Confirm);
        Assert.Equal(1, restarts);
        // The new start names the game, so it does not ask again, even for a player who chooses at every start.
        Assert.Equal("black-myth-wukong", startedWith);
        Assert.Equal("black-myth-wukong", AppSettings.Load(paths).Game);
    });

    [Fact]
    public void A_failed_restart_says_how_to_go_on_and_keeps_the_choice() => WithApp((model, paths) =>
    {
        model.RestartForGame = _ => false;
        model.SwitchGameCommand.Execute(null);

        model.GamePicker!.ChooseCommand.Execute(model.GamePicker.Cards.Single(card => card.Name == "Black Myth: Wukong"));

        Assert.False(model.IsGamePickerOpen);
        Assert.True(model.Toast?.IsError);
        Assert.Contains("Black Myth: Wukong", model.Toast!.Text);
        Assert.Equal("black-myth-wukong", AppSettings.Load(paths).Game);
    });

    [Fact]
    public void Asking_at_every_start_is_off_until_the_player_turns_it_on_and_it_is_saved() => WithApp((model, paths) =>
    {
        Assert.False(model.AskForGameAtStart);

        model.AskForGameAtStart = true;

        Assert.True(AppSettings.Load(paths).AskForGameAtStart);
    });

    [Fact]
    public void Switch_game_waits_while_a_dialog_is_open() => WithApp((model, _) =>
    {
        model.IsUpdateNotesOpen = true;

        Assert.False(model.SwitchGameCommand.CanExecute(null));
        model.IsUpdateNotesOpen = false;
        Assert.True(model.SwitchGameCommand.CanExecute(null));
    });

    private static readonly string UiFolder = Path.Combine(SourceTexts.Root, "UEManager.UI");

    private static HashSet<string> Keys(string file) =>
        Regex.Matches(File.ReadAllText(file), "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void Every_game_has_its_card_logo_and_cover_art()
    {
        Assert.All(GameProfile.BuiltIn(), game => Assert.All(new[] { "card.png", "logo.png", "background.jpg" },
            name => Assert.True(File.Exists(Path.Combine(UiFolder, "Theme", "Art", game.Id, name)), $"{game.Id}/{name}")));
        Assert.True(File.Exists(Path.Combine(UiFolder, "Theme", "Art", "backdrop.jpg")));
    }

    [Fact]
    public void A_game_look_changes_only_keys_of_the_shared_palette()
    {
        var palette = Keys(Path.Combine(UiFolder, "Theme", "Palette.xaml"));
        var games = Directory.GetFiles(Path.Combine(UiFolder, "Theme", "Games"), "*.xaml");

        Assert.Contains(games, file => Path.GetFileName(file) == "mortal-shell-2.xaml");
        var profiles = GameProfile.BuiltIn().Select(p => p.Id).ToList();
        Assert.All(games, file =>
        {
            Assert.Contains(Path.GetFileNameWithoutExtension(file), profiles);
            Assert.Empty(Keys(file).Except(palette));
        });
    }

    [Fact]
    public void The_window_and_the_controls_name_no_colour_of_a_game_themselves()
    {
        // Only the palettes hold colours; black shadows and the orange glow of the update box are the same for every game.
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "#D9A84E" };
        var files = new[] { "MainWindow.xaml", Path.Combine("Theme", "Controls.xaml") }
            .Concat(Directory.GetFiles(Path.Combine(UiFolder, "Views"), "*.xaml").Select(f => Path.GetRelativePath(UiFolder, f)));
        Assert.All(files, file =>
        {
            var colours = Regex.Matches(File.ReadAllText(Path.Combine(UiFolder, file)), "#[0-9A-Fa-f]{6,8}\\b")
                .Select(m => m.Value).Where(c => !allowed.Contains(c));
            Assert.Empty(colours);
        });
        // Nor does the code behind them, as the settings editor's selection once did.
        var code = Directory.GetFiles(UiFolder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));
        Assert.All(code, file => Assert.DoesNotMatch(@"Color\.From(A?rgb|Rgb)\(", File.ReadAllText(file)));
    }

    /// <summary>The colours of a look file by key: each Color, and each brush made of one colour.</summary>
    private static Dictionary<string, string> Colours(string file)
    {
        var text = File.ReadAllText(file);
        var colours = Regex.Matches(text, "<Color x:Key=\"([^\"]+)\">(#[0-9A-Fa-f]{6,8})</Color>")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        foreach (Match brush in Regex.Matches(text, "<SolidColorBrush x:Key=\"([^\"]+)\" Color=\"(#[0-9A-Fa-f]{6,8})\""))
            colours[brush.Groups[1].Value] = brush.Groups[2].Value;
        return colours;
    }

    /// <summary>The contrast of two opaque colours, as the WCAG measures it: 1 to 21.</summary>
    private static double Contrast(string a, string b)
    {
        static double Luminance(string colour)
        {
            var rgb = Convert.FromHexString(colour.TrimStart('#')[^6..]);
            static double Linear(byte part)
            {
                var c = part / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(rgb[0]) + 0.7152 * Linear(rgb[1]) + 0.0722 * Linear(rgb[2]);
        }
        var (light, dark) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (light + 0.05) / (dark + 0.05);
    }

    [Fact]
    public void The_settings_editor_stays_readable_in_every_game()
    {
        var shared = Colours(Path.Combine(UiFolder, "Theme", "Palette.xaml"));
        var looks = new Dictionary<string, Dictionary<string, string>> { ["shared"] = shared };
        foreach (var file in Directory.GetFiles(Path.Combine(UiFolder, "Theme", "Games"), "*.xaml"))
        {
            var look = new Dictionary<string, string>(shared);
            foreach (var (key, colour) in Colours(file))
                look[key] = colour;
            looks[Path.GetFileNameWithoutExtension(file)] = look;
        }
        // Comments may be a little dimmer than the rest.
        var least = new Dictionary<string, double>
        {
            ["Brush.Text"] = 4.5, ["Color.Syntax.Key"] = 4.5, ["Color.Syntax.String"] = 4.5, ["Color.Syntax.Value"] = 4.5,
            ["Color.Syntax.Comment"] = 4.0,
        };
        Assert.All(looks, look => Assert.All(least, rule =>
        {
            var (colour, field) = (look.Value[rule.Key], look.Value["Brush.Field"]);
            var contrast = Contrast(colour, field);
            Assert.True(contrast >= rule.Value, $"{look.Key}: {rule.Key} {colour} on {field} has a contrast of {contrast:F1}");
        }));
    }
}
