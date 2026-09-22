using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TTTXO.Core;
using TTTXO.Game.Services;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// Analytics event parameters must be snake_case English
    /// (Docs/01-Directrices-Proyecto.md), and the Dashboard's Event Manager is configured against
    /// those exact strings - a value that does not match is not a cosmetic problem, it is an event
    /// the ingestion pipeline drops.
    ///
    /// The mappers all end in a <c>_ =&gt; ToString()</c> arm, which turns an enum member nobody
    /// remembered to map into a PascalCase value that looks fine in code and silently breaks the
    /// contract. Walking every enum value is what catches that: add a difficulty or a mode without
    /// touching <see cref="GameAnalytics"/> and these fail.
    /// </summary>
    [TestFixture]
    public class GameAnalyticsCodeTests
    {
        private static readonly Regex SnakeCase = new(@"^[a-z][a-z0-9]*(_[a-z0-9]+)*$");

        [Test]
        public void ModeCode_IsSnakeCase_ForEveryGameMode()
        {
            foreach (GameMode mode in Enum.GetValues(typeof(GameMode)))
            {
                string code = GameAnalytics.ModeCode(mode);

                Assert.IsTrue(
                    SnakeCase.IsMatch(code),
                    $"GameMode.{mode} maps to '{code}', which is not snake_case - it fell through to ToString()");
            }
        }

        [Test]
        public void DifficultyCode_IsSnakeCase_ForEveryDifficulty()
        {
            foreach (AiDifficulty difficulty in Enum.GetValues(typeof(AiDifficulty)))
            {
                string code = GameAnalytics.DifficultyCode(difficulty);

                Assert.IsTrue(
                    SnakeCase.IsMatch(code),
                    $"AiDifficulty.{difficulty} maps to '{code}', which is not snake_case - it fell through to ToString()");
            }
        }

        [Test]
        public void ModeCode_IsDistinctPerGameMode()
        {
            Assert.AreNotEqual(
                GameAnalytics.ModeCode(GameMode.SinglePlayer),
                GameAnalytics.ModeCode(GameMode.LocalMultiplayer));
        }

        [Test]
        public void DifficultyCode_IsDistinctPerDifficulty()
        {
            var seen = new System.Collections.Generic.HashSet<string>();

            foreach (AiDifficulty difficulty in Enum.GetValues(typeof(AiDifficulty)))
            {
                Assert.IsTrue(
                    seen.Add(GameAnalytics.DifficultyCode(difficulty)),
                    $"AiDifficulty.{difficulty} shares its code with another difficulty");
            }
        }

        [Test]
        public void MatchStatusCode_MapsEveryTerminalStatus()
        {
            Assert.AreEqual("x_won", GameAnalytics.MatchStatusCode(MatchStatus.XWon));
            Assert.AreEqual("o_won", GameAnalytics.MatchStatusCode(MatchStatus.OWon));
            Assert.AreEqual("draw", GameAnalytics.MatchStatusCode(MatchStatus.Draw));
        }

        [Test]
        public void MatchStatusCode_TreatsAnythingElseAsInProgress()
        {
            Assert.AreEqual("in_progress", GameAnalytics.MatchStatusCode(MatchStatus.InProgress));
        }

        [Test]
        public void OnlineModeCodes_AreSnakeCase()
        {
            Assert.IsTrue(SnakeCase.IsMatch(GameAnalytics.OnlineQuickmatchModeCode));
            Assert.IsTrue(SnakeCase.IsMatch(GameAnalytics.RankedModeCode));
        }
    }
}
