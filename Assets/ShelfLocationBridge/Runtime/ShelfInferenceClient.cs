using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public enum ShelfVisualizationMode {
    PythonAnnotatedFrame,
    UnityGeometryOverlay
}

[Serializable]
public sealed class ShelfInferenceCycleResult {
    public string sourceId;
    public Camera sourceCamera;
    public string frameId;
    public FrameInputData metadata;
    public ShelfInferenceResponse response;
    public bool succeeded;
    public bool visualizationDisplayed;
    public string message;
    public float durationSeconds;
}

[Serializable]
public sealed class DualShelfInferenceCycleResult {
    public string cycleId;
    public Camera leftCamera, rightCamera;
    public FrameInputData leftMetadata, rightMetadata;
    public DualShelfInferenceResponse response;
    public bool succeeded, leftVisualizationDisplayed, rightVisualizationDisplayed;
    public bool stationaryPosePreserved;
    public string message;
    public float durationSeconds;
}

sealed class ShelfInferenceRequestContext {
    public Camera camera;
    public string sourceId;
    public string frameIdPrefix;
}

[RequireComponent(typeof(ShelfLocationBridge))]
public class ShelfInferenceClient : MonoBehaviour {
    [Header("Connection")]
    public string serverUrl = "http://127.0.0.1:8000";
    public bool autoStart = true;
    [Min(0)] public float startupDelay = 1f;
    [Min(.1f)] public float inferenceInterval = 2.5f;
    [Min(1)] public int timeoutSeconds = 30;
    [Header("Capture")]
    public ShelfLocationBridge bridge;
    [Range(1, 100)] public int jpegQuality = 80;
    [Header("Display")]
    public bool showOverlay = true;
    public ShelfVisualizationMode visualizationMode = ShelfVisualizationMode.PythonAnnotatedFrame;
    public bool debugLogging = false;
    public ShelfInferenceHud hud;

    public bool IsInferenceRunning { get; private set; }
    public string LastDualSessionId { get; private set; }
    public string LastDualStationId { get; private set; }
    public int SessionCompletionRequestCount { get; private set; }
    public event Action<ShelfInferenceCycleResult> InferenceCycleCompleted;
    public event Action<DualShelfInferenceCycleResult> DualInferenceCycleCompleted;

    readonly Dictionary<string, ShelfInferenceState> states = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> completionRequests = new(StringComparer.OrdinalIgnoreCase);
    UnityWebRequest activeRequest;
    string lastWarning;
    string lastVisualizationWarning;

    void Awake() {
        if (bridge == null) bridge = GetComponent<ShelfLocationBridge>();
        if (bridge == null) {
            Debug.LogError("ShelfInferenceClient requires ShelfLocationBridge on the same GameObject.");
            enabled = false;
            return;
        }
        EnsureHud();
    }

    void OnEnable() {
        EnsureHud();
        if (autoStart) StartCoroutine(InferenceLoop());
    }

    void OnDisable() {
        if (activeRequest != null) activeRequest.Abort();
        activeRequest = null;
        StopAllCoroutines();
        IsInferenceRunning = false;
        if (hud != null) {
            hud.ClearVisualization();
            hud.SetVisible(false);
        }
    }

    public bool RequestInferenceOnce() {
        return RequestInferenceOnce(
            bridge != null ? bridge.captureCamera : null, "DEFAULT", "frame");
    }

    public bool RequestInferenceOnce(
        Camera sourceCamera, string sourceId, string frameIdPrefix = null) {
        if (!isActiveAndEnabled || bridge == null || IsInferenceRunning)
            return false;
        if (sourceCamera == null) {
            Debug.LogError("[ShelfInference] A specified source camera is required.");
            return false;
        }
        string normalizedSource = string.IsNullOrWhiteSpace(sourceId)
            ? sourceCamera.name : sourceId.Trim().ToUpperInvariant();
        IsInferenceRunning = true;
        StartCoroutine(RunInferenceCycle(new ShelfInferenceRequestContext {
            camera = sourceCamera,
            sourceId = normalizedSource,
            frameIdPrefix = string.IsNullOrWhiteSpace(frameIdPrefix)
                ? normalizedSource.ToLowerInvariant() : frameIdPrefix
        }));
        return true;
    }

