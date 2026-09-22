using System;
using UnityEngine;

namespace TTTXO.Game.Editor
{
    /// <summary>
    /// Divides the radial lighting falloff out of a board surface, so the corners end up as bright as the
    /// centre.
    ///
    /// This is post-process rather than prompt because a vignette is measurable and exact, which is the
    /// line the tool's design note draws: ask for artistic judgement, impose what can be measured. The
    /// asking was tried first and lost - <c>BoardSurfaceRule</c> has said "lit uniformly with no vignette,
    /// no central focal point" since the category existed, and all four boards came back with a centre
    /// 1.47x to 1.64x brighter than their corners anyway. A model rendering "a tabletop" renders it lit,
    /// and studio lighting has falloff.
    ///
    /// Board skins specifically, not every opaque entry: a board is required to be an even, seamless
    /// surface that gets scaled and cropped to four different board sizes, so a bright middle is a defect.
    /// A profile banner is hero art with intentional composition, and flattening it radially would destroy
    /// the thing it was drawn for.
    /// </summary>
    public static class BackdropFlattener
    {
        /// <summary>How many radial bins the falloff curve is sampled into. 64 over a 2048px image is ~16px
        /// per bin - fine enough to follow a lens-style falloff, coarse enough that texture detail averages
        /// out instead of being fitted and then divided back into itself.</summary>
        private const int RadialBins = 64;

        /// <summary>
        /// Gain ceiling. A vignette this strong is a lighting artefact, not content; anything past it means
        /// the measurement found real dark content (a corner that is genuinely black) and amplifying it
        /// would just amplify JPEG noise.
        /// </summary>
        private const float MaxGain = 2.5f;

        /// <summary>Half-width of the smoothing window over the radial profile, in bins. Without it, a bin
        /// that happens to catch a dark knot becomes a visible ring in the corrected image.</summary>
        private const int SmoothingRadius = 4;

        public readonly struct Result
        {
            public bool Success { get; }
            public byte[] JpegBytes { get; }
            public string ErrorMessage { get; }

            /// <summary>Centre-to-corner luminance ratio before correction. 1.0 is perfectly flat.</summary>
            public float VignetteBefore { get; }

            /// <summary>The same ratio after correction, so a caller can report the change it actually got
            /// rather than the one it assumed.</summary>
            public float VignetteAfter { get; }

            private Result(bool success, byte[] jpeg, string error, float before, float after)
            {
                Success = success;
                JpegBytes = jpeg;
                ErrorMessage = error;
                VignetteBefore = before;
                VignetteAfter = after;
            }

            public static Result Ok(byte[] jpeg, float before, float after) => new(true, jpeg, null, before, after);
            public static Result Failure(string error) => new(false, null, error, 0f, 0f);
        }

        /// <summary>
        /// Measures the image's radial luminance profile and multiplies it back out. Returns re-encoded
        /// JPEG bytes at <paramref name="quality"/>; the caller still owns the decision to re-encode again
        /// for size.
        /// </summary>
        public static Result Flatten(byte[] imageBytes, int quality)
        {
            if (imageBytes == null || imageBytes.Length == 0)
            {
                return Result.Failure("No image bytes to flatten.");
            }

            var texture = new Texture2D(2, 2);
            try
            {
                if (!texture.LoadImage(imageBytes))
                {
                    return Result.Failure("Could not decode the image.");
                }

                int width = texture.width;
                int height = texture.height;
                var pixels = texture.GetPixels32();

                float centreX = (width - 1) * 0.5f;
                float centreY = (height - 1) * 0.5f;
                float maxRadius = Mathf.Sqrt(centreX * centreX + centreY * centreY);

                var binSum = new double[RadialBins];
                var binCount = new int[RadialBins];

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float dx = x - centreX;
                        float dy = y - centreY;
                        int bin = Mathf.Clamp(Mathf.FloorToInt(Mathf.Sqrt(dx * dx + dy * dy) / maxRadius * (RadialBins - 1)), 0, RadialBins - 1);
                        binSum[bin] += Luminance(pixels[y * width + x]);
                        binCount[bin]++;
                    }
                }

