using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShelfLocationTools {
    public static void ValidateMarketSceneBatch() {
        EditorSceneManager.OpenScene("Assets/Market_01.unity", OpenSceneMode.Single);
        ValidateScene(true);
    }

    public static void ExportMarketSceneBatch() {
        EditorSceneManager.OpenScene("Assets/Market_01.unity", OpenSceneMode.Single);
        ValidateScene(true);
        GetBridge().ExportStoreMap();
    }

    static ShelfLocationBridge GetBridge() =>
        UnityEngine.Object.FindFirstObjectByType<ShelfLocationBridge>();

    [MenuItem("Tools/Shelf Location/Auto Setup")]
    public static void Setup() {
        ShelfInferenceClient client =
            UnityEngine.Object.FindFirstObjectByType<ShelfInferenceClient>();
        if (client == null) {
            Debug.LogError("ShelfInferenceClient is missing from the scene.");
            return;
        }
        ShelfLocationBridge bridge = client.GetComponent<ShelfLocationBridge>();
        if (bridge == null) bridge = Undo.AddComponent<ShelfLocationBridge>(client.gameObject);
        if (bridge.captureCamera == null)
            bridge.captureCamera = client.GetComponentInChildren<Camera>() ?? Camera.main;
        client.bridge = bridge;

        UnitySequenceRecorder recorder = client.GetComponent<UnitySequenceRecorder>();
        if (recorder == null) recorder = Undo.AddComponent<UnitySequenceRecorder>(client.gameObject);
        recorder.bridge = bridge;
        EditorUtility.SetDirty(bridge);
        EditorUtility.SetDirty(client);
        EditorUtility.SetDirty(recorder);
        Debug.Log("Shelf inference setup is complete on the client GameObject.");
    }

    static UnitySequenceRecorder GetRecorder() {
        UnitySequenceRecorder recorder =
            UnityEngine.Object.FindFirstObjectByType<UnitySequenceRecorder>();
        if (recorder == null || recorder.bridge == null)
            Debug.LogError("UnitySequenceRecorder is missing or has no bridge reference.");
        return recorder != null && recorder.bridge != null ? recorder : null;
    }

    [MenuItem("Tools/Shelf Location/Start Offline Sequence Recording")]
    public static void StartRecording() {
        if (!EditorApplication.isPlaying) {
            Debug.LogError("Enter Play Mode before starting offline recording.");
            return;
        }
        UnitySequenceRecorder recorder = GetRecorder();
        if (recorder == null) return;
        recorder.StartRecording();
        Debug.Log($"Offline sequence recording started: {recorder.bridge.sessionId}");
    }

    [MenuItem("Tools/Shelf Location/Stop Offline Sequence Recording")]
    public static void StopRecording() {
        if (!EditorApplication.isPlaying) {
            Debug.LogError("Enter Play Mode before stopping offline recording.");
            return;
        }
        UnitySequenceRecorder recorder = GetRecorder();
        if (recorder == null) return;
        recorder.StopRecording();
        Debug.Log("Offline sequence recording stopped.");
    }

    static string ValidateGeometry(ShelfMapEntry entry) {
        if (entry.corners_world == null || entry.corners_world.Length != 4)
            return "four corners are required";
        var corners = new Vector3[4];
        for (int i = 0; i < corners.Length; i++)
            corners[i] = new Vector3(
                entry.corners_world[i][0], entry.corners_world[i][1], entry.corners_world[i][2]);
        Vector3 up = corners[1] - corners[0];
        Vector3 right = corners[3] - corners[0];
        Vector3 normal = Vector3.Cross(up, right);
        if (up.magnitude < .001f || right.magnitude < .001f || normal.magnitude < .001f)
            return "geometry has zero area";
        if (Mathf.Abs(Vector3.Dot(corners[2] - corners[0], normal.normalized)) > .005f)
            return "corners are not coplanar";
        if (Vector3.Dot(corners[2] - corners[1], right) <= 0 ||
            Vector3.Dot(corners[2] - corners[3], up) <= 0)
            return "corner order is not BL, TL, TR, BR";
        return null;
    }

    static List<string> SceneErrors() {
        var errors = new List<string>();
        ShelfLocationBridge[] bridges =
            UnityEngine.Object.FindObjectsByType<ShelfLocationBridge>(FindObjectsSortMode.None);
        ShelfInferenceClient[] clients =
            UnityEngine.Object.FindObjectsByType<ShelfInferenceClient>(FindObjectsSortMode.None);
        UnitySequenceRecorder[] recorders =
            UnityEngine.Object.FindObjectsByType<UnitySequenceRecorder>(FindObjectsSortMode.None);
        if (bridges.Length != 1) errors.Add($"Expected one ShelfLocationBridge; found {bridges.Length}.");
        if (clients.Length != 1) errors.Add($"Expected one ShelfInferenceClient; found {clients.Length}.");
        if (recorders.Length != 1) errors.Add($"Expected one UnitySequenceRecorder; found {recorders.Length}.");
        if (bridges.Length != 1) return errors;

        ShelfLocationBridge bridge = bridges[0];
        if (bridge.captureCamera == null) errors.Add("ShelfLocationBridge.captureCamera is missing.");
        if (clients.Length == 1 && clients[0].bridge != bridge)
            errors.Add("ShelfInferenceClient.bridge does not reference the scene bridge.");
        if (recorders.Length == 1 && recorders[0].bridge != bridge)
            errors.Add("UnitySequenceRecorder.bridge does not reference the scene bridge.");

        var ids = new HashSet<string>();
        List<ShelfMapEntry> entries = bridge.Entries();
        if (entries.Count == 0) errors.Add("No active KnownShelfRegion components were found.");
        foreach (ShelfMapEntry entry in entries) {
            if (string.IsNullOrWhiteSpace(entry.shelf_id) || !ids.Add(entry.shelf_id))
                errors.Add($"Shelf ID is empty or duplicated: {entry.shelf_id}");
            string geometryError = ValidateGeometry(entry);
            if (geometryError != null) errors.Add($"{entry.shelf_id}: {geometryError}");
        }
        return errors;
    }

    static void ValidateScene(bool throwOnError) {
        List<string> errors = SceneErrors();
        if (errors.Count == 0) {
            Debug.Log("Shelf inference scene validation passed.");
            return;
        }
        string message = "Shelf inference scene validation failed:\n- " + string.Join("\n- ", errors);
        if (throwOnError) throw new InvalidOperationException(message);
        Debug.LogError(message);
    }

    [MenuItem("Tools/Shelf Location/Validate Scene")]
    public static void Validate() => ValidateScene(false);

    [MenuItem("Tools/Shelf Location/Export Store Map")]
    public static void Export() {
        ValidateScene(true);
        GetBridge().ExportStoreMap();
        AssetDatabase.Refresh();
    }

    [MenuItem("Tools/Shelf Location/Capture Offline Frame")]
    public static void Capture() {
        if (!EditorApplication.isPlaying) {
            Debug.LogError("Enter Play Mode before capturing an offline frame.");
            return;
        }
        ShelfLocationBridge bridge = GetBridge();
        if (bridge != null) bridge.CaptureOfflineFrame();
    }

    [MenuItem("Tools/Shelf Location/Open Data Folder")]
    public static void Open() {
        ShelfLocationBridge bridge = GetBridge();
        if (bridge == null) return;
        Directory.CreateDirectory(bridge.DataRoot);
        EditorUtility.RevealInFinder(bridge.DataRoot);
    }
}
