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
    /// Leaderboard screen (Docs/06-Wireframes-UI.md screen 10, Milestone 5 - design-doc.md section
    /// 6.6): board-size filter chips (3x3/6x6, the only two Ranked boards - design-doc.md section
    /// 6.0) over <c>GetRankedLeaderboard</c>'s top N + the caller's own row.
    ///
    /// States (never fabricated data, per Docs/01-Directrices-Proyecto.md): loading while the call is
    /// in flight, a generic error message on failure, an empty-board message when the call succeeded
    /// but nobody has an entry yet, and - when the top list loaded fine but the caller has not
    /// finished placement on this board - a small note under the list instead of a highlighted own
    /// row (design-doc.md section 6.6 elegibilidad).
    /// </summary>
    public class LeaderboardScreenController : IScreenController
    {
        private static readonly int[] BoardSizes = { 3, 6 };

        private readonly Label _titleLabel;
        private readonly VisualElement _filterList;
        private readonly ScrollView _list;
        private readonly Label _statusLabel;
        private readonly Label _placementNoteLabel;

        private readonly Dictionary<int, Label> _filterChips = new();

        private int _selectedBoardSize = BoardSizes[0];

        /// <summary>Guards a stale response from a previous board-size selection (or a previous OnShow) overwriting a newer one's UI state.</summary>
        private int _requestGeneration;

        public LeaderboardScreenController(VisualElement root, ScreenRouter router, GameManager gameManager)
        {
            root.Q<Button>("leaderboard-back-button").text = UiText.Common.BackIcon;
            root.Q<Button>("leaderboard-back-button").clicked += () => router.GoBack(ScreenId.Profile);
            _titleLabel = root.Q<Label>("leaderboard-title-label");

            _filterList = root.Q<VisualElement>("leaderboard-filter-list");
            _list = root.Q<ScrollView>("leaderboard-list");
            _statusLabel = root.Q<Label>("leaderboard-status-label");
            _placementNoteLabel = root.Q<Label>("leaderboard-placement-note-label");

            BuildFilterChips();
            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
            _titleLabel.text = UiText.Leaderboard.Title;
            _placementNoteLabel.text = UiText.Leaderboard.PlacementRequiredMessage;

            foreach (var pair in _filterChips)
            {
                pair.Value.text = string.Format(CultureInfo.InvariantCulture, UiText.BoardSelect.BoardSizeFormat, pair.Key);
            }
        }

        public void OnShow()
        {
            RefreshList();
        }

        public void OnHide()
        {
        }

        public void RefreshLocalizedText()
        {
            ApplyLocalizedText();
            // Status/row text (loading/error/empty/MMR values) is re-derived on the next RefreshList
            // pass rather than reformatted in place - same tradeoff MatchHistoryScreenController makes
            // (RefreshList there is a cheap local rebuild; here it is a network call, acceptable since
            // a language change while this screen happens to be open is rare).
            RefreshList();
        }

        private void BuildFilterChips()
        {
            foreach (var boardSize in BoardSizes)
            {
                var chip = new Label(string.Format(CultureInfo.InvariantCulture, UiText.BoardSelect.BoardSizeFormat, boardSize));
                chip.AddToClassList("chip");
                chip.RegisterCallback<ClickEvent>(_ => SelectBoardSize(boardSize));

                _filterList.Add(chip);
                _filterChips[boardSize] = chip;
            }

            // Selection only - never RefreshList here. BuildFilterChips runs from the constructor,
            // which ScreenRouter.Initialize calls for every screen at app boot (AppBootstrap.Start),
            // long before UGS sign-in completes: firing the network call here hit CloudCode with no
            // session and logged a NullReferenceException for a screen the player had not even
            // opened. The first (and every later) load happens in OnShow instead.
            SetSelectedBoardSize(BoardSizes[0]);
        }

        /// <summary>Updates the selected size and its chip visuals without triggering a fetch.</summary>
        private void SetSelectedBoardSize(int boardSize)
        {
            _selectedBoardSize = boardSize;

            foreach (var pair in _filterChips)
            {
                pair.Value.EnableInClassList("chip--selected", pair.Key == boardSize);
            }
        }

        /// <summary>Player tapped a filter chip: change selection and reload.</summary>
        private void SelectBoardSize(int boardSize)
        {
            SetSelectedBoardSize(boardSize);
            RefreshList();
        }

        private void RefreshList()
        {
            int generation = ++_requestGeneration;
            int boardSize = _selectedBoardSize;

            _list.Clear();
            _placementNoteLabel.style.display = DisplayStyle.None;

            // The leaderboard is server-authoritative (Docs/01-Directrices-Proyecto.md) - with no UGS
            // session there is nothing to read, and calling anyway surfaces a raw NullReferenceException
            // from the CloudCode SDK. Degrade to the same generic error state as any other failure
            // instead (same posture as GameConfigService/UiText: never break, never expose internals).
            if (UgsInitializer.Status != UgsInitStatus.Ready)
            {
                ShowStatus(UiText.Leaderboard.UnavailableMessage);
                return;
            }

            ShowStatus(UiText.Leaderboard.LoadingMessage);

            _ = LoadAsync(boardSize, generation);
        }

        private async Task LoadAsync(int boardSize, int generation)
        {
            RankedLeaderboardResponse response = null;
            try
            {
                response = await RankedQueryService.GetLeaderboardAsync(boardSize);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"LeaderboardScreenController: GetRankedLeaderboard failed - {ex.Message}");
            }

            if (generation != _requestGeneration)
            {
                // A newer selection/refresh already owns the screen - discard this stale response.
                return;
            }

            _list.Clear();

            if (response == null)
            {
                ShowStatus(UiText.Leaderboard.UnavailableMessage);
                return;
            }

            bool hasTop = response.Top != null && response.Top.Count > 0;
            if (!hasTop && !response.HasOwnEntry)
            {
                ShowStatus(UiText.Leaderboard.EmptyMessage);
                return;
            }

            HideStatus();

            bool ownRowShown = false;
            if (hasTop)
            {
                foreach (var entry in response.Top)
                {
                    bool isSelf = response.HasOwnEntry && entry.PlayerId == response.OwnEntry.PlayerId;
                    ownRowShown |= isSelf;
                    _list.Add(BuildRow(entry, isSelf));
                }
            }

            if (response.HasOwnEntry && !ownRowShown)
            {
                _list.Add(BuildSeparator());
                _list.Add(BuildRow(response.OwnEntry, isSelf: true));
            }
            else if (!response.HasOwnEntry)
            {
                _placementNoteLabel.style.display = DisplayStyle.Flex;
            }
        }

        private static VisualElement BuildRow(RankedLeaderboardEntryDto entry, bool isSelf)
        {
            var row = new VisualElement();
            row.AddToClassList("leaderboard-row");
            row.EnableInClassList("leaderboard-row--self", isSelf);

            var rankLabel = new Label(string.Format(CultureInfo.InvariantCulture, UiText.Leaderboard.RankFormat, entry.Rank));
            rankLabel.AddToClassList("leaderboard-rank-label");
            row.Add(rankLabel);

            var nameLabel = new Label(string.IsNullOrEmpty(entry.PlayerName) ? UiText.Common.PlayerNamePlaceholder : entry.PlayerName);
            nameLabel.AddToClassList("leaderboard-name-label");
            row.Add(nameLabel);

            var mmrLabel = new Label(entry.Mmr.ToString(CultureInfo.InvariantCulture));
            mmrLabel.AddToClassList("leaderboard-mmr-label");
            row.Add(mmrLabel);

            return row;
        }

        private static VisualElement BuildSeparator()
        {
            var separator = new Label(UiText.Leaderboard.OwnEntrySeparator);
            separator.AddToClassList("leaderboard-separator-label");
            return separator;
        }

        private void ShowStatus(string message)
        {
            _statusLabel.text = message;
            _statusLabel.style.display = DisplayStyle.Flex;
        }

        private void HideStatus()
        {
            _statusLabel.style.display = DisplayStyle.None;
        }
    }
}
