using System.Collections.Generic;
using UnityEngine;

namespace TTTXO.Game.Editor
{
    /// <summary>
    /// Shrinks a generated PNG by reducing how many distinct colours it contains, without touching its
    /// resolution or its alpha channel.
    ///
    /// The art comes back from the API as JPEG, so even a flat-vector illustration arrives carrying
    /// thousands of near-duplicate colours that are pure compression noise - one avatar held 10,764 unique
    /// colours to render maybe a dozen real ones. Those near-duplicates are what make the re-encoded PNG
    /// large: DEFLATE cannot find repetition in a field where every pixel differs slightly from its
    /// neighbour. Collapsing them onto a small palette restores the repetition and the file drops by roughly
    /// two thirds, with the differences landing on antialiased edges where they are invisible.
    ///
    /// Quantization is applied to RGB only. Alpha is copied through untouched: it carries the chroma-key
    /// cut-out, and banding it would chew the silhouette.
    ///
    /// NOT suitable for art with real gradients - metallic tier badges, nebula banners, the space board.
    /// A small palette bands those visibly. It is meant for the flat-vector art the direction calls for.
    /// </summary>
    public static class TextureMinifier
    {
        /// <summary>Pixels at or below this alpha contribute nothing visible, so they are left out of the palette and flattened to transparent black - which compresses far better than the noise they usually hold.</summary>
        private const byte InvisibleAlpha = 8;

        /// <summary>How many distinct alpha values the indexed form may keep between fully clear and fully opaque. Those two are always exact; everything in between is antialiased edge, well under a tenth of a percent of a typical piece.</summary>
        private const int AlphaLevels = 16;

        public readonly struct Result
        {
            public bool Success { get; }
            public byte[] PngBytes { get; }
            public string ErrorMessage { get; }
            public int ColoursBefore { get; }
            public int ColoursAfter { get; }

            private Result(bool success, byte[] png, string error, int before, int after)
            {
                Success = success;
                PngBytes = png;
                ErrorMessage = error;
                ColoursBefore = before;
                ColoursAfter = after;
            }

            public static Result Ok(byte[] png, int before, int after) => new(true, png, null, before, after);
            public static Result Failure(string error) => new(false, null, error, 0, 0);
        }

        /// <summary>
        /// Returns <paramref name="pixels"/> with RGB collapsed onto a palette of at most
        /// <paramref name="maxColours"/> entries, alpha untouched. Null when there is nothing visible to
        /// quantize. Exposed so <see cref="IndexedPngEncoder"/> can reuse the same palette rather than
        /// deriving a second, different one.
        /// </summary>
        public static Color32[] QuantizeToPalette(Color32[] pixels, int maxColours)
        {
            var histogram = BuildHistogram(pixels);
            if (histogram.Count == 0)
            {
                return null;
            }

            if (histogram.Count <= maxColours)
            {
                return pixels;
            }

            var palette = BuildPalette(histogram, maxColours);
            var remap = BuildRemap(histogram, palette);
            var result = new Color32[pixels.Length];

            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a <= InvisibleAlpha)
                {
                    result[i] = new Color32(0, 0, 0, 0);
                    continue;
                }

                int key = (pixels[i].r << 16) | (pixels[i].g << 8) | pixels[i].b;
                Color32 mapped = remap[key];
                result[i] = new Color32(mapped.r, mapped.g, mapped.b, pixels[i].a);
            }