    public bool RequestDualInferenceOnce(
        Camera leftCamera, Camera rightCamera, string cycleId,
        string sessionId = null, string stationId = null) {
        if (!isActiveAndEnabled || bridge == null || IsInferenceRunning) return false;
        if (leftCamera == null || rightCamera == null || leftCamera == rightCamera) {
            Debug.LogError("[DualScan] Two distinct source cameras are required.");
            return false;
        }
        string normalizedCycle = string.IsNullOrWhiteSpace(cycleId)
            ? $"scan_cycle_{Time.frameCount:D6}" : cycleId.Trim();
        LastDualSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();
        LastDualStationId = string.IsNullOrWhiteSpace(stationId) ? null : stationId.Trim();
        IsInferenceRunning = true;
        StartCoroutine(RunDualInferenceCycle(
            leftCamera, rightCamera, normalizedCycle, LastDualSessionId, LastDualStationId));
        return true;
    }

    IEnumerator RunDualInferenceCycle(
        Camera leftCamera, Camera rightCamera, string cycleId,
        string sessionId, string stationId) {
        float startedRealtime = Time.realtimeSinceStartup;
        string baseUrl = serverUrl.TrimEnd('/');
        FrameInputData leftMetadata = null, rightMetadata = null;
        DualShelfInferenceResponse response = null;
        bool leftVisualization = false, rightVisualization = false;
        bool posePreserved = false;
        string failure = null;

        using (var health = UnityWebRequest.Get(baseUrl + "/health")) {
            activeRequest = health;
            health.timeout = Mathf.Max(1, timeoutSeconds);
            yield return health.SendWebRequest();
            activeRequest = null;
            bool ready = false;
            if (health.result == UnityWebRequest.Result.Success) {
                try {
                    var status = JsonUtility.FromJson<HealthStatus>(health.downloadHandler.text);
                    ready = status != null && status.status == "ok" &&
                        status.shelf_model_loaded && status.empty_model_loaded;
                } catch (ArgumentException exception) {
                    if (debugLogging)
                        Debug.LogWarning("[DualScan] Invalid health response: " + exception.Message);
                }
            }
            if (!ready) {
                failure = $"Python inference server unavailable at {baseUrl}";
                WarnOnce(failure);
                CompleteDualCycle(cycleId, leftCamera, rightCamera, leftMetadata, rightMetadata,
                    response, false, false, false, false, startedRealtime, failure);
                yield break;
            }
        }

        yield return new WaitForEndOfFrame();
        Vector3 rigPosition = transform.position;
        Quaternion rigRotation = transform.rotation;
        byte[] leftJpeg = null, rightJpeg = null;
        try {
            leftJpeg = bridge.CaptureRuntimeJpeg(
                leftCamera, jpegQuality, cycleId + "_left", out leftMetadata);
            Debug.Log($"[DualScan] Captured LEFT frame={leftMetadata.frame_id}");
            rightJpeg = bridge.CaptureRuntimeJpeg(
                rightCamera, jpegQuality, cycleId + "_right", out rightMetadata);
            Debug.Log($"[DualScan] Captured RIGHT frame={rightMetadata.frame_id}");
            posePreserved = Vector3.Distance(transform.position, rigPosition) <= .0001f &&
                Quaternion.Angle(transform.rotation, rigRotation) <= .001f;
            if (!posePreserved)
                throw new InvalidOperationException("RobotRig moved between dual captures.");
        } catch (Exception exception) {
            failure = "Dual camera capture failed: " + exception.Message;
        }
        if (leftJpeg == null || rightJpeg == null ||
            leftMetadata == null || rightMetadata == null) {
            CompleteDualCycle(cycleId, leftCamera, rightCamera, leftMetadata, rightMetadata,
                response, false, false, false, posePreserved, startedRealtime, failure);
            yield break;
        }

        var form = new List<IMultipartFormSection> {
            new MultipartFormFileSection(
                "left_image", leftJpeg, leftMetadata.image_path, "image/jpeg"),
            new MultipartFormDataSection("left_metadata", JsonUtility.ToJson(leftMetadata)),
            new MultipartFormFileSection(
                "right_image", rightJpeg, rightMetadata.image_path, "image/jpeg"),
            new MultipartFormDataSection("right_metadata", JsonUtility.ToJson(rightMetadata))
        };
        foreach (KeyValuePair<string, string> item in
                 BuildOperationalMetadata(sessionId, stationId))
            form.Add(new MultipartFormDataSection(item.Key, item.Value));
        Debug.Log($"[DualScan] POST /infer/dual cycle={cycleId}");
        using (var request = UnityWebRequest.Post(baseUrl + "/infer/dual", form)) {
            activeRequest = request;
            request.timeout = Mathf.Max(1, timeoutSeconds);
            yield return request.SendWebRequest();
            activeRequest = null;
            if (request.result != UnityWebRequest.Result.Success) {
                failure = request.error != null &&
                    request.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "Dual inference request timed out."
                    : $"Dual inference failed: HTTP {request.responseCode} {request.error}";
            } else {
                try {
                    response = DualShelfInferenceResponse.Parse(request.downloadHandler.text);
                    if (response.left.frame_id != leftMetadata.frame_id ||
                        response.right.frame_id != rightMetadata.frame_id)
                        throw new FormatException("Dual response frame IDs do not match captures.");
                    Debug.Log($"[DualScan] dual response complete cycle={cycleId}");
                } catch (Exception exception) {
                    failure = "Malformed dual inference response: " + exception.Message;
                    response = null;
                }
            }
        }
        if (response == null) {
            WarnOnce(failure);
            CompleteDualCycle(cycleId, leftCamera, rightCamera, leftMetadata, rightMetadata,
                null, false, false, false, posePreserved, startedRealtime, failure);
            yield break;
        }

        ApplyResponse("LEFT", response.left);
        ApplyResponse("RIGHT", response.right);
        if (!showOverlay || visualizationMode != ShelfVisualizationMode.PythonAnnotatedFrame) {
            leftVisualization = rightVisualization = true;
        } else {
            EnsureHud();
            ShelfInferenceResponse[] sides = { response.left, response.right };
            string[] sourceIds = { "LEFT", "RIGHT" };
            for (int sideIndex = 0; sideIndex < sides.Length; sideIndex++) {
                ShelfInferenceResponse side = sides[sideIndex];
                string sourceId = sourceIds[sideIndex];
                hud.PrepareVisualizationFrame(sourceId, side.frame_id);
                bool displayed = false;
                if (TryResolveVisualizationUrl(baseUrl, side.visualization_url, out string imageUrl)) {
                    using (var imageRequest = UnityWebRequest.Get(imageUrl)) {
                        activeRequest = imageRequest;
                        imageRequest.timeout = Mathf.Max(1, timeoutSeconds);
                        yield return imageRequest.SendWebRequest();
                        activeRequest = null;
                        if (imageRequest.result == UnityWebRequest.Result.Success) {
                            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                            displayed = texture.LoadImage(imageRequest.downloadHandler.data, true) &&
                                hud.TryShowVisualization(sourceId, side.frame_id, texture,
                                    side.image.width, side.image.height);
                            if (!displayed) Destroy(texture);
                        }
                        if (!displayed)
                            VisualizationWarning(
                                $"{sourceId} visualization failed: HTTP " +
                                $"{imageRequest.responseCode} {imageRequest.error}");
                    }
                } else VisualizationWarning(sourceId + " visualization URL is invalid.");
                if (sourceId == "LEFT") leftVisualization = displayed;
                else rightVisualization = displayed;
                if (displayed) Debug.Log($"[DualScan] {sourceId} visualization ready");
            }
        }
        string visualizationMessage = leftVisualization && rightVisualization ? null :
            $"Visualization outcome LEFT={leftVisualization} RIGHT={rightVisualization}";
        CompleteDualCycle(cycleId, leftCamera, rightCamera, leftMetadata, rightMetadata,
            response, true, leftVisualization, rightVisualization, posePreserved,
            startedRealtime, visualizationMessage);
    }

