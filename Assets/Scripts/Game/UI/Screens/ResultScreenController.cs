using System.Globalization;
using TTTXO.Core;
using TTTXO.Game.Bootstrap;
using TTTXO.Game.Services;
using UnityEngine.UIElements;

namespace TTTXO.Game.UI.Screens
{
    /// <summary>
    /// Result screen: shows the outcome and the soft currency earned, then offers Rematch or Home.
    /// Milestone 4: <see cref="ConfigureOnline"/> handles an online match's result - Rematch is
    /// hidden for online (no rematch flow in this milestone; only Home) and an extra note appears
    /// if the match ended by the risk #4 abandonment resolution instead of an actual win/loss.
    ///
    /// Milestone 5 (wireframe 7's "Card Ranked (solo ranked): 'MMR 1240 ▲ +18'"): when the just-ended
    /// call also settled a Ranked unit, <see cref="ConfigureOnline"/> receives the server's
    /// <see cref="RankedMmrResultDto"/> verbatim (never computed here - Docs/01-Directrices-Proyecto.md
    /// #Seguridad / anti-cheat) and the extra card is shown; absent for Quickmatch and for a
    /// non-terminal game of a 3x3 Ranked series (game 1 never carries a result).
    /// </summary>
    public class ResultScreenController : IScreenController
    {
        private readonly ScreenRouter _router;
        private readonly GameManager _gameManager;
        private readonly Label _statusLabel;
        private readonly Label _rewardTitleLabel;
        private readonly Label _rewardLabel;
        private readonly VisualElement _rankedCard;
        private readonly Label _rankedCardTitleLabel;
        private readonly Label _rankedMmrLabel;
        private readonly Button _rematchButton;
        private readonly Button _homeButton;

        /// <summary>The screen panel itself, so a full-screen effect has something to cover.</summary>
        private readonly VisualElement _root;

        private MatchStatus _status;
        private int _reward;
        private bool _isSinglePlayer;
        private CellOwner _humanSymbol;
        private CellOwner _player1Symbol;

        private bool _isOnline;
        private bool _endedByAbandonment;
        private RankedMmrResultDto _rankedMmrResult;

        public ResultScreenController(VisualElement root, ScreenRouter router, GameManager gameManager)
        {
            _root = root;
            _router = router;
            _gameManager = gameManager;

            _statusLabel = root.Q<Label>("result-status-label");
            _rewardTitleLabel = root.Q<Label>("result-reward-title-label");
            _rewardLabel = root.Q<Label>("result-reward-label");

            _rankedCard = root.Q<VisualElement>("result-ranked-card");
            _rankedCardTitleLabel = root.Q<Label>("result-ranked-card-title-label");
            _rankedMmrLabel = root.Q<Label>("result-ranked-mmr-label");

            _rematchButton = root.Q<Button>("result-rematch-button");
            _rematchButton.clicked += OnRematchClicked;

            _homeButton = root.Q<Button>("result-home-button");
            _homeButton.clicked += () => _router.Show(ScreenId.Home);

            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
            _rewardTitleLabel.text = UiText.Result.RewardCardTitle;
            _rankedCardTitleLabel.text = UiText.Result.RankedCardTitle;
            _rematchButton.text = UiText.Result.RematchButton;
            _homeButton.text = UiText.Result.HomeButton;

            // _statusLabel/_rewardLabel/_rankedMmrLabel depend on the just-finished match's outcome
            // (see BuildStatusText/OnShow) - refreshed the next time this screen is shown, same as any
            // other per-match dynamic label; recomputing them here would be redundant while hidden and
            // is unreachable while a match is in progress on the Game screen underneath.
        }

        public void RefreshLocalizedText()
        {
            ApplyLocalizedText();
        }

        /// <summary>Called by <see cref="GameScreenController"/> right before navigating here.</summary>
        public void Configure(MatchStatus status, int reward, bool isSinglePlayer, CellOwner humanSymbol, CellOwner player1Symbol)
        {
            _status = status;
            _reward = reward;
            _isSinglePlayer = isSinglePlayer;
            _humanSymbol = humanSymbol;
            _player1Symbol = player1Symbol;
            _isOnline = false;
            _endedByAbandonment = false;
            _rankedMmrResult = null;
        }

