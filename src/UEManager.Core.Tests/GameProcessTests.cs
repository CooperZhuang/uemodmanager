using System.Diagnostics;
using UEManager.Core.Games;

namespace UEManager.Core.Tests;

/// <summary>Whether the game runs: only its own exe counts, not another program of the same name.</summary>
public class GameProcessTests : IDisposable
{
    private readonly TestFolder folder = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public void The_path_of_a_process_is_read()
    {
        using var self = Process.GetCurrentProcess();

        var path = GameProcess.PathOf(self.Id, out var error);

        Assert.Equal(0, error);
        Assert.Equal(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(path!), ignoreCase: true);
    }

    [Fact]
    public void A_process_of_the_same_name_in_another_folder_is_not_the_game()
    {
        var game = GameLocator.Resolve(TestFolder.Profile(), folder.Game(), GameStore.Manual)!;
        using var self = Process.GetCurrentProcess();
        // The game's exe folder holds an exe with this test's own process name; this process runs from another folder.
        folder.File(Path.Combine(Path.GetRelativePath(folder.Combine(), game.BinariesPath), self.ProcessName + ".exe"), "not the test host");

        Assert.False(GameProcess.IsRunning(game));
    }

    [Fact]
    public void A_process_that_has_ended_is_not_the_game()
    {
        Assert.Null(GameProcess.PathOf(int.MaxValue - 3, out var error));
        Assert.NotEqual(0, error);
    }
}
