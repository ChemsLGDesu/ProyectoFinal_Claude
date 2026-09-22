using System.Collections.Generic;
using UnityEngine;

namespace TTTXO.Game.Editor
{
    /// <summary>
    /// Turns the flat chroma-key backdrop of a generated image into real alpha.
    ///
    /// The image API this tool calls only returns JPEG (asking for <c>image/png</c> is an HTTP 400), and
    /// JPEG has no alpha channel - so a prompt asking for "transparent background" makes the model *draw*
    /// a checkerboard instead of leaving one. The pipeline therefore asks for a solid, flat backdrop in a
    /// key color that appears nowhere in the game palette (see <see cref="ArtPromptCatalog.ChromaKeyHex"/>)
    /// and knocks that backdrop out here.
    ///
    /// The knockout seeds on every pixel that hard-matches the key color anywhere in the image, then flood
    /// fills outward through near-matches to catch antialiased edges. Seeding globally rather than from the
    /// image borders is deliberate: models routinely draw an outlined shape - a wireframe globe, a neon
    /// tube - whose interior cells are backdrop fully enclosed by artwork. A border-seeded fill can never
    /// reach those pockets and leaves them as bright green holes in the sprite.
    ///
    /// A global seed is safe here only because the key is pure green: the nearest palette color, the cyan
    /// accent #35E6C6, sits ~206 away in RGB distance, well past even <see cref="SoftThreshold"/>. If the
    /// key color ever changes, re-check that margin before trusting this.
    /// </summary>
    public static class ChromaKeyProcessor
    {
        /// <summary>Squared RGB distance (0-255 per channel) at or below which a pixel is definitely backdrop and becomes fully transparent. Generous because JPEG compression smears color around high-contrast edges.</summary>
        private const float HardThreshold = 90f;

        /// <summary>Squared RGB distance up to which a border-connected pixel is treated as a partially covered edge pixel and gets fractional alpha. Beyond this, a pixel is subject and stays opaque.</summary>
        private const float SoftThreshold = 165f;

        /// <summary>Result of one knockout pass.</summary>
        public readonly struct Result
        {
            public bool Success { get; }
            public byte[] PngBytes { get; }
            public string ErrorMessage { get; }

            /// <summary>Share of pixels that ended up fully transparent, 0-1. Surfaced so the caller can warn when a knockout plainly did not work (no backdrop found, or the whole image was keyed away).</summary>
            public float TransparentFraction { get; }

            private Result(bool success, byte[] pngBytes, string errorMessage, float transparentFraction)
            {
                Success = success;
                PngBytes = pngBytes;
                ErrorMessage = errorMessage;
                TransparentFraction = transparentFraction;
            }

            public static Result Ok(byte[] pngBytes, float transparentFraction) => new(true, pngBytes, null, transparentFraction);
            public static Result Failure(string errorMessage) => new(false, null, errorMessage, 0f);
        }

        /// <summary>Decodes <paramref name="imageBytes"/> (JPEG or PNG), removes the border-connected chroma backdrop, and re-encodes as a real PNG with an alpha channel. Never throws - decode failures come back as a failed <see cref="Result"/>.</summary>
        public static Result KnockOutBackground(byte[] imageBytes, Color32 keyColor)
        {
            if (imageBytes == null || imageBytes.Length == 0)
            {
                return Result.Failure("No image bytes to process.");
            }

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            Texture2D output = null;
            try
            {
                if (!ImageConversion.LoadImage(decoded, imageBytes, markNonReadable: false))
                {
                    return Result.Failure("Could not decode the generated image bytes.");
                }

                int width = decoded.width;
                int height = decoded.height;
                Color32[] pixels = decoded.GetPixels32();

                // 0 = untested, 1 = backdrop (alpha 0), 2 = edge pixel (fractional alpha), 3 = subject.
                var classification = new byte[pixels.Length];
                var queue = new Queue<int>();

                EnqueueHardMatches(queue, classification, pixels, keyColor);
                FloodFill(queue, classification, pixels, keyColor, width, height);

                int clearedCount = ApplyAlpha(pixels, classification, keyColor);

                // LoadImage adopts the *source file's* format, and JPEG decodes to alpha-less RGB24 - writing
                // the keyed pixels back into `decoded` would silently discard every alpha computed above and
                // yield a fully opaque PNG. Re-host them in a texture that is guaranteed to have the channel.
                output = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
                output.SetPixels32(pixels);
                output.Apply(updateMipmaps: false);

                byte[] png = ImageConversion.EncodeToPNG(output);
                if (png == null || png.Length == 0)
                {
                    return Result.Failure("Re-encoding the keyed image to PNG produced no bytes.");
                }

                return Result.Ok(png, clearedCount / (float)pixels.Length);
            }
            finally
            {
                Object.DestroyImmediate(decoded);
                if (output != null)
                {
                    Object.DestroyImmediate(output);
                }
            }
        }

