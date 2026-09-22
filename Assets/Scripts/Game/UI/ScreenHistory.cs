using System.Collections.Generic;

namespace TTTXO.Game.UI
{
    /// <summary>
    /// Navigation history behind the generic "back" buttons on sub-screens.
    ///
    /// Extracted from <see cref="ScreenRouter"/> so the rules can be exercised on their own: the
    /// router builds all twelve screens against a live <c>UIDocument</c> inside
    /// <see cref="ScreenRouter.Initialize"/>, which put this logic out of reach of any test even
    /// though it is the part most likely to strand a player. Nothing here touches UnityEngine.
    ///
    /// The four rules, all of which existed before this class and none of which changed:
    /// <list type="bullet">
    /// <item>forward navigation pushes the screen being left, so a sub-screen reachable from more
    /// than one place returns wherever the player actually came from;</item>
    /// <item>navigating to the screen already showing pushes nothing, so a redundant Show cannot
    /// make "back" a no-op that appears broken;</item>
    /// <item>going back pops instead of pushing - a single "previous screen" field would make
    /// Profile -&gt; Store -&gt; back loop between the two forever;</item>
    /// <item>arriving at Home clears everything, because Home is the hub and has no back button, so
    /// match-flow cycles never accumulate.</item>
    /// </list>
    /// </summary>
    public sealed class ScreenHistory
    {
        private readonly Stack<ScreenId> _entries = new();

        /// <summary>How many screens "back" can still walk through before falling back.</summary>
        public int Depth => _entries.Count;

        /// <summary>
        /// Records one completed navigation.
        /// </summary>
        /// <param name="leaving">
        /// The screen being left, or <c>null</c> when there is none to record - the first
        /// navigation of the session, or a current screen whose root was never registered.
        /// </param>
        /// <param name="target">The screen being shown.</param>
        /// <param name="isBack">
        /// True when this navigation came from <see cref="ResolveBack"/>, whose entry was already
        /// popped and must not be pushed straight back on.
        /// </param>
        public void RecordNavigation(ScreenId? leaving, ScreenId target, bool isBack)
        {
            if (leaving.HasValue && !isBack && target != leaving.Value)
            {
                _entries.Push(leaving.Value);
            }

            if (target == ScreenId.Home)
            {
                _entries.Clear();
            }
        }

        /// <summary>
        /// Consumes and returns where "back" should go: the last screen left, or
        /// <paramref name="fallback"/> when there is no history to walk.
        /// </summary>
        public ScreenId ResolveBack(ScreenId fallback)
        {
            return _entries.Count > 0 ? _entries.Pop() : fallback;
        }

        /// <summary>Drops the whole history.</summary>
        public void Clear()
        {
            _entries.Clear();
        }
    }
}
