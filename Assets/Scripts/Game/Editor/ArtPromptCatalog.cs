using System.Collections.Generic;
using UnityEngine;

namespace TTTXO.Game.Editor
{
    /// <summary>Which section of the game's art this entry belongs to (mirrors the catalog sections in the tool's design brief: brand/UI, piece skins, board skins, FX, profile, reactions, ranked, store).</summary>
    public enum ArtCategory
    {
        BrandUi,
        PieceSkins,
        BoardSkins,
        Effects,
        Profile,
        Reactions,
        Ranked,
        Store,
    }

    /// <summary>One generatable art asset: a stable id (also the output file name), its category/aspect ratio/background handling, and the English prompt describing the specific subject (the shared art-direction/palette/no-text rules are NOT repeated here - see <see cref="ArtPromptCatalog.BuildFullPrompt"/>).</summary>
    public sealed class ArtPromptEntry
    {
        public string Id { get; }
        public ArtCategory Category { get; }

        /// <summary>"1:1" or "16:9" - passed through as-is to the API's <c>response_format.aspect_ratio</c>.</summary>
        public string AspectRatio { get; }

        /// <summary>True for isolated sprites/icons that need a transparent PNG background; false for assets that are themselves a background/backdrop (menu background, board textures, banners, currency pack art).</summary>
        public bool TransparentBackground { get; }

        /// <summary>Subject-specific English prompt text. Editable at runtime from the tool window without recompiling (see <c>PlaceholderArtGeneratorWindow</c>'s per-entry prompt override field) - this is only the default.</summary>
        public string Prompt { get; }

        /// <summary>
        /// Non-null when the art already on disk is known to be defective and should be regenerated, with
        /// the measured reason. Set from a review of the generated output, not from a guess - the tool
        /// surfaces it so a piece that merely "exists" is not mistaken for a piece that is good enough.
        /// Clear the flag in the same commit that replaces the art.
        /// </summary>
        public string RegenerateReason { get; }

        /// <summary>
        /// When set, the generated art is rescaled after keying so the subject spans exactly this share of
        /// the canvas (see <see cref="SubjectNormalizer"/>). Use it for assets shown together as a SET,
        /// where an uneven size reads as a mistake - avatars in a picker grid, the two halves of a piece
        /// skin pair, a row of tier badges. Null leaves the framing exactly as generated.
        /// </summary>
        public float? SubjectCoverage { get; }

        /// <summary>
        /// Same idea as <see cref="SubjectCoverage"/> but measured on the subject's central HOLE, for rings
        /// and frames where the hole is what has to match: it is the opening an avatar sits in. Mutually
        /// exclusive with <see cref="SubjectCoverage"/> - normalizing a frame by its outer edge leaves the
        /// openings mismatched, which is the dimension the UI actually has to size against.
        /// </summary>
        public float? HoleCoverage { get; }

        public ArtPromptEntry(string id, ArtCategory category, string aspectRatio, bool transparentBackground, string prompt, string regenerateReason = null, float? subjectCoverage = null, float? holeCoverage = null)
        {
            Id = id;
            Category = category;
            AspectRatio = aspectRatio;
            TransparentBackground = transparentBackground;
            Prompt = prompt;
            RegenerateReason = regenerateReason;
            SubjectCoverage = subjectCoverage;
            HoleCoverage = holeCoverage;
        }
    }

    /// <summary>
    /// Source-of-truth catalog of every placeholder art prompt for the game (Milestones: store/cosmetics
    /// unlock work, general visual polish). C# is the chosen format over a ScriptableObject catalog
    /// because: (a) it can be code-reviewed and diffed like the rest of the project's data (same reasoning
    /// as <c>LocalizationSetup.Translations</c>), (b) it needs zero manual asset setup to add an entry, and
    /// (c) <see cref="PlaceholderArtGeneratorWindow"/> already exposes a per-entry text field to tweak a
    /// prompt for a single generation run without touching this file - so the "editable without recompiling"
    /// need from the brief is covered without giving up the versioned source of truth.
    ///
    /// Every prompt in <see cref="Entries"/> only describes its specific subject; the shared TTTXO art
    /// direction (palette hex codes, flat neon-dark mobile-icon style, and the hard "no text in the image"
    /// rule) lives once in <see cref="BuildFullPrompt"/> so a style or palette change is a one-line edit
    /// instead of touching 50+ strings.
    /// </summary>
    public static class ArtPromptCatalog
    {
        public const string OutputRootFolder = "Assets/Art/Generated";

        /// <summary>
        /// Style, palette and the no-text rule - true of every entry regardless of what it is for.
        ///
        /// The no-text rule leads and is restated straight after the palette on purpose. When the palette
        /// was merely a list of hex codes with the rule trailing at the end, profile_banner_neon came back
        /// with "#1C2733" and "#8A97A6" set as large decorative type across the art - two codes that appear
        /// nowhere in that entry's own prompt, so they were read straight off this list as content to draw.
        /// Hex strings look like text to render unless they are explicitly framed as a specification.
        /// </summary>
        private const string ArtDirectionHeader =
            "CRITICAL RULE, ABOVE ALL OTHERS: absolutely no text, letters, numbers, words, labels, hex codes " +
            "or typography of any kind anywhere in the image, not even a stylized logotype or a tiny caption " +
            "- every piece of game text is added later through Unity Localization in 10 languages, so " +
            "embedded text can never be localized and must never appear. " +
            "Mobile game icon art style. Dark, flat UI with neon accents, using this exact palette wherever " +
            "color is needed: background #0B0F14, panel surface #121821, elevated surface #1C2733, border " +
            "#263140, primary text #E7EDF3, muted text #8A97A6, cyan neon accent #35E6C6, violet neon accent " +
            "#7C5CFF, danger red #FF5D6C, warning amber #FFB84D, soft-currency gold #F4C948, hard-currency " +
            "lilac #B58CFF. Those hex codes are a colour specification telling you which colours to paint " +
            "with - they are NOT content: never draw, write, print or letter a hex code, a colour name or " +
            "any part of this list anywhere in the image. " +
            "Clean flat-vector illustration, bold simple shapes that stay legible at a small mobile icon " +
            "size, no watermark, no signature.";