            return result;
        }

        private static Dictionary<int, int> BuildHistogram(Color32[] pixels)
        {
            var histogram = new Dictionary<int, int>();
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a <= InvisibleAlpha)
                {
                    continue;
                }

                int key = (pixels[i].r << 16) | (pixels[i].g << 8) | pixels[i].b;
                histogram.TryGetValue(key, out int count);
                histogram[key] = count + 1;
            }
            return histogram;
        }

        /// <summary>
        /// Re-encodes an opaque JPEG at <paramref name="quality"/>. The counterpart to <see cref="Minify"/>
        /// for backdrops: palette quantization is the wrong tool there, because a board surface or a nebula
        /// banner is a real gradient that a small palette bands, and PNG on that kind of content is larger
        /// than JPEG anyway.
        ///
        /// The art arrives from the API at a needlessly high quality setting - a 1.8 MB nebula re-encodes
        /// to 214 KB with a mean per-pixel change of 2.6 out of 765.
        ///
        /// Re-encoding a JPEG is generation loss, so this refuses to act unless the saving is real (see
        /// <see cref="WorthwhileRatio"/>): running the batch twice must not quietly degrade it a second time.
        /// </summary>
        public static Result MinifyJpeg(byte[] jpegBytes, int quality)
        {
            if (jpegBytes == null || jpegBytes.Length == 0)
            {
                return Result.Failure("No image bytes to re-encode.");
            }

            if (quality < 1 || quality > 100)
            {
                return Result.Failure($"Quality {quality} is outside the 1-100 range.");
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            try
            {
                if (!ImageConversion.LoadImage(texture, jpegBytes, markNonReadable: false))
                {
                    return Result.Failure("Could not decode the image bytes.");
                }

                byte[] encoded = ImageConversion.EncodeToJPG(texture, quality);
                if (encoded == null || encoded.Length == 0)
                {
                    return Result.Failure("Re-encoding produced no bytes.");
                }

                if (encoded.Length > jpegBytes.Length * WorthwhileRatio)
                {
                    // Already at or near this quality - re-encoding would only add generation loss.
                    return Result.Ok(jpegBytes, 0, 0);
                }

                return Result.Ok(encoded, 0, 0);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>A re-encode has to save at least this much to be worth the generation loss it costs.</summary>
        private const float WorthwhileRatio = 0.9f;

        /// <summary>Quantizes <paramref name="pngBytes"/> to at most <paramref name="maxColours"/> colours and re-encodes. Never throws.</summary>
        public static Result Minify(byte[] pngBytes, int maxColours)
        {
            if (pngBytes == null || pngBytes.Length == 0)
            {
                return Result.Failure("No image bytes to minify.");
            }

            if (maxColours < 2 || maxColours > 4096)
            {
                return Result.Failure($"maxColours {maxColours} is outside the sensible 2-4096 range.");
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            Texture2D output = null;
            try
            {
                if (!ImageConversion.LoadImage(texture, pngBytes, markNonReadable: false))
                {
                    return Result.Failure("Could not decode the image bytes.");
                }

                int w = texture.width, h = texture.height;
                Color32[] px = texture.GetPixels32();

                // Work from the unique-colour histogram, not the pixel array: an avatar has millions of
                // pixels but only thousands of distinct colours, so every step below is thousands of times
                // cheaper without changing the result.
                var histogram = new Dictionary<int, int>();
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a <= InvisibleAlpha)
                    {
                        continue;
                    }

                    int key = (px[i].r << 16) | (px[i].g << 8) | px[i].b;
                    histogram.TryGetValue(key, out int count);
                    histogram[key] = count + 1;
                }

                if (histogram.Count == 0)
                {
                    return Result.Failure("The image has no visible pixels to quantize.");
                }

                int before = histogram.Count;
                if (before <= maxColours)
                {
                    // Already simple enough - re-encoding would only churn the file.
                    return Result.Ok(pngBytes, before, before);
                }

                var palette = BuildPalette(histogram, maxColours);
                var remap = BuildRemap(histogram, palette);

                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a <= InvisibleAlpha)
                    {
                        // Flatten fully invisible pixels to a single value so they compress to almost
                        // nothing, instead of leaving whatever colour hid under zero alpha.
                        px[i] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    int key = (px[i].r << 16) | (px[i].g << 8) | px[i].b;
                    Color32 mapped = remap[key];
                    px[i] = new Color32(mapped.r, mapped.g, mapped.b, px[i].a);
                }

                // Prefer the indexed form - one byte per pixel instead of four. It only fails when the
                // (colour, alpha) pairs cannot be squeezed into 256 palette entries, in which case the
                // truecolour encoder still produces a valid, if larger, file.
                byte[] png = IndexedPngEncoder.Encode(px, w, h, maxColours, AlphaLevels);

                if (png == null || png.Length == 0)
                {
                    output = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
                    output.SetPixels32(px);
                    output.Apply(updateMipmaps: false);
                    png = ImageConversion.EncodeToPNG(output);
                }

                if (png == null || png.Length == 0)
                {
                    return Result.Failure("Re-encoding the quantized image produced no bytes.");
                }

                return Result.Ok(png, before, palette.Count);
            }
            finally
            {
                Object.DestroyImmediate(texture);
                if (output != null)
                {
                    Object.DestroyImmediate(output);
                }
            }
        }

        /// <summary>One axis-aligned box of colour space holding a slice of the histogram.</summary>
        private sealed class Box
        {
            public List<int> Colours;
            public long Population;
            public byte MinR, MaxR, MinG, MaxG, MinB, MaxB;

            public int LongestAxisLength => Mathf.Max(MaxR - MinR, Mathf.Max(MaxG - MinG, MaxB - MinB));
        }

        /// <summary>
        /// Median cut: start with every colour in one box and repeatedly split the box that spans the widest
        /// range of a single channel, at the population median of that channel. Splitting by range rather
        /// than by population keeps rare-but-distinct colours - an eye glint, an accent - from being merged
        /// into a large flat area just because that area covers more pixels.
        /// </summary>
        private static List<Color32> BuildPalette(Dictionary<int, int> histogram, int maxColours)
        {
            var initial = new Box { Colours = new List<int>(histogram.Keys) };
            Measure(initial, histogram);

            var boxes = new List<Box> { initial };

            while (boxes.Count < maxColours)
            {
                Box target = null;
                int widest = 0;
                foreach (var box in boxes)
                {
                    if (box.Colours.Count < 2)
                    {
                        continue;
                    }

                    int span = box.LongestAxisLength;
                    if (span > widest)
                    {
                        widest = span;
                        target = box;
                    }
                }

                if (target == null || widest == 0)
                {
                    break;   // every remaining box is a single colour - nothing left to split
                }

                if (!TrySplit(target, histogram, out Box left, out Box right))
                {
                    break;
                }

                boxes.Remove(target);
                boxes.Add(left);
                boxes.Add(right);
            }

            var palette = new List<Color32>(boxes.Count);
            foreach (var box in boxes)
            {
                palette.Add(Average(box, histogram));
            }

            return palette;
        }

        private static bool TrySplit(Box box, Dictionary<int, int> histogram, out Box left, out Box right)
        {
            left = null;
            right = null;

            int rSpan = box.MaxR - box.MinR;
            int gSpan = box.MaxG - box.MinG;
            int bSpan = box.MaxB - box.MinB;
            int axis = rSpan >= gSpan && rSpan >= bSpan ? 0 : gSpan >= bSpan ? 1 : 2;

            box.Colours.Sort((a, b) => Channel(a, axis).CompareTo(Channel(b, axis)));

            // Split at the POPULATION median, not the list midpoint: a box can hold one colour covering
            // half the image and a hundred covering a few pixels each, and cutting by list position would
            // hand the dominant colour a box it does not need.
            long half = box.Population / 2;
            long running = 0;
            int cut = 0;
            for (int i = 0; i < box.Colours.Count; i++)
            {
                running += histogram[box.Colours[i]];
                if (running >= half)
                {
                    cut = i;
                    break;
                }
            }

            cut = Mathf.Clamp(cut, 0, box.Colours.Count - 2);

            left = new Box { Colours = box.Colours.GetRange(0, cut + 1) };
            right = new Box { Colours = box.Colours.GetRange(cut + 1, box.Colours.Count - cut - 1) };
            Measure(left, histogram);
            Measure(right, histogram);
            return true;
        }

        private static void Measure(Box box, Dictionary<int, int> histogram)
        {
            box.MinR = 255; box.MinG = 255; box.MinB = 255;
            box.MaxR = 0; box.MaxG = 0; box.MaxB = 0;
            box.Population = 0;

            foreach (int key in box.Colours)
            {
                byte r = (byte)(key >> 16), g = (byte)(key >> 8), b = (byte)key;
                if (r < box.MinR) box.MinR = r;
                if (r > box.MaxR) box.MaxR = r;
                if (g < box.MinG) box.MinG = g;
                if (g > box.MaxG) box.MaxG = g;
                if (b < box.MinB) box.MinB = b;
                if (b > box.MaxB) box.MaxB = b;
                box.Population += histogram[key];
            }
        }

        /// <summary>Population-weighted mean of a box, so the palette entry lands on the colour most pixels actually use rather than in the middle of the box.</summary>
        private static Color32 Average(Box box, Dictionary<int, int> histogram)
        {
            double r = 0, g = 0, b = 0;
            long total = 0;

            foreach (int key in box.Colours)
            {
                int count = histogram[key];
                r += ((key >> 16) & 0xFF) * (double)count;
                g += ((key >> 8) & 0xFF) * (double)count;
                b += (key & 0xFF) * (double)count;
                total += count;
            }

            if (total == 0)
            {
                return new Color32(0, 0, 0, 255);
            }

            return new Color32((byte)Mathf.Clamp((int)(r / total + 0.5), 0, 255),
                (byte)Mathf.Clamp((int)(g / total + 0.5), 0, 255),
                (byte)Mathf.Clamp((int)(b / total + 0.5), 0, 255), 255);
        }

        /// <summary>Maps every source colour to its nearest palette entry once, so the per-pixel pass is a dictionary lookup rather than a palette search.</summary>
        private static Dictionary<int, Color32> BuildRemap(Dictionary<int, int> histogram, List<Color32> palette)
        {
            var remap = new Dictionary<int, Color32>(histogram.Count);

            foreach (int key in histogram.Keys)
            {
                int r = (key >> 16) & 0xFF, g = (key >> 8) & 0xFF, b = key & 0xFF;
                int best = 0, bestDistance = int.MaxValue;

                for (int i = 0; i < palette.Count; i++)
                {
                    int dr = r - palette[i].r, dg = g - palette[i].g, db = b - palette[i].b;
                    int distance = dr * dr + dg * dg + db * db;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }

                remap[key] = palette[best];
            }

            return remap;
        }

        private static int Channel(int key, int axis)
        {
            return axis switch
            {
                0 => (key >> 16) & 0xFF,
                1 => (key >> 8) & 0xFF,
                _ => key & 0xFF,
            };
        }
    }
}
