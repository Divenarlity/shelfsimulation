using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public enum ShelfVisualizationMode {
    PythonAnnotatedFrame,
    UnityGeometryOverlay
}

[RequireComponent(typeof(ShelfLocationBridge))]
public class ShelfInferenceClient : MonoBehaviour {
    [Header("Connection")]
    public string serverUrl="http://127.0.0.1:8000";
    public bool autoStart=true;
    [Min(0)] public float startupDelay=1f;
    [Min(.1f)] public float inferenceInterval=1f;
    [Min(1)] public int timeoutSeconds=30;
    [Header("Capture")]
    public ShelfLocationBridge bridge;
    [Range(1,100)] public int jpegQuality=80;
    [Header("Display")]
    public bool showOverlay=true;
    public ShelfVisualizationMode visualizationMode=ShelfVisualizationMode.PythonAnnotatedFrame;
    public bool debugLogging=false;
    public ShelfInferenceHud hud;

    public event Action<ShelfInferenceResponse> ResultReceived;

    readonly ShelfInferenceState state=new ShelfInferenceState();
    UnityWebRequest activeRequest;
    string lastWarning;
    string lastVisualizationWarning;

    void Awake() {
        if(bridge==null)bridge=GetComponent<ShelfLocationBridge>();
        if(bridge==null) {
            Debug.LogError("ShelfInferenceClient requires ShelfLocationBridge on the same GameObject.");
            enabled=false;
            return;
        }
        EnsureHud();
    }

    void OnEnable() {
        EnsureHud();
        if(autoStart)StartCoroutine(InferenceLoop());
    }

    void OnDisable() {
        if(activeRequest!=null)activeRequest.Abort();
        StopAllCoroutines();
        if(hud!=null) {
            hud.ClearVisualization();
            hud.SetVisible(false);
        }
    }

