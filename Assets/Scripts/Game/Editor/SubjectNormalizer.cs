using UnityEngine;

namespace TTTXO.Game.Editor
{
    /// <summary>
    /// Rescales a cut-out subject so it occupies a fixed share of its canvas, then centres it.
    ///
    /// Asking the model for a framing ("fill roughly 70 percent of the shorter side") narrows the spread
    /// but never closes it - the six avatars came back between 60.1% and 78.1% of the canvas, a 30%
    /// difference in disc diameter, and a regenerated piece once overshot the target in the opposite
    /// direction. For assets displayed as a SET - an avatar picker grid, a pair of board pieces, a row of
    /// tier badges - that spread is visible and a frame sized for one of them fits none of the others.
    ///
    /// Doing it here instead makes the framing exact and costs no API quota, so an existing batch can be
    /// normalized in place rather than regenerated.
    /// </summary>
    public static class SubjectNormalizer
    {
        /// <summary>Alpha at or below which a pixel counts as empty when measuring the subject's bounds.</summary>
        private const byte EmptyAlpha = 16;

        public readonly struct Result
        {
            public bool Success { get; }
            public byte[] PngBytes { get; }
            public string ErrorMessage { get; }

            /// <summary>Share of the canvas the subject's bounding box occupied BEFORE normalization, 0-1.</summary>
            public float CoverageBefore { get; }

            private Result(bool success, byte[] pngBytes, string errorMessage, float coverageBefore)
            {
                Success = success;
                PngBytes = pngBytes;
                ErrorMessage = errorMessage;
                CoverageBefore = coverageBefore;
            }

            public static Result Ok(byte[] png, float before) => new(true, png, null, before);
            public static Result Failure(string error) => new(false, null, error, 0f);
        }

        /// <summary>
        /// Scales <paramref name="pngBytes"/> so the subject's longest bounding-box side spans
        /// <paramref name="targetCoverage"/> of the canvas, centred, and returns a re-encoded PNG.
        /// Never throws - a decode failure or an empty image comes back as a failed <see cref="Result"/>.
        /// </summary>
        public static Result Normalize(byte[] pngBytes, float targetCoverage)
        {
            if (pngBytes == null || pngBytes.Length == 0)
            {
                return Result.Failure("No image bytes to normalize.");
            }

            if (targetCoverage <= 0f || targetCoverage > 1f)
            {
                return Result.Failure($"Target coverage {targetCoverage} is outside the 0-1 range.");
            }

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            Texture2D output = null;
            try
            {
                if (!ImageConversion.LoadImage(decoded, pngBytes, markNonReadable: false))
                {
                    return Result.Failure("Could not decode the image bytes.");
                }

                int w = decoded.width, h = decoded.height;
                Color32[] src = decoded.GetPixels32();

                if (!TryMeasureSubject(src, w, h, out int minX, out int minY, out int maxX, out int maxY))
                {
                    return Result.Failure("The image has no visible subject to measure.");
                }

                int subjectW = maxX - minX + 1;
                int subjectH = maxY - minY + 1;
                float coverageBefore = Mathf.Max(subjectW / (float)w, subjectH / (float)h);
                float scale = targetCoverage / coverageBefore;

                // Premultiply before resampling: interpolating straight alpha pulls the transparent
                // pixels' RGB into the silhouette and leaves a dark fringe around it.
                var premul = new Vector4[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    float a = src[i].a / 255f;
                    premul[i] = new Vector4(src[i].r / 255f * a, src[i].g / 255f * a, src[i].b / 255f * a, a);
                }

                float srcCentreX = (minX + maxX) * 0.5f;
                float srcCentreY = (minY + maxY) * 0.5f;
                var dst = new Color32[src.Length];

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        // Map the destination pixel back through the scale-about-centre transform.
                        float sx = srcCentreX + (x - (w - 1) * 0.5f) / scale;
                        float sy = srcCentreY + (y - (h - 1) * 0.5f) / scale;
                        dst[y * w + x] = SampleBilinear(premul, w, h, sx, sy);
                    }
                }

                output = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
                output.SetPixels32(dst);
                output.Apply(updateMipmaps: false);

                byte[] png = ImageConversion.EncodeToPNG(output);
                if (png == null || png.Length == 0)
                {
                    return Result.Failure("Re-encoding the normalized image produced no bytes.");
                }

                return Result.Ok(png, coverageBefore);
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

