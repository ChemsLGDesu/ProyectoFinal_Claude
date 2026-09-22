using System.Collections.Generic;
using UnityEngine;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// Saves and restores every PlayerPrefs key the Game layer writes.
    ///
    /// EditMode tests run against the developer's real PlayerPrefs store - there is no per-test
    /// sandbox in Unity - so without this a test run would silently overwrite a local wallet
    /// balance, the equipped cosmetics or the volume sliders and leave them that way. Capture in
    /// SetUp, restore in TearDown, and the run is invisible from outside.
    ///
    /// The key list is explicit rather than discovered because PlayerPrefs cannot enumerate keys,
    /// and each entry carries its type because reading a key with the wrong getter does not
    /// round-trip. A key added to the Game layer without being listed here is simply not protected:
    /// keep this in sync with the const declarations in <see cref="Bootstrap.GameManager"/>,
    /// <see cref="Services.CosmeticSelection"/> and <see cref="Services.RankedProfileCache"/>.
    /// </summary>
    internal sealed class PlayerPrefsSandbox
    {
        private enum PrefKind
        {
            Int,
            Float,
            String,
        }

        private readonly struct Entry
        {
            public Entry(string key, PrefKind kind)
            {
                Key = key;
                Kind = kind;
            }

            public string Key { get; }

            public PrefKind Kind { get; }
        }

        /// <summary>
        /// Board sizes whose per-size Ranked MMR keys are protected. Mirrors
        /// <see cref="TTTXO.Core.BoardConfig.All"/>; a size outside this list is not restored.
        /// </summary>
        private static readonly int[] RankedBoardSizes = { 3, 6, 9, 11 };

        private static readonly Entry[] KnownKeys = BuildKnownKeys();

        private readonly Dictionary<string, object> _captured = new();

        private PlayerPrefsSandbox()
        {
        }

        /// <summary>Captures the current values, then deletes them so the test starts from a known-empty store.</summary>
        public static PlayerPrefsSandbox CaptureAndClear()
        {
            var sandbox = new PlayerPrefsSandbox();

            foreach (var entry in KnownKeys)
            {
                if (!PlayerPrefs.HasKey(entry.Key))
                {
                    continue;
                }

                sandbox._captured[entry.Key] = entry.Kind switch
                {
                    PrefKind.Int => (object)PlayerPrefs.GetInt(entry.Key),
                    PrefKind.Float => (object)PlayerPrefs.GetFloat(entry.Key),
                    _ => (object)PlayerPrefs.GetString(entry.Key),
                };
            }

            sandbox.Clear();
            return sandbox;
        }

        /// <summary>Deletes every known key without touching the captured snapshot.</summary>
        public void Clear()
        {
            foreach (var entry in KnownKeys)
            {
                PlayerPrefs.DeleteKey(entry.Key);
            }

            PlayerPrefs.Save();
        }

        /// <summary>Puts the captured values back, deleting anything the test added.</summary>
        public void Restore()
        {
            Clear();

            foreach (var pair in _captured)
            {
                switch (pair.Value)
                {
                    case int intValue:
                        PlayerPrefs.SetInt(pair.Key, intValue);
                        break;
                    case float floatValue:
                        PlayerPrefs.SetFloat(pair.Key, floatValue);
                        break;
                    default:
                        PlayerPrefs.SetString(pair.Key, (string)pair.Value);
                        break;
                }
            }

            PlayerPrefs.Save();
        }

        private static Entry[] BuildKnownKeys()
        {
            var keys = new List<Entry>
            {
                // GameManager - wallet and daily cap.
                new("TTTXO.SoftCurrencyBalance", PrefKind.Int),
                new("TTTXO.DailyEarnedSoftCurrency", PrefKind.Int),
                new("TTTXO.DailyResetDate", PrefKind.String),

                // GameManager - settings sliders and local mirror bookkeeping.
                new("TTTXO.SoundVolume", PrefKind.Float),
                new("TTTXO.MusicVolume", PrefKind.Float),
                new("TTTXO.MatchHistory", PrefKind.String),
                new("TTTXO.LocalStateUpdatedAt", PrefKind.String),

                // CosmeticSelection - equipped cosmetics.
                new("TTTXO.Cosmetic.Avatar", PrefKind.String),
                new("TTTXO.Cosmetic.Frame", PrefKind.String),
                new("TTTXO.Cosmetic.Banner", PrefKind.String),
                new("TTTXO.Cosmetic.PieceSkin", PrefKind.String),
                new("TTTXO.Cosmetic.BoardSkin", PrefKind.String),
            };

            foreach (int boardSize in RankedBoardSizes)
            {
                keys.Add(new Entry("TTTXO.RankedLastKnownMmr." + boardSize, PrefKind.Int));
            }

            return keys.ToArray();
        }
    }
}