    IEnumerator InferenceLoop() {
        yield return new WaitForSecondsRealtime(Mathf.Max(0,startupDelay));
        while(isActiveAndEnabled) {
            string baseUrl=serverUrl.TrimEnd('/');
            using(var health=UnityWebRequest.Get(baseUrl+"/health")) {
                activeRequest=health;
                health.timeout=Mathf.Max(1,timeoutSeconds);
                yield return health.SendWebRequest();
                activeRequest=null;
                bool ready=false;
                if(health.result==UnityWebRequest.Result.Success) {
                    try {
                        var status=JsonUtility.FromJson<HealthStatus>(health.downloadHandler.text);
                        ready=status!=null&&status.status=="ok"&&status.shelf_model_loaded&&status.empty_model_loaded;
                    } catch(Exception) { }
                }
                if(!ready) {
                    WarnOnce($"Python inference server unavailable at {baseUrl}");
                    ShowStatus("RAF İZLEME", "Python inference server bağlantısı bekleniyor");
                    yield return new WaitForSecondsRealtime(Mathf.Max(.1f,inferenceInterval));
                    continue;
                }
            }

            lastWarning=null;
            yield return new WaitForEndOfFrame();
            byte[] jpeg=null;
            FrameInputData metadata=null;
            try {
                jpeg=bridge.CaptureRuntimeJpeg(jpegQuality,out metadata);
                bridge.LogCameraState("AfterCaptureTopLeftTextureReturns");
            } catch(Exception e) {
                WarnOnce("Kamera yakalama hatası: "+e.Message);
            }
            if(jpeg==null||metadata==null) {
                yield return new WaitForSecondsRealtime(Mathf.Max(.1f,inferenceInterval));
                continue;
            }

            var form=new List<IMultipartFormSection> {
                new MultipartFormFileSection("image",jpeg,metadata.image_path,"image/jpeg"),
                new MultipartFormDataSection("metadata",JsonUtility.ToJson(metadata))
            };
            ShelfInferenceResponse response=null;
            using(var request=UnityWebRequest.Post(baseUrl+"/infer",form)) {
                activeRequest=request;
                request.timeout=Mathf.Max(1,timeoutSeconds);
                yield return request.SendWebRequest();
                activeRequest=null;
                if(request.result!=UnityWebRequest.Result.Success) {
                    string message=request.error!=null&&
                        (request.error.IndexOf("timed out",StringComparison.OrdinalIgnoreCase)>=0||
                         request.error.IndexOf("timeout",StringComparison.OrdinalIgnoreCase)>=0)
                        ? "Inference request timed out."
                        : $"Inference isteği başarısız: HTTP {request.responseCode} {request.error}";
                    WarnOnce(message);
                    ShowStatus("RAF İZLEME", "Inference bağlantısı tekrar denenecek");
                } else {
                    try {
                        response=ShelfInferenceResponse.Parse(request.downloadHandler.text);
                        if(response.frame_id!=metadata.frame_id)
                            throw new FormatException("Inference frame_id gönderilen kareyle uyuşmuyor.");
                        lastWarning=null;
                    } catch(Exception e) {
                        response=null;
                        WarnOnce("Malformed inference response: "+e.Message);
                        ShowStatus("RAF İZLEME", "Inference yanıtı geçersiz");
                    }
                }
            }
            if(response!=null) {
                if(showOverlay&&visualizationMode==ShelfVisualizationMode.PythonAnnotatedFrame) {
                    EnsureHud();
                    hud.PrepareVisualizationFrame(response.frame_id);
                    if(TryResolveVisualizationUrl(baseUrl,response.visualization_url,out string imageUrl)) {
                        using(var imageRequest=UnityWebRequest.Get(imageUrl)) {
                            activeRequest=imageRequest;
                            imageRequest.timeout=Mathf.Max(1,timeoutSeconds);
                            yield return imageRequest.SendWebRequest();
                            activeRequest=null;
                            if(imageRequest.result==UnityWebRequest.Result.Success) {
                                bridge.LogCameraState("AfterVisualizationDownload");
                                var texture=new Texture2D(2,2,TextureFormat.RGB24,false);
                                if(texture.LoadImage(imageRequest.downloadHandler.data,true)&&
                                   hud.TryShowVisualization(
                                       response.frame_id,texture,response.image.width,response.image.height)) {
                                    bridge.LogCameraState("AfterRawImageUpdated");
                                    lastVisualizationWarning=null;
                                } else {
                                    Destroy(texture);
                                    VisualizationWarning("Visualization image could not be decoded.");
                                }
                            } else {
                                VisualizationWarning(
                                    $"Visualization download failed: HTTP {imageRequest.responseCode} {imageRequest.error}");
                            }
                        }
                    } else VisualizationWarning("Inference response has no valid visualization URL.");
                }
                ApplyResponse(response);
            }
            yield return new WaitForSecondsRealtime(Mathf.Max(.1f,inferenceInterval));
        }
    }

    void ApplyResponse(ShelfInferenceResponse response) {
        ShelfInferenceShelf displayed=null;
        foreach(var shelf in response.shelves) {
            if(shelf.shelf_id=="UNKNOWN_SHELF")continue;
            string displayId=!string.IsNullOrWhiteSpace(shelf.shelf_level_id)
                ? shelf.shelf_level_id:shelf.shelf_id;
            if(state.Update(shelf)) {
                if(shelf.empty_space_count>0) {
                    Debug.Log($"[ShelfInference] {displayId} | {shelf.empty_space_count} boşluk");
                    foreach(var detection in shelf.detections??Array.Empty<ShelfInferenceDetection>())
                        Debug.Log($"[ShelfInference] {displayId} rafının {detection.section} bölümünde boşluk var.");
                } else Debug.Log($"[ShelfInference] {displayId} | Boşluk yok");
            }
            if(ShouldPreferShelf(shelf,displayed))displayed=shelf;
        }
        if(showOverlay) {
            EnsureHud();
            hud.ShowResponse(displayed,response);
        } else if(hud!=null)hud.SetVisible(false);
        if(debugLogging) {
            if(response.debug!=null&&response.debug.debug_only) {
                Debug.Log($"[ShelfInference Debug] {response.frame_id} | Failure stage: {response.debug.failure_stage} | " +
                    $"Full frame: {response.debug.full_frame_raw} | Full shelf ROI: {response.debug.full_shelf_roi_raw} | " +
                    $"Tiles@0.01: {response.debug.tile_raw} | Tiles@production: {response.debug.tile_production_raw} | " +
                    $"Production raw: {response.debug.production_raw} | Mask rejected: {response.debug.mask_rejected} | " +
                    $"Final: {response.debug.final} | {response.debug.directory}");
            } else Debug.Log($"[ShelfInference] {response.frame_id}: {response.counts.matched_shelves} eşleşme, {response.counts.final_empty_spaces} boşluk");
        }
        ResultReceived?.Invoke(response);
    }