        /// <summary>Seeds the flood fill with every pixel in the image that hard-matches the key color, wherever it sits - including backdrop pockets fully enclosed by artwork, which is the whole reason this is not border-seeded.</summary>
        private static void EnqueueHardMatches(Queue<int> queue, byte[] classification, Color32[] pixels, Color32 keyColor)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                if (Distance(pixels[i], keyColor) <= HardThreshold)
                {
                    classification[i] = 1;
                    queue.Enqueue(i);
                }
            }
        }

        private static void TrySeed(Queue<int> queue, byte[] classification, Color32[] pixels, Color32 keyColor, int index)
        {
            if (classification[index] != 0)
            {
                return;
            }

            float distance = Distance(pixels[index], keyColor);
            if (distance > SoftThreshold)
            {
                classification[index] = 3;
                return;
            }

            classification[index] = distance <= HardThreshold ? (byte)1 : (byte)2;
            queue.Enqueue(index);
        }

        /// <summary>4-connected flood fill outward from the seeded border. Stops at any pixel further than <see cref="SoftThreshold"/> from the key color, which is the subject's silhouette.</summary>
        private static void FloodFill(Queue<int> queue, byte[] classification, Color32[] pixels, Color32 keyColor, int width, int height)
        {
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % width;
                int y = index / width;

                if (x > 0)
                {
                    TrySeed(queue, classification, pixels, keyColor, index - 1);
                }

                if (x < width - 1)
                {
                    TrySeed(queue, classification, pixels, keyColor, index + 1);
                }

                if (y > 0)
                {
                    TrySeed(queue, classification, pixels, keyColor, index - width);
                }

                if (y < height - 1)
                {
                    TrySeed(queue, classification, pixels, keyColor, index + width);
                }
            }
        }

        /// <summary>Writes the alpha decided by <paramref name="classification"/> and de-spills edge pixels, so an antialiased silhouette does not keep a rim of the key color. Returns how many pixels became fully transparent.</summary>
        private static int ApplyAlpha(Color32[] pixels, byte[] classification, Color32 keyColor)
        {
            int cleared = 0;

            for (int i = 0; i < pixels.Length; i++)
            {
                switch (classification[i])
                {
                    case 1:
                        pixels[i] = new Color32(0, 0, 0, 0);
                        cleared++;
                        break;

                    case 2:
                    {
                        // Partially covered edge pixel: alpha ramps from 0 at the hard threshold to fully
                        // opaque at the soft one, and the key color is pulled back out of the RGB so the
                        // remaining fringe does not tint the sprite.
                        float distance = Distance(pixels[i], keyColor);
                        float t = Mathf.InverseLerp(HardThreshold, SoftThreshold, distance);
                        pixels[i] = new Color32(
                            DeSpill(pixels[i].r, keyColor.r, t),
                            DeSpill(pixels[i].g, keyColor.g, t),
                            DeSpill(pixels[i].b, keyColor.b, t),
                            (byte)Mathf.RoundToInt(Mathf.Clamp01(t) * 255f));
                        break;
                    }
                }
            }

            return cleared;
        }

        /// <summary>Removes the key color's contribution from a partially transparent pixel (un-premultiply against the backdrop), clamped to a byte.</summary>
        private static byte DeSpill(byte channel, byte keyChannel, float coverage)
        {
            if (coverage <= 0.001f)
            {
                return channel;
            }

            float recovered = (channel - keyChannel * (1f - coverage)) / coverage;
            return (byte)Mathf.Clamp(Mathf.RoundToInt(recovered), 0, 255);
        }

        private static float Distance(Color32 a, Color32 b)
        {
            float dr = a.r - b.r;
            float dg = a.g - b.g;
            float db = a.b - b.b;
            return Mathf.Sqrt(dr * dr + dg * dg + db * db);
        }
    }
}
