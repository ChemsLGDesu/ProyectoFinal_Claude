using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace TTTXO.Game.Editor
{
    /// <summary>Which Gemini image model to call. Values map to <see cref="GeminiImageModelExtensions.ToApiId"/>.</summary>
    public enum GeminiImageModel
    {
        FlashImage,
        ProImage,
        FlashLiteImage,
    }

    public static class GeminiImageModelExtensions
    {
        public static string ToApiId(this GeminiImageModel model) => model switch
        {
            GeminiImageModel.FlashImage => "gemini-3.1-flash-image",
            GeminiImageModel.ProImage => "gemini-3-pro-image",
            GeminiImageModel.FlashLiteImage => "gemini-3.1-flash-lite-image",
            _ => "gemini-3.1-flash-image",
        };

        public static string DisplayName(this GeminiImageModel model) => model switch
        {
            GeminiImageModel.FlashImage => "Flash (default, general purpose)",
            // No '/' in this label: EditorGUILayout.Popup treats it as a submenu separator, which
            // split this entry into a nested "costlier)" submenu instead of one selectable option.
            GeminiImageModel.ProImage => "Pro (premium quality, slower and costlier)",
            GeminiImageModel.FlashLiteImage => "Flash-Lite (fastest, cheapest)",
            _ => model.ToString(),
        };
    }

    /// <summary>
    /// An existing asset handed to the model as visual reference, so a recurring object keeps one design
    /// across every piece that shows it.
    ///
    /// The request shape mirrors the RESPONSE contract this client already parses - a content part with
    /// <c>type: "image"</c> carrying base64 in <c>data</c> - since the endpoint's input and output parts
    /// use the same vocabulary. If a live call rejects it, the field names in
    /// <see cref="GeminiImageClient.GenerateImageAsync"/> are the one place to correct, and the error body
    /// is logged in full.
    /// </summary>
    public readonly struct ReferenceImage
    {
        public byte[] Bytes { get; }
        public string MimeType { get; }

        public ReferenceImage(byte[] bytes, string mimeType)
        {
            Bytes = bytes;
            MimeType = mimeType;
        }
    }

    /// <summary>Outcome of a single <see cref="GeminiImageClient.GenerateImageAsync"/> call.</summary>
    public readonly struct GeminiImageResult
    {
        public bool Success { get; }
        public byte[] ImageBytes { get; }
        public string ErrorMessage { get; }

        private GeminiImageResult(bool success, byte[] imageBytes, string errorMessage)
        {
            Success = success;
            ImageBytes = imageBytes;
            ErrorMessage = errorMessage;
        }

        public static GeminiImageResult Ok(byte[] imageBytes) => new(true, imageBytes, null);
        public static GeminiImageResult Failure(string errorMessage) => new(false, null, errorMessage);
    }

    /// <summary>
    /// Thin wrapper around the Gemini image-generation HTTP API (<c>POST /v1beta/interactions</c>), used
    /// only by <see cref="PlaceholderArtGeneratorWindow"/>. Editor-only, non-blocking: requests are awaited
    /// via <see cref="UnityEngine.AsyncOperation.completed"/> instead of a synchronous wait, so the Editor
    /// UI stays responsive while a batch runs. The API key never touches disk as a project asset - it lives
    /// in <see cref="EditorPrefs"/> under a project-prefixed key, set/cleared only from the tool window.
    /// </summary>
    public static class GeminiImageClient
    {
        private const string ApiUrl = "https://generativelanguage.googleapis.com/v1beta/interactions";
        private const string ImageSize = "2K";
        private const string ApiKeyEditorPrefsKey = "TTTXO.ArtGen.GeminiApiKey";

        public static bool HasApiKey => !string.IsNullOrEmpty(GetApiKey());

        public static string GetApiKey() => EditorPrefs.GetString(ApiKeyEditorPrefsKey, string.Empty);

        public static void SetApiKey(string apiKey) => EditorPrefs.SetString(ApiKeyEditorPrefsKey, apiKey ?? string.Empty);

        public static void ClearApiKey() => EditorPrefs.DeleteKey(ApiKeyEditorPrefsKey);

        /// <summary>Sends one generation request and returns the raw JPEG bytes on success (this endpoint cannot return PNG - see the <c>mime_type</c> note in the request body). Never throws for expected failure modes (missing key, HTTP error, unparsable response) - those all come back as a failed <see cref="GeminiImageResult"/> with the real response body already logged to the Console for debugging.</summary>
        public static async Task<GeminiImageResult> GenerateImageAsync(string prompt, string aspectRatio, GeminiImageModel model, CancellationToken cancellationToken, IReadOnlyList<ReferenceImage> referenceImages = null)
        {
            string apiKey = GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                return GeminiImageResult.Failure("No Gemini API key configured. Set one in the Placeholder Art window before generating.");
            }

            // Reference images go in ahead of the text, so the prompt reads as instructions about them.
            // Describing a recurring object in words yields a different object every time: the three
            // currency packs came back with three unrelated gem designs from three prompts that all said
            // "violet faceted gem". Handing over the actual asset is the only way to keep them identical.
            var input = new JArray();
            if (referenceImages != null)
            {
                foreach (var reference in referenceImages)
                {
                    input.Add(new JObject
                    {
                        ["type"] = "image",
                        ["mime_type"] = reference.MimeType,
                        ["data"] = Convert.ToBase64String(reference.Bytes),
                    });
                }
            }

            input.Add(new JObject { ["type"] = "text", ["text"] = prompt });

            var requestBody = new JObject
            {
                ["model"] = model.ToApiId(),
                ["input"] = input,
                ["response_format"] = new JObject
                {
                    ["type"] = "image",
                    // JPEG is the ONLY value this endpoint accepts - "image/png" comes back as HTTP 400
                    // ("Supported values: 'image/jpeg'"). Everything downstream follows from that: JPEG has
                    // no alpha channel, so pieces that need transparency are generated over a flat chroma
                    // backdrop and keyed out by ChromaKeyProcessor instead of being requested transparent.
                    ["mime_type"] = "image/jpeg",
                    ["aspect_ratio"] = aspectRatio,
                    ["image_size"] = ImageSize,
                },
            };

            byte[] bodyBytes = Encoding.UTF8.GetBytes(requestBody.ToString(Formatting.None));

            using var request = new UnityWebRequest(ApiUrl, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("x-goog-api-key", apiKey);
            request.timeout = 180;

            await SendAsync(request, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return GeminiImageResult.Failure("Cancelled.");
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                Debug.LogError($"TTTXO ArtGen: Gemini request failed ({request.result}, HTTP {request.responseCode}): {request.error}\nResponse body: {body}");
                return GeminiImageResult.Failure($"HTTP {request.responseCode}: {request.error}. See Console for the full response body.");
            }

            string responseBody = request.downloadHandler.text;
            if (!TryExtractBase64Image(responseBody, out string base64, out string extractError))
            {
                Debug.LogError($"TTTXO ArtGen: could not find image data in Gemini response. {extractError}\nRaw response: {responseBody}");
                return GeminiImageResult.Failure($"{extractError} See Console for the raw response body.");
            }

            try
            {
                byte[] imageBytes = Convert.FromBase64String(base64);
                return GeminiImageResult.Ok(imageBytes);
            }
            catch (FormatException ex)
            {
                Debug.LogError($"TTTXO ArtGen: image data was not valid base64 ({ex.Message}).\nRaw response: {responseBody}");
                return GeminiImageResult.Failure("Image data was not valid base64. See Console for the raw response body.");
            }
        }

        /// <summary>Awaits a <see cref="UnityWebRequest"/> without blocking the main thread: subscribes to <see cref="AsyncOperation.completed"/> (fires from Unity's engine tick, independent of window focus/repaint) instead of polling, and aborts + resolves early if <paramref name="cancellationToken"/> fires first.</summary>
        private static Task SendAsync(UnityWebRequest request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            var operation = request.SendWebRequest();
            operation.completed += _ => tcs.TrySetResult(true);

            cancellationToken.Register(() =>
            {
                tcs.TrySetResult(true);
                try
                {
                    if (!request.isDone)
                    {
                        request.Abort();
                    }
                }
                catch (Exception)
                {
                    // Request may already have finished between the isDone check and Abort() - fine to ignore.
                }
            });

            return tcs.Task;
        }

        /// <summary>Tolerant response parsing per the verified API contract: bytes normally live in <c>steps[].content[]</c> entries where <c>type == "image"</c>, field <c>data</c>; the doc also mentions an <c>output_image.data</c> shortcut, checked first. Never throws on an unexpected shape - callers log the raw body instead of failing silently.</summary>
        private static bool TryExtractBase64Image(string responseBody, out string base64, out string errorDetail)
        {
            base64 = null;
            errorDetail = null;

            JObject root;
            try
            {
                root = JObject.Parse(responseBody);
            }
            catch (Exception ex)
            {
                errorDetail = $"Response is not valid JSON: {ex.Message}";
                return false;
            }

            var shortcut = root.SelectToken("output_image.data");
            if (shortcut != null && shortcut.Type == JTokenType.String)
            {
                base64 = shortcut.Value<string>();
                return true;
            }

            if (root["steps"] is JArray steps)
            {
                foreach (var step in steps)
                {
                    if (step["content"] is not JArray content)
                    {
                        continue;
                    }

                    foreach (var item in content)
                    {
                        string type = item["type"]?.Value<string>();
                        if (!string.Equals(type, "image", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string data = item["data"]?.Value<string>();
                        if (!string.IsNullOrEmpty(data))
                        {
                            base64 = data;
                            return true;
                        }
                    }
                }
            }

            errorDetail = "Could not find image data (checked 'output_image.data' and 'steps[].content[].data').";
            return false;
        }
    }
}