        /// <summary>
        /// Rules for a cut-out subject that will be composited ONTO the dark UI. The inverse of
        /// <see cref="BackdropRules"/> - do not apply these to an opaque entry, which is itself the thing
        /// other art sits on and therefore has to stay dark.
        /// </summary>
        private const string ForegroundSubjectRules =
            "CONTRAST: this art is composited onto a near-black #0B0F14 screen, so the subject must read " +
            "clearly against it - keep the dominant tones light and saturated enough to stand out, and never " +
            "render the subject in dark browns, deep navies or near-blacks. When a pair of pieces is meant " +
            "to differ in tone, make BOTH of them clearly visible on that dark screen and separate them by " +
            "hue rather than by making one of them dark. FRAMING: centre the subject in the canvas so that " +
            "it fills roughly 70 percent of the shorter side, with even margins on all four sides and " +
            "nothing touching or bleeding off the edges; on a square canvas the subject's own silhouette " +
            "must also read as roughly square, never as a wide or tall shape floating in empty space.";

        /// <summary>
        /// Rules for an opaque image that is itself a background - a board surface, a screen backdrop, a
        /// banner. Pieces, icons and localized UI text are drawn on top, so the requirement is the opposite
        /// of <see cref="ForegroundSubjectRules"/>: stay dark and quiet enough to be sat on.
        /// </summary>
        private const string BackdropRules =
            "BACKDROP: this image is a background - game pieces, icons and UI text are drawn on top of it, " +
            "so it must never compete with them. Keep the whole image dark, built on a near-black #0B0F14 " +
            "base, with low internal contrast and no large bright or white areas; any texture, pattern or " +
            "detail stays subtle and low-contrast, and the centre of the image is the quietest part. Bright " +
            "accents are allowed only as thin, sparse highlights. A light or white background is wrong here " +
            "no matter what the subject matter suggests.";

        /// <summary>
        /// Hard-edge requirement, applied to cut-out subjects only. A soft glow blends into the chroma
        /// backdrop and cannot be separated from it cleanly. <see cref="ArtCategory.Effects"/> is exempt -
        /// a particle or glow sprite IS its soft falloff, so forbidding it would defeat the entry.
        /// </summary>
        private const string HardEdgeRule =
            "Every shape must have a hard, crisp, fully opaque edge: no outer glow, no bloom, no neon halo, " +
            "no soft light spill, no blur and no feathering around the silhouette. A soft glow blends the " +
            "subject into the backdrop and cannot be separated from it cleanly, which leaves a tinted halo " +
            "once the background is removed.";

        /// <summary>Counterpart to <see cref="HardEdgeRule"/> for effect sprites, whose whole point is the falloff.</summary>
        private const string SoftFalloffRule =
            "This is a particle/glow effect sprite, so a soft luminous falloff IS the subject: let the glow " +
            "fade out smoothly. Keep the falloff strictly within the shape's own colors and make sure it has " +
            "faded completely to the flat backdrop color well before reaching the canvas edges.";

        /// <summary>
        /// Chroma-key backdrop color for entries that need alpha. Pure green sits ~48 degrees of hue away
        /// from the nearest palette color (cyan #35E6C6) and is absent from the art direction entirely, so
        /// <see cref="ChromaKeyProcessor"/> can knock it out without eating into a subject.
        /// </summary>
        public const string ChromaKeyHex = "#00FF00";

        public static readonly Color32 ChromaKeyColor = new(0, 255, 0, 255);

        // The API returns JPEG only, which has no alpha channel - asking for a transparent background makes
        // the model literally draw a checkerboard. So transparency is requested as a flat, unshaded backdrop
        // in ChromaKeyHex and removed after download by ChromaKeyProcessor.
        private static readonly string TransparentBackgroundInstruction =
            $"Background: a completely flat, uniform, solid pure green ({ChromaKeyHex}) backdrop filling the " +
            "entire canvas behind the subject. The isolated subject only, no scenery, no props, no ground " +
            "plane. CRITICAL: the background must be one single flat color with absolutely no gradient, no " +
            "vignette, no texture, no lighting variation, and no drop shadow or glow cast onto it. Never " +
            "draw a checkerboard or transparency pattern anywhere, for any reason. If the subject is a ring, " +
            "a frame or any shape with a hole through it, fill that hole with exactly the same flat green as " +
            "the backdrop - the hole IS background, so paint it as background. Do not use this green " +
            "anywhere in the subject itself. Keep the subject's outline crisp and clearly separated from " +
            "the background.";

        private const string OpaqueBackgroundInstruction =
            "Background: this image has its own full-bleed background art (not transparent).";

        public static readonly IReadOnlyList<ArtPromptEntry> Entries = BuildEntries();

        /// <summary>
        /// Composes the text sent to the API: the universal art direction, the rules that apply to this
        /// KIND of entry, the (possibly overridden) subject prompt, and the background instruction.
        ///
        /// The rules are selected rather than concatenated wholesale because they contradict each other by
        /// design. A cut-out subject must be light enough to read on the dark UI; an opaque entry IS that
        /// dark UI and must stay dark. A silhouette must have hard edges so the chroma backdrop can be keyed
        /// off it; an effect sprite is nothing but soft falloff. Sending every rule to every entry produced
        /// a bright orange board and a near-white one, both unusable.
        /// </summary>
        public static string BuildFullPrompt(ArtPromptEntry entry, string promptOverride = null)
        {
            string subject = string.IsNullOrWhiteSpace(promptOverride) ? entry.Prompt : promptOverride;

            string kindRules;
            if (!entry.TransparentBackground)
            {
                kindRules = BackdropRules;
            }
            else
            {
                string edgeRule = entry.Category == ArtCategory.Effects ? SoftFalloffRule : HardEdgeRule;
                kindRules = $"{ForegroundSubjectRules} {edgeRule}";
            }

            string backgroundInstruction = entry.TransparentBackground ? TransparentBackgroundInstruction : OpaqueBackgroundInstruction;
            return $"{ArtDirectionHeader}\n\n{kindRules}\n\n{subject}\n\n{backgroundInstruction}";
        }

        /// <summary>Palette size for flat art. 64 keeps it visually identical while cutting a 2.4 MB avatar to roughly 340 KB; above ~96 the file grows sharply for no visible gain.</summary>
        private const int FlatArtMinifyColours = 64;

        /// <summary>
        /// Palette size for shaded art. Measured on the tier badges, 192 came out both the smallest file
        /// AND the closest to the original - smaller than 64, 128 or 256. More entries land nearer the real
        /// colours, so runs of identical palette indices get longer and compress better, which outweighs
        /// the wider palette. Worth re-measuring rather than assuming for any new shaded category.
        /// </summary>
        private const int ShadedArtMinifyColours = 192;

