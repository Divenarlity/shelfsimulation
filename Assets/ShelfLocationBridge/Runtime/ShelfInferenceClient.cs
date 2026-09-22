using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[RequireComponent(typeof(ShelfLocationBridge))]
public class ShelfInferenceClient : MonoBehaviour {
    [Header("Connection")]
    public string serverUrl="http://127.0.0.1:8000";
    public bool autoStart=true;
    [Min(0)] public float startupDelay=1f;
    [Min(.1f)] public float inferenceInterval=1f;
    [Min(1)] public int timeoutSeconds=10;
    [Header("Capture")]
    public ShelfLocationBridge bridge;
    [Range(1,100)] public int jpegQuality=80;
    [Header("Display")]
    public bool showOverlay=true;
    public bool debugLogging=false;

    public event Action<ShelfInferenceResponse> ResultReceived;

    readonly ShelfInferenceState state=new ShelfInferenceState();
    UnityWebRequest activeRequest;
    GUIStyle overlayStyle;
    string displayText="Shelf inference başlatılıyor...";
    string lastWarning;

    void Awake() {
        if(bridge==null)bridge=GetComponent<ShelfLocationBridge>();
        if(bridge==null) {
            Debug.LogError("ShelfInferenceClient requires ShelfLocationBridge on the same GameObject.");
            enabled=false;
        }
    }

    void OnEnable() {
        if(autoStart)StartCoroutine(InferenceLoop());
    }

    void OnDisable() {
        if(activeRequest!=null)activeRequest.Abort();
        StopAllCoroutines();
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
                    displayText="Python inference server bağlantısı bekleniyor";
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
                    displayText="Inference bağlantısı tekrar denenecek";
                } else {
                    try {
                        var response=ShelfInferenceResponse.Parse(request.downloadHandler.text);
                        if(response.frame_id!=metadata.frame_id)
                            throw new FormatException("Inference frame_id gönderilen kareyle uyuşmuyor.");
                        lastWarning=null;
                        ApplyResponse(response);
                    } catch(Exception e) {
                        WarnOnce("Malformed inference response: "+e.Message);
                        displayText="Inference yanıtı geçersiz";
                    }
                }
            }
            yield return new WaitForSecondsRealtime(Mathf.Max(.1f,inferenceInterval));
        }
    }

    void ApplyResponse(ShelfInferenceResponse response) {
        ShelfInferenceShelf displayed=null;
        foreach(var shelf in response.shelves) {
            if(shelf.shelf_id=="UNKNOWN_SHELF")continue;
            if(state.Update(shelf)) {
                if(shelf.empty_space_count>0) {
                    Debug.Log($"[ShelfInference] {shelf.shelf_id} | {shelf.empty_space_count} boşluk");
                    foreach(var detection in shelf.detections??Array.Empty<ShelfInferenceDetection>())
                        Debug.Log($"[ShelfInference] {shelf.shelf_id} rafının {detection.section} bölümünde boşluk var.");
                } else Debug.Log($"[ShelfInference] {shelf.shelf_id} | Boşluk yok");
            }
            if(displayed==null||(displayed.empty_space_count==0&&shelf.empty_space_count>0))displayed=shelf;
        }
        if(displayed==null)displayText="Bilinen raf eşleşmesi bekleniyor";
        else if(displayed.empty_space_count==0)displayText=$"{displayed.shelf_id} — boşluk yok";
        else {
            var s=displayed.sections;
            displayText=$"RAF BOŞLUĞU TESPİT EDİLDİ\n{displayed.shelf_id} — {displayed.empty_space_count} boşluk\nSOL: {s.SOL} | ORTA: {s.ORTA} | SAĞ: {s.SAĞ}";
        }
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

    void WarnOnce(string message) {
        if(message==lastWarning)return;
        lastWarning=message;
        Debug.LogWarning("[ShelfInference] "+message);
    }

    void OnGUI() {
        if(!showOverlay)return;
        if(overlayStyle==null) {
            overlayStyle=new GUIStyle(GUI.skin.box){fontSize=18,alignment=TextAnchor.MiddleLeft,wordWrap=true};
            overlayStyle.normal.textColor=Color.white;
        }
        GUI.Box(new Rect(Mathf.Max(10,Screen.width-435),15,420,110),displayText,overlayStyle);
    }

    [Serializable]
    class HealthStatus {
        public string status;
        public bool shelf_model_loaded,empty_model_loaded;
    }
}
