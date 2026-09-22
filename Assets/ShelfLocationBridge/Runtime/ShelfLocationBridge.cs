using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable] public class ShelfMapEntry {
    public string shelf_id, parent_surface_id;
    public float[][] corners_world;
    public float[] center_world, front_normal_world;
    public float width_m, height_m;
    public bool active = true;
}
[Serializable] public class ImageMetadata { public int width, height; }
[Serializable] public class CameraIntrinsics { public float fx, fy, cx, cy; }
[Serializable] public class CameraMetadata {
    public float[] position_map, rotation_xyzw;
    public CameraIntrinsics intrinsics;
    public float vertical_fov, near_clip, far_clip;
    public float[] world_to_camera_matrix, projection_matrix, gpu_projection_matrix;
    public string quaternion_order = "xyzw", matrix_layout = "row_major";
}
[Serializable] public class PoseMetadata {
    public string source = "unity_camera_pose";
    public float quality = 1;
    public bool relocalized = true, scale_initialized = true;
}
[Serializable] public class FrameInputData {
    public int schema_version = 3;
    public string store_id, session_id, frame_id, image_path, pixel_origin = "top_left";
    public double timestamp;
    public ImageMetadata image;
    public CameraMetadata camera;
    public PoseMetadata pose;
}
[Serializable] public class FrameGroundTruth {
    public int schema_version = 2;
    public string frame_id;
    public List<string> visible_shelf_ids = new();
}

public class ShelfLocationBridge : MonoBehaviour {
    [Header("Capture")]
    public Camera captureCamera;
    [Min(1)] public int captureWidth = 1280, captureHeight = 720;

    [Header("ID-free runtime pose")]
    public string storeId = "MARKET-001";
    public string sessionId = "live_unity";
    [Range(0, 1)] public float simulatedPoseQuality = 1;
    public bool relocalized = true, scaleInitialized = true;

    public string DataRoot => Path.Combine(
        Directory.GetParent(Application.dataPath).FullName, "ShelfSystemData");

