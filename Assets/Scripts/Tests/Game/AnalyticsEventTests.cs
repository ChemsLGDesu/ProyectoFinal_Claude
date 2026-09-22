using System;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using TTTXO.Game.Services;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// Covers the JSON Lines serialization the beta analytics sink writes to disk
    /// (<see cref="AnalyticsEvent.ToJsonLine"/>).
    ///
    /// The sink itself is behind the <c>TTTXO_LOCAL_ANALYTICS</c> define so its file I/O can be
    /// stripped from production, but the serialization is not - precisely so it stays under test
    /// whether or not the define is set. A malformed line is not a visible failure: the beta keeps
    /// playing, the file keeps growing, and it only surfaces when someone tries to parse a month of
    /// data and finds it broken.
    /// </summary>
    [TestFixture]
    public class AnalyticsEventTests
    {
        private static readonly DateTime Timestamp = new(2026, 8, 28, 1, 2, 3, 456, DateTimeKind.Utc);

        private CultureInfo originalCulture;

        [SetUp]
        public void SetUp() => originalCulture = Thread.CurrentThread.CurrentCulture;

        [TearDown]
        public void TearDown() => Thread.CurrentThread.CurrentCulture = originalCulture;

        [Test]
        public void ToJsonLine_WritesTheTimestampNameAndParameters()
        {
            var evt = new AnalyticsEvent("match_started");
            evt.Add("mode", "ranked");
            evt.Add("board_size", 6);

            Assert.AreEqual(
                "{\"ts\":\"2026-08-28T01:02:03.456Z\",\"event\":\"match_started\"," +
                "\"params\":{\"mode\":\"ranked\",\"board_size\":6}}",
                evt.ToJsonLine(Timestamp));
        }

        [Test]
        public void ToJsonLine_WritesAnEmptyObject_WhenThereAreNoParameters()
        {
            Assert.AreEqual(
                "{\"ts\":\"2026-08-28T01:02:03.456Z\",\"event\":\"app_opened\",\"params\":{}}",
                new AnalyticsEvent("app_opened").ToJsonLine(Timestamp));
        }

        [Test]
        public void ToJsonLine_WritesBooleansUnquoted()
        {
            var evt = new AnalyticsEvent("match_finished");
            evt.Add("won", true);
            evt.Add("first_player_won", false);

            StringAssert.Contains("\"won\":true,\"first_player_won\":false", evt.ToJsonLine(Timestamp));
        }

        [Test]
        public void ToJsonLine_EscapesEveryCharacterJsonForbidsRaw()
        {
            var evt = new AnalyticsEvent("player_named");
            evt.Add("name", "he said \"hi\"\\then\nleft\t");

            StringAssert.Contains(
                "\"name\":\"he said \\\"hi\\\"\\\\then\\nleft\\t\"",
                evt.ToJsonLine(Timestamp));
        }

        [Test]
        public void ToJsonLine_EscapesControlCharactersAsUnicode()
        {
            var evt = new AnalyticsEvent("player_named");
            evt.Add("name", "a\u0001b");

            StringAssert.Contains("\"name\":\"a\\u0001b\"", evt.ToJsonLine(Timestamp));
        }

        [Test]
        public void ToJsonLine_LeavesNonAsciiAlone()
        {
            // The game ships in 10 latin-alphabet languages and player names are free text, so
            // accented and extended glyphs are ordinary input, not an edge case. The file is UTF-8;
            // escaping these would only make it harder to read.
            var evt = new AnalyticsEvent("player_named");
            evt.Add("name", "Ñandú Çelik Łukasz");

            StringAssert.Contains("\"name\":\"Ñandú Çelik Łukasz\"", evt.ToJsonLine(Timestamp));
        }

        [Test]
        public void ToJsonLine_UsesInvariantCulture_ForFloats()
        {
            // Under a comma-decimal locale a naive ToString() emits 1,5 - which silently turns one
            // parameter into two values and makes the line invalid JSON. The developer's own machine
            // is es-AR, so this is the default case here, not an exotic one.
            Thread.CurrentThread.CurrentCulture = new CultureInfo("es-AR");

            var evt = new AnalyticsEvent("matchmaking_wait_time");
            evt.Add("seconds", 1.5f);

            StringAssert.Contains("\"seconds\":1.5", evt.ToJsonLine(Timestamp));
        }

        [Test]
        public void ToJsonLine_NormalizesTheTimestampToUtc()
        {
            var local = new DateTime(2026, 8, 28, 1, 2, 3, 456, DateTimeKind.Utc).ToLocalTime();

            StringAssert.Contains("\"ts\":\"2026-08-28T01:02:03.456Z\"",
                new AnalyticsEvent("app_opened").ToJsonLine(local));
        }

        [Test]
        public void ToJsonLine_NeverSpansMoreThanOneLine()
        {
            // JSON Lines only works if one event is exactly one line - an unescaped newline in a
            // player-supplied string would split the record in two and corrupt every parser.
            var evt = new AnalyticsEvent("player_named");
            evt.Add("name", "first\nsecond\r\nthird");

            Assert.AreEqual(1, evt.ToJsonLine(Timestamp).Split('\n').Length);
        }

        [Test]
        public void Parameters_PreserveInsertionOrder()
        {
            var evt = new AnalyticsEvent("ranked_mmr_changed");
            evt.Add("mmr_before", 1000);
            evt.Add("mmr_after", 1024);
            evt.Add("delta", 24);

            Assert.AreEqual(
                new[] { "mmr_before", "mmr_after", "delta" },
                new[] { evt.Parameters[0].Key, evt.Parameters[1].Key, evt.Parameters[2].Key });
        }
    }
}
