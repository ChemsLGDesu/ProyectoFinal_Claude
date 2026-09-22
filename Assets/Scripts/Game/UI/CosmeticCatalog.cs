using System.Collections.Generic;

namespace TTTXO.Game.UI
{
    /// <summary>
    /// The profile cosmetics the equip UI can offer, each mapping a stored id to the USS modifier class
    /// that paints it (see <c>.profile-avatar--*</c> and <c>.profile-avatar-frame--*</c> in main.uss).
    ///
    /// Lives in the UI layer on purpose: it is a list of art variants and their style classes, which is a
    /// presentation concern. The stored id is what <see cref="TTTXO.Game.Services.CosmeticSelection"/>
    /// persists, so the ids must stay stable even if a class is renamed.
    ///
    /// Client-side only because every cosmetic here is free. When the store sells them, this list stops
    /// being the authority on what is offered - the server sends what the player owns and this becomes
    /// just the id-to-class mapping for whatever comes back.
    /// </summary>
    public static class CosmeticCatalog
    {
        public sealed class Option
        {
            /// <summary>Persisted identifier. Stable forever - changing one silently unequips whoever had it.</summary>
            public string Id { get; }

            /// <summary>USS modifier class that applies this option where it is equipped.</summary>
            public string StyleClass { get; }

            /// <summary>
            /// Class that paints a swatch in the equip panel. Usually the same one - an avatar's class
            /// paints an avatar anywhere. It differs for skins, whose class goes on the board container and
            /// reaches the cells through descendant rules: applied to a lone swatch it would paint nothing,
            /// so those get a preview class showing one representative piece or surface.
            /// </summary>
            public string PreviewClass { get; }

            public Option(string id, string styleClass, string previewClass = null)
            {
                Id = id;
                StyleClass = styleClass;
                PreviewClass = previewClass ?? styleClass;
            }
        }

        public static readonly IReadOnlyList<Option> Avatars = new[]
        {
            new Option("fox", "profile-avatar--fox"),
            new Option("cat", "profile-avatar--cat"),
            new Option("robot", "profile-avatar--robot"),
            new Option("owl", "profile-avatar--owl"),
            new Option("dragon", "profile-avatar--dragon"),
            new Option("astronaut", "profile-avatar--astronaut"),
        };

        public static readonly IReadOnlyList<Option> Frames = new[]
        {
            new Option("neon", "profile-avatar-frame--neon"),
            new Option("gold", "profile-avatar-frame--gold"),
        };

        public static readonly IReadOnlyList<Option> Banners = new[]
        {
            new Option("neon", "profile-banner--neon"),
            new Option("space", "profile-banner--space"),
        };

        /// <summary>
        /// Piece and board skins style the board, so their classes go on the board CONTAINER and paint the
        /// cells through descendant rules (see <c>.piece-skin--* .board-cell--x</c> in main.uss). One class
        /// swap restyles a whole 11x11 board instead of touching 121 cells.
        /// </summary>
        public static readonly IReadOnlyList<Option> PieceSkins = new[]
        {
            new Option("neon", "piece-skin--neon", "equip-preview--piece-neon"),
            new Option("minimalist", "piece-skin--minimalist", "equip-preview--piece-minimalist"),
            new Option("wood", "piece-skin--wood", "equip-preview--piece-wood"),
            new Option("space", "piece-skin--space", "equip-preview--piece-space"),
            new Option("animals", "piece-skin--animals", "equip-preview--piece-animals"),
            new Option("retro", "piece-skin--retro", "equip-preview--piece-retro"),
            new Option("elemental", "piece-skin--elemental", "equip-preview--piece-elemental"),
        };

        public static readonly IReadOnlyList<Option> BoardSkins = new[]
        {
            new Option("neon", "board-skin--neon", "equip-preview--board-neon"),
            new Option("wood", "board-skin--wood", "equip-preview--board-wood"),
            new Option("space", "board-skin--space", "equip-preview--board-space"),
            new Option("paper", "board-skin--paper", "equip-preview--board-paper"),
        };

        /// <summary>Resolves an id to its style class, falling back to the first option so an unknown id - a save from a newer build, a removed cosmetic - never leaves an element unpainted.</summary>
        public static string StyleClassFor(IReadOnlyList<Option> options, string id)
        {
            foreach (var option in options)
            {
                if (option.Id == id)
                {
                    return option.StyleClass;
                }
            }

            return options[0].StyleClass;
        }

        /// <summary>Every style class in a set, for clearing before applying the selected one.</summary>
        public static IEnumerable<string> AllStyleClasses(IReadOnlyList<Option> options)
        {
            foreach (var option in options)
            {
                yield return option.StyleClass;
            }
        }
    }
}