                var profile = new float[RadialBins];
                for (int i = 0; i < RadialBins; i++)
                {
                    profile[i] = binCount[i] > 0 ? (float)(binSum[i] / binCount[i]) : 0f;
                }

                var smoothed = Smooth(profile);

                // Target the overall mean rather than the centre: correcting towards the centre would
                // brighten the whole image, which is the one thing BackdropRules will not tolerate.
                double total = 0;
                int totalCount = 0;
                for (int i = 0; i < RadialBins; i++)
                {
                    if (binCount[i] <= 0) continue;
                    total += smoothed[i] * binCount[i];
                    totalCount += binCount[i];
                }

                if (totalCount == 0)
                {
                    return Result.Failure("The image had no measurable pixels.");
                }

                float target = (float)(total / totalCount);
                if (target <= 0.001f)
                {
                    return Result.Failure("The image is essentially black - nothing to flatten.");
                }

                float before = MeasureVignette(pixels, width, height);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float dx = x - centreX;
                        float dy = y - centreY;
                        float t = Mathf.Sqrt(dx * dx + dy * dy) / maxRadius * (RadialBins - 1);
                        int lo = Mathf.Clamp(Mathf.FloorToInt(t), 0, RadialBins - 1);
                        int hi = Mathf.Min(lo + 1, RadialBins - 1);

                        // Interpolating between bins, not snapping to one: a step in the gain curve shows
                        // up as a visible ring on a surface this flat.
                        float local = Mathf.Lerp(smoothed[lo], smoothed[hi], t - lo);
                        float gain = local > 0.001f ? Mathf.Clamp(target / local, 1f / MaxGain, MaxGain) : 1f;

                        int index = y * width + x;
                        var p = pixels[index];
                        pixels[index] = new Color32(
                            (byte)Mathf.Clamp(Mathf.RoundToInt(p.r * gain), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(p.g * gain), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt(p.b * gain), 0, 255),
                            p.a);
                    }
                }

                float after = MeasureVignette(pixels, width, height);

                texture.SetPixels32(pixels);
                texture.Apply();
                return Result.Ok(texture.EncodeToJPG(quality), before, after);
            }
            catch (Exception ex)
            {
                return Result.Failure(ex.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Centre-block mean over corner-block mean - the same figure the audit reports, so the
        /// before/after numbers are comparable to the ones measured outside this class.</summary>
        private static float MeasureVignette(Color32[] pixels, int width, int height)
        {
            double centre = 0, corner = 0;
            int centreCount = 0, cornerCount = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float lum = Luminance(pixels[y * width + x]);
                    if (Mathf.Abs(x - width / 2) < width / 6 && Mathf.Abs(y - height / 2) < height / 6)
                    {
                        centre += lum;
                        centreCount++;
                    }
                    else if ((x < width / 10 || x > width * 9 / 10) && (y < height / 10 || y > height * 9 / 10))
                    {
                        corner += lum;
                        cornerCount++;
                    }
                }
            }

            if (centreCount == 0 || cornerCount == 0) return 1f;
            double cornerMean = corner / cornerCount;
            return cornerMean > 0.0001 ? (float)(centre / centreCount / cornerMean) : 1f;
        }

        private static float[] Smooth(float[] profile)
        {
            var result = new float[profile.Length];
            for (int i = 0; i < profile.Length; i++)
            {
                float sum = 0;
                int count = 0;
                for (int k = -SmoothingRadius; k <= SmoothingRadius; k++)
                {
                    int j = i + k;
                    if (j < 0 || j >= profile.Length || profile[j] <= 0f) continue;
                    sum += profile[j];
                    count++;
                }

                result[i] = count > 0 ? sum / count : profile[i];
            }

            return result;
        }

        private static float Luminance(Color32 p) => (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) / 255f;
    }
}
