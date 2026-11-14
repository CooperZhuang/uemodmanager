using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Threading;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Catalog;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>The openmods page lists either the mods or the tools, and shows each mod's type.</summary>
public sealed class CatalogToolsSwitchTests : IDisposable
{
    private const string GamesUrl = "https://openmods.test/games/mods.json";

    private const string Games = """
        { "openmods": 1, "games": [ { "slug": "test", "name": "Test", "steamAppId": "1", "mods": 4, "list": "https://openmods.test/games/test/mods.json" } ] }
        """;

    private const string Mods = """
        { "openmods": 1, "mods": [
          { "name": "Witch Time", "tags": ["gameplay"], "source": "https://github.com/KintaroModding/witchtime",
            "release": { "tag": "v1.8", "files": [ { "name": "witchtime_1.8.zip", "size": 1, "url": "https://example.test/w.zip",
              "entries": [ "ue4ss/Mods/witchtime/enabled.txt", "ue4ss/Mods/witchtime/Scripts/main.lua" ] } ] } },
          { "name": "Animation Cancel", "tags": ["gameplay"], "source": "https://github.com/KintaroModding/animationcancel",
            "release": { "tag": "v1.0", "files": [ { "name": "animationcancel_1.0.zip", "size": 1, "url": "https://example.test/a.zip" } ] } },
          { "name": "UE MM", "tags": ["tools"], "source": "https://github.com/KintaroModding/uemodmanager",
            "release": { "tag": "v2.1.0", "files": [ { "name": "uemm_2.1.0.zip", "size": 1, "url": "https://example.test/u.zip", "entries": [ "UEModManager.exe" ] } ] } },
          { "name": "PBZModLoader", "tags": ["tools", "library"], "source": "https://github.com/Someone/loader", "release": null }
        ] }
        """;

    private const string ToolsOnly = """
        { "openmods": 1, "mods": [
          { "name": "UE MM", "tags": ["tools"], "source": "https://github.com/KintaroModding/uemodmanager", "release": null },
          { "name": "UE4SS", "tags": ["tools", "library"], "source": "https://github.com/KintaroModding/ue4ss", "release": null }
        ] }
        """;

    private const string NoTools = """
        { "openmods": 1, "mods": [
          { "name": "Witch Time", "tags": ["gameplay"], "source": "https://github.com/KintaroModding/witchtime", "release": null },
          { "name": "A library", "tags": ["library"], "source": "https://github.com/Someone/library", "release": null }
        ] }
        """;

