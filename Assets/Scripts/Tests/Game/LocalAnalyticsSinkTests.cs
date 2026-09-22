#if TTTXO_LOCAL_ANALYTICS
using System.IO;
using NUnit.Framework;
using TTTXO.Game.Services;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// Covers the beta sink's file I/O. Behind the same define as the sink itself, so these only
    /// run in a beta configuration - the serialization they depend on is covered unconditionally by
    /// <see cref="AnalyticsEventTests"/>.
    ///
    /// Every test redirects <c>DirectoryOverride</c> at a temp folder: the sink's real target is
    /// <c>Application.persistentDataPath</c>, which in the Editor is the developer's own data, and
    /// EditMode tests would otherwise scatter event files through it - the same reasoning behind
    /// <see cref="PlayerPrefsSandbox"/>.
    /// </summary>
    [TestFixture]
    public class LocalAnalyticsSinkTests
    {
        private string tempDirectory;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "tttxo-sink-" + Path.GetRandomFileName());
            LocalAnalyticsSink.DirectoryOverride = tempDirectory;
        }

        [TearDown]
        public void TearDown()
        {
            LocalAnalyticsSink.DirectoryOverride = null;

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Test]
        public void Flush_WritesOneLinePerRecordedEvent()
        {
            LocalAnalyticsSink.Record(Event("match_started", "mode", "ranked"));
            LocalAnalyticsSink.Record(Event("match_finished", "mode", "ranked"));

            LocalAnalyticsSink.Flush();

            string[] lines = File.ReadAllLines(SingleFile());
            Assert.AreEqual(2, lines.Length);
            StringAssert.Contains("\"event\":\"match_started\"", lines[0]);
            StringAssert.Contains("\"event\":\"match_finished\"", lines[1]);
        }

        [Test]
        public void Record_WritesNothingBeforeTheBufferIsFlushed()
        {
            LocalAnalyticsSink.Record(Event("match_started", "mode", "ranked"));

            Assert.IsFalse(
                Directory.Exists(tempDirectory),
                "a single event should stay buffered - writing per event would mean a disk write mid-match");
        }

        [Test]
        public void Flush_AppendsToTheSameSessionFile()
        {
            LocalAnalyticsSink.Record(Event("match_started", "mode", "ranked"));
            LocalAnalyticsSink.Flush();
            LocalAnalyticsSink.Record(Event("match_finished", "mode", "ranked"));
            LocalAnalyticsSink.Flush();

            Assert.AreEqual(1, Directory.GetFiles(tempDirectory).Length, "a session must not spread across files");
            Assert.AreEqual(2, File.ReadAllLines(SingleFile()).Length);
        }

        [Test]
        public void Flush_IsANoOpWhenNothingWasRecorded()
        {
            LocalAnalyticsSink.Flush();

            Assert.IsFalse(Directory.Exists(tempDirectory));
        }

        [Test]
        public void DeleteAll_RemovesEverythingOnDisk()
        {
            LocalAnalyticsSink.Record(Event("match_started", "mode", "ranked"));
            LocalAnalyticsSink.Flush();
            Assume.That(Directory.Exists(tempDirectory), "precondition: the sink wrote something");

            LocalAnalyticsSink.DeleteAll();

            Assert.IsFalse(
                Directory.Exists(tempDirectory),
                "Settings' \"delete my data\" must take the recorded events with it");
        }

        [Test]
        public void Flush_DegradesToAWarning_WhenTheDirectoryCannotBeWritten()
        {
            // A file where the directory should be: CreateDirectory throws, and the game must not.
            string blocker = Path.Combine(Path.GetTempPath(), "tttxo-sink-blocked-" + Path.GetRandomFileName());
            File.WriteAllText(blocker, "not a directory");
            LocalAnalyticsSink.DirectoryOverride = Path.Combine(blocker, "analytics");

            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("LocalAnalyticsSink"));

                LocalAnalyticsSink.Record(Event("match_started", "mode", "ranked"));
                Assert.DoesNotThrow(LocalAnalyticsSink.Flush);
            }
            finally
            {
                File.Delete(blocker);
            }
        }

        private string SingleFile()
        {
            string[] files = Directory.GetFiles(tempDirectory, "*.jsonl");
            Assert.AreEqual(1, files.Length, "expected exactly one session file");
            return files[0];
        }

        private static AnalyticsEvent Event(string name, string key, string value)
        {
            var evt = new AnalyticsEvent(name);
            evt.Add(key, value);
            return evt;
        }
    }
}
#endif
