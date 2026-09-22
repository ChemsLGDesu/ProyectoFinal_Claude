using System.Collections.Generic;
using NUnit.Framework;
using TTTXO.Game.UI;

namespace TTTXO.Game.Tests
{
    /// <summary>
    /// The catalog maps a persisted cosmetic id to the USS class that paints it. Two properties keep
    /// the equip UI honest: ids are unique inside a set (a duplicate would make the first one win
    /// silently) and an unknown id always resolves to something paintable, since a save written by a
    /// newer build must never leave an avatar or a board blank.
    /// </summary>
    [TestFixture]
    public class CosmeticCatalogTests
    {
        private static IEnumerable<TestCaseData> AllSets()
        {
            yield return new TestCaseData(CosmeticCatalog.Avatars).SetName("Avatars");
            yield return new TestCaseData(CosmeticCatalog.Frames).SetName("Frames");
            yield return new TestCaseData(CosmeticCatalog.Banners).SetName("Banners");
            yield return new TestCaseData(CosmeticCatalog.PieceSkins).SetName("PieceSkins");
            yield return new TestCaseData(CosmeticCatalog.BoardSkins).SetName("BoardSkins");
        }

        [TestCaseSource(nameof(AllSets))]
        public void EverySet_IsNonEmpty(IReadOnlyList<CosmeticCatalog.Option> options)
        {
            Assert.IsNotEmpty(options, "StyleClassFor falls back to options[0], so an empty set would throw");
        }

        [TestCaseSource(nameof(AllSets))]
        public void EverySet_HasUniqueIds(IReadOnlyList<CosmeticCatalog.Option> options)
        {
            var seen = new HashSet<string>();

            foreach (var option in options)
            {
                Assert.IsTrue(seen.Add(option.Id), $"duplicate cosmetic id '{option.Id}'");
            }
        }

        [TestCaseSource(nameof(AllSets))]
        public void EverySet_HasStyleAndPreviewClasses(IReadOnlyList<CosmeticCatalog.Option> options)
        {
            foreach (var option in options)
            {
                Assert.IsNotEmpty(option.StyleClass, $"'{option.Id}' has no style class");
                Assert.IsNotEmpty(option.PreviewClass, $"'{option.Id}' has no preview class");
            }
        }

        [TestCaseSource(nameof(AllSets))]
        public void StyleClassFor_ResolvesEveryKnownId(IReadOnlyList<CosmeticCatalog.Option> options)
        {
            foreach (var option in options)
            {
                Assert.AreEqual(option.StyleClass, CosmeticCatalog.StyleClassFor(options, option.Id));
            }
        }

        [TestCaseSource(nameof(AllSets))]
        public void StyleClassFor_FallsBackToTheFirstOption_ForAnUnknownId(
            IReadOnlyList<CosmeticCatalog.Option> options)
        {
            string resolved = CosmeticCatalog.StyleClassFor(options, "id-from-a-newer-build");

            Assert.AreEqual(options[0].StyleClass, resolved);
        }

        [TestCaseSource(nameof(AllSets))]
        public void AllStyleClasses_CoversEveryOption(IReadOnlyList<CosmeticCatalog.Option> options)
        {
            var classes = new List<string>(CosmeticCatalog.AllStyleClasses(options));

            Assert.AreEqual(options.Count, classes.Count);
            foreach (var option in options)
            {
                Assert.Contains(option.StyleClass, classes);
            }
        }

        [Test]
        public void PreviewClass_DefaultsToTheStyleClass_WhenNotGiven()
        {
            var option = new CosmeticCatalog.Option("id", "style-class");

            Assert.AreEqual("style-class", option.PreviewClass);
        }

        [Test]
        public void SkinSets_DeclareADistinctPreviewClass()
        {
            // Skin classes go on the board container and reach the cells through descendant rules, so
            // applied to a lone swatch they would paint nothing - each one needs its own preview class.
            foreach (var option in CosmeticCatalog.PieceSkins)
            {
                Assert.AreNotEqual(option.StyleClass, option.PreviewClass, $"piece skin '{option.Id}'");
            }

            foreach (var option in CosmeticCatalog.BoardSkins)
            {
                Assert.AreNotEqual(option.StyleClass, option.PreviewClass, $"board skin '{option.Id}'");
            }
        }
    }
}