    public static bool ShouldPreferShelf(ShelfInferenceShelf candidate,ShelfInferenceShelf current) {
        if(candidate==null)return false;
        if(current==null)return true;
        bool candidateGap=candidate.empty_space_count>0;
        bool currentGap=current.empty_space_count>0;
        if(candidateGap!=currentGap)return candidateGap;
        string candidateParent=candidate.parent_shelf_id??candidate.shelf_id??string.Empty;
        string currentParent=current.parent_shelf_id??current.shelf_id??string.Empty;
        int parentOrder=string.CompareOrdinal(candidateParent,currentParent);
        if(parentOrder!=0)return parentOrder<0;
        int candidateLevel=candidate.level_number>0?candidate.level_number:int.MaxValue;
        int currentLevel=current.level_number>0?current.level_number:int.MaxValue;
        if(candidateLevel!=currentLevel)return candidateLevel<currentLevel;
        return string.CompareOrdinal(candidate.shelf_id,current.shelf_id)<0;
    }

    void WarnOnce(string message) {
        if(message==lastWarning)return;
        lastWarning=message;
        Debug.LogWarning("[ShelfInference] "+message);
    }

    void VisualizationWarning(string message) {
        if(message==lastVisualizationWarning)return;
        lastVisualizationWarning=message;
        Debug.LogWarning("[ShelfInference] "+message);
    }

    public static bool TryResolveVisualizationUrl(
        string serverBaseUrl,string visualizationPath,out string resolvedUrl) {
        resolvedUrl=null;
        if(string.IsNullOrWhiteSpace(serverBaseUrl)||
           string.IsNullOrWhiteSpace(visualizationPath)||
           !visualizationPath.StartsWith("/visualization/",StringComparison.Ordinal)||
           !visualizationPath.EndsWith(".jpg",StringComparison.OrdinalIgnoreCase)||
           visualizationPath.Contains(".."))return false;
        if(!Uri.TryCreate(serverBaseUrl.TrimEnd('/')+"/",UriKind.Absolute,out Uri baseUri)||
           !Uri.TryCreate(baseUri,visualizationPath,out Uri resolved)||
           resolved.Scheme!=baseUri.Scheme||resolved.Host!=baseUri.Host||resolved.Port!=baseUri.Port)
            return false;
        resolvedUrl=resolved.AbsoluteUri;
        return true;
    }

    void EnsureHud() {
        if(!showOverlay)return;
        if(hud==null)hud=GetComponent<ShelfInferenceHud>();
        if(hud==null)hud=gameObject.AddComponent<ShelfInferenceHud>();
        hud.Initialize(bridge!=null?bridge.captureCamera:null);
        hud.SetVisualizationMode(visualizationMode);
        hud.SetVisible(true);
    }

    void ShowStatus(string title,string body) {
        if(!showOverlay)return;
        EnsureHud();
        hud.ShowStatus(title,body,false);
    }

    [Serializable]
    class HealthStatus {
        public string status;
        public bool shelf_model_loaded,empty_model_loaded;
    }
}
