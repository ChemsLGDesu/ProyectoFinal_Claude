using System.Collections.Generic;
using NUnit.Framework;
using TTTXO.Game.Services;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// RankedQueryService is a thin RPC wrapper on purpose - it deliberately does not catch, because
    /// each caller degrades differently - so there is little here to test and this file says so
    /// rather than padding. What is worth pinning is the argument map, whose one decision has a
    /// server-side consequence, and the response lookup, which the DTO's own docs warn callers not
    /// to assume always finds something.
    /// </summary>
    [TestFixture]
    public class RankedQueryServiceTests
    {
        [Test]
        public void BuildLeaderboardArgs_AlwaysSendsTheBoardSize()
        {
            var args = RankedQueryService.BuildLeaderboardArgs(6, null);

            Assert.AreEqual(6, args["boardSize"]);
        }

        [Test]
        public void BuildLeaderboardArgs_OmitsTheLimitEntirely_WhenTheCallerHasNoPreference()
        {
            var args = RankedQueryService.BuildLeaderboardArgs(3, null);

            Assert.IsFalse(
                args.ContainsKey("limit"),
                "sending a null or a zero would tell the server to return nothing instead of letting it apply its default");
            Assert.AreEqual(1, args.Count);
        }

        [Test]
        public void BuildLeaderboardArgs_SendsTheLimit_WhenGivenOne()
        {
            var args = RankedQueryService.BuildLeaderboardArgs(3, 100);

            Assert.AreEqual(100, args["limit"]);
        }

        // -------------------------------------------------------------------------------------
        // RankedProfileResponse.BoardFor - "null if boardSize is not in Boards ... callers must not
        // assume it is always present".
        // -------------------------------------------------------------------------------------

        [Test]
        public void BoardFor_FindsTheRequestedBoard()
        {
            var response = new RankedProfileResponse
            {
                Boards = new List<RankedBoardProfileDto>
                {
                    new() { BoardSize = 3, Mmr = 1180 },
                    new() { BoardSize = 6, Mmr = 940 },
                },
            };

            Assert.AreEqual(940, response.BoardFor(6).Mmr);
        }

        [Test]
        public void BoardFor_ReturnsNull_ForABoardTheResponseDoesNotCarry()
        {
            var response = new RankedProfileResponse
            {
                Boards = new List<RankedBoardProfileDto> { new() { BoardSize = 3 } },
            };

            Assert.IsNull(response.BoardFor(9), "9x9 is not a Ranked board and must not be invented");
        }

        [Test]
        public void BoardFor_ReturnsNull_WhenTheResponseCarriesNoBoardsAtAll()
        {
            var response = new RankedProfileResponse();

            Assert.IsNull(response.BoardFor(3), "a response with no Boards must not throw at the call site");
        }
    }
}
