using System;
using System.Collections.Generic;
using System.Globalization;
using TTTXO.Core;
using TTTXO.Game.Bootstrap;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI.Screens
{
    /// <summary>
    /// Match History screen (Docs/06-Wireframes-UI.md screen 9, local-only stub): All/Single/Local
    /// filter chips over the local history recorded by <see cref="GameManager.RecordMatchHistory"/>.
    /// Online/Ranked filters are omitted - those modes do not exist yet in Milestone 1.
    /// </summary>
    public class MatchHistoryScreenController : IScreenController
    {
        private enum Filter
        {
            All,
            SinglePlayer,
            LocalMultiplayer
        }

        private static readonly Filter[] FilterOrder = { Filter.All, Filter.SinglePlayer, Filter.LocalMultiplayer };

        private readonly ScreenRouter _router;
        private readonly GameManager _gameManager;
        private readonly Label _titleLabel;
        private readonly VisualElement _filterList;
        private readonly ScrollView _list;
        private readonly Label _emptyLabel;

        private readonly Dictionary<Filter, Label> _filterChips = new();
        private Filter _selectedFilter = Filter.All;

        public MatchHistoryScreenController(VisualElement root, ScreenRouter router, GameManager gameManager)
        {
            _router = router;
            _gameManager = gameManager;

            root.Q<Button>("match-history-back-button").text = UiText.Common.BackIcon;
            root.Q<Button>("match-history-back-button").clicked += () => _router.GoBack(ScreenId.Profile);
            _titleLabel = root.Q<Label>("match-history-title-label");

            _filterList = root.Q<VisualElement>("match-history-filter-list");
            _list = root.Q<ScrollView>("match-history-list");
            _emptyLabel = root.Q<Label>("match-history-empty-label");

            BuildFilterChips();
            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
            _titleLabel.text = UiText.MatchHistory.Title;
            _emptyLabel.text = UiText.MatchHistory.EmptyState;

            foreach (var pair in _filterChips)
            {
                pair.Value.text = FilterLabelFor(pair.Key);
            }
        }

        public void OnShow()
        {
            RefreshList();
        }

        public void OnHide()
        {
        }

        /// <summary>
        /// Refreshes static copy plus the currently built list items (their text is otherwise only
        /// re-derived from <see cref="UiText"/> on the next <see cref="RefreshList"/> call - see
        /// IScreenController.RefreshLocalizedText) so a language change is visible immediately even
        /// while this screen is the one on screen.
        /// </summary>
        public void RefreshLocalizedText()
        {
            ApplyLocalizedText();
            RefreshList();
        }

        private void BuildFilterChips()
        {
            foreach (var filter in FilterOrder)
            {
                var chip = new Label(FilterLabelFor(filter));
                chip.AddToClassList("chip");
                chip.RegisterCallback<ClickEvent>(_ => SelectFilter(filter));

                _filterList.Add(chip);
                _filterChips[filter] = chip;
            }

            SelectFilter(Filter.All);
        }

        private static string FilterLabelFor(Filter filter)
        {
            return filter switch
            {
                Filter.All => UiText.MatchHistory.FilterAll,
                Filter.SinglePlayer => UiText.MatchHistory.FilterSingle,
                Filter.LocalMultiplayer => UiText.MatchHistory.FilterLocal,
                _ => filter.ToString(),
            };
        }

        private void SelectFilter(Filter filter)
        {
            _selectedFilter = filter;

            foreach (var pair in _filterChips)
            {
                pair.Value.EnableInClassList("chip--selected", pair.Key == filter);
            }

            RefreshList();
        }

        private void RefreshList()
        {
            _list.Clear();

            bool hasAny = false;

            foreach (var entry in _gameManager.MatchHistory)
            {
                if (!MatchesFilter(entry))
                {
                    continue;
                }

                hasAny = true;
                _list.Add(BuildHistoryItem(entry));
            }

            _emptyLabel.style.display = hasAny ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private bool MatchesFilter(MatchHistoryEntry entry)
        {
            return _selectedFilter switch
            {
                Filter.All => true,
                Filter.SinglePlayer => entry.Mode == GameMode.SinglePlayer,
                Filter.LocalMultiplayer => entry.Mode == GameMode.LocalMultiplayer,
                _ => true,
            };
        }

        private static VisualElement BuildHistoryItem(MatchHistoryEntry entry)
        {
            var item = new VisualElement();
            item.AddToClassList("match-history-item");

            var main = new VisualElement();
            main.AddToClassList("match-history-item-main");

            var resultLabel = new Label(ResultLabelFor(entry));
            resultLabel.AddToClassList("match-history-result-label");
            resultLabel.AddToClassList(ResultModifierClassFor(entry.Outcome));
            main.Add(resultLabel);

            var modeLabel = entry.Mode == GameMode.SinglePlayer ? UiText.ModeSelect.SinglePlayerTitle : UiText.ModeSelect.LocalMultiplayerTitle;
            var metaLabel = new Label(string.Format(
                CultureInfo.InvariantCulture, UiText.MatchHistory.BoardSizeAndModeFormat, entry.BoardSize, modeLabel, FormatDuration(entry.DurationSeconds)));
            metaLabel.AddToClassList("match-history-meta-label");
            main.Add(metaLabel);

            item.Add(main);

            var timeLabel = new Label(FormatTimeAgo(entry.TimestampUnixSeconds));
            timeLabel.AddToClassList("match-history-time-label");
            item.Add(timeLabel);

            return item;
        }

        private static string ResultLabelFor(MatchHistoryEntry entry)
        {
            if (entry.Outcome == MatchOutcome.Draw)
            {
                return UiText.MatchHistory.ResultDraw;
            }

            if (entry.Mode == GameMode.SinglePlayer)
            {
                return entry.Outcome == MatchOutcome.Win ? UiText.MatchHistory.ResultWin : UiText.MatchHistory.ResultLoss;
            }

            // Local Multiplayer is recorded from Player 1's perspective (see MatchOutcome docs).
            return entry.Outcome == MatchOutcome.Win ? UiText.MatchHistory.LocalPlayer1Win : UiText.MatchHistory.LocalPlayer2Win;
        }

        private static string ResultModifierClassFor(MatchOutcome outcome)
        {
            return outcome switch
            {
                MatchOutcome.Win => "match-history-result-label--win",
                MatchOutcome.Loss => "match-history-result-label--loss",
                _ => "match-history-result-label--draw",
            };
        }

        private static string FormatDuration(int durationSeconds)
        {
            if (durationSeconds < 60)
            {
                return string.Format(CultureInfo.InvariantCulture, UiText.MatchHistory.DurationSecondsFormat, durationSeconds);
            }

            int minutes = durationSeconds / 60;
            int seconds = durationSeconds % 60;
            return string.Format(CultureInfo.InvariantCulture, UiText.MatchHistory.DurationMinutesFormat, minutes, seconds);
        }

        private static string FormatTimeAgo(long timestampUnixSeconds)
        {
            var then = DateTimeOffset.FromUnixTimeSeconds(timestampUnixSeconds);
            var elapsed = DateTimeOffset.UtcNow - then;

            if (elapsed.TotalMinutes < 1)
            {
                return UiText.MatchHistory.TimeAgoJustNow;
            }

            if (elapsed.TotalHours < 1)
            {
                return string.Format(CultureInfo.InvariantCulture, UiText.MatchHistory.TimeAgoMinutesFormat, (int)elapsed.TotalMinutes);
            }

            if (elapsed.TotalDays < 1)
            {
                return string.Format(CultureInfo.InvariantCulture, UiText.MatchHistory.TimeAgoHoursFormat, (int)elapsed.TotalHours);
            }

            return string.Format(CultureInfo.InvariantCulture, UiText.MatchHistory.TimeAgoDaysFormat, (int)elapsed.TotalDays);
        }
    }
}
