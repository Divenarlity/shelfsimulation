using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ShelfInferenceContractChecks {
    [MenuItem("Tools/Shelf Location/Run Inference Contract Checks")]
    public static void Run() {
        const string json="{\"success\":true,\"frame_id\":\"frame_000001\",\"empty_inference_mode\":\"full_frame_gated\",\"image\":{\"width\":1280,\"height\":720},\"counts\":{\"empty_model_predict_calls\":1,\"empty_inference_inputs\":1},\"shelves\":[{"+
            "\"shelf_id\":\"A-L-01\",\"status\":\"NO_EMPTY_SPACE\",\"empty_space_count\":0,"+
            "\"shelf_index\":0,\"confidence\":0.94,\"model_task\":\"segment\",\"empty_inference_source\":\"full_frame\","+
            "\"global_bbox_xyxy\":[320,180,960,540],"+
            "\"sections\":{\"SOL\":0,\"ORTA\":0,\"SAĞ\":0},\"detections\":[]}]}";
        var response=ShelfInferenceResponse.Parse(json);
        if(response.shelves.Length!=1||response.shelves[0].shelf_id!="A-L-01"||
           response.shelves[0].sections.SAĞ!=0||response.image.width!=1280||
           response.empty_inference_mode!="full_frame_gated"||
           response.shelves[0].empty_inference_source!="full_frame"||
           response.counts.empty_model_predict_calls!=1||response.counts.empty_inference_inputs!=1)
            throw new Exception("Valid inference response could not be parsed.");
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
        if(!DetectionOverlayManager.TryGetNormalizedRect(
               response.shelves[0].global_bbox_xyxy,response.image.width,response.image.height,
               out Rect shelfNormalized)||shelfNormalized!=normalized)
            throw new Exception("Shelf and empty overlays do not share the coordinate mapping.");
        var state=new ShelfInferenceState();
        if(!state.Update(response.shelves[0]))throw new Exception("Initial shelf state was not reported.");
        if(state.Update(response.shelves[0]))throw new Exception("Identical shelf state repeated an alert.");
        response.shelves[0].status="EMPTY_SPACE_DETECTED";
        response.shelves[0].empty_space_count=2;
        response.shelves[0].sections.SOL=1;
        response.shelves[0].sections.SAĞ=1;
        if(!state.Update(response.shelves[0]))throw new Exception("Changed shelf state did not report an alert.");
        const string positiveJson="{\"success\":true,\"frame_id\":\"frame_000002\",\"image\":{\"width\":1280,\"height\":720},\"counts\":{},\"shelves\":[{"+
            "\"shelf_id\":\"A-L-01\",\"status\":\"EMPTY_SPACE_DETECTED\",\"empty_space_count\":2,"+
            "\"shelf_index\":0,\"global_bbox_xyxy\":[0,0,1280,720],"+
            "\"sections\":{\"SOL\":1,\"ORTA\":0,\"SAĞ\":1},"+
            "\"detections\":[{\"confidence\":0.82,\"section\":\"SAĞ\",\"global_bbox_xyxy\":[1,2,3,4]}]}]}";
        var positive=ShelfInferenceResponse.Parse(positiveJson);
        if(positive.shelves[0].sections.SAĞ!=1||positive.shelves[0].detections.Length!=1||
           positive.shelves[0].detections[0].section!="SAĞ")
            throw new Exception("Positive inference response could not be parsed.");
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
        try {
            var camera=cameraObject.AddComponent<Camera>();
            camera.fieldOfView=75f;camera.aspect=1280f/720f;
            var bridge=bridgeObject.AddComponent<ShelfLocationBridge>();
            var metadata=bridge.BuildFrameMetadata(camera,1280,720,"frame_test","frame_test.jpg");
            if(Mathf.Abs(metadata.camera.intrinsics.fx-metadata.camera.intrinsics.fy)>0.1f)
                throw new Exception("Camera intrinsics do not match the Unity projection matrix.");
            if(JsonUtility.ToJson(metadata).Contains("visible_shelf_ids"))
                throw new Exception("Ground truth leaked into runtime metadata.");
        } finally {
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(bridgeObject);
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
