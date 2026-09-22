namespace TTTXO.Game.UI
{
    /// <summary>
    /// Identifies every screen the <see cref="ScreenRouter"/> can navigate to.
    /// Splash/Home/ModeSelect/BoardSelect/Game/Result are playable in Milestone 1;
    /// MatchHistory/Store/Settings are local-only stubs added in Milestone 1.5
    /// (see Docs/06-Wireframes-UI.md, "Alcance por milestone"). Matchmaking is Milestone 4
    /// (Docs/06-Wireframes-UI.md screen 5, "Buscando partida"). Profile and Leaderboard are
    /// Milestone 5 (Docs/06-Wireframes-UI.md screens 8/10) - both read live Ranked data from Cloud
    /// Code's <c>GetRankedProfile</c>/<c>GetRankedLeaderboard</c>, see ProfileScreenController/
    /// LeaderboardScreenController.
    /// </summary>
    public enum ScreenId
    {
        Splash,
        Home,
        ModeSelect,
        BoardSelect,
        Matchmaking,
        Game,
        Result,
        Profile,
        MatchHistory,
        Store,
        Settings,
        Leaderboard
    }
}
