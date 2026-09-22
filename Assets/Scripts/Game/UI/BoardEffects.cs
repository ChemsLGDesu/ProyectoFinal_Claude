using UnityEngine.UIElements;

namespace TTTXO.Game.UI
{
    /// <summary>
    /// Transient visual effects on the board and result screens.
    ///
    /// Each one is a throwaway overlay element added over its target, animated by USS transitions and then
    /// removed. Overlays rather than styling the target itself, because a cell already spends its
    /// background-image on the piece skin and an element only has one.
    ///
    /// Purely decorative: nothing here reports a result, gates input or is awaited. An effect that fails to
    /// appear costs the player nothing, which is why none of this is on the path that decides a match.
    /// </summary>
    public static class BoardEffects
    {
        /// <summary>Matches the transition-duration on the fx classes in main.uss. Overshoots slightly so the element is never pulled out from under a still-running transition.</summary>
        private const long PlaceBurstLifetimeMs = 420;

        private const long ConfettiLifetimeMs = 1600;

        /// <summary>Burst played on the cell a piece just landed in.</summary>
        public static void PlayPlaceBurst(VisualElement cell)
        {
            if (cell == null)
            {
                return;
            }

            Play(cell, "fx-place", "fx-place--active", PlaceBurstLifetimeMs);
        }

        /// <summary>Confetti over the whole result screen. Only for a win - a draw or a loss gets nothing.</summary>
        public static void PlayVictoryConfetti(VisualElement screen)
        {
            if (screen == null)
            {
                return;
            }

            Play(screen, "fx-confetti", "fx-confetti--active", ConfettiLifetimeMs);
        }

        /// <summary>
        /// Glow laid over a cell of the winning line. Unlike the others this one stays: the highlight has
        /// to remain readable while the result is shown, so it is cleared by the next
        /// <c>BuildBoard</c> rather than by a timer.
        /// </summary>
        public static void AddWinGlow(VisualElement cell)
        {
            if (cell == null)
            {
                return;
            }

            var overlay = CreateOverlay("fx-winline");
            cell.Add(overlay);
            overlay.schedule.Execute(() => overlay.AddToClassList("fx-winline--active")).ExecuteLater(0);
        }

        private static void Play(VisualElement target, string baseClass, string activeClass, long lifetimeMs)
        {
            var overlay = CreateOverlay(baseClass);
            target.Add(overlay);

            // The active class has to land on a LATER frame than the element itself, or the two style
            // values resolve in the same pass and the transition has nothing to interpolate between.
            overlay.schedule.Execute(() => overlay.AddToClassList(activeClass)).ExecuteLater(0);
            overlay.schedule.Execute(() => overlay.RemoveFromHierarchy()).ExecuteLater(lifetimeMs);
        }

        private static VisualElement CreateOverlay(string baseClass)
        {
            var overlay = new VisualElement();
            overlay.AddToClassList(baseClass);

            // Never intercept a tap: the cell underneath still has to be clickable, and the confetti
            // covers the entire result screen including its buttons.
            overlay.pickingMode = PickingMode.Ignore;
            return overlay;
        }
    }
}
