using System;
using NUnit.Framework;
using TTTXO.Game.Services;
using TTTXO.Game.UI;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// <see cref="CosmeticSelection"/> is a preference store, not an entitlement store: it records
    /// what is displayed, never what the player is allowed to use. These tests pin the behaviour the
    /// equip UI relies on today, and the catalog cross-checks make sure a stored default can never
    /// point at a cosmetic that no longer exists - the case that would leave an element unpainted.
    /// </summary>
    [TestFixture]
    public class CosmeticSelectionTests
    {
        private PlayerPrefsSandbox _prefs;
        private Action _subscription;

        [SetUp]
        public void SetUp()
        {
            _prefs = PlayerPrefsSandbox.CaptureAndClear();
        }

        [TearDown]
        public void TearDown()
        {
            if (_subscription != null)
            {
                CosmeticSelection.Changed -= _subscription;
                _subscription = null;
            }

            _prefs.Restore();
        }

        [Test]
        public void Defaults_AreReturned_WhenNothingWasEverEquipped()
        {
            Assert.AreEqual(CosmeticSelection.DefaultAvatarId, CosmeticSelection.AvatarId);
            Assert.AreEqual(CosmeticSelection.DefaultFrameId, CosmeticSelection.FrameId);
            Assert.AreEqual(CosmeticSelection.DefaultBannerId, CosmeticSelection.BannerId);
            Assert.AreEqual(CosmeticSelection.DefaultPieceSkinId, CosmeticSelection.PieceSkinId);
            Assert.AreEqual(CosmeticSelection.DefaultBoardSkinId, CosmeticSelection.BoardSkinId);
        }

        [Test]
        public void SetAvatar_StoresTheChoice()
        {
            CosmeticSelection.SetAvatar("dragon");

            Assert.AreEqual("dragon", CosmeticSelection.AvatarId);
        }

        [Test]
        public void SetAvatar_FallsBackToTheDefault_WhenGivenAnEmptyId([Values(null, "")] string id)
        {
            CosmeticSelection.SetAvatar("dragon");

            CosmeticSelection.SetAvatar(id);

            Assert.AreEqual(CosmeticSelection.DefaultAvatarId, CosmeticSelection.AvatarId);
        }

        [Test]
        public void SetBoardSkin_RaisesChanged_OnlyWhenTheValueActuallyChanges()
        {
            int changes = 0;
            _subscription = () => changes++;
            CosmeticSelection.Changed += _subscription;

            CosmeticSelection.SetBoardSkin("wood");
            CosmeticSelection.SetBoardSkin("wood");

            Assert.AreEqual(1, changes, "re-equipping the same skin must not churn the listening screens");
        }

        [Test]
        public void SetPieceSkin_DoesNotRaiseChanged_WhenSettingTheAlreadyImplicitDefault()
        {
            int changes = 0;
            _subscription = () => changes++;
            CosmeticSelection.Changed += _subscription;

            CosmeticSelection.SetPieceSkin(CosmeticSelection.DefaultPieceSkinId);

            Assert.AreEqual(0, changes);
        }

        [Test]
        public void ApplyCloudSnapshot_AppliesEveryProvidedSlot()
        {
            CosmeticSelection.ApplyCloudSnapshot("owl", "gold", "space", "retro", "paper");

            Assert.AreEqual("owl", CosmeticSelection.AvatarId);
            Assert.AreEqual("gold", CosmeticSelection.FrameId);
            Assert.AreEqual("space", CosmeticSelection.BannerId);
            Assert.AreEqual("retro", CosmeticSelection.PieceSkinId);
            Assert.AreEqual("paper", CosmeticSelection.BoardSkinId);
        }

        [Test]
        public void ApplyCloudSnapshot_KeepsTheLocalChoice_ForSlotsTheSnapshotOmits()
        {
            CosmeticSelection.SetAvatar("cat");

            CosmeticSelection.ApplyCloudSnapshot(null, "gold", string.Empty, null, null);

            Assert.AreEqual("cat", CosmeticSelection.AvatarId, "an absent slot must not blank the local choice");
            Assert.AreEqual("gold", CosmeticSelection.FrameId);
            Assert.AreEqual(CosmeticSelection.DefaultBannerId, CosmeticSelection.BannerId);
        }

        [Test]
        public void ApplyCloudSnapshot_RaisesChangedOnce()
        {
            int changes = 0;
            _subscription = () => changes++;
            CosmeticSelection.Changed += _subscription;

            CosmeticSelection.ApplyCloudSnapshot("owl", "gold", "space", "retro", "paper");

            Assert.AreEqual(1, changes);
        }

        // -------------------------------------------------------------------------------------
        // The defaults have to resolve inside the catalog, otherwise a fresh install equips an id
        // the UI cannot paint.
        // -------------------------------------------------------------------------------------

        [Test]
        public void DefaultAvatar_ExistsInTheCatalog()
        {
            AssertCatalogContains(CosmeticCatalog.Avatars, CosmeticSelection.DefaultAvatarId);
        }

        [Test]
        public void DefaultFrame_ExistsInTheCatalog()
        {
            AssertCatalogContains(CosmeticCatalog.Frames, CosmeticSelection.DefaultFrameId);
        }

        [Test]
        public void DefaultBanner_ExistsInTheCatalog()
        {
            AssertCatalogContains(CosmeticCatalog.Banners, CosmeticSelection.DefaultBannerId);
        }

        [Test]
        public void DefaultPieceSkin_ExistsInTheCatalog()
        {
            AssertCatalogContains(CosmeticCatalog.PieceSkins, CosmeticSelection.DefaultPieceSkinId);
        }

        [Test]
        public void DefaultBoardSkin_ExistsInTheCatalog()
        {
            AssertCatalogContains(CosmeticCatalog.BoardSkins, CosmeticSelection.DefaultBoardSkinId);
        }

        private static void AssertCatalogContains(
            System.Collections.Generic.IReadOnlyList<CosmeticCatalog.Option> options,
            string id)
        {
            foreach (var option in options)
            {
                if (option.Id == id)
                {
                    return;
                }
            }

            Assert.Fail($"default cosmetic id '{id}' is not in its catalog");
        }
    }
}
