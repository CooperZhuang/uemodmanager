namespace UEManager.Core.Games;

/// <summary>
/// Which game the app starts with. One app serves every game it has a profile for; the player chooses one, and the
/// app starts with it until the player switches, which starts the app again. So a run is always for one game.
/// </summary>
public static class GameChoice
{
    /// <summary>
    /// The game for this start: the one named on the command line (<c>--game</c>), or else the one the player chose
    /// last. Null when neither names a game the app knows: the app then asks, as at the very first start, and as once
    /// for everyone who used the app while it knew only one game.
    /// </summary>
    /// <param name="named">
    /// The game the command line names: the start that follows a choice in the game picker names it, so it never
    /// asks again; tests name one too.
    /// </param>
    /// <param name="askAtStart">The player wants to choose at every start: the game chosen last does not count.</param>
    public static GameProfile? Resolve(IReadOnlyList<GameProfile> games, string? named, string? chosen, bool askAtStart = false) =>
        Find(games, named) ?? (askAtStart ? null : Find(games, chosen));

    /// <summary>
    /// The games a start may use: a game that is not out yet only when the build allows it, and a hidden game only when
    /// the command line names it, as a test run does.
    /// </summary>
    public static IReadOnlyList<GameProfile> Playable(IEnumerable<GameProfile> games, string? named, bool comingSoonAllowed) =>
        games.Where(game => (!game.ComingSoon || comingSoonAllowed)
                && (!game.Hidden || game.Id.Equals(named?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();

    private static GameProfile? Find(IReadOnlyList<GameProfile> games, string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : games.FirstOrDefault(g => g.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
}