        /// <summary>
        /// How many colours <see cref="TextureMinifier"/> may keep for this entry, or null to leave it
        /// alone.
        ///
        /// Opaque entries are excluded because they keep the API's untouched JPEG bytes - there is no PNG
        /// to shrink.
        ///
        /// Ranked badges were excluded at first on the assumption that their metallic tier shading was a
        /// real gradient a small palette would band. Measuring proved otherwise: the shading is a handful
        /// of flat bands, and at 192 colours a badge is indistinguishable from its source even zoomed into
        /// the rim ramp. They get the wider palette rather than the exclusion.
        /// </summary>
        public static int? GetMinifyColours(ArtPromptEntry entry)
        {
            if (!entry.TransparentBackground)
            {
                return null;
            }

            return entry.Category == ArtCategory.Ranked ? ShadedArtMinifyColours : FlatArtMinifyColours;
        }

        /// <summary>
        /// JPEG quality for re-encoding an opaque entry, or null for entries that are not JPEG at all.
        ///
        /// 90 measured on the worst case in the catalog - the nebula profile banner, the piece with the
        /// most genuine gradient - where it cuts 1.8 MB to 214 KB while changing the mean pixel by 2.6 of a
        /// possible 765 and leaving only 0.3% of pixels visibly different. The art arrives from the API at
        /// a far higher setting than a backdrop behind UI text ever needs.
        /// </summary>
        public static int? GetJpegQuality(ArtPromptEntry entry)
        {
            return entry.TransparentBackground ? (int?)null : OpaqueJpegQuality;
        }

        private const int OpaqueJpegQuality = 90;

        /// <summary>
        /// Whether <see cref="BackdropFlattener"/> should divide the radial lighting falloff out of this
        /// entry after generation.
        ///
        /// Board skins only. A board is scaled and cropped to four different board sizes, so it has to be an
        /// even surface with no middle - and the prompt has asked for exactly that since the category
        /// existed, and been ignored by every board generated. Banners and other opaque art are composed
        /// pictures where a bright area is the subject, and flattening them radially would wreck them.
        /// </summary>
        public static bool ShouldFlattenLighting(ArtPromptEntry entry) =>
            !entry.TransparentBackground && entry.Category == ArtCategory.BoardSkins;

        /// <summary>
        /// The framing target for this entry, or null to leave it as generated.
        ///
        /// An explicit <see cref="ArtPromptEntry.SubjectCoverage"/> wins; otherwise a category default
        /// applies, because framing uniformity is a property of a SET rather than of one piece. Reactions
        /// sit together in a picker and store icons sit together in a tab row, so an uneven size reads as a
        /// mistake in both - the reactions came back spanning 39% to 78% of their canvas and the store
        /// icons 45% to 86%.
        ///
        /// Categories whose pieces are shown alone (boards, banners, brand marks) stay null: there is
        /// nothing to be uniform with, and rescaling would only throw away resolution.
        /// </summary>
        public static float? GetFramingCoverage(ArtPromptEntry entry)
        {
            if (entry.SubjectCoverage.HasValue)
            {
                return entry.SubjectCoverage;
            }

            if (!entry.TransparentBackground)
            {
                return null;
            }

            return entry.Category switch
            {
                ArtCategory.Reactions => SetFramingCoverage,
                ArtCategory.Store => SetFramingCoverage,
                _ => null,
            };
        }

        private const float SetFramingCoverage = 0.70f;

        /// <summary>
        /// Already-generated pieces to hand this entry as visual reference, as project asset paths.
        ///
        /// Words cannot pin down a recurring object. Three currency packs generated from three prompts that
        /// all said "violet faceted gem" came back with three unrelated gem designs - pale flat lavender,
        /// bright faceted violet, and a third mixed with coins - so the tiers did not read as the same
        /// product in different amounts. The canonical currency icons are already generated, so the packs
        /// are shown them instead of being asked to imagine them again.
        ///
        /// Only for objects that genuinely repeat across pieces. A reference on a one-off just constrains
        /// it for no reason.
        /// </summary>
        public static IReadOnlyList<string> GetReferenceAssets(ArtPromptEntry entry)
        {
            switch (entry.Id)
            {
                case "currency_pack_small":
                case "currency_pack_large":
                    return CurrencyReferences;

                // Medium additionally gets the small pack as a composition reference. Its prompt has
                // forbidden a drawn frame in so many words twice, and twice the model returned one anyway -
                // the second time as an inset rounded panel. Showing it a sibling that has no frame is the
                // same move that fixed the gem design, applied to layout instead of to an object.
                case "currency_pack_medium":
                    return CurrencyReferencesWithSibling;

                default:
                    return System.Array.Empty<string>();
            }
        }

        private static readonly string[] CurrencyReferences =
        {
            OutputRootFolder + "/brand-ui/currency_icon_hard.png",
            OutputRootFolder + "/brand-ui/currency_icon_soft.png",
        };

        private static readonly string[] CurrencyReferencesWithSibling =
        {
            OutputRootFolder + "/brand-ui/currency_icon_hard.png",
            OutputRootFolder + "/brand-ui/currency_icon_soft.png",
            OutputRootFolder + "/store/currency_pack_small.jpg",
        };

        public static string CategoryFolderName(ArtCategory category) => category switch
        {
            ArtCategory.BrandUi => "brand-ui",
            ArtCategory.PieceSkins => "piece-skins",
            ArtCategory.BoardSkins => "board-skins",
            ArtCategory.Effects => "effects",
            ArtCategory.Profile => "profile",
            ArtCategory.Reactions => "reactions",
            ArtCategory.Ranked => "ranked",
            ArtCategory.Store => "store",
            _ => "misc",
        };

        public static string CategoryDisplayName(ArtCategory category) => category switch
        {
            ArtCategory.BrandUi => "Brand / Base UI",
            ArtCategory.PieceSkins => "Piece Skins (X/O)",
            ArtCategory.BoardSkins => "Board Skins",
            ArtCategory.Effects => "Visual Effects",
            ArtCategory.Profile => "Profile",
            ArtCategory.Reactions => "Reactions / Stickers",
            ArtCategory.Ranked => "Ranked (Milestone 5)",
            ArtCategory.Store => "Store",
            _ => category.ToString(),
        };

        /// <summary>
        /// Project-relative asset path this entry is written to and imported from, e.g.
        /// <c>Assets/Art/Generated/piece-skins/piece_neon_x.png</c>.
        ///
        /// The extension follows what actually lands on disk: entries needing alpha are re-encoded to a real
        /// PNG after <see cref="ChromaKeyProcessor"/> knocks out their backdrop, while opaque entries keep the
        /// API's untouched JPEG bytes and are saved as <c>.jpg</c> - writing JPEG bytes into a <c>.png</c>
        /// file would be a lie about the format for no benefit.
        /// </summary>
        public static string GetOutputAssetPath(ArtPromptEntry entry) =>
            $"{OutputRootFolder}/{CategoryFolderName(entry.Category)}/{entry.Id}{(entry.TransparentBackground ? ".png" : ".jpg")}";

