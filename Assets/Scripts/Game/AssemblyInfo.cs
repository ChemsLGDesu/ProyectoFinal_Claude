using System.Runtime.CompilerServices;

// The test assembly reaches a handful of internals that are seams, not API: GameConfigService's
// response/fallback appliers and RankedQueryService's argument builder. Exposing them publicly
// would invite call sites that should be going through InitializeAsync/GetLeaderboardAsync
// instead, so they stay internal and only the tests can see them.
//
// TTTXO.Game.Tests is constrained to UNITY_INCLUDE_TESTS, so it does not exist in a player build
// and this line resolves to nothing there.
[assembly: InternalsVisibleTo("TTTXO.Game.Tests")]
