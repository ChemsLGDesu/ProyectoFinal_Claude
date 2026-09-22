using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace TTTXO.Game.Editor
{
    /// <summary>
    /// Editor-only tool (never included in a build - lives under <c>TTTXO.Game.Editor</c>) that generates
    /// placeholder art for every entry in <see cref="ArtPromptCatalog"/> via the Gemini image API. Batches
    /// run asynchronously (see <see cref="GeminiImageClient"/>) so the Editor never freezes, a single failed
    /// entry never aborts the rest of the batch, and generated files are written under
    /// <see cref="ArtPromptCatalog.OutputRootFolder"/> with a UI-sensible <see cref="TextureImporter"/>
    /// configuration applied automatically.
    /// </summary>
    public sealed class PlaceholderArtGeneratorWindow : EditorWindow
    {
        private enum EntryStatus
        {
            Pending,
            Generating,
            Done,
            Skipped,
            Failed,
        }

        private sealed class EntryState
        {
            public ArtPromptEntry Catalog;
            public bool Selected;
            public string PromptOverride;
            public EntryStatus Status;
            public string StatusMessage;
            public Texture2D Preview;
        }

        private Dictionary<ArtCategory, List<EntryState>> _byCategory;
        private Dictionary<ArtCategory, bool> _foldouts;
        private Vector2 _scroll;

        private GeminiImageModel _selectedModel = GeminiImageModel.FlashImage;
        private string _apiKeyInput = string.Empty;
        private bool _revealApiKey;

        private bool _isRunning;
        private int _progressCurrent;
        private int _progressTotal;
        private string _progressLabel = string.Empty;
        private CancellationTokenSource _cts;

        [MenuItem("TTTXO/Art/Generate Placeholder Art")]
        public static void ShowWindow()
        {
            var window = GetWindow<PlaceholderArtGeneratorWindow>("Placeholder Art");
            window.minSize = new Vector2(520, 640);
        }

        private void OnEnable()
        {
            BuildEntryStates();
            _apiKeyInput = GeminiImageClient.GetApiKey();
        }

        private void BuildEntryStates()
        {
            _byCategory = new Dictionary<ArtCategory, List<EntryState>>();
            _foldouts = new Dictionary<ArtCategory, bool>();

            foreach (var entry in ArtPromptCatalog.Entries)
            {
                if (!_byCategory.TryGetValue(entry.Category, out var list))
                {
                    list = new List<EntryState>();
                    _byCategory[entry.Category] = list;
                    _foldouts[entry.Category] = true;
                }

                var state = new EntryState
                {
                    Catalog = entry,
                    Selected = false,
                    PromptOverride = entry.Prompt,
                    Status = EntryStatus.Pending,
                    StatusMessage = string.Empty,
                };

                string assetPath = ArtPromptCatalog.GetOutputAssetPath(entry);
                state.Preview = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

                list.Add(state);
            }
        }

        private void OnGUI()
        {
            // Everything that drives a run lives above the list: key, model, selection, the generate button
            // and the progress bar. With the button at the bottom it sat below a 60-entry scroll view, so
            // starting a batch meant scrolling past everything you had just picked.
            DrawApiKeySection();
            EditorGUILayout.Space(6);
            DrawModelSection();
            EditorGUILayout.Space(6);
            DrawSelectionToolbar();
            EditorGUILayout.Space(4);
            DrawGenerateSection();
            EditorGUILayout.Space(6);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var category in _byCategory.Keys.ToList())
            {
                DrawCategory(category);
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Reads the reference assets this entry declares. A reference that has not been generated yet is
        /// skipped with a warning rather than failing the run - the piece still generates, just without the
        /// visual anchor, which is strictly better than refusing to produce anything.
        /// </summary>
        private static List<ReferenceImage> LoadReferenceImages(ArtPromptEntry entry)
        {
            var references = new List<ReferenceImage>();

            foreach (string assetPath in ArtPromptCatalog.GetReferenceAssets(entry))
            {
                string absolute = ToAbsolutePath(assetPath);
                if (!File.Exists(absolute))
                {
                    Debug.LogWarning($"TTTXO ArtGen: '{entry.Id}' references '{assetPath}', which has not been generated yet - generating without it.");
                    continue;
                }

                byte[] downscaled = Downscale(File.ReadAllBytes(absolute), ReferenceImageSize);
                references.Add(new ReferenceImage(downscaled, "image/png"));
            }

            return references;
        }

        /// <summary>
        /// Longest side a reference image is sent at. The art itself is 2048px, but a reference only has to
        /// show what the object looks like - two full-size references make a 0.51 MB request body against
        /// 147 KB at this size, for no added fidelity in what the model is being asked to copy.
        /// </summary>
        private const int ReferenceImageSize = 512;

        private static byte[] Downscale(byte[] imageBytes, int maxSide)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            Texture2D scaled = null;
            try
            {
                if (!ImageConversion.LoadImage(source, imageBytes, markNonReadable: false))
                {
                    return imageBytes;
                }

                int longest = Mathf.Max(source.width, source.height);
                if (longest <= maxSide)
                {
                    return imageBytes;
                }

                int w = Mathf.Max(1, Mathf.RoundToInt(source.width * (maxSide / (float)longest)));
                int h = Mathf.Max(1, Mathf.RoundToInt(source.height * (maxSide / (float)longest)));

                scaled = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        pixels[y * w + x] = source.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
                    }
                }

                scaled.SetPixels32(pixels);
                scaled.Apply(updateMipmaps: false);

                byte[] png = ImageConversion.EncodeToPNG(scaled);
                return png != null && png.Length > 0 ? png : imageBytes;
            }
            finally
            {
                DestroyImmediate(source);
                if (scaled != null)
                {
                    DestroyImmediate(scaled);
                }
            }
        }

        /// <summary>How many entries of this category already have a file on disk.</summary>
        private static int CountGenerated(List<EntryState> list)
        {
            int generated = 0;
            foreach (var state in list)
            {
                if (state.Preview != null || File.Exists(ToAbsolutePath(ArtPromptCatalog.GetOutputAssetPath(state.Catalog))))
                {
                    generated++;
                }
            }

            return generated;
        }

        private void DrawApiKeySection()
        {
            EditorGUILayout.LabelField("Gemini API Key", EditorStyles.boldLabel);

            if (!GeminiImageClient.HasApiKey)
            {
                EditorGUILayout.HelpBox("No API key configured. Set one below - generation stays disabled until you do.", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            _apiKeyInput = _revealApiKey
                ? EditorGUILayout.TextField(_apiKeyInput)
                : EditorGUILayout.PasswordField(_apiKeyInput);
            _revealApiKey = GUILayout.Toggle(_revealApiKey, "Show", EditorStyles.miniButton, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Key"))
            {
                GeminiImageClient.SetApiKey(_apiKeyInput);
            }

            using (new EditorGUI.DisabledScope(!GeminiImageClient.HasApiKey))
            {
                if (GUILayout.Button("Clear Key"))
                {
                    GeminiImageClient.ClearApiKey();
                    _apiKeyInput = string.Empty;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawModelSection()
        {
            EditorGUILayout.LabelField("Model", EditorStyles.boldLabel);
            var models = (GeminiImageModel[])Enum.GetValues(typeof(GeminiImageModel));
            string[] labels = models.Select(m => m.DisplayName()).ToArray();
            int currentIndex = Array.IndexOf(models, _selectedModel);
            int newIndex = EditorGUILayout.Popup(currentIndex, labels);
            _selectedModel = models[newIndex];
        }

        private void DrawSelectionToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
            {
                SetAllSelected(true);
            }

            if (GUILayout.Button("Select None"))
            {
                SetAllSelected(false);
            }

            var all = _byCategory.Values.SelectMany(l => l).ToList();
            int selectedCount = all.Count(e => e.Selected);
            int generatedCount = _byCategory.Values.Sum(CountGenerated);

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(
                $"{selectedCount} selected  ·  {generatedCount} / {ArtPromptCatalog.Entries.Count} generated",
                GUILayout.Width(230));
            EditorGUILayout.EndHorizontal();
        }

        private void SetAllSelected(bool value)
        {
            foreach (var list in _byCategory.Values)
            {
                foreach (var state in list)
                {
                    state.Selected = value;
                }
            }
        }

        private void DrawCategory(ArtCategory category)
        {
            var list = _byCategory[category];
            int selectedInCategory = list.Count(e => e.Selected);
            int generatedInCategory = CountGenerated(list);

            EditorGUILayout.BeginHorizontal();

            // Two counts, not one: "selected" is what the next run will touch, "generated" is how much of
            // the category already exists. They answer different questions and both are worth a glance.
            _foldouts[category] = EditorGUILayout.Foldout(
                _foldouts[category],
                $"{ArtPromptCatalog.CategoryDisplayName(category)}   —   {selectedInCategory} selected  ·  {generatedInCategory}/{list.Count} generated",
                true);

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(selectedInCategory == list.Count))
            {
                if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(38)))
                {
                    foreach (var state in list) state.Selected = true;
                }
            }

            using (new EditorGUI.DisabledScope(selectedInCategory == 0))
            {
                if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(44)))
                {
                    foreach (var state in list) state.Selected = false;
                }
            }

            EditorGUILayout.EndHorizontal();

            if (!_foldouts[category])
            {
                return;
            }

            EditorGUI.indentLevel++;
            foreach (var state in list)
            {
                DrawEntry(state);
            }

            EditorGUI.indentLevel--;
        }

        private void DrawEntry(EntryState state)
        {
            // Tint the whole box when selected, so selection reads at a glance down a long list rather than
            // from a 13-pixel checkbox.
            var previousBackground = GUI.backgroundColor;
            if (state.Selected)
            {
                GUI.backgroundColor = new Color(0.45f, 0.75f, 1f);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = previousBackground;

            EditorGUILayout.BeginHorizontal();
            state.Selected = EditorGUILayout.Toggle(state.Selected, GUILayout.Width(20));

            if (state.Preview != null)
            {
                GUILayout.Label(state.Preview, GUILayout.Width(32), GUILayout.Height(32));
            }
            else
            {
                GUILayout.Space(36);
            }

            EditorGUILayout.LabelField(state.Catalog.Id, EditorStyles.boldLabel, GUILayout.Width(200));
            EditorGUILayout.LabelField(state.Catalog.AspectRatio, GUILayout.Width(40));
            EditorGUILayout.LabelField(state.Catalog.TransparentBackground ? "alpha" : "opaque", GUILayout.Width(45));

            bool existsOnDisk = state.Preview != null || File.Exists(ToAbsolutePath(ArtPromptCatalog.GetOutputAssetPath(state.Catalog)));
            bool needsRegen = !string.IsNullOrEmpty(state.Catalog.RegenerateReason);

            if (existsOnDisk)
            {
                // A flagged entry exists but is known-bad, so "already generated" alone would read as done.
                if (needsRegen)
                {
                    var previousLabelColor = GUI.color;
                    GUI.color = Color.yellow;
                    GUILayout.Label("NEEDS REGEN", EditorStyles.miniBoldLabel, GUILayout.Width(100));
                    GUI.color = previousLabelColor;
                }
                else
                {
                    GUILayout.Label("already generated", EditorStyles.miniLabel, GUILayout.Width(100));
                }
            }

            EditorGUILayout.EndHorizontal();

            // The whole header row toggles selection - the checkbox alone is a small target in a list this
            // long. Only the header, not the box: the prompt field below has to stay editable.
            // A click that landed on the checkbox itself has already been consumed by it, so the event type
            // is no longer MouseDown here and the row cannot double-toggle.
            var headerRect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && headerRect.Contains(Event.current.mousePosition))
            {
                state.Selected = !state.Selected;
                Event.current.Use();
                Repaint();
            }
            else if (Event.current.type == EventType.Repaint)
            {
                EditorGUIUtility.AddCursorRect(headerRect, MouseCursor.Link);
            }

            if (needsRegen)
            {
                EditorGUILayout.HelpBox(state.Catalog.RegenerateReason, MessageType.Warning);
            }

            state.PromptOverride = EditorGUILayout.TextArea(state.PromptOverride, GUILayout.Height(40));

            if (state.Status != EntryStatus.Pending)
            {
                var color = state.Status switch
                {
                    EntryStatus.Done => Color.green,
                    EntryStatus.Failed => Color.red,
                    EntryStatus.Generating => Color.yellow,
                    _ => GUI.color,
                };

                var previous = GUI.color;
                GUI.color = color;
                EditorGUILayout.LabelField($"[{state.Status}] {state.StatusMessage}", EditorStyles.wordWrappedLabel);
                GUI.color = previous;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawGenerateSection()
        {
            if (_isRunning)
            {
                var rect = EditorGUILayout.GetControlRect(false, 20);
                float progress = _progressTotal > 0 ? (float)_progressCurrent / _progressTotal : 0f;
                EditorGUI.ProgressBar(rect, progress, _progressLabel);

                if (GUILayout.Button("Cancel"))
                {
                    _cts?.Cancel();
                }

                return;
            }

            var all = _byCategory.Values.SelectMany(l => l).ToList();

            // Entries flagged in the catalog as known-bad art (see ArtPromptEntry.RegenerateReason). Selecting
            // them is a separate step from generating, so the flagged set can be picked up in a later session
            // - typically once an API quota resets - without hunting through every category by hand.
            var flagged = all.Where(e => !string.IsNullOrEmpty(e.Catalog.RegenerateReason)).ToList();
            if (flagged.Count > 0)
            {
                if (GUILayout.Button($"Select the {flagged.Count} entries flagged for regeneration"))
                {
                    foreach (var entry in all)
                    {
                        entry.Selected = !string.IsNullOrEmpty(entry.Catalog.RegenerateReason);
                    }
                }

                EditorGUILayout.HelpBox(
                    $"{flagged.Count} generated {(flagged.Count == 1 ? "piece is" : "pieces are")} flagged as defective and should be regenerated. Their prompts have already been corrected - choose \"Overwrite All\" when generating.",
                    MessageType.Warning);
            }

            int selectedCount = all.Count(e => e.Selected);
            int missingCount = all.Count(e => !File.Exists(ToAbsolutePath(ArtPromptCatalog.GetOutputAssetPath(e.Catalog))));

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!GeminiImageClient.HasApiKey || selectedCount == 0))
            {
                if (GUILayout.Button($"Generate Selected ({selectedCount})", GUILayout.Height(30)))
                {
                    _ = RunGenerationAsync();
                }
            }

            // "Everything missing" rather than "everything": regenerating the whole catalog is almost never
            // what someone means by generate-all, and it is the expensive mistake to make with a quota.
            using (new EditorGUI.DisabledScope(!GeminiImageClient.HasApiKey || missingCount == 0))
            {
                if (GUILayout.Button($"Generate All Missing ({missingCount})", GUILayout.Height(30), GUILayout.Width(200)))
                {
                    foreach (var entry in all)
                    {
                        entry.Selected = !File.Exists(ToAbsolutePath(ArtPromptCatalog.GetOutputAssetPath(entry.Catalog)));
                    }

                    _ = RunGenerationAsync();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private async System.Threading.Tasks.Task RunGenerationAsync()
        {
            var selected = _byCategory.Values.SelectMany(l => l).Where(e => e.Selected).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            var alreadyExisting = selected
                .Where(e => File.Exists(ToAbsolutePath(ArtPromptCatalog.GetOutputAssetPath(e.Catalog))))
                .ToList();

            if (alreadyExisting.Count > 0)
            {
                // "No sobrescribir sin avisar": a single up-front dialog for the whole batch (instead of one
                // dialog per existing file) - lets the user overwrite everything, skip only the existing
                // ones, or bail out entirely, without being interrupted mid-batch for a large selection.
                int choice = EditorUtility.DisplayDialogComplex(
                    "Some files already exist",
                    $"{alreadyExisting.Count} of the {selected.Count} selected entries already have a generated file under {ArtPromptCatalog.OutputRootFolder}. What do you want to do?",
                    "Overwrite All",
                    "Cancel",
                    "Skip Existing");

                if (choice == 1)
                {
                    return;
                }

                if (choice == 2)
                {
                    var existingIds = new HashSet<string>(alreadyExisting.Select(e => e.Catalog.Id));
                    selected = selected.Where(e => !existingIds.Contains(e.Catalog.Id)).ToList();
                    if (selected.Count == 0)
                    {
                        return;
                    }
                }
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();
            _progressCurrent = 0;
            _progressTotal = selected.Count;

            foreach (var state in selected)
            {
                if (_cts.IsCancellationRequested)
                {
                    state.Status = EntryStatus.Skipped;
                    state.StatusMessage = "Cancelled before it started.";
                    continue;
                }

                _progressCurrent++;
                _progressLabel = $"Generating '{state.Catalog.Id}' ({_progressCurrent}/{_progressTotal})...";
                state.Status = EntryStatus.Generating;
                state.StatusMessage = string.Empty;
                Repaint();

                try
                {
                    string fullPrompt = ArtPromptCatalog.BuildFullPrompt(state.Catalog, state.PromptOverride);
                    var result = await GeminiImageClient.GenerateImageAsync(
                        fullPrompt,
                        state.Catalog.AspectRatio,
                        _selectedModel,
                        _cts.Token,
                        LoadReferenceImages(state.Catalog));

                    if (!result.Success)
                    {
                        state.Status = EntryStatus.Failed;
                        state.StatusMessage = result.ErrorMessage;
                    }
                    else
                    {
                        string assetPath = ArtPromptCatalog.GetOutputAssetPath(state.Catalog);
                        byte[] bytesToWrite = result.ImageBytes;
                        string statusMessage = "Generated.";

                        if (state.Catalog.TransparentBackground)
                        {
                            // The API can only return opaque JPEG, so the flat chroma backdrop the prompt
                            // asked for becomes the alpha channel here (see ChromaKeyProcessor).
                            var keyed = ChromaKeyProcessor.KnockOutBackground(result.ImageBytes, ArtPromptCatalog.ChromaKeyColor);
                            if (!keyed.Success)
                            {
                                state.Status = EntryStatus.Failed;
                                state.StatusMessage = $"Background knockout failed: {keyed.ErrorMessage}";
                                Repaint();
                                continue;
                            }

                            bytesToWrite = keyed.PngBytes;
                            statusMessage = DescribeKnockout(keyed.TransparentFraction);

                            // Entries shown together as a set get their framing enforced rather than
                            // requested, because the prompt alone never lands close enough. A ring is
                            // measured by its opening, everything else by its outer bounds.
                            float? target = state.Catalog.HoleCoverage ?? ArtPromptCatalog.GetFramingCoverage(state.Catalog);
                            if (target.HasValue)
                            {
                                bool byHole = state.Catalog.HoleCoverage.HasValue;
                                var framed = byHole
                                    ? SubjectNormalizer.NormalizeByHole(bytesToWrite, target.Value)
                                    : SubjectNormalizer.Normalize(bytesToWrite, target.Value);

                                if (framed.Success)
                                {
                                    bytesToWrite = framed.PngBytes;
                                    statusMessage += $" {(byHole ? "Hole" : "Framing")} normalized from {framed.CoverageBefore * 100f:F0}% to {target.Value * 100f:F0}%.";
                                }
                                else
                                {
                                    // Keep the keyed art - it is still usable, just framed inconsistently.
                                    statusMessage += $" Framing NOT normalized: {framed.ErrorMessage}";
                                }
                            }

                            // Last step, so it runs on the final pixels: the art arrives via JPEG carrying
                            // thousands of near-duplicate colours that are pure compression noise.
                            int? colours = ArtPromptCatalog.GetMinifyColours(state.Catalog);
                            if (colours.HasValue)
                            {
                                int sizeBefore = bytesToWrite.Length;
                                var small = TextureMinifier.Minify(bytesToWrite, colours.Value);
                                if (small.Success)
                                {
                                    bytesToWrite = small.PngBytes;
                                    statusMessage += $" Minified {sizeBefore / 1024}KB to {bytesToWrite.Length / 1024}KB ({small.ColoursBefore} colours to {small.ColoursAfter}).";
                                }
                                else
                                {
                                    statusMessage += $" NOT minified: {small.ErrorMessage}";
                                }
                            }
                        }
                        else
                        {
                            // Opaque art is JPEG all the way through, so it is re-encoded rather than
                            // quantized - a backdrop gradient bands under a palette.
                            int? quality = ArtPromptCatalog.GetJpegQuality(state.Catalog);

                            // Before the size pass, not after: flattening decodes and re-encodes, so
                            // running it second would throw away the quality choice made below.
                            if (quality.HasValue && ArtPromptCatalog.ShouldFlattenLighting(state.Catalog))
                            {
                                var flat = BackdropFlattener.Flatten(bytesToWrite, quality.Value);
                                if (flat.Success)
                                {
                                    bytesToWrite = flat.JpegBytes;
                                    statusMessage += $" Flattened lighting {flat.VignetteBefore:F2}x to {flat.VignetteAfter:F2}x.";
                                }
                                else
                                {
                                    statusMessage += $" NOT flattened: {flat.ErrorMessage}";
                                }
                            }

                            if (quality.HasValue)
                            {
                                int sizeBefore = bytesToWrite.Length;
                                var smaller = TextureMinifier.MinifyJpeg(bytesToWrite, quality.Value);
                                if (smaller.Success && smaller.PngBytes.Length < sizeBefore)
                                {
                                    bytesToWrite = smaller.PngBytes;
                                    statusMessage += $" Re-encoded {sizeBefore / 1024}KB to {bytesToWrite.Length / 1024}KB at quality {quality.Value}.";
                                }
                            }
                        }

                        WriteAndImport(assetPath, bytesToWrite, state.Catalog.TransparentBackground);
                        state.Preview = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                        state.Status = EntryStatus.Done;
                        state.StatusMessage = statusMessage;
                    }
                }
                catch (Exception ex)
                {
                    state.Status = EntryStatus.Failed;
                    state.StatusMessage = ex.Message;
                    Debug.LogException(ex);
                }

                Repaint();
            }

            _isRunning = false;
            _cts.Dispose();
            _cts = null;
            AssetDatabase.Refresh();
            Repaint();
        }

        /// <summary>
        /// Turns the share of knocked-out pixels into a status the user can act on. A backdrop that barely
        /// moved usually means the model ignored the flat-green instruction; one that ate nearly everything
        /// means it keyed the subject too. Both produce a usable-looking file, so they have to be called out
        /// rather than silently reported as "Generated".
        /// </summary>
        private static string DescribeKnockout(float transparentFraction)
        {
            int percent = Mathf.RoundToInt(transparentFraction * 100f);

            if (transparentFraction < 0.02f)
            {
                return $"Generated, but almost no background was removed ({percent}% transparent) - the model likely ignored the flat green backdrop. Check the image and retry.";
            }

            if (transparentFraction > 0.98f)
            {
                return $"Generated, but nearly the whole image was keyed out ({percent}% transparent) - the subject may have been green too. Check the image and retry.";
            }

            return $"Generated ({percent}% transparent).";
        }

        /// <summary>Writes the final image bytes to disk, imports them, and applies a UI-sensible <see cref="TextureImporter"/> setup (sprite, no mip maps, uncompressed, alpha honored per <paramref name="transparent"/>). <paramref name="imageBytes"/> is a real PNG for transparent entries (post-knockout) and the API's untouched JPEG for opaque ones.</summary>
        private static void WriteAndImport(string assetPath, byte[] imageBytes, bool transparent)
        {
            string absolutePath = ToAbsolutePath(assetPath);
            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(absolutePath, imageBytes);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
            {
                Debug.LogWarning($"TTTXO ArtGen: could not get a TextureImporter for '{assetPath}' right after import - importer settings were not applied.");
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = transparent;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot!, assetPath).Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
