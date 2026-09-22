using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TTTXO.Core;
using TTTXO.Game.Services;
using TTTXO.Game.UI.Screens;
using UnityEngine;
using UnityEngine.TestTools;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// Which board sizes the Board Select screen offers, per flow.
    ///
    /// This exists because the 9x9/11x11 launch cut shipped half-done and the whole suite stayed
    /// green. The sizes were removed from BOARD_CONFIGS and from the offline fallback - both covered
    /// by tests - but the screen returned <c>BoardConfig.All</c> outright for local play, so 1P and
    /// 2P local kept offering them: the one place they were actually played. Nothing could see it,
    /// because the filter lived inside a private method behind a UIDocument.
    ///
    /// The lesson these tests encode: a cut is only real on the surface the player touches, and
    /// "the config no longer lists it" is not the same claim as "the screen no longer shows it".
    /// </summary>
    [TestFixture]
    public class BoardSelectEligibilityTests
    {
        private static BoardConfigDto Board(int size, int winLength, params string[] modes)
        {
            return new BoardConfigDto { Size = size, WinLength = winLength, Modes = modes };
        }

        private static string Local1P => GameAnalytics.ModeCode(GameMode.SinglePlayer);

        private static string Local2P => GameAnalytics.ModeCode(GameMode.LocalMultiplayer);

        /// <summary>The config as it ships after the launch cut: 3x3 and 6x6, every mode.</summary>
        private static List<BoardConfigDto> ShippedConfig()
        {
            return new List<BoardConfigDto>
            {
                Board(3, 3, Local1P, Local2P, GameAnalytics.OnlineQuickmatchModeCode, GameAnalytics.RankedModeCode),
                Board(6, 4, Local1P, Local2P, GameAnalytics.OnlineQuickmatchModeCode, GameAnalytics.RankedModeCode),
            };
        }

        private static int[] SizesFor(bool online, bool ranked, List<BoardConfigDto> configs)
        {
            return BoardSelectScreenController.EligibleBoardSizes(online, ranked, configs)
                .Select(b => b.Size)
                .ToArray();
        }

        [Test]
        public void LocalFlow_OffersOnlyTheSizesTheConfigAdvertises()
        {
            // The regression. Core still knows 9x9 and 11x11; local play must not offer them.
            CollectionAssert.AreEquivalent(new[] { 3, 6 }, SizesFor(online: false, ranked: false, ShippedConfig()));
        }

        [Test]
        public void LocalFlow_OffersASizeThatIsLocalOnly()
        {
            // A size no online mode advertises still belongs in local play - the filter is per mode,
            // not "is this size online-capable".
            var configs = ShippedConfig();
            configs.Add(Board(9, 5, Local1P, Local2P));

            CollectionAssert.AreEquivalent(new[] { 3, 6, 9 }, SizesFor(online: false, ranked: false, configs));
        }

        [Test]
        public void LocalFlow_IgnoresASizeOfferedOnlyOnline()
        {
            var configs = ShippedConfig();
            configs.Add(Board(9, 5, GameAnalytics.OnlineQuickmatchModeCode));

            CollectionAssert.AreEquivalent(new[] { 3, 6 }, SizesFor(online: false, ranked: false, configs));
        }

        [Test]
        public void QuickmatchAndRanked_FilterByTheirOwnMode()
        {
            var configs = ShippedConfig();
            configs.Add(Board(9, 5, Local1P, Local2P, GameAnalytics.OnlineQuickmatchModeCode));

            CollectionAssert.AreEquivalent(new[] { 3, 6, 9 }, SizesFor(online: true, ranked: false, configs));
            CollectionAssert.AreEquivalent(new[] { 3, 6 }, SizesFor(online: true, ranked: true, configs));
        }

        [Test]
        public void EmptyConfig_FallsBackRatherThanLeavingAnEmptyGrid()
        {
            // A config that never loaded must not strand the player with nothing to pick. Local and
            // Quickmatch open up to every known size; Ranked falls back to its fixed {3, 6}, because
            // there it is a design invariant rather than configurable data.
            var empty = new List<BoardConfigDto>();
            int[] everySize = BoardConfig.All.Select(b => b.Size).ToArray();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no board size advertises a local mode"));
            CollectionAssert.AreEquivalent(everySize, SizesFor(online: false, ranked: false, empty));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no board size advertises 'online_quickmatch'"));
            CollectionAssert.AreEquivalent(everySize, SizesFor(online: true, ranked: false, empty));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no board size advertises 'ranked'"));
            CollectionAssert.AreEquivalent(new[] { 3, 6 }, SizesFor(online: true, ranked: true, empty));
        }

        [Test]
        public void NullConfig_IsTreatedAsEmptyRatherThanThrowing()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no board size advertises a local mode"));
            Assert.IsNotEmpty(SizesFor(online: false, ranked: false, null));
        }
    }
}