    /// <summary>Mods of several types, one without a known type, and two tools.</summary>
    private const string Typed = """
        { "openmods": 1, "mods": [
          { "name": "Witch Time", "source": "https://github.com/A/witchtime", "release": { "tag": "v1", "files": [ { "name": "w.zip", "size": 1, "url": "https://example.test/w.zip",
            "entries": [ "ue4ss/Mods/witchtime/enabled.txt", "ue4ss/Mods/witchtime/Scripts/main.lua" ] } ] } },
          { "name": "Both Ways", "source": "https://github.com/A/both", "release": { "tag": "v1", "files": [ { "name": "b.zip", "size": 1, "url": "https://example.test/b.zip",
            "entries": [ "Mods/Both/Scripts/main.lua", "Mods/Both/dlls/main.dll" ] } ] } },
          { "name": "Bypass", "source": "https://github.com/A/bypass", "release": { "tag": "v1", "files": [ { "name": "c.zip", "size": 1, "url": "https://example.test/c.zip",
            "entries": [ "ue4ss/Mods/cppbypass/dlls/main.dll" ] } ] } },
          { "name": "Pak One", "source": "https://github.com/A/pakone", "release": { "tag": "v1", "files": [ { "name": "p.zip", "size": 1, "url": "https://example.test/p.zip",
            "entries": [ "Content/Paks/~mods/a_P.pak" ] } ] } },
          { "name": "Pak Two", "source": "https://github.com/A/paktwo", "release": { "tag": "v1", "files": [ { "name": "q.zip", "size": 1, "url": "https://example.test/q.zip",
            "entries": [ "Content/Paks/~mods/b_P.pak" ] } ] } },
          { "name": "Unread", "source": "https://github.com/A/unread", "release": { "tag": "v1", "files": [ { "name": "u.zip", "size": 1, "url": "https://example.test/u.zip" } ] } },
          { "name": "UE MM", "tags": ["tools"], "source": "https://github.com/KintaroModding/uemodmanager",
            "release": { "tag": "v2.1.0", "files": [ { "name": "uemm_2.1.0.zip", "size": 1, "url": "https://example.test/m.zip", "entries": [ "UEModManager.exe" ] } ] } },
          { "name": "Loader", "tags": ["tools"], "source": "https://github.com/Someone/loader", "release": null }
        ] }
        """;

    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-catalog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private CatalogViewModel Load(string mods)
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Test" }, paths, new ArtSet(null, null));
        var http = new HttpClient(new Pages(new() { [GamesUrl] = Games, ["https://openmods.test/games/test/mods.json"] = mods }));
        var catalog = new CatalogViewModel(model, http, GamesUrl, "1", isTest: false, CatalogSortField.Name, false);
        WaitFor(catalog.LoadAsync());
        return catalog;
    }

    private static string[] Shown(CatalogViewModel catalog) => catalog.ItemsView.Cast<CatalogItemViewModel>().Select(item => item.Name).ToArray();

    [Fact]
    public void The_page_shows_the_mods_or_the_tools() => UiThread.Run(() =>
    {
        var catalog = Load(Mods);

        Assert.True(catalog.ShowMods);
        Assert.Equal(["Animation Cancel", "Witch Time"], Shown(catalog));
        Assert.True(catalog.HasTools);
        Assert.Equal("Mods (2)", catalog.ModsText);
        Assert.Equal("Tools (2)", catalog.ToolsText);
        Assert.Equal("2 of 2 shown", catalog.CountText);

        catalog.ShowToolsSideCommand.Execute(null);

        Assert.False(catalog.ShowMods);
        Assert.Equal(["PBZModLoader", "UE MM"], Shown(catalog));

        catalog.SearchText = "ue mm";
        Assert.Equal("1 of 2 shown", catalog.CountText);

        catalog.ShowModsSideCommand.Execute(null);
        Assert.False(catalog.ShowTools);
        Assert.True(catalog.ShowMods);
        Assert.Empty(Shown(catalog));
    });

    [Fact]
    public void A_search_that_matches_only_the_other_side_offers_to_switch() => UiThread.Run(() =>
    {
        var catalog = Load(Mods);

        catalog.SearchText = "ue mm";

        Assert.True(catalog.ShowNoMatch);
        Assert.True(catalog.ShowSwitchHint);
        Assert.Equal("No mods match the search", catalog.NoMatchTitle);
        Assert.Equal("1 tool matches it.", catalog.NoMatchText);
        Assert.Equal("Show tools", catalog.SwitchText);

        catalog.SwitchSideCommand.Execute(null);

        Assert.True(catalog.ShowTools);
        Assert.Equal(["UE MM"], Shown(catalog));
        Assert.False(catalog.ShowNoMatch);

        catalog.SearchText = "time";

        Assert.Equal("No tools match the search", catalog.NoMatchTitle);
        Assert.Equal("1 mod matches it.", catalog.NoMatchText);
        Assert.Equal("Show mods", catalog.SwitchText);

        catalog.SearchText = "nothing like this";

        Assert.False(catalog.ShowSwitchHint);
        Assert.Equal("Try another name, author or tag.", catalog.NoMatchText);
    });

    [Fact]
    public void A_list_of_tools_alone_says_so() => UiThread.Run(() =>
    {
        var catalog = Load(ToolsOnly);

        Assert.True(catalog.ShowNoMatch);
        Assert.True(catalog.ShowSwitchHint);
        Assert.Equal("Only tools are listed for this game", catalog.NoMatchTitle);
        Assert.Null(catalog.NoMatchText);
        Assert.Equal("Show tools", catalog.SwitchText);
        // The count keeps room for the larger side, so the switch does not move.
        Assert.Equal("0 of 0 shown", catalog.CountText);
        Assert.Equal("2 of 2 shown", catalog.CountRoomText);
    });

    [Fact]
    public void A_list_without_tools_has_no_switch_and_shows_libraries_as_mods() => UiThread.Run(() =>
    {
        var catalog = Load(NoTools);

        Assert.False(catalog.HasTools);
        Assert.Equal(["A library", "Witch Time"], Shown(catalog));
    });

    [Fact]
    public void Each_card_shows_the_type_the_mods_page_gives_and_the_search_finds_it() => UiThread.Run(() =>
    {
        var catalog = Load(Mods);
        var items = catalog.Items.ToDictionary(item => item.Name);

        Assert.Equal("UE4SS Lua", items["Witch Time"].KindText);
        Assert.Null(items["Animation Cancel"].KindText);   // openmods has not read its zip yet
        Assert.Equal("Other", items["UE MM"].KindText);
        Assert.Null(items["PBZModLoader"].KindText);

        catalog.SearchText = "ue4ss lua";
        Assert.Equal(["Witch Time"], Shown(catalog));
        catalog.ShowTools = true;
        catalog.SearchText = "other";
        Assert.Equal(["UE MM"], Shown(catalog));
    });

    private static string[] Choices(CatalogViewModel catalog) => catalog.TypeChoices.Select(choice => choice.Text).ToArray();

    private static void Choose(CatalogViewModel catalog, string? type) =>
        catalog.TypeChoices.Single(choice => choice.Type == type).SelectCommand.Execute(null);

    [Fact]
    public void The_type_filter_shows_one_type_of_the_open_side_at_a_time() => UiThread.Run(() =>
    {
        var catalog = Load(Typed);

        // In the order of the Mod type info view; a mod with Lua and a DLL counts under both, one without a type under none.
        Assert.True(catalog.HasTypeChoices);
        Assert.Equal(["All (6)", "UE4SS Lua (2)", "UE4SS DLL (2)", "PAK (2)"], Choices(catalog));
        Assert.Null(catalog.ShownType);
        Assert.True(catalog.TypeChoices[0].IsActive);

        Choose(catalog, "UE4SS Lua");

        Assert.Equal(["Both Ways", "Witch Time"], Shown(catalog));
        Assert.Equal("2 of 6 shown", catalog.CountText);
        Assert.Equal([false, true, false, false], catalog.TypeChoices.Select(choice => choice.IsActive));

        Choose(catalog, "UE4SS DLL");
        Assert.Equal(["Both Ways", "Bypass"], Shown(catalog));

        Choose(catalog, null);
        Assert.Equal(6, Shown(catalog).Length);
        Assert.True(catalog.TypeChoices[0].IsActive);
    });

    [Fact]
    public void Each_side_of_the_switch_starts_with_all_of_its_types() => UiThread.Run(() =>
    {
        var catalog = Load(Typed);
        Choose(catalog, "PAK");

        catalog.ShowToolsSideCommand.Execute(null);

        Assert.Null(catalog.ShownType);
        Assert.Equal(["All (2)", "Other (1)"], Choices(catalog));
        Assert.Equal(["Loader", "UE MM"], Shown(catalog));

        Choose(catalog, "Other");
        catalog.ShowModsSideCommand.Execute(null);

        Assert.Null(catalog.ShownType);
        Assert.Equal(6, Shown(catalog).Length);
    });

    [Fact]
    public void A_search_that_matches_only_other_types_offers_all_types_first() => UiThread.Run(() =>
    {
        var catalog = Load(Typed);
        Choose(catalog, "PAK");

        catalog.SearchText = "witch";

        Assert.True(catalog.ShowNoMatch);
        Assert.True(catalog.ShowAllTypesHint);
        Assert.False(catalog.ShowSwitchHint);
        Assert.Equal("1 mod of another type matches it.", catalog.NoMatchText);

        catalog.ShowAllTypesCommand.Execute(null);

        Assert.Null(catalog.ShownType);
        Assert.Equal(["Witch Time"], Shown(catalog));

        // Nothing of any type here, but a tool: the page offers the other side, which opens on all its types.
        Choose(catalog, "PAK");
        catalog.SearchText = "ue mm";

        Assert.False(catalog.ShowAllTypesHint);
        Assert.True(catalog.ShowSwitchHint);
        Assert.Equal("1 tool matches it.", catalog.NoMatchText);
    });

    [Fact]
    public void A_side_where_no_type_would_leave_out_a_card_has_no_type_filter() => UiThread.Run(() =>
    {
        Assert.False(Load(NoTools).HasTypeChoices);   // no types known
        Assert.True(Load(Mods).HasTypeChoices);       // one type, and one card without
    });

    /// <summary>
    /// The switch's buttons are plain buttons: the view model shows the side on them, and a click only runs a command.
    /// Radio buttons bound both ways to ShowMods and ShowTools wrote an unchecked neighbor into the other property, which
    /// checked it again, until the stack overflowed at the start on a PC.
    /// </summary>
    [Fact]
    public void The_switch_buttons_never_write_the_side_back()
    {
        var xaml = File.ReadAllText(Path.Combine(SourceTexts.Root, "UEManager.UI", "Catalog", "CatalogToolbar.xaml"));
        var buttons = System.Text.RegularExpressions.Regex.Matches(xaml, @"<Button Style=""\{StaticResource Segment\}""[^>]*>").Select(m => m.Value).ToList();

        Assert.DoesNotContain("RadioButton", xaml);
        Assert.Equal(2, buttons.Count);
        Assert.All(buttons, button =>
        {
            Assert.Contains("ui:Segment.IsActive=\"{Binding Show", button);
            Assert.Contains("Command=\"{Binding Show", button);
            Assert.DoesNotContain("Mode=TwoWay", button);
        });
    }

    /// <summary>The type filter's buttons, like the switch's, show the shown type and never write it back.</summary>
    [Fact]
    public void The_type_buttons_never_write_the_type_back()
    {
        var xaml = File.ReadAllText(Path.Combine(SourceTexts.Root, "UEManager.UI", "Catalog", "CatalogTypeFilter.xaml"));
        var button = System.Text.RegularExpressions.Regex.Match(xaml, @"<Button Style=""\{StaticResource Segment\}""[^>]*>").Value;

        Assert.DoesNotContain("RadioButton", xaml);
        Assert.Contains("ui:Segment.IsActive=\"{Binding IsActive}\"", button);
        Assert.Contains("Command=\"{Binding SelectCommand}\"", button);
        Assert.DoesNotContain("Mode=TwoWay", xaml);
    }

    /// <summary>The page and its full view share the toolbar with the switch and the type filter, and keep none of their own.</summary>
    [Theory]
    [InlineData("CatalogView.xaml")]
    [InlineData("CatalogFullView.xaml")]
    public void Both_views_use_the_shared_toolbar(string view)
    {
        var xaml = File.ReadAllText(Path.Combine(SourceTexts.Root, "UEManager.UI", "Catalog", view));

        Assert.Contains("<vm:CatalogToolbar ", xaml);
        Assert.Contains("<vm:CatalogTypeFilter ", xaml);
        Assert.DoesNotContain("TypeChoices", xaml);
        Assert.DoesNotContain("RadioButton", xaml);
        Assert.DoesNotContain("SortByCommand", xaml);
    }

    /// <summary>
    /// The full view's grid: as many columns as fit cards of at least 300 pixels with their gap of 16. The card list is
    /// 1273 wide in a window of the start size, 1360: four cards there.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(300, 1)]
    [InlineData(615, 1)]
    [InlineData(616, 2)]
    [InlineData(1273, 4)]
    [InlineData(1855, 5)]
    [InlineData(2487, 7)]
    public void The_full_view_fits_as_many_columns_as_the_card_list_holds(double width, int columns)
    {
        Assert.Equal(columns, CatalogFullView.ColumnsFor(width));
    }

    private static void WaitFor(Task task)
    {
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    /// <summary>Answers known URLs with their text, every other URL with 404.</summary>
    private sealed class Pages(Dictionary<string, string> pages) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(pages.TryGetValue(request.RequestUri!.AbsoluteUri, out var text)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