    static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };
    static float[] M(Matrix4x4 matrix) {
        var values = new float[16];
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                values[row * 4 + column] = matrix[row, column];
        return values;
    }

    public static ShelfMapEntry BuildEntry(KnownShelfRegion region) {
        Vector3[] corners = region.WorldCorners();
        return new ShelfMapEntry {
            shelf_id = region.shelfId,
            parent_surface_id = region.parentSurfaceId,
            corners_world = new[] { V(corners[0]), V(corners[1]), V(corners[2]), V(corners[3]) },
            center_world = V(region.Center),
            front_normal_world = V(region.FrontNormal),
            width_m = region.Width,
            height_m = region.Height,
            active = region.activeRegion
        };
    }

    public List<ShelfMapEntry> Entries() {
        var entries = new List<ShelfMapEntry>();
        foreach (KnownShelfRegion region in
                 FindObjectsByType<KnownShelfRegion>(FindObjectsSortMode.None)) {
            if (region.activeRegion) entries.Add(BuildEntry(region));
        }
        entries.Sort((left, right) => string.CompareOrdinal(left.shelf_id, right.shelf_id));
        return entries;
    }

    public void ExportStoreMap() {
        Directory.CreateDirectory(DataRoot);
        List<ShelfMapEntry> entries = Entries();
        var json = new StringBuilder();
        json.Append("{\n  \"schema_version\": 2,\n  \"coordinate_system\": \"unity_world\",\n  \"shelves\": [\n");
        for (int i = 0; i < entries.Count; i++) {
            ShelfMapEntry entry = entries[i];
            if (i > 0) json.Append(",\n");
            json.Append("    {\n");
            json.Append($"      \"shelf_id\": {Q(entry.shelf_id)},\n");
            json.Append($"      \"parent_surface_id\": {Q(entry.parent_surface_id)},\n");
            json.Append("      \"corners_world\": [");
            for (int corner = 0; corner < 4; corner++) {
                if (corner > 0) json.Append(",");
                json.Append("\n        ");
                AppendVector(json, entry.corners_world[corner]);
            }
            json.Append("\n      ],\n      \"center_world\": ");
            AppendVector(json, entry.center_world);
            json.Append(",\n      \"front_normal_world\": ");
            AppendVector(json, entry.front_normal_world);
            json.Append($",\n      \"width_m\": {F(entry.width_m)},\n");
            json.Append($"      \"height_m\": {F(entry.height_m)},\n");
            json.Append($"      \"active\": {(entry.active ? "true" : "false")}\n    }}");
        }
        json.Append("\n  ]\n}\n");
        File.WriteAllText(Path.Combine(DataRoot, "store_map.json"), json.ToString());
    }

    static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    static string Q(string value) => value == null
        ? "null"
        : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static void AppendVector(StringBuilder json, float[] value) =>
        json.Append($"[{F(value[0])}, {F(value[1])}, {F(value[2])}]");

    public FrameInputData BuildFrameMetadata(
        Camera camera, int width, int height, string frameId, string imageName) {
        Transform cameraTransform = camera.transform;
        float fx = Mathf.Abs(camera.projectionMatrix[0, 0]) * width * .5f;
        float fy = Mathf.Abs(camera.projectionMatrix[1, 1]) * height * .5f;
        Matrix4x4 gpu = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
        return new FrameInputData {
            store_id = storeId,
            session_id = sessionId,
            frame_id = frameId,
            image_path = imageName,
            timestamp = Time.realtimeSinceStartupAsDouble,
            image = new ImageMetadata { width = width, height = height },
            pose = new PoseMetadata {
                quality = simulatedPoseQuality,
                relocalized = relocalized,
                scale_initialized = scaleInitialized
            },
            camera = new CameraMetadata {
                position_map = V(cameraTransform.position),
                rotation_xyzw = new[] {
                    cameraTransform.rotation.x, cameraTransform.rotation.y,
                    cameraTransform.rotation.z, cameraTransform.rotation.w
                },
                intrinsics = new CameraIntrinsics { fx = fx, fy = fy, cx = width * .5f, cy = height * .5f },
                vertical_fov = camera.fieldOfView,
                near_clip = camera.nearClipPlane,
                far_clip = camera.farClipPlane,
                world_to_camera_matrix = M(camera.worldToCameraMatrix),
                projection_matrix = M(camera.projectionMatrix),
                gpu_projection_matrix = M(gpu)
            }
        };
    }

    Camera ResolveCamera() {
        Camera camera = captureCamera != null ? captureCamera : Camera.main;
        if (camera == null) throw new InvalidOperationException("Shelf inference camera is missing.");
        if (captureWidth <= 0 || captureHeight <= 0)
            throw new InvalidOperationException("Capture dimensions must be positive.");
        return camera;
    }

    Texture2D CaptureTopLeftTexture(Camera camera) {
        RenderTexture target = RenderTexture.GetTemporary(
            captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        float previousAspect = camera.aspect;
        Texture2D raw = null;
        try {
            camera.aspect = (float)captureWidth / captureHeight;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            raw = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
            raw.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
            raw.Apply();
            Color32[] source = raw.GetPixels32();
            var topLeft = new Color32[source.Length];
            for (int row = 0; row < captureHeight; row++)
                Array.Copy(source, row * captureWidth, topLeft,
                           (captureHeight - 1 - row) * captureWidth, captureWidth);
            var result = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
            result.SetPixels32(topLeft);
            result.Apply();
            return result;
        } finally {
            camera.targetTexture = previousTarget;
            camera.aspect = previousAspect;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            if (raw != null) Destroy(raw);
        }
    }

    public byte[] CaptureRuntimeJpeg(int quality, out FrameInputData metadata) {
        Camera camera = ResolveCamera();
        string frameId = $"frame_{Time.frameCount:D6}";
        Texture2D texture = CaptureTopLeftTexture(camera);
        try {
            metadata = BuildFrameMetadata(
                camera, captureWidth, captureHeight, frameId, $"{frameId}.jpg");
            return texture.EncodeToJPG(Mathf.Clamp(quality, 1, 100));
        } finally {
            Destroy(texture);
        }
    }

    public void CaptureOfflineFrame() => StartCoroutine(CaptureOffline());

    IEnumerator CaptureOffline() {
        yield return new WaitForEndOfFrame();
        Camera camera = ResolveCamera();
        string root = Path.Combine(DataRoot, "sessions", sessionId);
        string inputRoot = Path.Combine(root, "input");
        string truthRoot = Path.Combine(root, "ground_truth");
        Directory.CreateDirectory(inputRoot);
        Directory.CreateDirectory(truthRoot);
        string frameId = $"frame_{Time.frameCount:D6}";
        string imageName = $"{frameId}.png";
        Texture2D texture = CaptureTopLeftTexture(camera);
        try {
            File.WriteAllBytes(Path.Combine(inputRoot, imageName), texture.EncodeToPNG());
        } finally {
            Destroy(texture);
        }
        FrameInputData metadata = BuildFrameMetadata(
            camera, captureWidth, captureHeight, frameId, imageName);
        File.WriteAllText(
            Path.Combine(inputRoot, $"{frameId}.json"), JsonUtility.ToJson(metadata, true));

        var groundTruth = new FrameGroundTruth { frame_id = frameId };
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
        foreach (ShelfMapEntry entry in Entries()) {
            var bounds = new Bounds(
                new Vector3(entry.center_world[0], entry.center_world[1], entry.center_world[2]),
                Vector3.zero);
            foreach (float[] point in entry.corners_world)
                bounds.Encapsulate(new Vector3(point[0], point[1], point[2]));
            if (GeometryUtility.TestPlanesAABB(planes, bounds))
                groundTruth.visible_shelf_ids.Add(entry.shelf_id);
        }
        File.WriteAllText(
            Path.Combine(truthRoot, $"{frameId}.json"), JsonUtility.ToJson(groundTruth, true));
    }
}