        /// <summary>
        /// Called by <see cref="GameScreenController"/> for an online match (Milestone 4).
        /// <paramref name="rankedMmrResult"/> (Milestone 5) is non-null only when this call also
        /// settled a Ranked unit - always null for Quickmatch.
        /// </summary>
        public void ConfigureOnline(MatchStatus status, int reward, CellOwner localPlayerSymbol, bool endedByAbandonment, RankedMmrResultDto rankedMmrResult = null)
        {
            _status = status;
            _reward = reward;
            _isSinglePlayer = true; // reuses the "You/Rival" perspective wording, not the AI-vs-human meaning.
            _humanSymbol = localPlayerSymbol;
            _player1Symbol = localPlayerSymbol;
            _isOnline = true;
            _endedByAbandonment = endedByAbandonment;
            _rankedMmrResult = rankedMmrResult;
        }

        public void OnShow()
        {
            _statusLabel.text = BuildStatusText();
            _rewardLabel.text = string.Format(CultureInfo.InvariantCulture, UiText.Result.RewardFormat, _reward);
            _rematchButton.style.display = _isOnline ? DisplayStyle.None : DisplayStyle.Flex;

            _rankedCard.style.display = _rankedMmrResult != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (_rankedMmrResult != null)
            {
                _rankedMmrLabel.text = BuildRankedMmrText(_rankedMmrResult);
                _rankedMmrLabel.EnableInClassList("ranked-mmr-value--up", _rankedMmrResult.Delta > 0);
                _rankedMmrLabel.EnableInClassList("ranked-mmr-value--down", _rankedMmrResult.Delta < 0);
            }

            // Only a win. A draw or a loss gets nothing - celebrating either would read as the game not
            // knowing what just happened. In local two-player there is no "you", so it fires for whoever
            // won: the screen is being read by both players at once.
            if (DidLocalPlayerWin())
            {
                BoardEffects.PlayVictoryConfetti(_root);
            }
        }

        /// <summary>True when the outcome is a win for the player reading the screen - the human in 1P/online, or either player in local two-player.</summary>
        private bool DidLocalPlayerWin()
        {
            if (_status != MatchStatus.XWon && _status != MatchStatus.OWon)
            {
                return false;
            }

            if (!_isSinglePlayer && !_isOnline)
            {
                return true;
            }

            return _status == (_humanSymbol == CellOwner.X ? MatchStatus.XWon : MatchStatus.OWon);
        }

        /// <summary>"MMR {value} {arrow}{signed delta}" (wireframe 7: "MMR 1240 ▲ +18") - arrow/sign are glyphs/numbers, not language content (see UiText.Result.RankedMmrCardFormat).</summary>
        private static string BuildRankedMmrText(RankedMmrResultDto mmr)
        {
            string arrow = mmr.Delta > 0 ? "▲" : mmr.Delta < 0 ? "▼" : "•";
            string signedDelta = mmr.Delta > 0
                ? "+" + mmr.Delta.ToString(CultureInfo.InvariantCulture)
                : mmr.Delta.ToString(CultureInfo.InvariantCulture);

            return string.Format(CultureInfo.InvariantCulture, UiText.Result.RankedMmrCardFormat, mmr.MmrAfter, arrow, signedDelta);
        }

        public void OnHide()
        {
        }

        private string BuildStatusText()
        {
            if (_status == MatchStatus.Draw)
            {
                string drawText = UiText.Result.Draw;
                return _isOnline && _endedByAbandonment ? AppendAbandonmentNote(drawText) : drawText;
            }

            if (_isSinglePlayer)
            {
                bool humanWon = _status == (_humanSymbol == CellOwner.X ? MatchStatus.XWon : MatchStatus.OWon);
                string text = humanWon ? UiText.Result.YouWin : UiText.Result.YouLose;
                return _isOnline && _endedByAbandonment ? AppendAbandonmentNote(text) : text;
            }

            bool player1Won = _status == (_player1Symbol == CellOwner.X ? MatchStatus.XWon : MatchStatus.OWon);
            return player1Won ? UiText.Result.Player1Wins : UiText.Result.Player2Wins;
        }

        private static string AppendAbandonmentNote(string statusText)
        {
            return statusText + "\n" + UiText.Result.OpponentLeftNote;
        }

        private void OnRematchClicked()
        {
            // Same (mode, board, difficulty) key as before, so GameManager keeps alternating who
            // starts as X instead of resetting the sequence (see GameManager.ConfigureMatch).
            _gameManager.ConfigureMatch(_gameManager.Mode, _gameManager.SelectedBoard, _gameManager.SelectedDifficulty);
            _router.Show(ScreenId.Game);
        }
    }
}
