using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;

namespace TTTXO.Game.Services
{
    /// <summary>One board size's worth of the local player's own Ranked status. Kept 1:1 with the server-side <c>RankedBoardProfileDto</c> (CloudCode~/TicTacToeModule/Dtos.cs).</summary>
    [Serializable]
    public class RankedBoardProfileDto
    {
        public int BoardSize;
        public int Mmr;
        public int PlacementsPlayed;
        public int PlacementMatchesRequired;
        public bool IsPlaced;

        /// <summary>"bronze" | "silver" | "gold" | "platinum" | "diamond" - the screen maps this to a localized display name, never shown verbatim.</summary>
        public string Tier;
    }

    /// <summary>Response of <c>GetRankedProfile</c>. Kept 1:1 with the server-side <c>RankedProfileResponse</c>.</summary>
    [Serializable]
    public class RankedProfileResponse
    {
        public int Season;
        public long SeasonEndUnixSeconds;
        public List<RankedBoardProfileDto> Boards;

        /// <summary>Convenience lookup - null if <paramref name="boardSize"/> is not in <see cref="Boards"/> (should not happen for 3/6, the only Ranked boards, but callers must not assume it is always present).</summary>
        public RankedBoardProfileDto BoardFor(int boardSize)
        {
            if (Boards == null)
            {
                return null;
            }

            foreach (var board in Boards)
            {
                if (board.BoardSize == boardSize)
                {
                    return board;
                }
            }

            return null;
        }
    }

    /// <summary>One leaderboard row (top N entry, or the caller's own row). Kept 1:1 with the server-side <c>RankedLeaderboardEntryDto</c>.</summary>
    [Serializable]
    public class RankedLeaderboardEntryDto
    {
        public string PlayerId;
        public string PlayerName;
        public int Rank;
        public int Mmr;
    }

    /// <summary>Response of <c>GetRankedLeaderboard</c>. Kept 1:1 with the server-side <c>RankedLeaderboardResponse</c>.</summary>
    [Serializable]
    public class RankedLeaderboardResponse
    {
        public int BoardSize;
        public int Season;
        public List<RankedLeaderboardEntryDto> Top;
        public bool HasOwnEntry;
        public RankedLeaderboardEntryDto OwnEntry;
    }

    /// <summary>
    /// Cloud Code RPC wrappers for the two Ranked read-only endpoints (Milestone 5, design-doc.md
    /// section 6): <c>GetRankedProfile</c> (wireframe 8, Profile's MMR/tier/placement) and
    /// <c>GetRankedLeaderboard</c> (wireframe 10, Leaderboard's top N + own row) - see
    /// CloudCode~/TicTacToeModule/RankedQueryFunctions.cs for the server side.
    ///
    /// Deliberately NOT wrapped in a try/catch here (unlike <see cref="GameConfigService"/>/
    /// <c>UiText.Localize</c>): "degrade gracefully" means something different for each caller -
    /// <see cref="MatchmakingService"/> falls back to <see cref="RankedProfileCache"/>'s last-known
    /// value, while <see cref="TTTXO.Game.UI.Screens.ProfileScreenController"/>/
    /// <see cref="TTTXO.Game.UI.Screens.LeaderboardScreenController"/> show a decent loading/error
    /// state instead of fabricated numbers (Docs/01-Directrices-Proyecto.md: no screen may invent
    /// data) - each caller owns that decision instead of this thin wrapper picking one for everybody.
    /// </summary>
    public static class RankedQueryService
    {
        private const string ModuleName = "TicTacToeModule";
        private const string GetProfileFunction = "GetRankedProfile";
        private const string GetLeaderboardFunction = "GetRankedLeaderboard";

        public static Task<RankedProfileResponse> GetProfileAsync()
        {
            return CloudCodeService.Instance.CallModuleEndpointAsync<RankedProfileResponse>(ModuleName, GetProfileFunction);
        }

        public static Task<RankedLeaderboardResponse> GetLeaderboardAsync(int boardSize, int? limit = null)
        {
            return CloudCodeService.Instance.CallModuleEndpointAsync<RankedLeaderboardResponse>(
                ModuleName, GetLeaderboardFunction, BuildLeaderboardArgs(boardSize, limit));
        }

        /// <summary>
        /// Builds the <c>GetRankedLeaderboard</c> argument map. Split out because it holds the only
        /// decision in this wrapper: <c>limit</c> is omitted rather than sent as a null or a zero
        /// when the caller does not specify one, so the server applies its own default (see
        /// CloudCode~/TicTacToeModule/RankedQueryFunctions.cs) instead of being told to return
        /// nothing.
        /// </summary>
        internal static Dictionary<string, object> BuildLeaderboardArgs(int boardSize, int? limit)
        {
            var args = new Dictionary<string, object> { { "boardSize", boardSize } };
            if (limit.HasValue)
            {
                args["limit"] = limit.Value;
            }

            return args;
        }
    }
}
