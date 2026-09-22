using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// The Ranked season boundary lives in TWO config-as-code surfaces that UGS deploys separately
    /// and never reconciles with each other: <c>RANKED_CONFIG.seasonStartUnixSeconds</c>/
    /// <c>seasonDurationMonths</c> in Assets/RemoteConfig/GameConfig.rc (what Cloud Code's
    /// <c>RankedSeasonCalculator</c> uses to decide the season id, the soft reset and the tier
    /// reward) and <c>ResetConfig.Start</c>/<c>Schedule</c> in the two .lb files (when the
    /// Leaderboards service actually clears and archives the board).
    ///
    /// Assets/Leaderboards/README.md calls keeping them aligned mandatory, but until now it was a
    /// convention enforced by nothing. Drift is invisible and expensive: the board would clear on one
    /// date while MMR soft resets and season rewards fire on another, so a player loses their
    /// standing weeks before they are paid for it, and the "quedan N dias" countdown points at the
    /// wrong instant.
    ///
    /// These read both surfaces as text - the .lb/.rc assets are not part of any Unity assembly, so
    /// text is the only seam a test has (same approach as <see cref="ContractMirrorTests"/>).
    /// </summary>
    [TestFixture]
    public class SeasonConfigAlignmentTests
    {
        private static readonly string[] LeaderboardAssets =
        {
            "Leaderboards/ranked_3x3.lb",
            "Leaderboards/ranked_6x6.lb",
        };

        [Test]
        public void EveryLeaderboardFirstResetsAtTheSeasonAnchor()
        {
            long anchor = ReadSeasonAnchorUnixSeconds();

            foreach (string asset in LeaderboardAssets)
            {
                string start = ReadJsonString(asset, "Start");
                long startUnixSeconds = DateTimeOffset
                    .Parse(start, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    .ToUnixTimeSeconds();

                Assert.AreEqual(
                    anchor,
                    startUnixSeconds,
                    $"{asset} resets from {start} but RANKED_CONFIG.seasonStartUnixSeconds is {anchor} " +
                    "- the leaderboard would clear on a different instant than Cloud Code closes the season");
            }
        }

        [Test]
        public void EveryLeaderboardResetsOnTheSeasonCadence()
        {
            var anchor = DateTimeOffset.FromUnixTimeSeconds(ReadSeasonAnchorUnixSeconds());
            int durationMonths = ReadSeasonDurationMonths();

            Assert.AreEqual(
                1,
                durationMonths,
                "seasonDurationMonths is no longer 1, so the cron below no longer describes the cadence. " +
                "A cron month field of '*/N' means 'every Nth month of the calendar year', NOT 'every N " +
                "months counted from the anchor', so re-derive both .lb schedules by hand rather than " +
                "relaxing this assertion");

            Assert.LessOrEqual(
                anchor.Day,
                28,
                "the season anchor falls after the 28th, where the two surfaces stop agreeing: " +
                "RankedSeasonCalculator uses AddMonths, which CLAMPS (Jan 31 -> Feb 28), while a cron " +
                "day-of-month simply does not fire in months that lack that day");

            foreach (string asset in LeaderboardAssets)
            {
                string schedule = ReadJsonString(asset, "Schedule");
                string[] fields = schedule.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                Assert.AreEqual(5, fields.Length, $"{asset} schedule '{schedule}' is not a 5-field cron expression");
                Assert.AreEqual(anchor.Minute.ToString(CultureInfo.InvariantCulture), fields[0], $"{asset} resets at a different minute than the season anchor");
                Assert.AreEqual(anchor.Hour.ToString(CultureInfo.InvariantCulture), fields[1], $"{asset} resets at a different hour than the season anchor");
                Assert.AreEqual(anchor.Day.ToString(CultureInfo.InvariantCulture), fields[2], $"{asset} resets on a different day of the month than the season anchor");
                Assert.AreEqual("*", fields[3], $"{asset} does not reset every month, but seasons are one month long");
                Assert.AreEqual("*", fields[4], $"{asset} constrains the day of the week, which would skip season boundaries");
            }
        }

        [Test]
        public void NoLeaderboardCarriesItsOwnTierBands()
        {
            foreach (string asset in LeaderboardAssets)
            {
                StringAssert.DoesNotContain(
                    "TieringConfig",
                    ReadAsset(asset),
                    $"{asset} defines leaderboard-side tier bands, which would be a SECOND tier table " +
                    "next to RANKED_CONFIG.tierMinMmr - the one RankedTierCalculator uses for the profile " +
                    "and the season reward. Two tables drift, and the player sees one tier on the board " +
                    "and another on their profile. The tier is drawn client-side from RANKED_CONFIG");
            }
        }

        private static long ReadSeasonAnchorUnixSeconds() =>
            long.Parse(ReadRemoteConfigNumber("seasonStartUnixSeconds"), CultureInfo.InvariantCulture);

        private static int ReadSeasonDurationMonths() =>
            int.Parse(ReadRemoteConfigNumber("seasonDurationMonths"), CultureInfo.InvariantCulture);

        private static string ReadRemoteConfigNumber(string key)
        {
            var match = Regex.Match(
                ReadAsset("RemoteConfig/GameConfig.rc"),
                @"""" + Regex.Escape(key) + @"""\s*:\s*(\d+)");

            Assert.IsTrue(match.Success, $"could not find '{key}' in GameConfig.rc - the format changed");

            return match.Groups[1].Value;
        }

        private static string ReadJsonString(string relativePath, string key)
        {
            var match = Regex.Match(
                ReadAsset(relativePath),
                @"""" + Regex.Escape(key) + @"""\s*:\s*""([^""]+)""");

            Assert.IsTrue(match.Success, $"could not find '{key}' in {relativePath} - the format changed");

            return match.Groups[1].Value;
        }

        private static string ReadAsset(string relativePath)
        {
            string path = Path.Combine(Application.dataPath, relativePath);
            Assert.IsTrue(File.Exists(path), $"config asset not found at {path}");

            return File.ReadAllText(path);
        }
    }
}
