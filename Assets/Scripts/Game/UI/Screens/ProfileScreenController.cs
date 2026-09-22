using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using TTTXO.Game.Bootstrap;
using TTTXO.Game.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI.Screens
{
    /// <summary>
    /// Profile screen (Docs/06-Wireframes-UI.md screen 8): banner/avatar placeholder, local player
    /// name (no <c>#1234</c> Authentication suffix yet), win rate/streak computed from the local
    /// Match History, a Ranked section (Milestone 5, see <see cref="RefreshRankedSectionAsync"/>), a
    /// Leaderboard nav entry, and "Delete my data" behind an explicit confirmation.
    ///
    /// Milestone 5 (design-doc.md section 6, wireframe 8's stats row: "win rate % / racha 🔥 / MMR"):
    /// the Ranked section reads the CALLER's own authoritative MMR/tier/placement via
    /// <c>GetRankedProfile</c> (<see cref="RankedQueryService.GetProfileAsync"/>) - never a
    /// locally-cached guess (see <see cref="RankedProfileCache"/>'s remarks on why it is
    /// matchmaking-only now) - on every <see cref="OnShow"/>. A failed read never throws or shows
    /// fabricated data: the row just falls back to a neutral placeholder until the next successful
    /// refresh (Docs/01-Directrices-Proyecto.md).
    /// </summary>
    public class ProfileScreenController : IScreenController
    {
        /// <summary>Non-language placeholder (glyph, like Common.BackIcon/Chevron) shown while the Ranked section has no data yet (loading, or a failed read) - never a fabricated MMR/tier.</summary>
        private const string RankedValuePlaceholder = "—";

        private readonly ScreenRouter _router;
        private readonly GameManager _gameManager;
        private readonly VisualElement _root;
        private readonly Label _winRateValueLabel;
        private readonly Label _streakValueLabel;
        private readonly Label _rankedBoard3ValueLabel;
        private readonly Label _rankedBoard6ValueLabel;
        private readonly VisualElement _rankedBoard3Badge;
        private readonly VisualElement _rankedBoard6Badge;
        private readonly EquippedAvatarView _avatarView;
        private readonly VisualElement _bannerElement;
        private readonly VisualElement _equipPanel;

        /// <summary>Every swatch built for the equip panel, so the selected ring can be moved without rebuilding the rows.</summary>
        private readonly List<(VisualElement Element, string Id, Func<string> CurrentId)> _equipOptions = new();

        /// <summary>Last successful <c>GetRankedProfile</c> response, re-applied (reformatted only, never re-fetched) by <see cref="RefreshLocalizedText"/> so a language change updates the tier name/placement text without a network round trip.</summary>
        private RankedProfileResponse _lastRankedProfile;

        /// <summary>Guards against a stale <see cref="RefreshRankedSectionAsync"/> response landing after the screen was hidden/re-shown (e.g. quick back-and-forth navigation).</summary>
        private int _rankedRequestGeneration;

        public ProfileScreenController(VisualElement root, ScreenRouter router, GameManager gameManager)
        {
            _root = root;
            _router = router;
            _gameManager = gameManager;

            root.Q<Button>("profile-back-button").text = UiText.Common.BackIcon;
            root.Q<Button>("profile-back-button").clicked += () => _router.GoBack(ScreenId.Home);

            // Docs/05-UI-Pantallas-TicTacToe.md #131: equipped icon/frame are "editable desde acá, con
            // acceso directo a Tienda para cambiar". The link used to jump straight to the Store, which
            // skipped the editing half - it now reveals the equip panel, and the Store link inside that
            // panel is what takes you shopping for more.
            _avatarView = new EquippedAvatarView(
                root.Q<VisualElement>("profile-avatar"),
                root.Q<VisualElement>("profile-avatar-frame"));
            _bannerElement = root.Q<VisualElement>("profile-banner");
            _equipPanel = root.Q<VisualElement>("profile-equip-panel");

            root.Q<VisualElement>("profile-edit-look-link").RegisterCallback<ClickEvent>(_ => ToggleEquipPanel());
            root.Q<VisualElement>("profile-store-link").RegisterCallback<ClickEvent>(_ => _router.Show(ScreenId.Store));

            BuildEquipOptions(
                root.Q<VisualElement>("profile-avatar-options"),
                CosmeticCatalog.Avatars,
                CosmeticSelection.SetAvatar,
                () => CosmeticSelection.AvatarId);

            BuildEquipOptions(
                root.Q<VisualElement>("profile-frame-options"),
                CosmeticCatalog.Frames,
                CosmeticSelection.SetFrame,
                () => CosmeticSelection.FrameId);

            BuildEquipOptions(
                root.Q<VisualElement>("profile-banner-options"),
                CosmeticCatalog.Banners,
                CosmeticSelection.SetBanner,
                () => CosmeticSelection.BannerId,
                "equip-option--banner");

            BuildEquipOptions(
                root.Q<VisualElement>("profile-piece-skin-options"),
                CosmeticCatalog.PieceSkins,
                CosmeticSelection.SetPieceSkin,
                () => CosmeticSelection.PieceSkinId,
                "equip-option--piece");

            BuildEquipOptions(
                root.Q<VisualElement>("profile-board-skin-options"),
                CosmeticCatalog.BoardSkins,
                CosmeticSelection.SetBoardSkin,
                () => CosmeticSelection.BoardSkinId,
                "equip-option--board");

            CosmeticSelection.Changed += ApplyEquippedCosmetics;
            ApplyEquippedCosmetics();

            _winRateValueLabel = root.Q<Label>("profile-winrate-value-label");
            _streakValueLabel = root.Q<Label>("profile-streak-value-label");
            _rankedBoard3ValueLabel = root.Q<Label>("profile-ranked-board3-value-label");
            _rankedBoard6ValueLabel = root.Q<Label>("profile-ranked-board6-value-label");
            _rankedBoard3Badge = root.Q<VisualElement>("profile-ranked-board3-badge");
            _rankedBoard6Badge = root.Q<VisualElement>("profile-ranked-board6-badge");

            root.Q<Label>("profile-history-chevron-label").text = UiText.Common.Chevron;
            root.Q<VisualElement>("profile-history-button").RegisterCallback<ClickEvent>(_ => _router.Show(ScreenId.MatchHistory));

            root.Q<Label>("profile-leaderboard-chevron-label").text = UiText.Common.Chevron;
            root.Q<VisualElement>("profile-leaderboard-button").RegisterCallback<ClickEvent>(_ => _router.Show(ScreenId.Leaderboard));

            var deleteButton = root.Q<Button>("profile-delete-data-button");
            deleteButton.clicked += OnDeleteDataClicked;

            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
            _root.Q<Label>("profile-title-label").text = UiText.Profile.Title;
            _root.Q<Label>("profile-name-label").text = UiText.Common.PlayerNamePlaceholder;
            _root.Q<Label>("profile-edit-look-label").text = string.Format(
                CultureInfo.InvariantCulture, UiText.Profile.EditLookLinkFormat, UiText.Icons.EditLook, UiText.Common.Chevron);

            // Its own string, and its own icon: reusing the "edit look" one printed the same sentence
            // twice on the same card, once above the panel and once inside it.
            _root.Q<Label>("profile-store-link-label").text = string.Format(
                CultureInfo.InvariantCulture, UiText.Profile.StoreLinkFormat, UiText.Icons.Store, UiText.Common.Chevron);
            _root.Q<Label>("profile-equip-avatars-title").text = UiText.Profile.EquipAvatarsTitle;
            _root.Q<Label>("profile-equip-frames-title").text = UiText.Profile.EquipFramesTitle;
            _root.Q<Label>("profile-equip-banners-title").text = UiText.Profile.EquipBannersTitle;
            _root.Q<Label>("profile-equip-pieces-title").text = UiText.Profile.EquipPieceSkinsTitle;
            _root.Q<Label>("profile-equip-boards-title").text = UiText.Profile.EquipBoardSkinsTitle;

            _root.Q<Label>("profile-winrate-caption-label").text = UiText.Profile.WinRateCaption;
            _root.Q<Label>("profile-streak-caption-label").text = UiText.Profile.StreakCaption;
            _root.Q<Label>("profile-ranked-section-title-label").text = UiText.Profile.RankedSectionTitle;
            _root.Q<Label>("profile-ranked-board3-size-label").text = string.Format(CultureInfo.InvariantCulture, UiText.BoardSelect.BoardSizeFormat, 3);
            _root.Q<Label>("profile-ranked-board6-size-label").text = string.Format(CultureInfo.InvariantCulture, UiText.BoardSelect.BoardSizeFormat, 6);
            _root.Q<Label>("profile-history-label").text = UiText.Profile.MatchHistoryButton;
            _root.Q<Label>("profile-leaderboard-label").text = UiText.Profile.LeaderboardButton;
            _root.Q<Button>("profile-delete-data-button").text = UiText.Profile.DeleteDataButton;
        }

        public void OnShow()
        {
            RefreshStats();
            _ = RefreshRankedSectionAsync();
        }

        public void OnHide()
        {
        }

        public void RefreshLocalizedText()
        {
            ApplyLocalizedText();

            // Re-apply the last fetched Ranked data with the new locale's format strings/tier names -
            // no network round trip, same "reformat cached data" posture MatchHistoryScreenController
            // uses (there it rebuilds from local history; here from the last GetRankedProfile response).
            ApplyRankedBoard(_rankedBoard3ValueLabel, _rankedBoard3Badge, _lastRankedProfile?.BoardFor(3));
            ApplyRankedBoard(_rankedBoard6ValueLabel, _rankedBoard6Badge, _lastRankedProfile?.BoardFor(6));
        }

        private void RefreshStats()
        {
            var history = _gameManager.MatchHistory;

            int wins = 0;
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].Outcome == MatchOutcome.Win)
                {
                    wins++;
                }
            }

            int winRatePercent = history.Count > 0 ? Mathf.RoundToInt(100f * wins / history.Count) : 0;
            _winRateValueLabel.text = string.Format(CultureInfo.InvariantCulture, UiText.Profile.WinRateValueFormat, winRatePercent);

            int streak = 0;
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].Outcome != MatchOutcome.Win)
                {
                    break;
                }

                streak++;
            }

            _streakValueLabel.text = string.Format(CultureInfo.InvariantCulture, UiText.Profile.StreakValueFormat, UiText.Icons.Streak, streak);
        }

        /// <summary>
        /// Milestone 5 (design-doc.md section 6): fetches the caller's own authoritative Ranked
        /// status for both boards via a single <c>GetRankedProfile</c> call and applies it to the two
        /// rows. Never throws past this method (mirrors GameConfigService's own posture) - a failed
        /// call just leaves both rows at <see cref="RankedValuePlaceholder"/> and logs a warning, the
        /// same "degrade gracefully, never fabricate data" rule every other screen in this project
        /// follows.
        /// </summary>
        private async Task RefreshRankedSectionAsync()
        {
            int generation = ++_rankedRequestGeneration;
            _rankedBoard3ValueLabel.text = RankedValuePlaceholder;
            _rankedBoard6ValueLabel.text = RankedValuePlaceholder;

            RankedProfileResponse profile = null;
            try
            {
                profile = await RankedQueryService.GetProfileAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"ProfileScreenController: GetRankedProfile failed - {ex.Message}");
            }

            if (generation != _rankedRequestGeneration)
            {
                // The screen was hidden/re-shown (or another refresh started) before this returned -
                // that later call owns the labels now, this response is stale.
                return;
            }

            _lastRankedProfile = profile;
            ApplyRankedBoard(_rankedBoard3ValueLabel, _rankedBoard3Badge, profile?.BoardFor(3));
            ApplyRankedBoard(_rankedBoard6ValueLabel, _rankedBoard6Badge, profile?.BoardFor(6));
        }

        private void ToggleEquipPanel()
        {
            bool showing = _equipPanel.style.display == DisplayStyle.Flex;
            _equipPanel.style.display = showing ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>Builds one clickable swatch per option, painted with the same USS class that paints the equipped item - so a swatch is literally a preview of what selecting it does.</summary>
        private void BuildEquipOptions(
            VisualElement container,
            IReadOnlyList<CosmeticCatalog.Option> options,
            Action<string> select,
            Func<string> currentId,
            string shapeClass = null)
        {
            container.Clear();

            foreach (var option in options)
            {
                var swatch = new VisualElement();
                swatch.AddToClassList("equip-option");
                if (!string.IsNullOrEmpty(shapeClass))
                {
                    swatch.AddToClassList(shapeClass);
                }

                swatch.AddToClassList(option.PreviewClass);

                string id = option.Id;
                swatch.RegisterCallback<ClickEvent>(_ => select(id));

                container.Add(swatch);
                _equipOptions.Add((swatch, id, currentId));
            }
        }

        /// <summary>
        /// Repaints the equipped avatar and frame and moves the selected ring. Driven by
        /// <see cref="CosmeticSelection.Changed"/> rather than called at the click site, so a change made
        /// anywhere - a click here, a cloud snapshot arriving mid-session - lands the same way.
        /// </summary>
        private void ApplyEquippedCosmetics()
        {
            _avatarView.Apply();
            ApplySelectedClass(_bannerElement, CosmeticCatalog.Banners, CosmeticSelection.BannerId);

            foreach (var (element, id, currentId) in _equipOptions)
            {
                element.EnableInClassList("equip-option--selected", id == currentId());
            }
        }

        private static void ApplySelectedClass(VisualElement element, IReadOnlyList<CosmeticCatalog.Option> options, string id)
        {
            foreach (string styleClass in CosmeticCatalog.AllStyleClasses(options))
            {
                element.RemoveFromClassList(styleClass);
            }

            element.AddToClassList(CosmeticCatalog.StyleClassFor(options, id));
        }

        private static void ApplyRankedBoard(Label label, VisualElement badge, RankedBoardProfileDto board)
        {
            if (board == null)
            {
                label.text = RankedValuePlaceholder;
                ApplyTierBadge(badge, null);
                return;
            }

            label.text = board.IsPlaced
                ? string.Format(CultureInfo.InvariantCulture, UiText.Profile.RankedValueFormat, board.Mmr, TierDisplayName(board.Tier))
                : string.Format(CultureInfo.InvariantCulture, UiText.Profile.RankedPlacementFormat, board.PlacementsPlayed, board.PlacementMatchesRequired);

            // Only a placed board has earned a tier. While in placement the server still reports a tier
            // code, so keying the medal off IsPlaced rather than off the code is what stops the screen
            // showing a rank the player has not finished qualifying for.
            ApplyTierBadge(badge, board.IsPlaced ? board.Tier : null);
        }

        /// <summary>
        /// Swaps the medal art for a tier code, or hides the badge when there is no earned tier. Mirrors
        /// <see cref="TierDisplayName"/>'s mapping of the server's internal codes, including its fallback:
        /// an unknown code reads as bronze rather than leaving a blank hole in the row.
        /// </summary>
        private static void ApplyTierBadge(VisualElement badge, string tierCode)
        {
            foreach (string cssClass in TierBadgeClasses)
            {
                badge.RemoveFromClassList(cssClass);
            }

            if (string.IsNullOrEmpty(tierCode))
            {
                badge.style.display = DisplayStyle.None;
                return;
            }

            badge.AddToClassList(tierCode switch
            {
                "silver" => "tier-badge--silver",
                "gold" => "tier-badge--gold",
                "platinum" => "tier-badge--platinum",
                "diamond" => "tier-badge--diamond",
                _ => "tier-badge--bronze",
            });

            badge.style.display = DisplayStyle.Flex;
        }

        private static readonly string[] TierBadgeClasses =
        {
            "tier-badge--bronze",
            "tier-badge--silver",
            "tier-badge--gold",
            "tier-badge--platinum",
            "tier-badge--diamond",
        };

        /// <summary>Maps the server's English internal tier code (design-doc.md section 6.5: "bronze".."diamond") to its localized display name - the server never sends player-facing text (Docs/01-Directrices-Proyecto.md#Idioma).</summary>
        private static string TierDisplayName(string tierCode)
        {
            return tierCode switch
            {
                "silver" => UiText.Profile.RankedTierSilver,
                "gold" => UiText.Profile.RankedTierGold,
                "platinum" => UiText.Profile.RankedTierPlatinum,
                "diamond" => UiText.Profile.RankedTierDiamond,
                _ => UiText.Profile.RankedTierBronze,
            };
        }

        private void OnDeleteDataClicked()
        {
            _router.ConfirmDialog.Show(
                UiText.Profile.DeleteConfirmTitle,
                UiText.Profile.DeleteConfirmMessage,
                UiText.Profile.DeleteConfirmButton,
                () =>
                {
                    _gameManager.DeleteAllPlayerData();
                    RefreshStats();
                    _router.Show(ScreenId.Home);
                });
        }
    }
}
