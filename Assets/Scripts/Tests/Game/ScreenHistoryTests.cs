using NUnit.Framework;
using TTTXO.Game.UI;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// The back-stack rules, exercised through a stand-in that reproduces
    /// <see cref="ScreenRouter"/>'s exact call sequence minus the UIDocument. The scenarios matter
    /// more than the primitives here: a back button that strands a player or ping-pongs between two
    /// screens is invisible until someone hits it, and the router itself cannot be instantiated in a
    /// test because <c>Initialize()</c> builds all twelve screens against a live document.
    /// </summary>
    [TestFixture]
    public class ScreenHistoryTests
    {
        /// <summary>
        /// Mirrors <see cref="ScreenRouter.Show"/> and <see cref="ScreenRouter.GoBack"/>: the same
        /// order of operations, the same back flag, no visual side effects. If the router's own
        /// sequence ever changes, this has to change with it.
        /// </summary>
        private sealed class Navigator
        {
            private ScreenId? _current;
            private bool _navigatingBack;

            public ScreenHistory History { get; } = new();

            public ScreenId? Current => _current;

            public void Show(ScreenId target)
            {
                History.RecordNavigation(_current, target, _navigatingBack);
                _current = target;
            }

            public void GoBack(ScreenId fallback)
            {
                var target = History.ResolveBack(fallback);
                _navigatingBack = true;
                try
                {
                    Show(target);
                }
                finally
                {
                    _navigatingBack = false;
                }
            }
        }

        // -------------------------------------------------------------------------------------
        // The four rules, one at a time.
        // -------------------------------------------------------------------------------------

        [Test]
        public void NewHistory_IsEmpty()
        {
            var history = new ScreenHistory();

            Assert.AreEqual(0, history.Depth);
        }

        [Test]
        public void ResolveBack_ReturnsTheFallback_WhenThereIsNoHistory()
        {
            var history = new ScreenHistory();

            Assert.AreEqual(ScreenId.Home, history.ResolveBack(ScreenId.Home));
        }

        [Test]
        public void RecordNavigation_PushesTheScreenBeingLeft()
        {
            var history = new ScreenHistory();

            history.RecordNavigation(ScreenId.Profile, ScreenId.Store, isBack: false);

            Assert.AreEqual(1, history.Depth);
            Assert.AreEqual(ScreenId.Profile, history.ResolveBack(ScreenId.Home));
        }

        [Test]
        public void RecordNavigation_PushesNothing_WhenThereIsNoScreenToLeave()
        {
            var history = new ScreenHistory();

            history.RecordNavigation(null, ScreenId.Splash, isBack: false);

            Assert.AreEqual(0, history.Depth, "the first navigation of the session has nothing behind it");
        }

        [Test]
        public void RecordNavigation_PushesNothing_WhenTheTargetIsAlreadyShowing()
        {
            var history = new ScreenHistory();

            history.RecordNavigation(ScreenId.Store, ScreenId.Store, isBack: false);

            Assert.AreEqual(0, history.Depth, "a redundant Show must not make back a no-op that looks broken");
        }

        [Test]
        public void RecordNavigation_PushesNothing_WhenGoingBack()
        {
            var history = new ScreenHistory();

            history.RecordNavigation(ScreenId.Store, ScreenId.Profile, isBack: true);

            Assert.AreEqual(0, history.Depth, "the popped entry must not be pushed straight back on");
        }

        [Test]
        public void RecordNavigation_ClearsEverything_OnArrivingAtHome()
        {
            var history = new ScreenHistory();
            history.RecordNavigation(ScreenId.Home, ScreenId.ModeSelect, isBack: false);
            history.RecordNavigation(ScreenId.ModeSelect, ScreenId.BoardSelect, isBack: false);

            history.RecordNavigation(ScreenId.BoardSelect, ScreenId.Home, isBack: false);

            Assert.AreEqual(0, history.Depth, "Home is the hub and has no back button");
        }

        [Test]
        public void ResolveBack_ConsumesTheEntry()
        {
            var history = new ScreenHistory();
            history.RecordNavigation(ScreenId.Home, ScreenId.Profile, isBack: false);
            history.RecordNavigation(ScreenId.Profile, ScreenId.Store, isBack: false);

            Assert.AreEqual(ScreenId.Profile, history.ResolveBack(ScreenId.Home));
            Assert.AreEqual(ScreenId.Home, history.ResolveBack(ScreenId.Home));
            Assert.AreEqual(0, history.Depth);
        }

        [Test]
        public void Clear_DropsEverything()
        {
            var history = new ScreenHistory();
            history.RecordNavigation(ScreenId.Home, ScreenId.Profile, isBack: false);

            history.Clear();

            Assert.AreEqual(0, history.Depth);
        }

        // -------------------------------------------------------------------------------------
        // Real navigation, driven through the router's own call sequence.
        // -------------------------------------------------------------------------------------

        [Test]
        public void ProfileAndStore_DoNotLoopBetweenEachOther()
        {
            // The failure a single-level "previous screen" field produces, and the reason the
            // history is a stack (see ScreenHistory's own docs).
            var nav = new Navigator();
            nav.Show(ScreenId.Home);
            nav.Show(ScreenId.Profile);
            nav.Show(ScreenId.Store);

            nav.GoBack(ScreenId.Home);
            Assert.AreEqual(ScreenId.Profile, nav.Current);

            nav.GoBack(ScreenId.Home);
            Assert.AreEqual(ScreenId.Home, nav.Current, "back from Profile reaches Home instead of bouncing to Store");
        }

        [Test]
        public void Back_WalksTheChainInReverseOrder()
        {
            var nav = new Navigator();
            nav.Show(ScreenId.Home);
            nav.Show(ScreenId.ModeSelect);
            nav.Show(ScreenId.BoardSelect);
            nav.Show(ScreenId.Game);

            nav.GoBack(ScreenId.Home);
            Assert.AreEqual(ScreenId.BoardSelect, nav.Current);

            nav.GoBack(ScreenId.Home);
            Assert.AreEqual(ScreenId.ModeSelect, nav.Current);

            nav.GoBack(ScreenId.Home);
            Assert.AreEqual(ScreenId.Home, nav.Current);
        }

        [Test]
        public void Back_FallsBackOnceTheChainIsExhausted()
        {
            var nav = new Navigator();
            nav.Show(ScreenId.Home);
            nav.Show(ScreenId.Settings);

            nav.GoBack(ScreenId.Home);
            nav.GoBack(ScreenId.Home);

            Assert.AreEqual(ScreenId.Home, nav.Current, "back past the start lands on the fallback, never nowhere");
            Assert.AreEqual(0, nav.History.Depth);
        }

        [Test]
        public void SubScreens_ReturnToWhereverTheyWereOpenedFrom()
        {
            // Settings is reachable from Home and from Profile; the point of the stack is that back
            // depends on the route taken, not on a hardcoded parent.
            var viaHome = new Navigator();
            viaHome.Show(ScreenId.Home);
            viaHome.Show(ScreenId.Settings);
            viaHome.GoBack(ScreenId.Home);
            Assert.AreEqual(ScreenId.Home, viaHome.Current);

            var viaProfile = new Navigator();
            viaProfile.Show(ScreenId.Home);
            viaProfile.Show(ScreenId.Profile);
            viaProfile.Show(ScreenId.Settings);
            viaProfile.GoBack(ScreenId.Home);
            Assert.AreEqual(ScreenId.Profile, viaProfile.Current);
        }

        [Test]
        public void MatchFlowCycles_DoNotAccumulateHistory()
        {
            var nav = new Navigator();

            for (int i = 0; i < 10; i++)
            {
                nav.Show(ScreenId.Home);
                nav.Show(ScreenId.ModeSelect);
                nav.Show(ScreenId.BoardSelect);
                nav.Show(ScreenId.Game);
                nav.Show(ScreenId.Result);

                Assert.AreEqual(4, nav.History.Depth, $"cycle {i} should carry exactly one match flow");
            }

            nav.Show(ScreenId.Home);

            Assert.AreEqual(0, nav.History.Depth, "returning to the hub drops ten cycles' worth of history");
        }

        [Test]
        public void RepeatedShowOfTheSameScreen_DoesNotStackUp()
        {
            var nav = new Navigator();
            nav.Show(ScreenId.Home);
            nav.Show(ScreenId.Profile);

            nav.Show(ScreenId.Profile);
            nav.Show(ScreenId.Profile);

            Assert.AreEqual(1, nav.History.Depth);

            nav.GoBack(ScreenId.Home);
            Assert.AreEqual(ScreenId.Home, nav.Current, "back still works after redundant navigations");
        }

        [Test]
        public void Splash_IsNotSomewhereBackCanReturnTo()
        {
            // Startup is Splash -> Home, and Home clears - so the splash can never be reached again
            // by a back button, which would show a screen with no way out of it.
            var nav = new Navigator();
            nav.Show(ScreenId.Splash);
            nav.Show(ScreenId.Home);
            nav.Show(ScreenId.Profile);

            nav.GoBack(ScreenId.Home);

            Assert.AreEqual(ScreenId.Home, nav.Current);
            Assert.AreEqual(0, nav.History.Depth);
        }
    }
}
