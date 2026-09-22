#if TTTXO_LOCAL_ANALYTICS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace TTTXO.Game.Services
{
    /// <summary>
    /// Beta-only analytics sink: appends every event to a JSON Lines file on disk instead of
    /// sending it to Unity Analytics, so a beta can be instrumented without paying per custom
    /// event. While <c>TTTXO_LOCAL_ANALYTICS</c> is defined, <see cref="GameAnalytics"/> routes
    /// here and does NOT touch <c>AnalyticsService</c> at all.
    ///
    /// The whole class sits behind the define on purpose: "unplugged for production" has to mean
    /// the file-writing code is not in the build, not merely that a runtime flag is false. A
    /// build-time guard (Assets/Scripts/Game/Editor/LocalAnalyticsBuildGuard.cs) fails any
    /// non-development build that still has the define set, so shipping it is not a thing anyone
    /// can forget. The serialization it relies on lives in <see cref="AnalyticsEvent.ToJsonLine"/>,
    /// which is always compiled and always tested.
    ///
    /// Never throws: Docs/01-Directrices-Proyecto.md requires that analytics cannot break the game,
    /// and that applies at least as strongly to a sink doing file I/O on a player's device.
    /// </summary>
    public static class LocalAnalyticsSink
    {
        /// <summary>Events buffered before touching the disk. Small enough that a hard crash loses little, large enough that a match does not cause a write per event.</summary>
        private const int FlushThreshold = 25;

        private static readonly List<string> Buffer = new();

        private static string directoryOverride;
        private static string sessionFilePath;
        private static bool ioFailed;

        /// <summary>Where the .jsonl files live. Tests point this elsewhere so they never write into the developer's real persistent data; production beta builds leave it null.</summary>
        internal static string DirectoryOverride
        {
            get => directoryOverride;
            set
            {
                directoryOverride = value;
                sessionFilePath = null;
                ioFailed = false;
                Buffer.Clear();
            }
        }

        public static string Directory =>
            directoryOverride ?? Path.Combine(Application.persistentDataPath, "analytics");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallFlushHooks()
        {
            Application.quitting += Flush;

            // Mobile rarely raises `quitting` - backgrounding is the last reliable moment to get
            // the tail of the session onto disk.
            Application.focusChanged += focused =>
            {
                if (!focused)
                {
                    Flush();
                }
            };
        }

        public static void Record(AnalyticsEvent evt)
        {
            try
            {
                Buffer.Add(evt.ToJsonLine(DateTime.UtcNow));

                if (Buffer.Count >= FlushThreshold)
                {
                    Flush();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"LocalAnalyticsSink: failed to buffer '{evt?.Name}' - {ex.Message}");
            }
        }

        public static void Flush()
        {
            if (Buffer.Count == 0 || ioFailed)
            {
                return;
            }

            try
            {
                string directory = Directory;
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                sessionFilePath ??= Path.Combine(
                    directory,
                    $"events-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.jsonl");

                File.AppendAllLines(sessionFilePath, Buffer);
                Buffer.Clear();
            }
            catch (Exception ex)
            {
                // Latch instead of retrying: if the disk is full or the path is unwritable, every
                // later event would log the same warning and stall the frame that emitted it.
                ioFailed = true;
                Buffer.Clear();
                Debug.LogWarning(
                    $"LocalAnalyticsSink: disabled for this session, could not write to {Directory} - {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes every recorded event. Called by <c>GameManager.DeleteAllPlayerData</c>: the
        /// Settings screen promises to erase the player's data, and events sitting in a local file
        /// are exactly that. Without this the privacy path would be quietly incomplete.
        /// </summary>
        public static void DeleteAll()
        {
            Buffer.Clear();
            sessionFilePath = null;

            try
            {
                if (System.IO.Directory.Exists(Directory))
                {
                    System.IO.Directory.Delete(Directory, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"LocalAnalyticsSink: could not delete {Directory} - {ex.Message}");
            }
        }
    }
}
#endif
