using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShelfLocationTools {
    public static void ValidateMarketSceneBatch() {
        EditorSceneManager.OpenScene("Assets/Market_01.unity", OpenSceneMode.Single);
        Validate();
    }

    public static void ExportMarketSceneBatch() {
        EditorSceneManager.OpenScene("Assets/Market_01.unity", OpenSceneMode.Single);
        Export();
    }

    static ShelfLocationBridge GetBridgeInstance() {
        return UnityEngine.Object.FindFirstObjectByType<ShelfLocationBridge>();
    }

    [MenuItem("Tools/Shelf Location/Auto Setup")]
    public static void Setup() {
        var bridges = UnityEngine.Object.FindObjectsByType<ShelfLocationBridge>(FindObjectsSortMode.None);
        ShelfLocationBridge bridge;
        if (bridges.Length == 0) {
            var go = new GameObject("ShelfLocationBridge");
            bridge = go.AddComponent<ShelfLocationBridge>();
            Undo.RegisterCreatedObjectUndo(go, "Shelf Location Auto Setup");
        } else {
            bridge = bridges[0];
            for (int i = 1; i < bridges.Length; i++) {
                Undo.DestroyObjectImmediate(bridges[i].gameObject);
            }
        }

        if (bridge.captureCamera == null) {
            bridge.captureCamera = Camera.main != null ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
        }
        if (bridge.resolver == null) {
            bridge.resolver = UnityEngine.Object.FindFirstObjectByType<ShelfResolver>();
        }
        EditorUtility.SetDirty(bridge);

        var recorders = UnityEngine.Object.FindObjectsByType<UnitySequenceRecorder>(FindObjectsSortMode.None);
        UnitySequenceRecorder recorder;
        if (recorders.Length == 0) {
            recorder = bridge.gameObject.AddComponent<UnitySequenceRecorder>();
            Undo.RegisterCreatedObjectUndo(recorder, "Shelf Location Auto Setup");
        } else {
            recorder = recorders[0];
            for (int i = 1; i < recorders.Length; i++) {
                Undo.DestroyObjectImmediate(recorders[i]);
            }
        }

        recorder.bridge = bridge;
        EditorUtility.SetDirty(recorder);

        Debug.Log("Auto Setup tamamlandı: Sahne için tek ShelfLocationBridge ve UnitySequenceRecorder doğrulandı.");
    }

    static UnitySequenceRecorder GetValidRecorder() {
        var recorders = UnityEngine.Object.FindObjectsByType<UnitySequenceRecorder>(FindObjectsSortMode.None);
        if (recorders.Length == 0) {
            Debug.LogError("Sahnede UnitySequenceRecorder bulunamadı. Önce Auto Setup çalıştırın.");
            return null;
        }
        if (recorders.Length > 1) {
            Debug.LogError($"Sahnede birden fazla ({recorders.Length}) UnitySequenceRecorder var. Önce Auto Setup çalıştırın.");
            return null;
        }
        var recorder = recorders[0];
        if (recorder.bridge == null) {
            Debug.LogError("UnitySequenceRecorder bridge referansı eksik. Önce Auto Setup çalıştırın.");
            return null;
        }
        return recorder;
    }

    [MenuItem("Tools/Shelf Location/Start Mapping Recording")]
    public static void StartMappingRecording() {
        if (!EditorApplication.isPlaying) {
            Debug.LogError("Recording başlatmak için Play modunda olmalısınız.");
            return;
        }
        var recorder = GetValidRecorder();
        if (recorder == null) return;
        recorder.StartMappingPass();
        Debug.Log($"Mapping kaydı başlatıldı. Session ID: {recorder.bridge.sessionId}");
    }

    [MenuItem("Tools/Shelf Location/Start Localization Recording")]
    public static void StartLocalizationRecording() {
        if (!EditorApplication.isPlaying) {
            Debug.LogError("Recording başlatmak için Play modunda olmalısınız.");
            return;
        }
        var recorder = GetValidRecorder();
        if (recorder == null) return;
        recorder.StartLocalizationPass();
        Debug.Log($"Localization kaydı başlatıldı. Session ID: {recorder.bridge.sessionId}");
    }

    [MenuItem("Tools/Shelf Location/Stop Recording")]
    public static void StopRecording() {
        if (!EditorApplication.isPlaying) {
            Debug.LogError("Recording durdurmak için Play modunda olmalısınız.");
            return;
        }
        var recorder = GetValidRecorder();
        if (recorder == null) return;
        recorder.StopRecording();
        Debug.Log($"Kaydetme durduruldu. Session ID: {recorder.bridge.sessionId}");
    }

    static string ValidateGeometry(ShelfMapEntry e) {
        if (e.corners_world == null || e.corners_world.Length != 4) return "dört köşe eksik";
        Vector3[] p = new Vector3[4];
        for (int i = 0; i < 4; i++) p[i] = new Vector3(e.corners_world[i][0], e.corners_world[i][1], e.corners_world[i][2]);
        Vector3 up = p[1] - p[0], right = p[3] - p[0], normal = Vector3.Cross(up, right);
        if (up.magnitude < .001f || right.magnitude < .001f || normal.magnitude < .001f) return "sıfır alanlı geometri";
        float plane = Mathf.Abs(Vector3.Dot((p[2] - p[0]), normal.normalized));
        if (plane > .005f) return "düzlemsel olmayan köşeler";
        if (Vector3.Dot(p[2] - p[1], right) <= 0 || Vector3.Dot(p[2] - p[3], up) <= 0) return "köşe sırası BL,TL,TR,BR değil";
        if (e.front_normal_world == null || new Vector3(e.front_normal_world[0], e.front_normal_world[1], e.front_normal_world[2]).sqrMagnitude < .5f) return "ön normal geçersiz";
        return null;
    }

    [MenuItem("Tools/Shelf Location/Validate Scene")]
    public static void Validate() {
        var errors = new List<string>();
        var ids = new HashSet<string>();
        var signatures = new HashSet<string>();

        var bridges = UnityEngine.Object.FindObjectsByType<ShelfLocationBridge>(FindObjectsSortMode.None);
        if (bridges.Length == 0) {
            errors.Add("Sahnede ShelfLocationBridge eksik.");
        } else if (bridges.Length > 1) {
            errors.Add($"Sahnede birden fazla ShelfLocationBridge bulundu ({bridges.Length} adet).");
        }

        var recorders = UnityEngine.Object.FindObjectsByType<UnitySequenceRecorder>(FindObjectsSortMode.None);
        if (recorders.Length == 0) {
            errors.Add("Sahnede UnitySequenceRecorder eksik.");
        } else if (recorders.Length > 1) {
            errors.Add($"Sahnede birden fazla UnitySequenceRecorder bulundu ({recorders.Length} adet).");
        }

        if (bridges.Length == 1 && recorders.Length == 1) {
            if (recorders[0].bridge == null) {
                errors.Add("UnitySequenceRecorder.bridge atanmamış.");
            } else if (recorders[0].bridge != bridges[0]) {
                errors.Add("UnitySequenceRecorder başka bir bridge referansına sahip.");
            }
        }

        ShelfLocationBridge b = bridges.Length > 0 ? bridges[0] : null;

        if (b != null) {
            if (b.captureCamera == null) {
                errors.Add("ShelfLocationBridge captureCamera atanmamış.");
            } else {
                Camera c = b.captureCamera;
                if (c.fieldOfView <= 0 || c.nearClipPlane <= 0 || c.farClipPlane <= c.nearClipPlane) {
                    errors.Add($"Kamera parametreleri geçersiz: FOV={c.fieldOfView}, Near={c.nearClipPlane}, Far={c.farClipPlane}");
                }
            }

            List<ShelfMapEntry> entries;
            try {
                entries = b.Entries();
            } catch (Exception e) {
                errors.Add(e.Message);
                entries = new List<ShelfMapEntry>();
            }

            foreach (var e in entries) {
                if (string.IsNullOrWhiteSpace(e.shelf_id) || !ids.Add(e.shelf_id)) errors.Add($"Boş/yinelenen ID: {e.shelf_id}");
                string geometry = ValidateGeometry(e);
                if (geometry != null) errors.Add($"{e.shelf_id}: {geometry}");
                if (e.corners_world != null) {
                    string signature = "";
                    foreach (var p in e.corners_world) signature += $"{p[0]:F3},{p[1]:F3},{p[2]:F3};";
                    if (!signatures.Add(signature)) errors.Add($"{e.shelf_id}: başka raf bölgesiyle aynı geometri");
                }
            }

            try {
                Directory.CreateDirectory(b.DataRoot);
                string p = Path.Combine(b.DataRoot, ".write_test");
                File.WriteAllText(p, "ok");
                File.Delete(p);
            } catch (Exception e) {
                errors.Add($"Çıktı yazılamıyor: {e.Message}");
            }

            if (errors.Count == 0) Debug.Log($"Shelf Location doğrulaması başarılı: {entries.Count} atanabilir mantıksal raf bölgesi.");
        }

        foreach (var id in UnityEngine.Object.FindObjectsByType<ShelfIdentity>(FindObjectsSortMode.None)) {
            int child = id.GetComponentsInChildren<KnownShelfRegion>(true).Length;
            if (id.expectsMultiplePhysicalShelves && child < 2) errors.Add($"{id.rackId}: birden fazla fiziksel raf bekleniyor fakat iki KnownShelfRegion yok.");
            else if (child == 1) Debug.LogWarning($"{id.rackId}: yalnızca bir KnownShelfRegion var; görüntü birden fazla fiziksel raf içeriyorsa ayrı bölgeler ekleyin.");
        }

        if (errors.Count > 0) Debug.LogError("Shelf Location doğrulama hataları:\n- " + string.Join("\n- ", errors));
    }

    [MenuItem("Tools/Shelf Location/Export Store Map")]
    public static void Export() {
        Validate();
        var b = GetBridgeInstance();
        if (b != null) {
            b.ExportStoreMap();
            AssetDatabase.Refresh();
        }
    }

    [MenuItem("Tools/Shelf Location/Capture Test Frame")]
    public static void Capture() {
        if (!EditorApplication.isPlaying) {
            Debug.LogError("Capture Test Frame için Play moduna geçin.");
            return;
        }
        var b = GetBridgeInstance();
        if (b != null) b.CaptureTestFrame();
    }

    [MenuItem("Tools/Shelf Location/Open Data Folder")]
    public static void Open() {
        var b = GetBridgeInstance();
        if (b != null) {
            Directory.CreateDirectory(b.DataRoot);
            EditorUtility.RevealInFinder(b.DataRoot);
        }
    }

    [MenuItem("Tools/Shelf Location/Add Known Shelf Region")]
    public static void AddRegion() {
        GameObject parent = Selection.activeGameObject;
        if (parent == null) {
            Debug.LogError("Önce raf yüzeyi veya child nesnesini seçin.");
            return;
        }
        var go = new GameObject("KnownShelfRegion");
        Undo.RegisterCreatedObjectUndo(go, "Add Known Shelf Region");
        go.transform.SetParent(parent.transform, false);
        var region = go.AddComponent<KnownShelfRegion>();
        region.sourceRenderer = parent.GetComponentInChildren<Renderer>();
        var identity = parent.GetComponentInParent<ShelfIdentity>();
        region.parentSurfaceId = identity != null ? identity.rackId : parent.name;
        Selection.activeGameObject = go;
        Debug.Log("KnownShelfRegion eklendi. Inspector'da mevcuttaki shelfId ve normalized bölge/anchor alanlarını atayın.");
    }

    [MenuItem("Tools/Shelf Location/Create Two Regions From Selected Surface")]
    public static void CreateTwoRegions() {
        GameObject selected = Selection.activeGameObject;
        if (selected == null) {
            Debug.LogError("Önce ShelfIdentity içeren raf yüzeyini seçin.");
            return;
        }
        var identity = selected.GetComponentInParent<ShelfIdentity>();
        if (identity == null) {
            Debug.LogError("Seçimde ShelfIdentity bulunamadı.");
            return;
        }
        identity.expectsMultiplePhysicalShelves = true;
        Renderer renderer = identity.targetRenderer != null ? identity.targetRenderer : identity.GetComponentInChildren<Renderer>();
        var top = new GameObject("KnownShelfRegion_Top");
        Undo.RegisterCreatedObjectUndo(top, "Create Shelf Regions");
        top.transform.SetParent(identity.transform, false);
        var tr = top.AddComponent<KnownShelfRegion>();
        tr.shelfId = identity.rackId;
        tr.parentSurfaceId = identity.rackId;
        tr.sourceRenderer = renderer;
        tr.normalizedMin = new Vector2(0, .5f);
        tr.normalizedMax = Vector2.one;

        var bottom = new GameObject("KnownShelfRegion_Bottom");
        Undo.RegisterCreatedObjectUndo(bottom, "Create Shelf Regions");
        bottom.transform.SetParent(identity.transform, false);
        var br = bottom.AddComponent<KnownShelfRegion>();
        br.shelfId = "";
        br.parentSurfaceId = identity.rackId;
        br.sourceRenderer = renderer;
        br.normalizedMin = Vector2.zero;
        br.normalizedMax = new Vector2(1, .5f);
        br.activeRegion = false;

        EditorUtility.SetDirty(identity);
        Selection.objects = new UnityEngine.Object[] { top, bottom };
        Debug.Log("İki oriented region oluşturuldu. Top mevcut parent ID ile hazır. Bottom shelfId alanına önceden tanımlı ikinci ID'yi girip Active Region'ı açın; sonra Validate Scene çalıştırın.");
    }

    [MenuItem("Tools/Shelf Location/Select Unassigned Regions")]
    public static void SelectUnassigned() {
        var found = new List<GameObject>();
        foreach (var r in UnityEngine.Object.FindObjectsByType<KnownShelfRegion>(FindObjectsSortMode.None))
            if (string.IsNullOrWhiteSpace(r.shelfId)) found.Add(r.gameObject);
        Selection.objects = found.ToArray();
        Debug.Log($"{found.Count} ID atanmamış bölge seçildi.");
    }
}