        /// <summary>
        /// Same rescale, but driven by the size of the subject's central HOLE instead of its outer bounds.
        ///
        /// For a ring or frame the hole is the dimension that matters: it is what an avatar has to fit
        /// inside. The two profile frames came back with holes at 58.6% and 54.3% of their canvas while
        /// their outer diameters were nearly identical, so normalizing the outside would have left them
        /// still mismatched where it counts, and the UI would need a different avatar scale per frame.
        /// </summary>
        public static Result NormalizeByHole(byte[] pngBytes, float targetHoleCoverage)
        {
            if (pngBytes == null || pngBytes.Length == 0)
            {
                return Result.Failure("No image bytes to normalize.");
            }

            if (targetHoleCoverage <= 0f || targetHoleCoverage > 1f)
            {
                return Result.Failure($"Target hole coverage {targetHoleCoverage} is outside the 0-1 range.");
            }

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            Texture2D output = null;
            try
            {
                if (!ImageConversion.LoadImage(decoded, pngBytes, markNonReadable: false))
                {
                    return Result.Failure("Could not decode the image bytes.");
                }

                int w = decoded.width, h = decoded.height;
                Color32[] src = decoded.GetPixels32();

                if (!TryMeasureHole(src, w, h, out int minX, out int minY, out int maxX, out int maxY))
                {
                    return Result.Failure("No enclosed hole found at the centre - is this actually a ring?");
                }

                float coverageBefore = Mathf.Max((maxX - minX + 1) / (float)w, (maxY - minY + 1) / (float)h);
                float scale = targetHoleCoverage / coverageBefore;

                var premul = new Vector4[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    float a = src[i].a / 255f;
                    premul[i] = new Vector4(src[i].r / 255f * a, src[i].g / 255f * a, src[i].b / 255f * a, a);
                }

                float centreX = (minX + maxX) * 0.5f;
                float centreY = (minY + maxY) * 0.5f;
                var dst = new Color32[src.Length];

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float sx = centreX + (x - (w - 1) * 0.5f) / scale;
                        float sy = centreY + (y - (h - 1) * 0.5f) / scale;
                        dst[y * w + x] = SampleBilinear(premul, w, h, sx, sy);
                    }
                }

                output = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
                output.SetPixels32(dst);
                output.Apply(updateMipmaps: false);

                byte[] png = ImageConversion.EncodeToPNG(output);
                if (png == null || png.Length == 0)
                {
                    return Result.Failure("Re-encoding the normalized image produced no bytes.");
                }

                return Result.Ok(png, coverageBefore);
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

        /// <summary>Flood fills the transparent region containing the canvas centre and returns its bounds. Flooding rather than scanning a few axes keeps it honest on an ornate frame whose inner edge is not a clean circle.</summary>
        private static bool TryMeasureHole(Color32[] px, int w, int h, out int minX, out int minY, out int maxX, out int maxY)
        {
            minX = w; minY = h; maxX = -1; maxY = -1;

            int start = (h / 2) * w + (w / 2);
            if (px[start].a > EmptyAlpha)
            {
                return false;
            }

            var visited = new bool[px.Length];
            var queue = new System.Collections.Generic.Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % w, y = index / w;

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                if (x > 0) TryEnqueue(px, visited, queue, index - 1);
                if (x < w - 1) TryEnqueue(px, visited, queue, index + 1);
                if (y > 0) TryEnqueue(px, visited, queue, index - w);
                if (y < h - 1) TryEnqueue(px, visited, queue, index + w);
            }

            return maxX >= 0;
        }

        private static void TryEnqueue(Color32[] px, bool[] visited, System.Collections.Generic.Queue<int> queue, int index)
        {
            if (visited[index] || px[index].a > EmptyAlpha)
            {
                return;
            }

            visited[index] = true;
            queue.Enqueue(index);
        }

        private static bool TryMeasureSubject(Color32[] px, int w, int h, out int minX, out int minY, out int maxX, out int maxY)
        {
            minX = w; minY = h; maxX = -1; maxY = -1;

            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a <= EmptyAlpha)
                {
                    continue;
                }

                int x = i % w, y = i / w;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            return maxX >= 0;
        }

        /// <summary>Bilinear sample of a premultiplied buffer, returning un-premultiplied Color32. Outside the source bounds it returns fully transparent, so scaling up never smears the edge pixels outward.</summary>
        private static Color32 SampleBilinear(Vector4[] premul, int w, int h, float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;

            Vector4 c00 = Fetch(premul, w, h, x0, y0);
            Vector4 c10 = Fetch(premul, w, h, x0 + 1, y0);
            Vector4 c01 = Fetch(premul, w, h, x0, y0 + 1);
            Vector4 c11 = Fetch(premul, w, h, x0 + 1, y0 + 1);

            Vector4 top = Vector4.Lerp(c00, c10, fx);
            Vector4 bottom = Vector4.Lerp(c01, c11, fx);
            Vector4 c = Vector4.Lerp(top, bottom, fy);

            if (c.w <= 0.0001f)
            {
                return new Color32(0, 0, 0, 0);
            }

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.x / c.w * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.y / c.w * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.z / c.w * 255f), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(c.w * 255f), 0, 255));
        }

        private static Vector4 Fetch(Vector4[] premul, int w, int h, int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
            {
                return Vector4.zero;
            }

            return premul[y * w + x];
        }
    }
}
