using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class ShelfPresentationTools {
    const string SourceScenePath = "Assets/Market_01.unity";
    const string PresentationScenePath = "Assets/Market_Presentation.unity";
    const string AssetRoot = "Assets/_ShelfPresentation";
    const string MaterialRoot = AssetRoot + "/Materials";
    const string ExistingTextureRoot = "Assets/Project/Textures/ShelfImages";
    const float ShelfHeight = 2.2f;
    const float SegmentWidth = 2.2f;
    const float VisualOverlap = .004f;

    sealed class ShelfSpec {
        public readonly string id;
        public readonly string row;
        public readonly string material;
        public readonly float x;
        public readonly float zMin;
        public readonly float zMax;
        public readonly Vector3 normal;

        public ShelfSpec(
            string id, string row, string material, float x,
            float zMin, float zMax, Vector3 normal) {
            this.id = id;
            this.row = row;
            this.material = material;
            this.x = x;
            this.zMin = zMin;
            this.zMax = zMax;
            this.normal = normal;
        }
    }

    static readonly ShelfSpec[] Specs = {
        new("A-L-01", "A_Left_Row", "M_A_Left_01", -3.68f, -2.2f, 0f, Vector3.right),
        new("A-L-02", "A_Left_Row", "M_A_Left_02", -3.68f, 0f, 2.2f, Vector3.right),
        new("A-R-01", "A_Right_Row", "M_A_Right_01", -.82f, -2.2f, 0f, Vector3.left),
        new("A-R-02", "A_Right_Row", "M_A_Right_02", -.82f, 0f, 2.2f, Vector3.left),
        new("B-L-01", "B_Left_Row", "M_B_Left_01", .82f, -2.2f, 0f, Vector3.right),
        new("B-L-02", "B_Left_Row", "M_B_Left_02", .82f, 0f, 2.2f, Vector3.right),
        new("B-R-01", "B_Right_Row", "M_B_Right_01", 3.68f, -2.2f, 0f, Vector3.left),
        new("B-R-02", "B_Right_Row", "M_B_Right_02", 3.68f, 0f, 2.2f, Vector3.left)
    };

    [MenuItem("Tools/Shelf Location/Build Presentation Market")]
    public static void BuildPresentationMenu() => BuildPresentation(false);

    public static void BuildPresentationBatch() => BuildPresentation(true);

    static void BuildPresentation(bool batch) {
        EnsureFolders();

        Scene source = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
        if (!EditorSceneManager.SaveScene(source, PresentationScenePath, true))
            throw new InvalidOperationException("Could not duplicate Market_01 as Market_Presentation.");

        Scene scene = EditorSceneManager.OpenScene(PresentationScenePath, OpenSceneMode.Single);
        RemoveObsoleteRouteAndMissingScripts(scene);
        ShelfLocationBridge bridge = UnityEngine.Object.FindFirstObjectByType<ShelfLocationBridge>();
        ShelfInferenceClient client = UnityEngine.Object.FindFirstObjectByType<ShelfInferenceClient>();
        UnitySequenceRecorder recorder = UnityEngine.Object.FindFirstObjectByType<UnitySequenceRecorder>();
        if (bridge == null || client == null || recorder == null || bridge.captureCamera == null)
            throw new InvalidOperationException(
                "The source scene must contain the bridge, inference client, recorder, and capture camera.");

        var regions = UnityEngine.Object.FindObjectsByType<KnownShelfRegion>(FindObjectsSortMode.None)
            .ToDictionary(region => region.shelfId, StringComparer.Ordinal);
        foreach (ShelfSpec spec in Specs)
            if (!regions.ContainsKey(spec.id))
                throw new InvalidOperationException($"KnownShelfRegion {spec.id} is missing.");

        bridge.transform.SetParent(null, true);
        foreach (KnownShelfRegion region in regions.Values) region.transform.SetParent(null, true);
        foreach (GameObject root in scene.GetRootGameObjects()) {
            if (root == bridge.gameObject || regions.Values.Any(region => region.gameObject == root))
                continue;
            UnityEngine.Object.DestroyImmediate(root);
        }

        GameObject environment = NewObject("MarketEnvironment", null);
        Transform architecture = NewObject("Architecture", environment.transform).transform;
        Transform lighting = NewObject("Lighting", environment.transform).transform;
        Transform aisleA = NewObject("Aisle_A", environment.transform).transform;
        Transform aisleB = NewObject("Aisle_B", environment.transform).transform;
        Transform signs = NewObject("Signs", environment.transform).transform;
        Transform systems = NewObject("Systems", environment.transform).transform;

        var rows = new Dictionary<string, Transform>(StringComparer.Ordinal) {
            ["A_Left_Row"] = NewObject("A_Left_Row", aisleA).transform,
            ["A_Right_Row"] = NewObject("A_Right_Row", aisleA).transform,
            ["B_Left_Row"] = NewObject("B_Left_Row", aisleB).transform,
            ["B_Right_Row"] = NewObject("B_Right_Row", aisleB).transform
        };

        Material floorMaterial = GetOrCreateLitMaterial(
            "M_Architecture_Floor", new Color(.68f, .70f, .71f), .28f, .2f);
        Material wallMaterial = GetOrCreateLitMaterial(
            "M_Architecture_Wall", new Color(.86f, .87f, .85f), .05f, .18f);
        Material ceilingMaterial = GetOrCreateLitMaterial(
            "M_Architecture_Ceiling", new Color(.92f, .92f, .90f), .02f, .12f);
        Material frameMaterial = GetOrCreateLitMaterial(
            "M_Shelf_Frame", new Color(.11f, .14f, .16f), .55f, .3f);
        Material signMaterial = GetOrCreateLitMaterial(
            "M_Signboard", new Color(.035f, .20f, .30f), .15f, .25f);
        Material exitMaterial = GetOrCreateLitMaterial(
            "M_Exit_Sign", new Color(.04f, .38f, .18f), .08f, .2f);
        Material fixtureMaterial = GetOrCreateEmissiveMaterial(
            "M_Ceiling_Light", new Color(1f, .97f, .88f), 2.4f);

        CreateArchitecture(architecture, floorMaterial, wallMaterial, ceilingMaterial);
        CreateLighting(lighting, fixtureMaterial);

        Texture2D[] sourceTextures = FindExistingShelfTextures();
        var shelfMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);
        for (int i = 0; i < Specs.Length; i++)
            shelfMaterials[Specs[i].material] = GetOrCreatePhotoMaterial(
                Specs[i].material, sourceTextures.Length > 0 ? sourceTextures[i % sourceTextures.Length] : null);

        foreach (ShelfSpec spec in Specs)
            ConfigureShelfRegion(regions[spec.id], spec, rows[spec.row], shelfMaterials[spec.material]);

        CreateRowFrame(rows["A_Left_Row"], -3.68f, Vector3.right, frameMaterial);
        CreateRowFrame(rows["A_Right_Row"], -.82f, Vector3.left, frameMaterial);
        CreateRowFrame(rows["B_Left_Row"], .82f, Vector3.right, frameMaterial);
        CreateRowFrame(rows["B_Right_Row"], 3.68f, Vector3.left, frameMaterial);

        CreateSigns(signs, signMaterial, exitMaterial);

        bridge.transform.SetParent(systems, true);
        bridge.gameObject.name = "RobotRig";
        bridge.transform.SetPositionAndRotation(new Vector3(-2.25f, 0f, -5.15f), Quaternion.identity);
        ConfigureDualScanner(bridge, client, systems, out Camera leftCamera, out Camera rightCamera);

        client.bridge = bridge;
        client.autoStart = false;
        client.startupDelay = 1f;
        client.inferenceInterval = 2.5f;
        client.debugLogging = true;
        recorder.bridge = bridge;
        EditorUtility.SetDirty(bridge);
        EditorUtility.SetDirty(client);
        EditorUtility.SetDirty(recorder);

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.64f, .66f, .68f);
        RenderSettings.fog = false;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ValidatePresentationScene();
        bridge.ExportStoreMap();
        Debug.Log(
            "Market_Presentation created: 2 aisles, 4 continuous rows, 8 photo panels, " +
            "8 aligned KnownShelfRegion objects, a dual-camera rig, and a 6-station serpentine route.");
        if (batch) EditorApplication.Exit(0);
    }

    static void ConfigureDualScanner(
        ShelfLocationBridge bridge, ShelfInferenceClient client, Transform systems,
        out Camera leftCamera, out Camera rightCamera) {
        Camera sourceCamera = bridge.captureCamera;
        Transform cameraMount = NewObject("CameraMount", bridge.transform).transform;
        cameraMount.localPosition = new Vector3(0f, 1.1f, 0f);
        cameraMount.localRotation = Quaternion.identity;

        leftCamera = sourceCamera;
        leftCamera.gameObject.name = "LeftCamera";
        leftCamera.transform.SetParent(cameraMount, false);
        leftCamera.transform.localPosition = Vector3.zero;
        leftCamera.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        leftCamera.fieldOfView = 78f;
        leftCamera.nearClipPlane = .08f;
        leftCamera.farClipPlane = 50f;
        leftCamera.clearFlags = CameraClearFlags.SolidColor;
        leftCamera.backgroundColor = new Color(.82f, .85f, .86f);
        leftCamera.rect = new Rect(0f, 0f, 1f, 1f);
        leftCamera.targetDisplay = 0;
        leftCamera.enabled = true;
        leftCamera.gameObject.tag = "MainCamera";
        if (leftCamera.GetComponent<AudioListener>() == null)
            leftCamera.gameObject.AddComponent<AudioListener>();

        GameObject rightObject = new("RightCamera", typeof(Camera));
        rightObject.transform.SetParent(cameraMount, false);
        rightCamera = rightObject.GetComponent<Camera>();
        rightCamera.CopyFrom(leftCamera);
        // Camera.CopyFrom also mirrors transform state in current Unity versions.
        // Apply the physical back-to-back mount only after copying optics.
        rightObject.transform.localPosition = Vector3.zero;
        rightObject.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        rightCamera.targetDisplay = 1;
        rightCamera.enabled = true;
        rightObject.tag = "Untagged";
        foreach (AudioListener listener in rightObject.GetComponents<AudioListener>())
            UnityEngine.Object.DestroyImmediate(listener);

        bridge.captureCamera = leftCamera;
        bridge.captureWidth = 1280;
        bridge.captureHeight = 720;
        if (bridge.GetComponent<DualDisplayBootstrap>() == null)
            bridge.gameObject.AddComponent<DualDisplayBootstrap>();

        Transform route = NewObject("DualScanRoute", systems).transform;
        Transform aisleA = NewObject("Aisle_A_Centerline", route).transform;
        Transform aisleB = NewObject("Aisle_B_Centerline", route).transform;
        Transform transition = NewObject("Aisle_A_to_B_Transition", route).transform;

        float aisleHalfWidth = Mathf.Abs(-3.68f - -2.25f);
        float horizontalFov = Camera.VerticalToHorizontalFieldOfView(
            leftCamera.fieldOfView, (float)bridge.captureWidth / bridge.captureHeight);
        float visibleHalfLength = aisleHalfWidth * Mathf.Tan(horizontalFov * .5f * Mathf.Deg2Rad);
        // The coverage span would allow fewer views, but the live validation contract
        // requires at least three. Seventy-percent half-span spacing gives generous overlap.
        float stationOffset = Mathf.Min(1.45f, visibleHalfLength * .7f);
        // At the exact B-row midpoint the nearer B-L and distant A-L map polygons are
        // collinear. A small coverage-relative offset supplies parallax while retaining
        // overlap with both B shelf segments and keeping the route monotonic.
        float bMiddleParallax = Mathf.Min(.8f, stationOffset * .55f);
        float[] aZ = { -stationOffset, 0f, stationOffset };
        float[] bZ = { stationOffset, bMiddleParallax, -stationOffset };

        DualShelfScanStation[] aStations = new DualShelfScanStation[3];
        DualShelfScanStation[] bStations = new DualShelfScanStation[3];
        for (int i = 0; i < 3; i++) {
            Transform aPose = CreateRoutePose(
                $"Station_A_{i + 1:D2}", aisleA, new Vector3(-2.25f, 0f, aZ[i]), 0f);
            Transform bPose = CreateRoutePose(
                $"Station_B_{i + 1:D2}", aisleB, new Vector3(2.25f, 0f, bZ[i]), 180f);
            aStations[i] = new DualShelfScanStation {
                stationId = $"A-{i + 1:D2}", pose = aPose,
                expectedLeftShelves = ExpectedSegments("A-L", aZ[i]),
                expectedRightShelves = ExpectedSegments("A-R", aZ[i])
            };
            bStations[i] = new DualShelfScanStation {
                stationId = $"B-{i + 1:D2}", pose = bPose,
                expectedLeftShelves = ExpectedSegments("B-R", bZ[i]),
                expectedRightShelves = ExpectedSegments("B-L", bZ[i])
            };
        }

        Transform[] transitionPoses = {
            CreateRoutePose("Transition_A_NorthExit", transition, new Vector3(-2.25f, 0f, 3.2f), 0f),
            CreateRoutePose("Transition_CrossToB", transition, new Vector3(2.25f, 0f, 3.2f), 90f),
            CreateRoutePose("Transition_B_NorthEntry", transition, new Vector3(2.25f, 0f, stationOffset), 180f)
        };

        DualShelfScanController controller = bridge.GetComponent<DualShelfScanController>();
        if (controller == null) controller = bridge.gameObject.AddComponent<DualShelfScanController>();
        controller.cameraMount = cameraMount;
        controller.leftCamera = leftCamera;
        controller.rightCamera = rightCamera;
        controller.inferenceClient = client;
        controller.aisles = new[] {
            new DualShelfAislePlan {
                aisleId = "A", stations = aStations, transitionToNextAisle = transitionPoses
            },
            new DualShelfAislePlan {
                aisleId = "B", stations = bStations, transitionToNextAisle = Array.Empty<Transform>()
            }
        };
        controller.autoRun = true;
        controller.loopAfterComplete = false;
        controller.startAisleIndex = 0;
        controller.startStationIndex = 0;
        controller.pauseSimulation = false;
        controller.moveSpeed = 1.4f;
        controller.turnSpeedDegrees = 150f;
        controller.settleSeconds = .45f;
        controller.targetFrameRate = 60;
        controller.maxInferenceRetries = 2;
        controller.continueAfterSideFailure = true;

        EditorUtility.SetDirty(leftCamera);
        EditorUtility.SetDirty(rightCamera);
        EditorUtility.SetDirty(controller);
    }

    static Transform CreateRoutePose(
        string name, Transform parent, Vector3 position, float yawDegrees) {
        Transform pose = NewObject(name, parent).transform;
        pose.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
        return pose;
    }

    static string ExpectedSegments(string prefix, float z) {
        if (z < -1f) return prefix + "-01";
        if (z > 1f) return prefix + "-02";
        return prefix + "-01, " + prefix + "-02";
    }

    static void RemoveObsoleteRouteAndMissingScripts(Scene scene) {
        foreach (GameObject root in scene.GetRootGameObjects())
            CleanupHierarchy(root);
    }

    static void CleanupHierarchy(GameObject gameObject) {
        for (int i = gameObject.transform.childCount - 1; i >= 0; i--) {
            GameObject child = gameObject.transform.GetChild(i).gameObject;
            if (IsObsoleteRouteObject(child.name)) {
                UnityEngine.Object.DestroyImmediate(child);
                continue;
            }
            CleanupHierarchy(child);
        }
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
    }

    static bool IsObsoleteRouteObject(string objectName) =>
        objectName.StartsWith("ScanPoint_", StringComparison.Ordinal) ||
        objectName.StartsWith("TransitPoint_", StringComparison.Ordinal) ||
        objectName.StartsWith("PatrolPoint_", StringComparison.Ordinal) ||
        objectName.IndexOf("PatrolRoute", StringComparison.OrdinalIgnoreCase) >= 0;

    static void ConfigureShelfRegion(
        KnownShelfRegion region, ShelfSpec spec, Transform row, Material photoMaterial) {
        for (int i = region.transform.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(region.transform.GetChild(i).gameObject);

        region.gameObject.name = spec.id;
        region.transform.SetParent(row, false);
        region.transform.SetPositionAndRotation(
            new Vector3(spec.x, ShelfHeight * .5f, (spec.zMin + spec.zMax) * .5f),
            Quaternion.LookRotation(spec.normal, Vector3.up));
        region.transform.localScale = Vector3.one;
        region.activeRegion = true;

        if (spec.normal.x > 0f) {
            region.bottomLeftWorld = new Vector3(spec.x, 0f, spec.zMin);
            region.topLeftWorld = new Vector3(spec.x, ShelfHeight, spec.zMin);
            region.topRightWorld = new Vector3(spec.x, ShelfHeight, spec.zMax);
            region.bottomRightWorld = new Vector3(spec.x, 0f, spec.zMax);
        } else {
            region.bottomLeftWorld = new Vector3(spec.x, 0f, spec.zMax);
            region.topLeftWorld = new Vector3(spec.x, ShelfHeight, spec.zMax);
            region.topRightWorld = new Vector3(spec.x, ShelfHeight, spec.zMin);
            region.bottomRightWorld = new Vector3(spec.x, 0f, spec.zMin);
        }

        GameObject panel = CreateCube(
            "PhotoPanel", region.transform, Vector3.zero,
            new Vector3(SegmentWidth + VisualOverlap, ShelfHeight, .05f), photoMaterial);
        panel.transform.localPosition = new Vector3(0f, 0f, -.025f);
        EditorUtility.SetDirty(region);
    }

    static void CreateRowFrame(Transform row, float x, Vector3 normal, Material material) {
        Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);
        CreateWorldCube(
            "Continuous_Top_Cap", row, new Vector3(x, ShelfHeight + .055f, 0f), rotation,
            new Vector3(4.48f, .11f, .13f), material, -.065f);
        CreateWorldCube(
            "Continuous_Base", row, new Vector3(x, -.055f, 0f), rotation,
            new Vector3(4.48f, .11f, .13f), material, -.065f);
        CreateWorldCube(
            "End_Cap_South", row, new Vector3(x, ShelfHeight * .5f, -2.235f), rotation,
            new Vector3(.07f, ShelfHeight, .13f), material, -.065f);
        CreateWorldCube(
            "End_Cap_North", row, new Vector3(x, ShelfHeight * .5f, 2.235f), rotation,
            new Vector3(.07f, ShelfHeight, .13f), material, -.065f);
    }

    static void CreateWorldCube(
        string name, Transform parent, Vector3 surfaceCenter, Quaternion rotation,
        Vector3 scale, Material material, float localDepthOffset) {
        GameObject gameObject = CreateCube(name, parent, Vector3.zero, scale, material);
        gameObject.transform.SetPositionAndRotation(surfaceCenter, rotation);
        gameObject.transform.position += rotation * new Vector3(0f, 0f, localDepthOffset);
    }

    static void CreateArchitecture(
        Transform root, Material floor, Material wall, Material ceiling) {
        CreateCube("Floor", root, new Vector3(0f, -.12f, 0f), new Vector3(12f, .24f, 13f), floor);
        CreateCube("Ceiling", root, new Vector3(0f, 3.6f, 0f), new Vector3(12f, .12f, 13f), ceiling);
        CreateCube("FrontWall", root, new Vector3(0f, 1.75f, -6.45f), new Vector3(12f, 3.5f, .14f), wall);
        CreateCube("BackWall", root, new Vector3(0f, 1.75f, 6.45f), new Vector3(12f, 3.5f, .14f), wall);
        CreateCube("LeftWall", root, new Vector3(-5.95f, 1.75f, 0f), new Vector3(.14f, 3.5f, 13f), wall);
        CreateCube("RightWall", root, new Vector3(5.95f, 1.75f, 0f), new Vector3(.14f, 3.5f, 13f), wall);
    }

    static void CreateLighting(Transform root, Material fixtureMaterial) {
        GameObject daylight = NewObject("PresentationFillLight", root);
        Light directional = daylight.AddComponent<Light>();
        directional.type = LightType.Directional;
        directional.color = new Color(1f, .98f, .93f);
        directional.intensity = .38f;
        directional.shadows = LightShadows.Soft;
        daylight.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

        float[] aisleCenters = { -2.25f, 2.25f };
        float[] zPositions = { -3.5f, 0f, 3.5f };
        int index = 1;
        foreach (float x in aisleCenters) {
            foreach (float z in zPositions) {
                GameObject fixture = CreateCube(
                    $"CeilingLight_{index:D2}", root, new Vector3(x, 3.43f, z),
                    new Vector3(.18f, .055f, 2.45f), fixtureMaterial);
                GameObject lightObject = NewObject("Neutral_Area_Fill", fixture.transform);
                lightObject.transform.localPosition = new Vector3(0f, -.12f, 0f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, .96f, .86f);
                light.intensity = 4.2f;
                light.range = 4.8f;
                light.shadows = LightShadows.None;
                index++;
            }
        }
    }

    static void CreateSigns(Transform root, Material signMaterial, Material exitMaterial) {
        CreateSign(root, "Aisle_A_Sign", "KORİDOR A", new Vector3(-2.25f, 2.86f, -3.55f), 1.55f, signMaterial);
        CreateSign(root, "Aisle_B_Sign", "KORİDOR B", new Vector3(2.25f, 2.86f, -3.55f), 1.55f, signMaterial);
        CreateSign(root, "Category_Drinks", "İÇECEKLER", new Vector3(-2.25f, 2.82f, 3.55f), 1.65f, signMaterial);
        CreateSign(root, "Category_Snacks", "ATIŞTIRMALIK", new Vector3(2.25f, 2.82f, 3.55f), 1.9f, signMaterial);
        CreateSign(root, "Exit_Sign", "ÇIKIŞ", new Vector3(0f, 2.85f, 6.34f), 1.1f, exitMaterial);
    }

    static void CreateSign(
        Transform root, string name, string label, Vector3 position, float width, Material boardMaterial) {
        CreateCube(name, root, position, new Vector3(width, .48f, .075f), boardMaterial);
        GameObject textObject = NewObject(name + "_Label", root);
        textObject.transform.position = position + new Vector3(0f, -.01f, -.043f);
        TextMesh text = textObject.AddComponent<TextMesh>();
        text.text = label;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 80;
        text.characterSize = .045f;
        text.color = Color.white;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null) {
            text.font = font;
            text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }

    static Texture2D[] FindExistingShelfTextures() =>
        AssetDatabase.FindAssets("t:Texture2D", new[] { ExistingTextureRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
            .Where(texture => texture != null)
            .ToArray();

    static Material GetOrCreatePhotoMaterial(string name, Texture2D texture) {
        string path = $"{MaterialRoot}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
        if (material == null) {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        } else material.shader = shader;
        material.color = Color.white;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material GetOrCreateLitMaterial(
        string name, Color color, float metallic, float smoothness) {
        string path = $"{MaterialRoot}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (material == null) {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        } else material.shader = shader;
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material GetOrCreateEmissiveMaterial(string name, Color color, float intensity) {
        Material material = GetOrCreateLitMaterial(name, color, 0f, .28f);
        Color emission = color * intensity;
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", emission);
        material.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
        return material;
    }

    static GameObject NewObject(string name, Transform parent) {
        var gameObject = new GameObject(name);
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    static GameObject CreateCube(
        string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material) {
        GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gameObject.name = name;
        gameObject.transform.SetParent(parent, false);
        gameObject.transform.localPosition = localPosition;
        gameObject.transform.localRotation = Quaternion.identity;
        gameObject.transform.localScale = localScale;
        Renderer renderer = gameObject.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        Collider collider = gameObject.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        return gameObject;
    }

    static void EnsureFolders() {
        EnsureFolder("Assets", "_ShelfPresentation");
        EnsureFolder(AssetRoot, "Materials");
        EnsureFolder(AssetRoot, "Textures");
        EnsureFolder(AssetRoot, "Signs");
        EnsureFolder(AssetRoot, "Documentation");
    }

    static void EnsureFolder(string parent, string child) {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
    }

    [MenuItem("Tools/Shelf Location/Validate Presentation Scene")]
    public static void ValidatePresentationMenu() {
        EditorSceneManager.OpenScene(PresentationScenePath, OpenSceneMode.Single);
        ValidatePresentationScene();
    }

    public static void ValidatePresentationBatch() {
        EditorSceneManager.OpenScene(PresentationScenePath, OpenSceneMode.Single);
        ValidatePresentationScene();
    }

    public static void RunValidationBatch() {
        ShelfInferenceContractChecks.Run();
        ShelfLocationTools.ValidateMarketSceneBatch();
        EditorSceneManager.OpenScene(PresentationScenePath, OpenSceneMode.Single);
        ValidatePresentationScene();
        Debug.Log("Dual-camera market validation suite passed.");
    }

    static void ValidatePresentationScene() {
        var errors = new List<string>();
        Scene scene = EditorSceneManager.GetActiveScene();
        ShelfLocationBridge[] bridges =
            UnityEngine.Object.FindObjectsByType<ShelfLocationBridge>(FindObjectsSortMode.None);
        ShelfInferenceClient[] clients =
            UnityEngine.Object.FindObjectsByType<ShelfInferenceClient>(FindObjectsSortMode.None);
        DualShelfScanController[] controllers =
            UnityEngine.Object.FindObjectsByType<DualShelfScanController>(FindObjectsSortMode.None);
        DualDisplayBootstrap[] displayBootstraps =
            UnityEngine.Object.FindObjectsByType<DualDisplayBootstrap>(FindObjectsSortMode.None);
        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        AudioListener[] listeners =
            UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        KnownShelfRegion[] regions =
            UnityEngine.Object.FindObjectsByType<KnownShelfRegion>(FindObjectsSortMode.None);

        if (bridges.Length != 1) errors.Add($"Expected one ShelfLocationBridge; found {bridges.Length}.");
        if (clients.Length != 1) errors.Add($"Expected one ShelfInferenceClient; found {clients.Length}.");
        if (controllers.Length != 1) errors.Add($"Expected one DualShelfScanController; found {controllers.Length}.");
        if (displayBootstraps.Length != 1)
            errors.Add($"Expected one DualDisplayBootstrap; found {displayBootstraps.Length}.");
        if (cameras.Length != 2) errors.Add($"Expected two cameras; found {cameras.Length}.");
        if (listeners.Length != 1) errors.Add($"Expected one AudioListener; found {listeners.Length}.");
        if (regions.Length != Specs.Length) errors.Add($"Expected {Specs.Length} shelf regions; found {regions.Length}.");
        if (GameObject.Find("MarketEnvironment") == null) errors.Add("MarketEnvironment root is missing.");
        if (GameObject.Find("Architecture") == null) errors.Add("Architecture group is missing.");
        if (GameObject.Find("Lighting") == null) errors.Add("Lighting group is missing.");
        if (GameObject.Find("Signs") == null) errors.Add("Signs group is missing.");
        if (GameObject.Find("Systems") == null) errors.Add("Systems group is missing.");

        if (bridges.Length == 1) {
            ShelfLocationBridge bridge = bridges[0];
            if (bridge.captureCamera == null) errors.Add("The capture camera reference is null.");
            else if (!bridge.captureCamera.transform.IsChildOf(bridge.transform))
                errors.Add("The capture camera is no longer mounted under RobotRig.");
            if (clients.Length == 1 && clients[0].bridge != bridge)
                errors.Add("ShelfInferenceClient.bridge is incorrect.");
        }
        if (clients.Length == 1 && clients[0].autoStart)
            errors.Add("Inference autoStart must be disabled; the scan controller owns requests.");
        if (controllers.Length == 1) {
            DualShelfScanController controller = controllers[0];
            if (controller.targetFrameRate != 60)
                errors.Add("Presentation target frame rate must be 60 FPS.");
            if (controller.leftCamera == null || controller.rightCamera == null ||
                controller.cameraMount == null)
                errors.Add("Dual camera references are incomplete.");
            else {
                if (Vector3.Angle(
                    controller.leftCamera.transform.forward,
                    controller.rightCamera.transform.forward) < 179.9f)
                    errors.Add("LeftCamera and RightCamera are not 180 degrees apart.");
                if (controller.leftCamera.transform.parent != controller.cameraMount ||
                    controller.rightCamera.transform.parent != controller.cameraMount)
                    errors.Add("Both cameras must be direct children of CameraMount.");
                if (controller.leftCamera.transform.localPosition.sqrMagnitude > .000001f ||
                    controller.rightCamera.transform.localPosition.sqrMagnitude > .000001f)
                    errors.Add("The two camera optical centers do not coincide.");
                if (!controller.leftCamera.CompareTag("MainCamera") ||
                    controller.rightCamera.CompareTag("MainCamera"))
                    errors.Add("Only LeftCamera may carry the MainCamera tag.");
                if (!controller.leftCamera.enabled || !controller.rightCamera.enabled)
                    errors.Add("Both shelf cameras must remain continuously enabled.");
                if (controller.leftCamera.targetDisplay != 0 ||
                    controller.rightCamera.targetDisplay != 1)
                    errors.Add("LeftCamera must target Display 1 and RightCamera Display 2.");
                if (!Mathf.Approximately(controller.leftCamera.fieldOfView, controller.rightCamera.fieldOfView) ||
                    !Mathf.Approximately(controller.leftCamera.nearClipPlane, controller.rightCamera.nearClipPlane) ||
                    !Mathf.Approximately(controller.leftCamera.farClipPlane, controller.rightCamera.farClipPlane))
                    errors.Add("Dual camera projection settings differ.");
                FrameInputData leftMetadata = bridges[0].BuildFrameMetadata(
                    controller.leftCamera, 1280, 720, "check_left", "check_left.jpg");
                FrameInputData rightMetadata = bridges[0].BuildFrameMetadata(
                    controller.rightCamera, 1280, 720, "check_right", "check_right.jpg");
                if (leftMetadata.frame_id == rightMetadata.frame_id ||
                    leftMetadata.camera.rotation_xyzw.SequenceEqual(rightMetadata.camera.rotation_xyzw))
                    errors.Add("Per-camera metadata does not preserve distinct frame IDs and rotations.");
                if (!Mathf.Approximately(
                    leftMetadata.camera.intrinsics.fx, rightMetadata.camera.intrinsics.fx) ||
                    !Mathf.Approximately(
                    leftMetadata.camera.intrinsics.fy, rightMetadata.camera.intrinsics.fy))
                    errors.Add("Dual camera intrinsics differ.");
            }
            if (controller.aisles == null || controller.aisles.Length != 2 ||
                controller.aisles.Any(aisle => aisle == null || aisle.stations == null ||
                    aisle.stations.Length < 3))
                errors.Add("The route must contain two aisles with at least three stations each.");
            else if (controller.aisles[0].transitionToNextAisle == null ||
                controller.aisles[0].transitionToNextAisle.Length < 2)
                errors.Add("The A-to-B aisle transition is missing.");
        }

        var byId = regions.ToDictionary(region => region.shelfId, StringComparer.Ordinal);
        foreach (ShelfSpec spec in Specs) {
            if (!byId.TryGetValue(spec.id, out KnownShelfRegion region)) {
                errors.Add($"KnownShelfRegion {spec.id} is missing.");
                continue;
            }
            if (Mathf.Abs(region.Width - SegmentWidth) > .001f ||
                Mathf.Abs(region.Height - ShelfHeight) > .001f)
                errors.Add($"{spec.id} dimensions are not {SegmentWidth:F1} x {ShelfHeight:F1} m.");
            if (Vector3.Distance(region.Center, new Vector3(spec.x, 1.1f, (spec.zMin + spec.zMax) * .5f)) > .001f)
                errors.Add($"{spec.id} is not aligned with its photo panel.");
            Transform panel = region.transform.Find("PhotoPanel");
            if (panel == null || panel.GetComponent<Renderer>() == null)
                errors.Add($"{spec.id} has no photo panel renderer.");
        }

        int photoPanels = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Count(transform => transform.name == "PhotoPanel");
        if (photoPanels != Specs.Length) errors.Add($"Expected 8 photo panels; found {photoPanels}.");

        foreach (GameObject root in scene.GetRootGameObjects())
            ValidateHierarchy(root, errors);

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Market_Presentation validation failed:\n- " + string.Join("\n- ", errors));
        Debug.Log(
            "Market_Presentation validation passed: no missing scripts or obsolete patrol components, " +
            "two back-to-back cameras, one AudioListener/MainCamera, 6 scan stations, an aisle " +
            "transition, 4 rows, 8 panels, and 8 aligned shelf IDs.");
    }

    static void ValidateHierarchy(GameObject gameObject, List<string> errors) {
        int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
        if (missing > 0) errors.Add($"{gameObject.name} has {missing} missing script(s).");
        if (IsObsoleteRouteObject(gameObject.name))
            errors.Add($"Obsolete route object remains: {gameObject.name}.");
        foreach (MonoBehaviour behaviour in gameObject.GetComponents<MonoBehaviour>())
            if (behaviour != null && behaviour.GetType().Name.IndexOf("Patrol", StringComparison.OrdinalIgnoreCase) >= 0)
                errors.Add($"Obsolete movement component remains: {behaviour.GetType().Name}.");
        foreach (Transform child in gameObject.transform) ValidateHierarchy(child.gameObject, errors);
    }

    public static void CapturePresentationBatch() {
        EditorSceneManager.OpenScene(PresentationScenePath, OpenSceneMode.Single);
        ValidatePresentationScene();
        Camera camera = UnityEngine.Object.FindFirstObjectByType<ShelfLocationBridge>().captureCamera;
        const int width = 1280;
        const int height = 720;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        camera.targetTexture = target;
        RenderTexture.active = target;
        camera.Render();
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        string path = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "Market_Presentation_Camera.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(target);
        Debug.Log("Presentation camera capture written to " + path);
    }

    public static void BuildPlayerBatch() {
        EditorSceneManager.OpenScene(PresentationScenePath, OpenSceneMode.Single);
        ValidatePresentationScene();
        string outputDirectory = Path.Combine(Path.GetTempPath(), "ShelfPresentationBuild");
        Directory.CreateDirectory(outputDirectory);
        string executable = Path.Combine(outputDirectory, "MarketPresentation.exe");
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { PresentationScenePath },
            locationPathName = executable,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException(
                $"Windows Development Player build failed: {report.summary.result}, " +
                $"{report.summary.totalErrors} error(s).");
        Debug.Log(
            $"Windows Development Player build passed: {executable} | " +
            $"{report.summary.totalSize} bytes | {report.summary.totalTime}.");
    }
}
