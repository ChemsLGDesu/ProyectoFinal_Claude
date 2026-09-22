using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TTTXO.Core;
using TTTXO.Game.Services;
using UnityEngine;
using UnityEngine.TestTools;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// Two things this service promises and nothing verified until now.
    ///
    /// The first is that the game stays fully playable with no session and no network: when Cloud
    /// Code is unreachable the config comes from TTTXO.Core's own values instead. That path needs no
    /// seam at all - UgsInitializer.Status is NotStarted in a test, so InitializeAsync takes the
    /// fallback branch without touching the network - and it is the one that ships to every player
    /// on a plane.
    ///
    /// The second is that the fallback agrees with what is actually deployed. The fallback builder
    /// carries a comment saying it mirrors Assets/RemoteConfig/GameConfig.rc's BOARD_CONFIGS; if
    /// they drift, an offline player is offered a different set of modes than an online one, and
    /// nothing anywhere would say so.
    ///
    /// The response-handling cases - incomplete, partial, version mismatch - are the shapes a live
    /// integration run never produces, which is why they go through ApplyServerResponse directly.
    /// </summary>
    [TestFixture]
    public class GameConfigServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            // Static service state leaks between tests; the fallback sets every exposed property, so
            // it doubles as a reset to a known configuration.
            GameConfigService.ApplyLocalFallback();
        }

        [TearDown]
        public void TearDown()
        {
            GameConfigService.ApplyLocalFallback();
        }

        // -------------------------------------------------------------------------------------
        // The offline fallback.
        // -------------------------------------------------------------------------------------

        [Test]
        public void LocalFallback_ReportsItselfAsLoadedAndLocal()
        {
            Assert.IsTrue(GameConfigService.IsLoaded);
            Assert.IsTrue(GameConfigService.IsUsingLocalFallback);
            Assert.AreEqual(GameProtocol.Version, GameConfigService.ServerVersion);
        }

        [Test]
        public void LocalFallback_CoversEveryShippedBoardAndTakesItsRulesFromCore()
        {
            Assert.AreEqual(GameConfigService.ShippedBoardSizes.Length, GameConfigService.BoardConfigs.Count);

            foreach (int size in GameConfigService.ShippedBoardSizes)
            {
                var dto = FindBoard(size);
                Assert.IsNotNull(dto, $"board {size} missing from the fallback");
                Assert.AreEqual(BoardConfig.ForSize(size).WinLength, dto.WinLength, $"board {size} win length");
            }
        }

        [Test]
        public void LocalFallback_OmitsTheBoardsDeferredToPostLaunch()
        {
            // 9x9 and 11x11 are still in Core - rules, rewards and AI params all intact - but are not
            // offered to players: an 11x11 cell measures ~29dp on a 360dp phone against Android's 48dp
            // minimum tap target (Docs/09-Encuadre-Dispositivos.md, L-05). This is the assertion that
            // fails if one of the two halves of the cut is undone without the other.
            foreach (var board in BoardConfig.All)
            {
                bool shipped = Array.IndexOf(GameConfigService.ShippedBoardSizes, board.Size) >= 0;
                Assert.AreEqual(shipped, FindBoard(board.Size) != null, $"board {board.Size} offered?");
            }
        }

        [Test]
        public void LocalFallback_OffersRankedOnThreeAndSixOnly()
        {
            // design-doc.md section 6.0: Ranked ships as 3x3 and 6x6 only. Every shipped board happens
            // to be a ranked board today; the assertion is kept general so it still means something if
            // a non-ranked board is shipped again.
            foreach (int size in GameConfigService.ShippedBoardSizes)
            {
                bool offersRanked = Array.IndexOf(FindBoard(size).Modes, GameAnalytics.RankedModeCode) >= 0;
                bool shouldOfferRanked = size == 3 || size == 6;

                Assert.AreEqual(shouldOfferRanked, offersRanked, $"board {size} ranked availability");
            }
        }

        [Test]
        public void LocalFallback_OffersTheLocalModesAndQuickmatchEverywhere()
        {
            foreach (int size in GameConfigService.ShippedBoardSizes)
            {
                var modes = FindBoard(size).Modes;

                Assert.Contains(GameAnalytics.ModeCode(GameMode.SinglePlayer), modes, $"board {size}");
                Assert.Contains(GameAnalytics.ModeCode(GameMode.LocalMultiplayer), modes, $"board {size}");
                Assert.Contains(GameAnalytics.OnlineQuickmatchModeCode, modes, $"board {size}");
            }
        }

        [Test]
        public void LocalFallback_CoversTheNamedDifficultiesOnEveryBoard()
        {
            AiDifficulty[] named = { AiDifficulty.Easy, AiDifficulty.Medium, AiDifficulty.Hard };

            Assert.AreEqual(named.Length * BoardConfig.All.Count, GameConfigService.AiDifficultyParamsList.Count);

            foreach (var difficulty in named)
            {
                foreach (var board in BoardConfig.All)
                {
                    Assert.IsNotNull(
                        FindAiParams(GameAnalytics.DifficultyCode(difficulty), board.Size),
                        $"{difficulty} on board {board.Size} missing");
                }
            }
        }

        [Test]
        public void LocalFallback_OmitsAdaptive()
        {
            // Adaptive is interpolated from a continuous level by AdaptiveAiController, never looked
            // up by name - a row for it here would be a value nobody reads and everybody trusts.
            Assert.IsNull(FindAiParams(GameAnalytics.DifficultyCode(AiDifficulty.Adaptive), 3));
        }

        [Test]
        public void LocalFallback_TakesItsAiParamsFromCore()
        {
            foreach (var difficulty in new[] { AiDifficulty.Easy, AiDifficulty.Medium, AiDifficulty.Hard })
            {
                foreach (var board in BoardConfig.All)
                {
                    var expected = AiParams.ForDifficulty(difficulty, board.Size);
                    var actual = FindAiParams(GameAnalytics.DifficultyCode(difficulty), board.Size);
                    string where = $"{difficulty}/{board.Size}";

                    Assert.AreEqual(expected.WinProbability, actual.WinProbability, 0.0001, $"{where} win");
                    Assert.AreEqual(expected.BlockProbability, actual.BlockProbability, 0.0001, $"{where} block");
                    Assert.AreEqual(expected.BlunderChance, actual.BlunderChance, 0.0001, $"{where} blunder");
                    Assert.AreEqual(expected.SearchDepth, actual.SearchDepth, $"{where} depth");
                }
            }
        }

        [Test]
        public void LocalFallback_UsesTheRatifiedTuningDefaults()
        {
            Assert.AreEqual(new MatchmakingConfigDto().WaitCeilingSeconds, GameConfigService.MatchmakingConfig.WaitCeilingSeconds);
            Assert.AreEqual(new RankedConfigDto().InitialMmr, GameConfigService.RankedConfig.InitialMmr);
            Assert.AreEqual(new RankedConfigDto().PlacementMatches, GameConfigService.RankedConfig.PlacementMatches);
        }

        // -------------------------------------------------------------------------------------
        // The fallback against what is actually deployed.
        // -------------------------------------------------------------------------------------

        [Test]
        public void LocalFallback_MirrorsTheDeployedRemoteConfigBoards()
        {
            var deployed = ReadDeployedBoardConfigs();

            Assert.AreEqual(deployed.Count, GameConfigService.BoardConfigs.Count, "board count differs from GameConfig.rc");

            foreach (var pair in deployed)
            {
                var dto = FindBoard(pair.Key);
                Assert.IsNotNull(dto, $"board {pair.Key} is deployed but missing from the offline fallback");
                Assert.AreEqual(
                    pair.Value.WinLength,
                    dto.WinLength,
                    $"board {pair.Key} win length differs from GameConfig.rc");
                CollectionAssert.AreEquivalent(
                    pair.Value.Modes,
                    dto.Modes,
                    $"board {pair.Key} offers different modes offline than the deployed Remote Config");
            }
        }

        // -------------------------------------------------------------------------------------
        // Handling a server response.
        // -------------------------------------------------------------------------------------

        [Test]
        public void ApplyServerResponse_RejectsANullResponse()
        {
            Assert.Throws<InvalidOperationException>(() => GameConfigService.ApplyServerResponse(null));
        }

        [Test]
        public void ApplyServerResponse_RejectsAResponseMissingBoardConfigs()
        {
            var response = new GetGameConfigResponse
            {
                Version = GameProtocol.Version,
                AiDifficultyParams = new List<AiDifficultyParamsDto>(),
            };

            Assert.Throws<InvalidOperationException>(() => GameConfigService.ApplyServerResponse(response));
        }

        [Test]
        public void ApplyServerResponse_RejectsAResponseMissingAiParams()
        {
            var response = new GetGameConfigResponse
            {
                Version = GameProtocol.Version,
                BoardConfigs = new List<BoardConfigDto>(),
            };

            Assert.Throws<InvalidOperationException>(() => GameConfigService.ApplyServerResponse(response));
        }

        [Test]
        public void ApplyServerResponse_LeavesTheExistingConfigUntouched_WhenItRejects()
        {
            int boardsBefore = GameConfigService.BoardConfigs.Count;

            Assert.Throws<InvalidOperationException>(() => GameConfigService.ApplyServerResponse(null));

            Assert.IsTrue(GameConfigService.IsUsingLocalFallback, "a rejected response must not half-apply");
            Assert.AreEqual(boardsBefore, GameConfigService.BoardConfigs.Count);
        }

        [Test]
        public void ApplyServerResponse_ClearsTheLocalFallbackFlag()
        {
            GameConfigService.ApplyServerResponse(MinimalValidResponse(GameProtocol.Version));

            Assert.IsFalse(GameConfigService.IsUsingLocalFallback);
            Assert.IsTrue(GameConfigService.IsLoaded);
            Assert.AreEqual(GameProtocol.Version, GameConfigService.ServerVersion);
        }

        [Test]
        public void ApplyServerResponse_SubstitutesDefaults_ForOmittedTuningSections()
        {
            // A response without these sections must not leave nulls behind: every call site reads
            // them without a null check.
            var response = MinimalValidResponse(GameProtocol.Version);
            response.MatchmakingConfig = null;
            response.RankedConfig = null;

            GameConfigService.ApplyServerResponse(response);

            Assert.IsNotNull(GameConfigService.MatchmakingConfig);
            Assert.IsNotNull(GameConfigService.RankedConfig);
            Assert.AreEqual(new RankedConfigDto().InitialMmr, GameConfigService.RankedConfig.InitialMmr);
        }

        [Test]
        public void ApplyServerResponse_WarnsOnAVersionMismatch_ButStillApplies()
        {
            // The handshake warns and never blocks play - that is the Milestone 2 decision recorded
            // in Docs/01-Directrices-Proyecto.md, and real IAP is what eventually changes it.
            int mismatched = GameProtocol.Version + 1;
            LogAssert.Expect(LogType.Warning, new Regex("GameProtocol.Version"));

            GameConfigService.ApplyServerResponse(MinimalValidResponse(mismatched));

            Assert.AreEqual(mismatched, GameConfigService.ServerVersion);
            Assert.IsFalse(GameConfigService.IsUsingLocalFallback, "a version mismatch must not stop the config being used");
        }

        // -------------------------------------------------------------------------------------

        private static GetGameConfigResponse MinimalValidResponse(int version)
        {
            return new GetGameConfigResponse
            {
                Version = version,
                BoardConfigs = new List<BoardConfigDto> { new() { Size = 3, WinLength = 3, Modes = new[] { "single_player" } } },
                AiDifficultyParams = new List<AiDifficultyParamsDto>(),
                MatchmakingConfig = new MatchmakingConfigDto(),
                RankedConfig = new RankedConfigDto(),
            };
        }

        private static BoardConfigDto FindBoard(int size)
        {
            foreach (var dto in GameConfigService.BoardConfigs)
            {
                if (dto.Size == size)
                {
                    return dto;
                }
            }

            return null;
        }

        private static AiDifficultyParamsDto FindAiParams(string difficultyCode, int boardSize)
        {
            foreach (var dto in GameConfigService.AiDifficultyParamsList)
            {
                if (dto.Difficulty == difficultyCode && dto.BoardSize == boardSize)
                {
                    return dto;
                }
            }

            return null;
        }

        private readonly struct DeployedBoard
        {
            public DeployedBoard(int winLength, string[] modes)
            {
                WinLength = winLength;
                Modes = modes;
            }

            public int WinLength { get; }

            public string[] Modes { get; }
        }

        /// <summary>
        /// Reads BOARD_CONFIGS out of the deployed Remote Config as text. The file is config-as-code
        /// with typed sibling sections, so a regex over the three keys that only board entries carry
        /// is both simpler and less brittle than modelling the whole document.
        /// </summary>
        private static Dictionary<int, DeployedBoard> ReadDeployedBoardConfigs()
        {
            string path = Path.Combine(Application.dataPath, "RemoteConfig", "GameConfig.rc");
            Assert.IsTrue(File.Exists(path), $"deployed Remote Config not found at {path}");

            var boards = new Dictionary<int, DeployedBoard>();
            var matches = Regex.Matches(
                File.ReadAllText(path),
                @"""size""\s*:\s*(\d+)\s*,\s*""winLength""\s*:\s*(\d+)\s*,\s*""modes""\s*:\s*\[([^\]]*)\]");

            foreach (Match match in matches)
            {
                var modes = new List<string>();
                foreach (Match mode in Regex.Matches(match.Groups[3].Value, @"""([^""]+)"""))
                {
                    modes.Add(mode.Groups[1].Value);
                }

                boards[int.Parse(match.Groups[1].Value)] =
                    new DeployedBoard(int.Parse(match.Groups[2].Value), modes.ToArray());
            }

            Assert.IsNotEmpty(boards, "parsed no boards out of GameConfig.rc - the format changed");
            return boards;
        }
    }
}