    void CompleteDualCycle(
        string cycleId, Camera leftCamera, Camera rightCamera,
        FrameInputData leftMetadata, FrameInputData rightMetadata,
        DualShelfInferenceResponse response, bool succeeded,
        bool leftVisualization, bool rightVisualization, bool posePreserved,
        float startedRealtime, string message) {
        IsInferenceRunning = false;
        float duration = Time.realtimeSinceStartup - startedRealtime;
        DualInferenceCycleCompleted?.Invoke(new DualShelfInferenceCycleResult {
            cycleId = cycleId, leftCamera = leftCamera, rightCamera = rightCamera,
            leftMetadata = leftMetadata, rightMetadata = rightMetadata, response = response,
            succeeded = succeeded, leftVisualizationDisplayed = leftVisualization,
            rightVisualizationDisplayed = rightVisualization,
            stationaryPosePreserved = posePreserved, message = message,
            durationSeconds = duration
        });
    }

    public static Dictionary<string, string> BuildOperationalMetadata(
        string sessionId, string stationId) {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(sessionId)) fields["session_id"] = sessionId.Trim();
        if (!string.IsNullOrWhiteSpace(stationId)) fields["station_id"] = stationId.Trim();
        return fields;
    }

    public bool RequestSessionCompletionOnce(string sessionId) {
        if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(sessionId)) return false;
        string normalized = sessionId.Trim();
        if (!TryRegisterCompletionRequest(completionRequests, normalized)) return false;
        SessionCompletionRequestCount++;
        StartCoroutine(RunSessionCompletion(normalized));
        return true;
    }

    public static bool TryRegisterCompletionRequest(
        HashSet<string> completedSessionIds, string sessionId) {
        return completedSessionIds != null && !string.IsNullOrWhiteSpace(sessionId) &&
            completedSessionIds.Add(sessionId.Trim());
    }

    IEnumerator RunSessionCompletion(string sessionId) {
        string url = serverUrl.TrimEnd('/') + "/session/" +
            UnityWebRequest.EscapeURL(sessionId) + "/complete";
        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)) {
            request.downloadHandler = new DownloadHandlerBuffer();
            activeRequest = request;
            request.timeout = Mathf.Max(1, timeoutSeconds);
            yield return request.SendWebRequest();
            activeRequest = null;
            if (request.result == UnityWebRequest.Result.Success)
                Debug.Log($"[DualScan] session completion sent id={sessionId}");
            else
                Debug.LogWarning(
                    $"[DualScan] session completion failed id={sessionId}: " +
                    $"HTTP {request.responseCode} {request.error}");
        }
    }

    IEnumerator InferenceLoop() {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, startupDelay));
        while (isActiveAndEnabled) {
            if (RequestInferenceOnce())
                while (isActiveAndEnabled && IsInferenceRunning) yield return null;
            yield return new WaitForSecondsRealtime(Mathf.Max(.1f, inferenceInterval));
        }
    }

    IEnumerator RunInferenceCycle(ShelfInferenceRequestContext context) {
        float startedRealtime = Time.realtimeSinceStartup;
        string baseUrl = serverUrl.TrimEnd('/');
        FrameInputData metadata = null;
        ShelfInferenceResponse response = null;
        bool visualizationDisplayed = false;

        using (var health = UnityWebRequest.Get(baseUrl + "/health")) {
            activeRequest = health;
            health.timeout = Mathf.Max(1, timeoutSeconds);
            yield return health.SendWebRequest();
            activeRequest = null;
            bool ready = false;
            if (health.result == UnityWebRequest.Result.Success) {
                try {
                    var status = JsonUtility.FromJson<HealthStatus>(health.downloadHandler.text);
                    ready = status != null && status.status == "ok" &&
                        status.shelf_model_loaded && status.empty_model_loaded;
                } catch (ArgumentException exception) {
                    if (debugLogging)
                        Debug.LogWarning("[ShelfInference] Invalid health response: " + exception.Message);
                }
            }
            if (!ready) {
                string message = $"Python inference server unavailable at {baseUrl}";
                WarnOnce(message);
                ShowStatus("RAF İZLEME", "Python inference server bağlantısı bekleniyor");
                CompleteCycle(context, false, startedRealtime, metadata, response, false, message);
                yield break;
            }
        }

        lastWarning = null;
        yield return new WaitForEndOfFrame();
        byte[] jpeg = null;
        try {
            jpeg = bridge.CaptureRuntimeJpeg(
                context.camera, jpegQuality, context.frameIdPrefix, out metadata);
            bridge.LogCameraState("AfterCaptureTopLeftTextureReturns", context.camera);
        } catch (Exception e) {
            WarnOnce("Kamera yakalama hatası: " + e.Message);
        }
        if (jpeg == null || metadata == null) {
            const string message = "Camera capture did not produce an inference frame.";
            ShowStatus("RAF İZLEME", "Kamera yakalama hatası");
            CompleteCycle(context, false, startedRealtime, metadata, response, false, message);
            yield break;
        }

        if (debugLogging)
            Debug.Log($"[ShelfInference] Starting {metadata.frame_id} at {startedRealtime:F3}s");
        var form = new List<IMultipartFormSection> {
            new MultipartFormFileSection("image", jpeg, metadata.image_path, "image/jpeg"),
            new MultipartFormDataSection("metadata", JsonUtility.ToJson(metadata))
        };
        string requestError = null;
        using (var request = UnityWebRequest.Post(baseUrl + "/infer", form)) {
            activeRequest = request;
            request.timeout = Mathf.Max(1, timeoutSeconds);
            yield return request.SendWebRequest();
            activeRequest = null;
            if (request.result != UnityWebRequest.Result.Success) {
                requestError = request.error != null &&
                    (request.error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     request.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                    ? "Inference request timed out."
                    : $"Inference isteği başarısız: HTTP {request.responseCode} {request.error}";
            } else {
                try {
                    response = ShelfInferenceResponse.Parse(request.downloadHandler.text);
                    if (response.frame_id != metadata.frame_id)
                        throw new FormatException("Inference frame_id gönderilen kareyle uyuşmuyor.");
                    lastWarning = null;
                } catch (Exception e) {
                    requestError = "Malformed inference response: " + e.Message;
                }
            }
        }

        if (response == null) {
            WarnOnce(requestError);
            ShowStatus("RAF İZLEME", "Inference bağlantısı tekrar denenecek");
            CompleteCycle(context, false, startedRealtime, metadata, response, false, requestError);
            yield break;
        }

        string visualizationMessage = null;
        if (showOverlay && visualizationMode == ShelfVisualizationMode.PythonAnnotatedFrame) {
            EnsureHud();
            hud.PrepareVisualizationFrame(context.sourceId, response.frame_id);
            if (TryResolveVisualizationUrl(baseUrl, response.visualization_url, out string imageUrl)) {
                using (var imageRequest = UnityWebRequest.Get(imageUrl)) {
                    activeRequest = imageRequest;
                    imageRequest.timeout = Mathf.Max(1, timeoutSeconds);
                    yield return imageRequest.SendWebRequest();
                    activeRequest = null;
                    if (imageRequest.result == UnityWebRequest.Result.Success) {
                        bridge.LogCameraState("AfterVisualizationDownload", context.camera);
                        var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                        if (texture.LoadImage(imageRequest.downloadHandler.data, true) &&
                            hud.TryShowVisualization(
                                context.sourceId, response.frame_id, texture,
                                response.image.width, response.image.height)) {
                            visualizationDisplayed = true;
                            bridge.LogCameraState("AfterRawImageUpdated", context.camera);
                            lastVisualizationWarning = null;
                        } else {
                            Destroy(texture);
                            visualizationMessage = "Visualization image could not be decoded.";
                            VisualizationWarning(visualizationMessage);
                        }
                    } else {
                        visualizationMessage =
                            $"Visualization download failed: HTTP {imageRequest.responseCode} {imageRequest.error}";
                        VisualizationWarning(visualizationMessage);
                    }
                }
            } else {
                visualizationMessage = "Inference response has no valid visualization URL.";
                VisualizationWarning(visualizationMessage);
            }
        }

        ApplyResponse(context.sourceId, response);
        CompleteCycle(
            context, true, startedRealtime, metadata, response,
            !showOverlay || visualizationMode != ShelfVisualizationMode.PythonAnnotatedFrame ||
                visualizationDisplayed,
            visualizationMessage);
    }

    void CompleteCycle(
        ShelfInferenceRequestContext context, bool succeeded, float startedRealtime,
        FrameInputData metadata, ShelfInferenceResponse response,
        bool visualizationDisplayed, string message) {
        IsInferenceRunning = false;
        float completedRealtime = Time.realtimeSinceStartup;
        string frameId = response != null ? response.frame_id : metadata != null ? metadata.frame_id : null;
        if (debugLogging) {
            string outcome = succeeded ? "completed" : "failed";
            Debug.Log(
                $"[ShelfInference] {frameId ?? "no-frame"} {outcome} in " +
                $"{completedRealtime - startedRealtime:F3}s" +
                (string.IsNullOrWhiteSpace(message) ? string.Empty : $" | {message}"));
        }
        InferenceCycleCompleted?.Invoke(new ShelfInferenceCycleResult {
            sourceId = context.sourceId,
            sourceCamera = context.camera,
            frameId = frameId,
            metadata = metadata,
            response = response,
            succeeded = succeeded,
            visualizationDisplayed = visualizationDisplayed,
            message = message,
            durationSeconds = completedRealtime - startedRealtime
        });
    }

    void ApplyResponse(string sourceId, ShelfInferenceResponse response) {
        if (!states.TryGetValue(sourceId, out ShelfInferenceState state)) {
            state = new ShelfInferenceState();
            states[sourceId] = state;
        }
        ShelfInferenceShelf displayed = null;
        foreach (var shelf in response.shelves) {
            if (shelf.shelf_id == "UNKNOWN_SHELF") continue;
            string displayId = !string.IsNullOrWhiteSpace(shelf.shelf_level_id)
                ? shelf.shelf_level_id : shelf.shelf_id;
            if (state.Update(shelf)) {
                if (shelf.empty_space_count > 0) {
                    Debug.Log($"[ShelfInference] {displayId} | {shelf.empty_space_count} boşluk");
                    foreach (var detection in shelf.detections ?? Array.Empty<ShelfInferenceDetection>())
                        Debug.Log(
                            $"[ShelfInference] {displayId} rafının {detection.section} bölümünde boşluk var.");
                } else Debug.Log($"[ShelfInference] {displayId} | Boşluk yok");
            }
            if (ShouldPreferShelf(shelf, displayed)) displayed = shelf;
        }
        if (showOverlay) {
            EnsureHud();
            hud.ShowResponse(sourceId, displayed, response);
        } else if (hud != null) hud.SetVisible(false);
        if (debugLogging) {
            if (response.debug != null && response.debug.debug_only) {
                Debug.Log(
                    $"[ShelfInference Debug] {response.frame_id} | Failure stage: {response.debug.failure_stage} | " +
                    $"Full frame: {response.debug.full_frame_raw} | Full shelf ROI: {response.debug.full_shelf_roi_raw} | " +
                    $"Tiles@0.01: {response.debug.tile_raw} | Tiles@production: {response.debug.tile_production_raw} | " +
                    $"Production raw: {response.debug.production_raw} | Mask rejected: {response.debug.mask_rejected} | " +
                    $"Final: {response.debug.final} | {response.debug.directory}");
            } else {
                Debug.Log(
                    $"[ShelfInference] {response.frame_id}: {response.counts.matched_shelves} eşleşme, " +
                    $"{response.counts.final_empty_spaces} boşluk");
            }
        }
    }

    public static bool ShouldPreferShelf(ShelfInferenceShelf candidate, ShelfInferenceShelf current) {
        if (candidate == null) return false;
        if (current == null) return true;
        bool candidateGap = candidate.empty_space_count > 0;
        bool currentGap = current.empty_space_count > 0;
        if (candidateGap != currentGap) return candidateGap;
        string candidateParent = candidate.parent_shelf_id ?? candidate.shelf_id ?? string.Empty;
        string currentParent = current.parent_shelf_id ?? current.shelf_id ?? string.Empty;
        int parentOrder = string.CompareOrdinal(candidateParent, currentParent);
        if (parentOrder != 0) return parentOrder < 0;
        int candidateLevel = candidate.level_number > 0 ? candidate.level_number : int.MaxValue;
        int currentLevel = current.level_number > 0 ? current.level_number : int.MaxValue;
        if (candidateLevel != currentLevel) return candidateLevel < currentLevel;
        return string.CompareOrdinal(candidate.shelf_id, current.shelf_id) < 0;
    }

    void WarnOnce(string message) {
        if (message == lastWarning) return;
        lastWarning = message;
        Debug.LogWarning("[ShelfInference] " + message);
    }

    void VisualizationWarning(string message) {
        if (message == lastVisualizationWarning) return;
        lastVisualizationWarning = message;
        Debug.LogWarning("[ShelfInference] " + message);
    }

    public static bool TryResolveVisualizationUrl(
        string serverBaseUrl, string visualizationPath, out string resolvedUrl) {
        resolvedUrl = null;
        if (string.IsNullOrWhiteSpace(serverBaseUrl) ||
            string.IsNullOrWhiteSpace(visualizationPath) ||
            !visualizationPath.StartsWith("/visualization/", StringComparison.Ordinal) ||
            !visualizationPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            visualizationPath.Contains("..")) return false;
        if (!Uri.TryCreate(serverBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out Uri baseUri) ||
            !Uri.TryCreate(baseUri, visualizationPath, out Uri resolved) ||
            resolved.Scheme != baseUri.Scheme || resolved.Host != baseUri.Host ||
            resolved.Port != baseUri.Port) return false;
        resolvedUrl = resolved.AbsoluteUri;
        return true;
    }

    void EnsureHud() {
        if (!showOverlay) return;
        if (hud == null) hud = GetComponent<ShelfInferenceHud>();
        if (hud == null) hud = gameObject.AddComponent<ShelfInferenceHud>();
        hud.Initialize(bridge != null ? bridge.captureCamera : null);
        hud.SetVisualizationMode(visualizationMode);
        hud.SetVisible(true);
    }

    void ShowStatus(string title, string body) {
        if (!showOverlay) return;
        EnsureHud();
        hud.ShowStatus(title, body, false);
    }

    [Serializable]
    class HealthStatus {
        public string status;
        public bool shelf_model_loaded, empty_model_loaded;
    }
}