        private static List<ArtPromptEntry> BuildEntries()
        {
            var entries = new List<ArtPromptEntry>();

            // ============================================================
            // A. Brand / base UI
            // ============================================================

            // NOTE: the source brief asks for a "wordmark", but a literal text logotype would violate the
            // project-wide "no text in generated images" rule (all game text comes from Unity Localization
            // in 10 languages and cannot be baked into art). This entry instead covers the brand's graphic
            // mark - the X/O emblem used for the app icon and splash logo - with no lettering at all.
            entries.Add(new ArtPromptEntry("brand_mark_icon", ArtCategory.BrandUi, "1:1", true,
                "App icon / brand emblem for a tic-tac-toe game: a bold graphic mark built only from a " +
                "stylized X shape and O shape paired together, X rendered in cyan #35E6C6 and O rendered in " +
                "violet #7C5CFF, both as thick glowing neon-tube strokes with rounded corners, arranged as a " +
                "clean, symmetric icon-style emblem. This is the graphic brand mark used for the app icon " +
                "and splash screen - it must be purely the X/O graphic shapes, not a lettered logotype of " +
                "the game's name."));

            entries.Add(new ArtPromptEntry("currency_icon_soft", ArtCategory.BrandUi, "1:1", true,
                "A single flat-icon gold coin representing the game's soft currency, color #F4C948 with a " +
                "subtle darker gold rim shading and a minimal geometric shine highlight, centered, mobile " +
                "game currency icon style, nothing embossed on the coin face."));

            entries.Add(new ArtPromptEntry("currency_icon_hard", ArtCategory.BrandUi, "1:1", true,
                "A single flat-icon faceted gemstone representing the game's premium hard currency, color " +
                "#B58CFF with crisp faceted-cut shading and a soft violet inner glow, centered, mobile game " +
                "currency icon style, nothing embossed on the gem face."));

            entries.Add(new ArtPromptEntry("mode_icon_single_player", ArtCategory.BrandUi, "1:1", true,
                "Minimal flat icon representing a 'single player vs AI' game mode: a simplified humanoid " +
                "head-and-shoulders silhouette in off-white #E7EDF3 facing a small simplified robot/AI head " +
                "silhouette in cyan #35E6C6, both bold flat shapes, centered composition, mobile menu icon " +
                "style."));

            entries.Add(new ArtPromptEntry("mode_icon_local_2p", ArtCategory.BrandUi, "1:1", true,
                "Minimal flat icon representing a 'local two players, same device' game mode: two simplified " +
                "humanoid head-and-shoulders silhouettes side by side, one in cyan #35E6C6 and one in violet " +
                "#7C5CFF, both bold flat shapes, centered composition, mobile menu icon style."));

            entries.Add(new ArtPromptEntry("mode_icon_online", ArtCategory.BrandUi, "1:1", true,
                "Minimal flat icon representing an 'online multiplayer' game mode: a simplified globe/network " +
                "silhouette in cyan #35E6C6 with a subtle violet #7C5CFF signal/connection accent arc, bold " +
                "flat shapes, centered composition, mobile menu icon style."));

            entries.Add(new ArtPromptEntry("menu_background", ArtCategory.BrandUi, "16:9", false,
                "Full-bleed background art for a game's main menu screen: dark flat backdrop, base color " +
                "#0B0F14, with a very subtle multi-layer parallax composition of soft geometric shapes and " +
                "faint thin grid lines in muted cyan #35E6C6 and violet #7C5CFF at low opacity. Calm and " +
                "unobtrusive, no focal subject, safe to place UI buttons and text on top without competing " +
                "for attention."));

            // ============================================================
            // B. Piece skins (X/O pairs) - identity rule: whatever the material, X and O must stay
            // unmistakably distinguishable from each other via color and/or shape.
            // ============================================================

            entries.Add(new ArtPromptEntry("piece_neon_x", ArtCategory.PieceSkins, "1:1", true,
                "A single bold uppercase X symbol rendered as a glowing cyan neon tube, color #35E6C6, thick " +
                "rounded strokes with a soft outer glow, centered, game piece icon for a tic-tac-toe board. " +
                "This is the game's default piece skin."));
            entries.Add(new ArtPromptEntry("piece_neon_o", ArtCategory.PieceSkins, "1:1", true,
                "A single bold O symbol rendered as a glowing violet neon tube, color #7C5CFF, thick rounded " +
                "strokes with a soft outer glow, centered, game piece icon for a tic-tac-toe board. This is " +
                "the game's default piece skin."));

            entries.Add(new ArtPromptEntry("piece_wood_x", ArtCategory.PieceSkins, "1:1", true,
                "A single bold X symbol carved from warm reddish-brown cherry wood with visible wood grain " +
                "texture, centered, game piece icon for a tic-tac-toe board, part of a wood-themed skin pair. " +
                "Keep the wood mid-toned and warm, never dark walnut or near-black: it has to stay clearly " +
                "visible on a near-black screen. It is separated from the matching O piece by its reddish " +
                "hue, not by being darker."));
            entries.Add(new ArtPromptEntry("piece_wood_o", ArtCategory.PieceSkins, "1:1", true,
                "A single bold O symbol carved from light oak wood with visible wood grain texture, " +
                "centered, game piece icon for a tic-tac-toe board, part of a wood-themed skin pair. Use a " +
                "noticeably lighter, warmer wood tone than the matching X piece so the two stay clearly " +
                "distinguishable."));

            entries.Add(new ArtPromptEntry("piece_space_x", ArtCategory.PieceSkins, "1:1", true,
                "A single bold X symbol styled as a glowing cyan-blue plasma/comet-trail shape with small " +
                "star-sparkle accents, centered, game piece icon for a tic-tac-toe board, sci-fi space theme, " +
                "part of a space-themed skin pair."));
            entries.Add(new ArtPromptEntry("piece_space_o", ArtCategory.PieceSkins, "1:1", true,
                "A single bold O symbol styled as a bright glowing violet orbit ring, color #7C5CFF, with " +
                "small star-sparkle accents, centered, game piece icon for a tic-tac-toe board, sci-fi space " +
                "theme, part of a space-themed skin pair, clearly distinguishable in color and shape from " +
                "the matching X piece. The ring is drawn face-on as a bright circular O, not as a planet " +
                "with a wide horizontal ring: the silhouette must be as tall as it is wide."));

            entries.Add(new ArtPromptEntry("piece_animals_x", ArtCategory.PieceSkins, "1:1", true,
                "A single bold X symbol formed from a cute flat-illustration fox silhouette bent into an X " +
                "pose, cool-toned fur colors (blues and teals), centered, game piece icon for a tic-tac-toe " +
                "board, playful mobile game style, part of an animals-themed skin pair."));
            entries.Add(new ArtPromptEntry("piece_animals_o", ArtCategory.PieceSkins, "1:1", true,
                "A single bold O symbol formed from a cute flat-illustration owl silhouette curled into a " +
                "ring pose, warm-toned feather colors (purples and pinks), centered, game piece icon for a " +
                "tic-tac-toe board, playful mobile game style, part of an animals-themed skin pair, clearly " +
                "distinguishable in color and shape from the matching X piece."));

            entries.Add(new ArtPromptEntry("piece_retro_x", ArtCategory.PieceSkins, "1:1", true,
                "A single bold X symbol in chunky 16-bit pixel-art style, hard pixel edges, cool cyan-toned " +
                "retro arcade color palette, centered, game piece icon for a tic-tac-toe board, part of a " +
                "retro-arcade-themed skin pair."));
            entries.Add(new ArtPromptEntry("piece_retro_o", ArtCategory.PieceSkins, "1:1", true,
                "A single bold O symbol in chunky 16-bit pixel-art style, hard pixel edges, bright hot-pink " +
                "and light-violet retro arcade color palette at high luminance, centered, game piece icon " +
                "for a tic-tac-toe board, part of a retro-arcade-themed skin pair, clearly distinguishable " +
                "in color from the matching X piece."));

            entries.Add(new ArtPromptEntry("piece_minimalist_x", ArtCategory.PieceSkins, "1:1", true,
                "A single bold X symbol as a perfectly clean flat geometric mark, thin uniform stroke " +
                "weight, solid cyan #35E6C6 fill, no texture or gradient, centered, extremely minimal modern " +
                "icon style, game piece for a tic-tac-toe board, part of a minimalist-themed skin pair."));
            entries.Add(new ArtPromptEntry("piece_minimalist_o", ArtCategory.PieceSkins, "1:1", true,
                "A single bold O symbol as a perfectly clean flat geometric mark, thin uniform stroke " +
                "weight, solid violet #7C5CFF fill, no texture or gradient, centered, extremely minimal " +
                "modern icon style, game piece for a tic-tac-toe board, part of a minimalist-themed skin " +
                "pair."));

            // The elemental pair is the only skin that puts the WARM piece on X and the cool one on O -
            // every other pair runs cool-X / warm-O, following the base UI's cyan X and violet O. Fire on
            // the X and ice on the O is the pairing that reads at a glance, so the inversion is deliberate;
            // it is also why the ice O leans white-blue rather than the cyan #35E6C6 that would collide with
            // the default X. Both are described as flat cut-paper shapes because piece skins are held to the
            // hard-edge rule (see HardEdgeRule): a real glowing flame cannot be keyed off the backdrop
            // cleanly. The fire and ice read from silhouette and colour banding, not from luminosity.
            entries.Add(new ArtPromptEntry("piece_elemental_x", ArtCategory.PieceSkins, "1:1", true,
                "A single bold X symbol made of stylised flame: the two strokes of the X are shaped like " +
                "flat cut-out fire tongues with crisp curved tips, filled with banded warm colour going " +
                "from deep red #FF5D6C at the base through orange to bright amber #FFB84D at the tips, " +
                "flat-vector illustration with hard clean edges and no glow, centered, game piece icon for " +
                "a tic-tac-toe board, part of an elemental-themed skin pair. The X shape must stay " +
                "immediately readable as an X, with the flame styling on the strokes themselves rather " +
                "than as separate flames around it."));
            entries.Add(new ArtPromptEntry("piece_elemental_o", ArtCategory.PieceSkins, "1:1", true,
                "A single bold O symbol made of stylised ice: a thick ring built from flat angular crystal " +
                "facets with crisp straight edges, filled with banded cool colour going from pale icy white " +
                "through light sky blue to a deeper glacial blue in the shadowed facets, a few small frost " +
                "shards on the outer edge, flat-vector illustration with hard clean edges and no glow, " +
                "centered, game piece icon for a tic-tac-toe board, part of an elemental-themed skin pair, " +
                "clearly distinguishable from the matching fire X. The O shape must stay immediately " +
                "readable as a closed ring, as tall as it is wide."));

            // ============================================================
            // C. Board / background skins
            // ============================================================

            // A board skin is the SURFACE ONLY - never a drawn grid. The board is played at four sizes
            // (BOARD_CONFIGS: 3x3, 6x6, 9x9, 11x11) and the grid comes from the UI, where every cell is a
            // .board-cell element with its own border (Assets/UI/Styles/main.uss). A grid baked into the
            // texture can only ever match one size, and would double up with the UI's own lines on all of
            // them. These prompts therefore ask for a seamless, evenly-lit material with no grid at all.
            // The lighting clause here is deliberately short, and it is NOT what keeps the surface flat -
            // BackdropFlattener is. Rewriting it into a long positive spec ("the four corners must be
            // exactly as bright as the middle ... a flat scan under completely even light, there is no
            // light source ...") was tried and made things worse, not better: the vignette went UP on three
            // of the four boards (neon 1.64x -> 3.16x, wood 1.59x -> 2.29x) and neon lost most of its
            // circuit texture in the process. A model asked for a tabletop renders it lit, and the more the
            // prompt argues about lighting the more of its budget goes there instead of into the material.
            const string BoardSurfaceRule =
                "This is the board SURFACE ONLY: absolutely no grid, no lines, no cells, no borders and no " +
                "frame anywhere in the image - the game draws its own grid on top at several different " +
                "sizes. An even, seamless, edge-to-edge material filling the whole canvas, lit uniformly " +
                "with no vignette, no central focal point and no visible edges, so it can be scaled and " +
                "cropped to any board size. No game pieces.";

            entries.Add(new ArtPromptEntry("board_neon", ArtCategory.BoardSkins, "1:1", false,
                "Top-down flat texture of a game board surface: dark near-black base #0B0F14 with a very " +
                "subtle darker cyan #35E6C6 circuit-like sheen and faint scanline texture. " + BoardSurfaceRule));
            entries.Add(new ArtPromptEntry("board_paper", ArtCategory.BoardSkins, "1:1", false,
                "Top-down flat texture of a game board surface: dark charcoal-grey pressed paper/cardstock " +
                "with a subtle fibrous grain, like dark sugar paper. " + BoardSurfaceRule));
            // The natural-material boards have to opt out of the accent-colour licence that BackdropRules
            // grants ("bright accents are allowed only as thin, sparse highlights"). Wood grain reads to the
            // model as an invitation to run those accents along it: the first pass came back with two cyan
            // and two violet neon veins spanning the canvas edge to edge - the brightest thing in the image,
            // and lines, which BoardSurfaceRule already forbids. Saying "deep and muted rather than bright"
            // was not enough against an explicit permission, so the opt-out has to be explicit too.
            const string NoAccentColourRule =
                "This is a natural material and the only colours in it are that material's own: no cyan, no " +
                "violet, no neon and no glowing accent of any kind, and in particular never a bright line " +
                "running along the grain. No line, streak, vein or highlight may cross the canvas from one " +
                "edge to another. The brightest pixel in the image stays within a few percent of the base " +
                "tone.";

            entries.Add(new ArtPromptEntry("board_wood", ArtCategory.BoardSkins, "1:1", false,
                "Top-down flat texture of a game board surface: dark stained walnut tabletop with visible " +
                "but low-contrast wood grain, deep and muted rather than bright. " + NoAccentColourRule +
                " " + BoardSurfaceRule));
            entries.Add(new ArtPromptEntry("board_space", ArtCategory.BoardSkins, "1:1", false,
                "Top-down flat texture of a game board surface: deep-space backdrop with soft dark nebula " +
                "clouds in near-black navy and muted violet #7C5CFF tones and faint sparse stars. " + BoardSurfaceRule));

            // ============================================================
            // D. Visual effects (particle/animation sprites)
            // ============================================================

            entries.Add(new ArtPromptEntry("fx_place_particle", ArtCategory.Effects, "1:1", true,
                "A single small burst/spark particle sprite for a 'piece placed' visual effect: a soft " +
                "radial glow burst in cyan #35E6C6 with a few thin light rays radiating outward, centered, " +
                "simple clean particle sprite for a mobile game."));
            entries.Add(new ArtPromptEntry("fx_victory_confetti", ArtCategory.Effects, "1:1", true,
                "A cluster of colorful flat confetti shapes (small rectangles, circles and thin curled " +
                "ribbons) in the game's palette - cyan #35E6C6, violet #7C5CFF and gold #F4C948 - scattered " +
                "in a loose burst composition, centered, clean particle/sticker sprite for a victory " +
                "celebration effect."));
            entries.Add(new ArtPromptEntry("fx_winline_glow", ArtCategory.Effects, "1:1", true,
                "A single elongated soft glow streak sprite representing a winning-line highlight: a " +
                "horizontal bar of bright glowing light with a bright cyan-white core #35E6C6 fading to " +
                "transparent at both edges, simple clean glow sprite for a mobile game."));

            // ============================================================
            // E. Profile (avatars, frames, banners)
            // ============================================================

            // The six avatars are shown side by side in a picker grid and each one sits inside a profile
            // frame, so their discs have to be the same size. Asking the model for it is not enough - the
            // first batch came back spanning 60.1% to 78.1% of the canvas - so the framing is enforced
            // after the fact by SubjectNormalizer. 0.75 is the median of that batch, which keeps the
            // rescaling small in both directions.
            const float AvatarDiscCoverage = 0.75f;

            entries.Add(new ArtPromptEntry("avatar_fox", ArtCategory.Profile, "1:1", true,
                "Circular flat-vector mascot avatar: a friendly stylized fox face, front-facing, bold clean " +
                "shapes, a dark flat background disc #1C2733, cyan #35E6C6 used sparingly as an accent " +
                "(collar or eye glint), centered, mobile profile-picture icon style. Part of a set of six " +
                "avatars that should share one consistent flat-mascot art style.", subjectCoverage: AvatarDiscCoverage));
            entries.Add(new ArtPromptEntry("avatar_cat", ArtCategory.Profile, "1:1", true,
                "Circular flat-vector mascot avatar: a friendly stylized cat face, front-facing, bold clean " +
                "shapes, a dark flat background disc #1C2733, violet #7C5CFF used sparingly as an accent " +
                "(collar or eye glint), centered, mobile profile-picture icon style. Part of a set of six " +
                "avatars that should share one consistent flat-mascot art style.", subjectCoverage: AvatarDiscCoverage));
            entries.Add(new ArtPromptEntry("avatar_robot", ArtCategory.Profile, "1:1", true,
                "Circular flat-vector mascot avatar: a friendly stylized robot face with a glowing cyan " +
                "#35E6C6 visor/eye strip, front-facing, bold clean shapes, a dark flat background disc " +
                "#1C2733, centered, mobile profile-picture icon style. Part of a set of six avatars that " +
                "should share one consistent flat-mascot art style.", subjectCoverage: AvatarDiscCoverage));
            entries.Add(new ArtPromptEntry("avatar_owl", ArtCategory.Profile, "1:1", true,
                "Circular flat-vector mascot avatar: a friendly stylized owl face, front-facing, bold clean " +
                "shapes, a dark flat background disc #1C2733, gold #F4C948 used sparingly as an accent (beak " +
                "or eye glint), centered, mobile profile-picture icon style. Part of a set of six avatars " +
                "that should share one consistent flat-mascot art style.", subjectCoverage: AvatarDiscCoverage));
            entries.Add(new ArtPromptEntry("avatar_dragon", ArtCategory.Profile, "1:1", true,
                "Circular flat-vector mascot avatar: a friendly stylized baby-dragon face, front-facing, " +
                "bold clean shapes, a dark flat background disc #1C2733, violet #7C5CFF scale/horn accent, " +
                "centered, mobile profile-picture icon style. Part of a set of six avatars that should share " +
                "one consistent flat-mascot art style.", subjectCoverage: AvatarDiscCoverage));
            entries.Add(new ArtPromptEntry("avatar_astronaut", ArtCategory.Profile, "1:1", true,
                "Circular flat-vector mascot avatar: a friendly stylized astronaut helmet with a violet " +
                "#7C5CFF visor reflection, front-facing, bold clean shapes, a dark flat background disc " +
                "#1C2733, centered, mobile profile-picture icon style. Part of a set of six avatars that " +
                "should share one consistent flat-mascot art style.", subjectCoverage: AvatarDiscCoverage));

            // Every frame must present the SAME opening, or the UI needs a per-frame avatar scale. The
            // first batch came back with holes at 58.6% and 54.3% of the canvas while their outer diameters
            // were nearly identical - normalizing the outside would have left the openings mismatched, so
            // frames are normalized by their hole instead. 0.58 is neon's natural size, which keeps the
            // rescaling small and avoids pushing an enlarged ring off the canvas.
            const float FrameHoleCoverage = 0.58f;

            // Never ask a frame for a "transparent center": the API returns JPEG, so the model can only
            // depict transparency by drawing it, and gold_ornate came back with an opaque checkerboard
            // filling the ring. The hole is described as flat backdrop green instead, which the keyer lifts
            // out - ChromaKeyProcessor seeds on every hard match rather than from the image borders, so a
            // hole fully enclosed by the ring is still reached.
            entries.Add(new ArtPromptEntry("profile_frame_neon", ArtCategory.Profile, "1:1", true,
                "A circular decorative profile-picture frame/border ring: a thin cyan neon ring #35E6C6 " +
                "with subtle violet #7C5CFF accent notches at four points, mobile game profile-frame " +
                "cosmetic item. The entire area enclosed by the ring is empty backdrop and must be painted " +
                "in exactly the same flat green as the background behind it - nothing inside the ring, no " +
                "checkerboard, no pattern, no avatar.", holeCoverage: FrameHoleCoverage));
            entries.Add(new ArtPromptEntry("profile_frame_gold_ornate", ArtCategory.Profile, "1:1", true,
                "A circular decorative profile-picture frame/border ring: an ornate ridged gold #F4C948 " +
                "metallic ring with engraved flourish detailing, premium-feeling mobile game profile-frame " +
                "cosmetic item. The entire area enclosed by the ring is empty backdrop and must be painted " +
                "in exactly the same flat green as the background behind it - nothing inside the ring, no " +
                "checkerboard, no pattern, no avatar.", holeCoverage: FrameHoleCoverage));

            entries.Add(new ArtPromptEntry("profile_banner_neon", ArtCategory.Profile, "16:9", false,
                "Wide profile banner background: dark flat backdrop #0B0F14 with bold diagonal cyan #35E6C6 " +
                "and violet #7C5CFF neon light streaks, energetic but clean. Leave calm, low-detail negative " +
                "space in the lower-left area where an avatar and name will be placed on top later. Nothing " +
                "in this image is written or lettered - the streaks are pure abstract shapes."));
            entries.Add(new ArtPromptEntry("profile_banner_space", ArtCategory.Profile, "16:9", false,
                "Wide profile banner background: deep-space scene with nebula clouds in navy and violet " +
                "#7C5CFF tones and scattered stars. Leave calm, low-detail negative space in the lower-left " +
                "area where an avatar and name will be placed on top later."));

            // ============================================================
            // F. Reactions / stickers - icons only, "gg"/"nice"/etc. are represented visually, never as
            // literal letters (that would violate the no-text rule).
            // ============================================================

            entries.Add(new ArtPromptEntry("reaction_gg", ArtCategory.Reactions, "1:1", true,
                "Sticker-style icon representing friendly post-match sportsmanship ('good game'): two " +
                "simplified flat hands doing a fist-bump, one sleeve cyan #35E6C6 and one sleeve violet " +
                "#7C5CFF, small motion-impact lines, bold clean sticker illustration, no letters."));
            entries.Add(new ArtPromptEntry("reaction_nice_move", ArtCategory.Reactions, "1:1", true,
                "Sticker-style icon representing praise for a good move: a bright glowing star-burst shape " +
                "with a small flat thumbs-up shape layered at its center, gold #F4C948 and cyan #35E6C6 " +
                "accents, bold clean sticker illustration, no letters."));
            entries.Add(new ArtPromptEntry("reaction_rematch", ArtCategory.Reactions, "1:1", true,
                "Sticker-style icon representing 'play again': two curved arrows forming a circular " +
                "refresh/replay loop, one arrow cyan #35E6C6 and one arrow violet #7C5CFF, bold clean " +
                "sticker illustration, no letters."));
            entries.Add(new ArtPromptEntry("reaction_applause", ArtCategory.Reactions, "1:1", true,
                "Sticker-style icon representing applause: a pair of simplified flat clapping hands with " +
                "small motion lines and warm gold #F4C948 sparkle accents around them, bold clean sticker " +
                "illustration, no letters."));
            entries.Add(new ArtPromptEntry("reaction_thinking", ArtCategory.Reactions, "1:1", true,
                "Sticker-style icon representing 'thinking': a simplified flat face silhouette resting on a " +
                "hand, with a small glowing thought bubble above it containing a simple gear shape, violet " +
                "#7C5CFF accent, bold clean sticker illustration, no letters."));
            entries.Add(new ArtPromptEntry("reaction_wave", ArtCategory.Reactions, "1:1", true,
                "Sticker-style icon representing a friendly wave hello/goodbye: a single simplified flat " +
                "waving open-hand shape with soft motion lines, cyan #35E6C6 accent, bold clean sticker " +
                "illustration, no letters."));

            // ============================================================
            // G. Ranked (Milestone 5) - tier badges must read as one family, only material/glow changes.
            // ============================================================

            entries.Add(new ArtPromptEntry("ranked_badge_bronze", ArtCategory.Ranked, "1:1", true,
                "Circular competitive rank badge icon: flat-metallic bronze material with a bold minimal " +
                "shield emblem silhouette centered inside the badge, subtle metallic highlight, premium " +
                "mobile game rank badge. Part of a five-tier badge family (bronze, silver, gold, platinum, " +
                "diamond) - all five must share the exact same shield-emblem silhouette and only differ in " +
                "material/color/glow, so they read as one consistent set."));
            entries.Add(new ArtPromptEntry("ranked_badge_silver", ArtCategory.Ranked, "1:1", true,
                "Circular competitive rank badge icon: flat-metallic silver material with a bold minimal " +
                "shield emblem silhouette centered inside the badge, subtle metallic highlight, premium " +
                "mobile game rank badge. Part of a five-tier badge family (bronze, silver, gold, platinum, " +
                "diamond) - all five must share the exact same shield-emblem silhouette and only differ in " +
                "material/color/glow, so they read as one consistent set."));
            entries.Add(new ArtPromptEntry("ranked_badge_gold", ArtCategory.Ranked, "1:1", true,
                "Circular competitive rank badge icon: flat-metallic gold material, color #F4C948, with a " +
                "bold minimal shield emblem silhouette centered inside the badge, subtle metallic highlight, " +
                "premium mobile game rank badge. Part of a five-tier badge family (bronze, silver, gold, " +
                "platinum, diamond) - all five must share the exact same shield-emblem silhouette and only " +
                "differ in material/color/glow, so they read as one consistent set."));
            entries.Add(new ArtPromptEntry("ranked_badge_platinum", ArtCategory.Ranked, "1:1", true,
                "Circular competitive rank badge icon: cool platinum/white-blue metallic material with a " +
                "bold minimal shield emblem silhouette centered inside the badge, subtle metallic highlight, " +
                "premium mobile game rank badge. Part of a five-tier badge family (bronze, silver, gold, " +
                "platinum, diamond) - all five must share the exact same shield-emblem silhouette and only " +
                "differ in material/color/glow, so they read as one consistent set."));
            entries.Add(new ArtPromptEntry("ranked_badge_diamond", ArtCategory.Ranked, "1:1", true,
                "Circular competitive rank badge icon: faceted diamond-crystal material with a subtle cyan " +
                "#35E6C6 inner glow, bold minimal shield emblem silhouette centered inside the badge, the " +
                "most premium-looking badge of the set. Part of a five-tier badge family (bronze, silver, " +
                "gold, platinum, diamond) - all five must share the exact same shield-emblem silhouette and " +
                "only differ in material/color/glow, so they read as one consistent set."));

            entries.Add(new ArtPromptEntry("ranked_banner_top100", ArtCategory.Ranked, "16:9", false,
                "Wide premium banner background celebrating a global top-100 seasonal ranked achievement: " +
                "dark backdrop #0B0F14 with radiant gold #F4C948 and cyan #35E6C6 light rays converging " +
                "toward the center, subtle particle sparkles. Leave calm, low-detail negative space where a " +
                "badge and player name will be placed on top later."));

            // ============================================================
            // H. Store (category icons + currency pack art)
            // ============================================================

            entries.Add(new ArtPromptEntry("store_icon_pieces", ArtCategory.Store, "1:1", true,
                "Flat minimal icon representing the 'game pieces / skins' store category: a simplified X " +
                "and O pair, X in cyan #35E6C6 and O in violet #7C5CFF, bold flat shapes, mobile store tab " +
                "icon style."));
            entries.Add(new ArtPromptEntry("store_icon_boards", ArtCategory.Store, "1:1", true,
                "Flat minimal icon representing the 'game boards' store category: a simplified 3x3 grid " +
                "drawn as thick bright cyan #35E6C6 bars only - the squares between the bars are empty " +
                "background, NOT a filled dark tile, and there is no panel or card behind the grid. Bold " +
                "flat shapes, mobile store tab icon style, matching a set of cut-out glyph icons."));
            entries.Add(new ArtPromptEntry("store_icon_fx", ArtCategory.Store, "1:1", true,
                "Flat minimal icon representing the 'visual effects' store category: a simplified " +
                "sparkle/burst shape with gold #F4C948 and cyan #35E6C6 accents, bold flat shapes, mobile " +
                "store tab icon style."));
            entries.Add(new ArtPromptEntry("store_icon_sound", ArtCategory.Store, "1:1", true,
                "Flat minimal icon representing the 'sound' store category: a simplified speaker shape with " +
                "small sound-wave arcs, violet #7C5CFF accent, bold flat shapes, mobile store tab icon " +
                "style."));
            entries.Add(new ArtPromptEntry("store_icon_profile", ArtCategory.Store, "1:1", true,
                "Flat minimal icon representing the 'profile customization' store category: a simplified " +
                "circular avatar silhouette inside a decorative frame ring, cyan #35E6C6 frame accent, bold " +
                "flat shapes, mobile store tab icon style."));
            entries.Add(new ArtPromptEntry("store_icon_reactions", ArtCategory.Store, "1:1", true,
                "Flat minimal icon representing the 'reactions / stickers' store category: a simplified " +
                "speech-bubble shape with a small star inside it, violet #7C5CFF accent, bold flat shapes, " +
                "mobile store tab icon style."));
            entries.Add(new ArtPromptEntry("store_icon_currency", ArtCategory.Store, "1:1", true,
                "Flat minimal icon representing the 'currency' store category: a simplified stacked-coins " +
                "shape combining a gold #F4C948 coin and a violet #B58CFF gem, bold flat shapes, mobile " +
                "store tab icon style."));

            // All three tiers are shown the canonical gem and coin icons as reference (see
            // GetReferenceAssets) and told to reuse that exact design. Generated from wording alone they
            // came back as three unrelated gems, so the tiers did not read as one product in three amounts.
            const string CurrencyPackRule =
                "The two reference images are the game's hard-currency gem and soft-currency coin. Reuse " +
                "those EXACT designs - same faceting, same colours, same proportions - only repeated and " +
                "arranged; do not invent a different gem or coin. All three pack tiers share one round " +
                "cluster shape filling the frame and differ only in how much is in the cluster. Dark flat " +
                "card background #121821 with a radial highlight behind the cluster. No frame, border, card " +
                "outline or corner brackets - the UI draws its own card.";

            entries.Add(new ArtPromptEntry("currency_pack_small", ArtCategory.Store, "1:1", false,
                "Product illustration card for a SMALL hard-currency purchase pack: a small, tidy cluster of " +
                "a handful of gems with a couple of coins mixed among them and a soft glow, clearly the " +
                "least generous of the three tiers. " + CurrencyPackRule));
            entries.Add(new ArtPromptEntry("currency_pack_medium", ArtCategory.Store, "1:1", false,
                "Product illustration card for a MEDIUM hard-currency purchase pack: a fuller cluster with " +
                "a few coins mixed among the gems and a brighter glow, visibly more generous than the small " +
                "pack and visibly less than the large one. A THIRD reference image is provided: it is the " +
                "small pack of this same set. Copy its composition exactly - gems and coins floating " +
                "directly on the plain dark background with nothing drawn behind or around them - and only " +
                "make the cluster bigger. Note that it has NO rounded panel, NO card, NO lighter rectangle " +
                "behind the cluster and no border of any kind; yours must have none either. " +
                CurrencyPackRule));
            entries.Add(new ArtPromptEntry("currency_pack_large", ArtCategory.Store, "1:1", false,
                "Product illustration card for a LARGE hard-currency purchase pack: a huge, densely packed " +
                "cluster of gems and coins filling the frame corner to corner with a strong radiant glow " +
                "and a light burst, clearly the most generous of the three tiers - it must cover visibly " +
                "more of the card than the medium pack. Do not draw it as a pyramid pile resting on the " +
                "ground: that leaves the top of the frame empty and makes it read smaller than the medium " +
                "pack. " + CurrencyPackRule));

            return entries;
        }
    }
}
