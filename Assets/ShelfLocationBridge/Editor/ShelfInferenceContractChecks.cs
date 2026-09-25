using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UI;

public static class ShelfInferenceContractChecks {
    static bool MatrixApproximately(Matrix4x4 left,Matrix4x4 right,float tolerance=.0001f) {
        for(int row=0;row<4;row++)
            for(int column=0;column<4;column++)
                if(Mathf.Abs(left[row,column]-right[row,column])>tolerance)return false;
        return true;
    }

    [MenuItem("Tools/Shelf Location/Run Inference Contract Checks")]
    public static void Run() {
        const string json="{\"success\":true,\"frame_id\":\"frame_000001\",\"visualization_url\":\"/visualization/0123456789abcdef0123456789abcdef.jpg\",\"empty_inference_mode\":\"shelf_level_roi\",\"image\":{\"width\":1280,\"height\":720},\"counts\":{\"empty_model_predict_calls\":1,\"empty_inference_inputs\":1},\"timing\":{\"empty_batch_inference_ms\":12.5},\"shelves\":[{"+
            "\"shelf_id\":\"A-L-01-01\",\"parent_shelf_id\":\"A-L-01\",\"shelf_level_id\":\"A-L-01-01\",\"level_number\":1,\"status\":\"NO_EMPTY_SPACE\",\"empty_space_count\":0,"+
            "\"shelf_index\":0,\"confidence\":0.94,\"model_task\":\"segment\",\"empty_inference_source\":\"shelf_level_roi\","+
            "\"mask_polygon\":[{\"x\":320,\"y\":180},{\"x\":960,\"y\":180},{\"x\":900,\"y\":540},{\"x\":350,\"y\":520}],"+
            "\"global_bbox_xyxy\":[320,180,960,540],"+
            "\"sections\":{\"SOL\":0,\"ORTA\":0,\"SAĞ\":0},\"detections\":[]}]}";
        var response=ShelfInferenceResponse.Parse(json);
        if(response.shelves.Length!=1||response.shelves[0].shelf_id!="A-L-01-01"||
           response.shelves[0].parent_shelf_id!="A-L-01"||
           response.shelves[0].shelf_level_id!="A-L-01-01"||response.shelves[0].level_number!=1||
           response.shelves[0].sections.SAĞ!=0||response.image.width!=1280||
           response.empty_inference_mode!="shelf_level_roi"||
           response.shelves[0].empty_inference_source!="shelf_level_roi"||
           response.timing==null||Mathf.Abs(response.timing.empty_batch_inference_ms-12.5f)>.001f||
           response.shelves[0].mask_polygon==null||
           response.shelves[0].mask_polygon.Length!=4||
           response.shelves[0].mask_polygon[2].x!=900f||
           response.visualization_url!="/visualization/0123456789abcdef0123456789abcdef.jpg"||
           response.counts.empty_model_predict_calls!=1||response.counts.empty_inference_inputs!=1)
            throw new Exception("Valid inference response could not be parsed.");
        if(!ShelfInferenceClient.TryResolveVisualizationUrl(
               "http://127.0.0.1:8000",response.visualization_url,out string visualizationUrl)||
           visualizationUrl!="http://127.0.0.1:8000/visualization/0123456789abcdef0123456789abcdef.jpg")
            throw new Exception("Visualization URL could not be resolved against the inference server.");
        if(ShelfInferenceClient.TryResolveVisualizationUrl(
               "http://127.0.0.1:8000","https://example.com/visualization/a.jpg",out _)||
           ShelfInferenceClient.TryResolveVisualizationUrl(
               "http://127.0.0.1:8000","/visualization/../secret.jpg",out _))
            throw new Exception("An unsafe visualization URL was accepted.");
        if(!DetectionOverlayManager.UsesPolygonVisualization(response.shelves[0]))
            throw new Exception("A valid segmentation shelf did not select polygon visualization.");
        if(!DetectionOverlayManager.TryMapPolygon(
               response.shelves[0].mask_polygon,response.image.width,response.image.height,
               16f/9f,out Vector2[] mappedPolygon)||mappedPolygon.Length!=4)
            throw new Exception("A valid shelf segmentation polygon could not be mapped.");
        if(!DetectionOverlayManager.TryGetNormalizedPoint(
               320f,180f,1280,720,out Vector2 topLeft)||
           Mathf.Abs(topLeft.x-.25f)>.0001f||Mathf.Abs(topLeft.y-.75f)>.0001f)
            throw new Exception("Top-left polygon coordinates were not normalized correctly.");
        if((mappedPolygon[0]-topLeft).sqrMagnitude>.0000001f)
            throw new Exception("Mapped polygon points did not use the shared normalization path.");
        if(!DetectionOverlayManager.TryGetNormalizedRect(
               new[]{320f,180f,960f,540f},1280,720,out Rect normalized)||
           Mathf.Abs(normalized.xMin-.25f)>.0001f||Mathf.Abs(normalized.yMin-.25f)>.0001f||
           Mathf.Abs(normalized.xMax-.75f)>.0001f||Mathf.Abs(normalized.yMax-.75f)>.0001f)
            throw new Exception("Top-left detection coordinates were not normalized correctly.");
        Rect narrowerDisplay=DetectionOverlayManager.MapCaptureToDisplayAspect(
            normalized,16f/9f,4f/3f);
        if(Mathf.Abs(narrowerDisplay.xMin-(1f/6f))>.0001f||
           Mathf.Abs(narrowerDisplay.xMax-(5f/6f))>.0001f)
            throw new Exception("Detection coordinates did not adapt to the display aspect ratio.");
        Vector2 pointOnNarrowerDisplay=DetectionOverlayManager.MapCaptureToDisplayAspect(
            topLeft,16f/9f,4f/3f);
        if(Mathf.Abs(pointOnNarrowerDisplay.x-narrowerDisplay.xMin)>.0001f||
           Mathf.Abs(pointOnNarrowerDisplay.y-narrowerDisplay.yMax)>.0001f)
            throw new Exception("Polygon and bbox coordinates do not share aspect mapping.");
        if(!DetectionOverlayManager.TryGetNormalizedRect(
               response.shelves[0].global_bbox_xyxy,response.image.width,response.image.height,
               out Rect shelfNormalized)||shelfNormalized!=normalized)
            throw new Exception("Shelf and empty overlays do not share the coordinate mapping.");
        var detectionShelf=new ShelfInferenceShelf {
            model_task="detect",mask_polygon=response.shelves[0].mask_polygon
        };
        if(DetectionOverlayManager.UsesPolygonVisualization(detectionShelf))
            throw new Exception("A detection shelf incorrectly selected polygon visualization.");
        var triangleSegment=new ShelfInferenceShelf {
            shelf_id="UNKNOWN_SHELF",model_task="segment",mask_polygon=new[] {
                new ShelfPolygonPoint {x=0f,y=0f},new ShelfPolygonPoint {x=2f,y=0f},
                new ShelfPolygonPoint {x=1f,y=1f}
            }
        };
        if(!DetectionOverlayManager.UsesPolygonVisualization(triangleSegment))
            throw new Exception("A three-point UNKNOWN_SHELF polygon was not accepted.");
        var malformedSegment=new ShelfInferenceShelf {
            model_task="segment",mask_polygon=new[] {
                new ShelfPolygonPoint {x=0f,y=0f},new ShelfPolygonPoint {x=1f,y=1f}
            }
        };
        if(DetectionOverlayManager.UsesPolygonVisualization(malformedSegment)||
           DetectionOverlayManager.UsesPolygonVisualization(
               new ShelfInferenceShelf {model_task="segment"}))
            throw new Exception("A missing or malformed segmentation polygon did not fall back.");
        var state=new ShelfInferenceState();
        if(!state.Update(response.shelves[0]))throw new Exception("Initial shelf state was not reported.");
        if(state.Update(response.shelves[0]))throw new Exception("Identical shelf state repeated an alert.");
        response.shelves[0].status="EMPTY_SPACE_DETECTED";
        response.shelves[0].empty_space_count=2;
        response.shelves[0].sections.SOL=1;
        response.shelves[0].sections.SAĞ=1;
        if(!state.Update(response.shelves[0]))throw new Exception("Changed shelf state did not report an alert.");
        const string positiveJson="{\"success\":true,\"frame_id\":\"frame_000002\",\"image\":{\"width\":1280,\"height\":720},\"counts\":{},\"shelves\":[{"+
            "\"shelf_id\":\"A-L-02-01\",\"parent_shelf_id\":\"A-L-02\",\"shelf_level_id\":\"A-L-02-01\",\"level_number\":1,\"status\":\"EMPTY_SPACE_DETECTED\",\"empty_space_count\":2,"+
            "\"shelf_index\":0,\"global_bbox_xyxy\":[0,0,1280,720],"+
            "\"sections\":{\"SOL\":1,\"ORTA\":0,\"SAĞ\":1},"+
            "\"detections\":[{\"confidence\":0.82,\"section\":\"SAĞ\",\"parent_shelf_id\":\"A-L-02\",\"shelf_level_id\":\"A-L-02-01\",\"global_bbox_xyxy\":[1,2,3,4]}]}]}";
        var positive=ShelfInferenceResponse.Parse(positiveJson);
        if(positive.shelves[0].sections.SAĞ!=1||positive.shelves[0].detections.Length!=1||
           positive.shelves[0].detections[0].section!="SAĞ"||
           positive.shelves[0].detections[0].shelf_level_id!="A-L-02-01"||
           !string.IsNullOrEmpty(positive.visualization_url))
            throw new Exception("Semantic response without visualization could not be parsed.");
        var lowerLevel=new ShelfInferenceShelf {
            shelf_id="A-L-02-03",parent_shelf_id="A-L-02",shelf_level_id="A-L-02-03",
            level_number=3,empty_space_count=1
        };
        if(!ShelfInferenceClient.ShouldPreferShelf(positive.shelves[0],lowerLevel)||
           ShelfInferenceClient.ShouldPreferShelf(lowerLevel,positive.shelves[0]))
            throw new Exception("HUD priority did not prefer the highest shelf level with a gap.");
        if(!DetectionOverlayManager.TryGetNormalizedRect(
               positive.shelves[0].detections[0].global_bbox_xyxy,
               positive.image.width,positive.image.height,out Rect emptyRect)||
           emptyRect.width<=0f||emptyRect.height<=0f)
            throw new Exception("Empty-shelf detection bbox could not be mapped.");
        const string debugJson="{\"success\":true,\"frame_id\":\"frame_debug\",\"image\":{\"width\":1280,\"height\":720},\"counts\":{},\"debug\":{\"debug_only\":true,"+
            "\"failure_stage\":\"none\",\"full_frame_raw\":1,\"full_shelf_roi_raw\":1,\"tile_raw\":0,"+
            "\"production_raw\":1,\"mask_rejected\":0,\"final\":1},"+
            "\"shelves\":[{\"shelf_id\":\"A-L-02\",\"status\":\"NO_EMPTY_SPACE\","+
            "\"shelf_index\":0,\"global_bbox_xyxy\":[0,0,1280,720],"+
            "\"empty_space_count\":0,\"sections\":{\"SOL\":0,\"ORTA\":0,\"SAĞ\":0},"+
            "\"detections\":[]}]}";
        var debugResponse=ShelfInferenceResponse.Parse(debugJson);
        if(debugResponse.debug==null||!debugResponse.debug.debug_only||
           debugResponse.debug.failure_stage!="none"||debugResponse.debug.full_shelf_roi_raw!=1)
            throw new Exception("Live debug counts could not be parsed.");
        try {
            ShelfInferenceResponse.Parse("{\"success\":true,\"frame_id\":\"x\"}");
            throw new Exception("Malformed response was accepted.");
        } catch(FormatException) { }
        var cameraObject=new GameObject("InferenceMetadataTestCamera");
        var bridgeObject=new GameObject("InferenceMetadataTestBridge");
        var hudObject=new GameObject("InferenceHudTest");
        RenderTexture priorTarget=null;
        RenderTexture priorActive=null;
        try {
            var camera=cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(
                new Vector3(1.25f,2.5f,-3.75f),Quaternion.Euler(12f,34f,0f));
            camera.fieldOfView=75f;
            camera.rect=new Rect(.1f,.2f,.7f,.6f);
            priorTarget=new RenderTexture(32,24,16);
            priorTarget.Create();
            camera.targetTexture=priorTarget;
            camera.aspect=1.3f;
            priorActive=new RenderTexture(8,8,0);
            priorActive.Create();
            RenderTexture.active=priorActive;
            var bridge=bridgeObject.AddComponent<ShelfLocationBridge>();
            bridge.captureCamera=camera;
            bridge.captureWidth=64;
            bridge.captureHeight=32;

            Vector3 positionBefore=camera.transform.position;
            Quaternion rotationBefore=camera.transform.rotation;
            Rect rectBefore=camera.rect;
            float aspectBefore=camera.aspect;
            RenderTexture targetBefore=camera.targetTexture;
            Matrix4x4 projectionBefore=camera.projectionMatrix;
            var metadata=bridge.BuildFrameMetadata(camera,1280,720,"frame_test","frame_test.jpg");
            if(Mathf.Abs(metadata.camera.intrinsics.fx-metadata.camera.intrinsics.fy)>0.1f)
                throw new Exception("Camera intrinsics do not match the Unity projection matrix.");
            if(JsonUtility.ToJson(metadata).Contains("visible_shelf_ids"))
                throw new Exception("Ground truth leaked into runtime metadata.");
            if(camera.transform.position!=positionBefore||camera.transform.rotation!=rotationBefore||
               camera.rect!=rectBefore||camera.aspect!=aspectBefore||
               camera.targetTexture!=targetBefore||
               !MatrixApproximately(camera.projectionMatrix,projectionBefore))
                throw new Exception("BuildFrameMetadata mutated the live camera.");

            byte[] captured=bridge.CaptureRuntimeJpeg(85,out FrameInputData capturedMetadata);
            if(captured==null||captured.Length==0||capturedMetadata.image.width!=64||
               capturedMetadata.image.height!=32)
                throw new Exception("Camera-state capture guard did not produce an image.");
            if(camera.transform.position!=positionBefore||camera.transform.rotation!=rotationBefore||
               camera.rect!=rectBefore||Mathf.Abs(camera.aspect-aspectBefore)>.0001f||
               camera.targetTexture!=targetBefore||RenderTexture.active!=priorActive||
               !MatrixApproximately(camera.projectionMatrix,projectionBefore))
                throw new Exception("Inference capture permanently mutated camera state.");

            Renderer[] renderersBefore=UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsSortMode.None);
            var rendererMatrices=new Matrix4x4[renderersBefore.Length];
            var rendererEnabled=new bool[renderersBefore.Length];
            var rendererMaterials=new Material[renderersBefore.Length];
            for(int i=0;i<renderersBefore.Length;i++) {
                rendererMatrices[i]=renderersBefore[i].localToWorldMatrix;
                rendererEnabled[i]=renderersBefore[i].enabled;
                rendererMaterials[i]=renderersBefore[i].sharedMaterial;
            }
            var hud=hudObject.AddComponent<ShelfInferenceHud>();
            hud.Initialize(camera);
            Transform canvas=hud.CanvasTransform;
            if(canvas==null||canvas.parent!=null||hud.CanvasRenderMode!=RenderMode.ScreenSpaceOverlay||
               canvas.localRotation!=Quaternion.identity||
               canvas.localScale!=Vector3.one||canvas.gameObject.layer!=0||
               canvas.GetComponentsInChildren<Renderer>(true).Length!=0||
               canvas.GetComponent<Canvas>()==null||canvas.GetComponent<CanvasScaler>()==null)
                throw new Exception("Runtime visualization is not an identity root ScreenSpaceOverlay UI canvas.");
            Renderer[] renderersAfter=UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsSortMode.None);
            if(renderersAfter.Length!=renderersBefore.Length)
                throw new Exception("Runtime HUD created a world-space Renderer.");
            for(int i=0;i<renderersBefore.Length;i++)
                if(renderersBefore[i]==null||renderersBefore[i].enabled!=rendererEnabled[i]||
                   renderersBefore[i].sharedMaterial!=rendererMaterials[i]||
                   !MatrixApproximately(renderersBefore[i].localToWorldMatrix,rendererMatrices[i]))
                    throw new Exception("Runtime visualization mutated physical scene geometry.");
            hud.SetVisualizationMode(ShelfVisualizationMode.PythonAnnotatedFrame);
            hud.PrepareVisualizationFrame("frame_current");
            var jpegSource=new Texture2D(16,9,TextureFormat.RGB24,false);
            jpegSource.SetPixels(new Color[16*9]);
            jpegSource.Apply();
            byte[] jpegBytes=jpegSource.EncodeToJPG(90);
            UnityEngine.Object.DestroyImmediate(jpegSource);
            var texture=new Texture2D(2,2,TextureFormat.RGB24,false);
            if(!texture.LoadImage(jpegBytes,true)||texture.width!=16||texture.height!=9)
                throw new Exception("A valid JPEG did not load into a runtime Texture2D.");
            if(!hud.TryShowVisualization("frame_current",texture,1280,720)||
               !hud.IsPythonVisualizationVisible||hud.IsGeometryOverlayVisible||
               !hud.IsAlertCardVisible||hud.CurrentVisualizationTexture!=texture||
               hud.VisualizationFrameId!="frame_current")
                throw new Exception("Python visualization was not shown in the default HUD mode.");
            if(hud.VisualizationAspectMode!=AspectRatioFitter.AspectMode.FitInParent||
               hud.VisualizationUvRect!=new Rect(0f,0f,1f,1f)||
               hud.VisualizationLocalScale!=Vector3.one||
               hud.VisualizationLocalEulerAngles.sqrMagnitude>.0001f)
                throw new Exception("Python visualization uses cropped or inverted UI mapping.");
            if((hud.VisualizationViewportAnchorMin-rectBefore.min).sqrMagnitude>.000001f||
               (hud.VisualizationViewportAnchorMax-rectBefore.max).sqrMagnitude>.000001f||
               camera.rect!=rectBefore)
                throw new Exception("HUD viewport mapping changed or ignored the camera rect.");
            hud.PrepareVisualizationFrame("frame_next");
            if(texture!=null)
                throw new Exception("The replaced runtime visualization texture was not released.");
            var staleTexture=new Texture2D(16,9,TextureFormat.RGB24,false);
            if(hud.TryShowVisualization("frame_current",staleTexture,1280,720)||
               hud.IsPythonVisualizationVisible||hud.CurrentVisualizationTexture!=null)
                throw new Exception("A stale visualization frame was accepted or retained.");
            UnityEngine.Object.DestroyImmediate(staleTexture);
            hud.SetVisualizationMode(ShelfVisualizationMode.UnityGeometryOverlay);
            if(hud.IsPythonVisualizationVisible||!hud.IsGeometryOverlayVisible||
               !hud.IsAlertCardVisible)
                throw new Exception("Unity geometry debug mode did not preserve HUD layering.");
            hud.ShowResponse(positive.shelves[0],positive);
            if(hud.BodyText==null||!hud.BodyText.Contains("A-L-02-01"))
                throw new Exception("HUD did not display the shelf-level semantic ID.");
            if(canvas==null||canvas.Find("PythonModelOutputViewport")==null||
               canvas.Find("DetectionOverlay")==null||canvas.Find("AlertCard")==null||
               canvas.Find("PythonModelOutputViewport").GetSiblingIndex()>=
                   canvas.Find("AlertCard").GetSiblingIndex())
                throw new Exception("HUD visualization hierarchy is incomplete or incorrectly layered.");
        } finally {
            RenderTexture.active=null;
            Camera cleanupCamera=cameraObject.GetComponent<Camera>();
            if(cleanupCamera!=null)cleanupCamera.targetTexture=null;
            if(priorTarget!=null) {
                priorTarget.Release();
                UnityEngine.Object.DestroyImmediate(priorTarget);
            }
            if(priorActive!=null) {
                priorActive.Release();
                UnityEngine.Object.DestroyImmediate(priorActive);
            }
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(bridgeObject);
            UnityEngine.Object.DestroyImmediate(hudObject);
        }
        Debug.Log("ShelfInference contract checks passed.");
    }

    public static void BuildLiveSmokePlayer() {
        var args=Environment.GetCommandLineArgs();
        int index=Array.IndexOf(args,"-shelfSmokeOutput");
        if(index<0||index+1>=args.Length)throw new Exception("-shelfSmokeOutput path is required.");
        string output=Path.GetFullPath(args[index+1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var options=new BuildPlayerOptions {
            scenes=new[]{"Assets/Market_01.unity"},
            locationPathName=output,
            target=BuildTarget.StandaloneWindows64,
            options=BuildOptions.Development
        };
        BuildReport report=BuildPipeline.BuildPlayer(options);
        if(report.summary.result!=BuildResult.Succeeded)
            throw new Exception("Shelf inference player build failed: "+report.summary.result);
        Debug.Log("ShelfInference player build passed: "+output);
    }
}
