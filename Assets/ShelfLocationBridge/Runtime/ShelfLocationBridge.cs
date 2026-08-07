using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using UnityEngine;

[Serializable] public class ShelfMapEntry {
    public string shelf_id,parent_surface_id; public float[][] corners_world;
    public float[] center_world,front_normal_world; public float width_m,height_m; public bool active=true;
}
[Serializable] public class ShelfMapData {
    public int schema_version=2; public string coordinate_system="unity_world";
    public List<ShelfMapEntry> shelves=new();
}
[Serializable] public class ImageMetadata { public int width,height; }
[Serializable] public class CameraIntrinsics { public float fx,fy,cx,cy; }
[Serializable] public class CameraMetadata {
    public float[] position_map,rotation_xyzw; public CameraIntrinsics intrinsics;
    public float vertical_fov,near_clip,far_clip; public float[] world_to_camera_matrix,projection_matrix,gpu_projection_matrix;
    public string quaternion_order="xyzw",matrix_layout="row_major";
}
[Serializable] public class PoseMetadata {
    public string source="unity_camera_pose"; public float quality=1;
    public bool relocalized=true,scale_initialized=true;
}
[Serializable] public class FrameInputData {
    public int schema_version=3; public string store_id,session_id,frame_id,image_path,pixel_origin="top_left";
    public double timestamp; public ImageMetadata image; public CameraMetadata camera; public PoseMetadata pose;
}
[Serializable] public class FrameGroundTruth {
    public int schema_version=2; public string frame_id; public List<string> visible_shelf_ids=new();
}

public class ShelfLocationBridge : MonoBehaviour {
    public Camera captureCamera; public ShelfResolver resolver; public int captureWidth=1280,captureHeight=720;
    [Header("ID-free robot runtime session")]
    public string storeId="MARKET-001",sessionId="mapping_pass_01";
    [Range(0,1)] public float simulatedPoseQuality=1;
    public bool relocalized=true,scaleInitialized=true;
    public string DataRoot=>Path.Combine(Directory.GetParent(Application.dataPath).FullName,"ShelfSystemData");
    static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
    static float[] M(Matrix4x4 m){var a=new float[16];for(int r=0;r<4;r++)for(int c=0;c<4;c++)a[r*4+c]=m[r,c];return a;}

    public static ShelfMapEntry BuildEntry(KnownShelfRegion region) {
        var p=region.WorldCorners();
        return new ShelfMapEntry{shelf_id=region.shelfId,parent_surface_id=region.parentSurfaceId,
          corners_world=new[]{V(p[0]),V(p[1]),V(p[2]),V(p[3])},center_world=V(region.Center),
          front_normal_world=V(region.FrontNormal),width_m=region.Width,height_m=region.Height,active=region.activeRegion};
    }
    public static ShelfMapEntry BuildLegacyEntry(ShelfIdentity id) {
        Renderer r=id.targetRenderer!=null?id.targetRenderer:id.GetComponentInChildren<Renderer>();
        if(r==null)throw new InvalidOperationException($"{id.rackId}: Renderer eksik.");
        Bounds b=r.localBounds; Transform t=r.transform; float z=id.FrontDirection.normalized==t.forward?b.max.z:b.min.z;
        Vector3[] p={t.TransformPoint(b.min.x,b.min.y,z),t.TransformPoint(b.min.x,b.max.y,z),
                     t.TransformPoint(b.max.x,b.max.y,z),t.TransformPoint(b.max.x,b.min.y,z)};
        Vector3 center=(p[0]+p[1]+p[2]+p[3])*.25f;
        return new ShelfMapEntry{shelf_id=id.rackId,parent_surface_id=id.name,corners_world=new[]{V(p[0]),V(p[1]),V(p[2]),V(p[3])},
          center_world=V(center),front_normal_world=V(Vector3.Cross(p[1]-p[0],p[3]-p[0]).normalized),
          width_m=(p[3]-p[0]).magnitude,height_m=(p[1]-p[0]).magnitude};
    }
    public List<ShelfMapEntry> Entries() {
        var entries=new List<ShelfMapEntry>();
        var regions=FindObjectsByType<KnownShelfRegion>(FindObjectsSortMode.None);
        var parentIdentities=new HashSet<ShelfIdentity>();
        foreach(var region in regions)if(region.activeRegion){
            entries.Add(BuildEntry(region)); var parent=region.GetComponentInParent<ShelfIdentity>();if(parent)parentIdentities.Add(parent);
        }
        foreach(var id in FindObjectsByType<ShelfIdentity>(FindObjectsSortMode.None))
            if(!parentIdentities.Contains(id))entries.Add(BuildLegacyEntry(id));
        entries.Sort((a,b)=>string.CompareOrdinal(a.shelf_id,b.shelf_id)); return entries;
    }
    public void ExportStoreMap() {
        Directory.CreateDirectory(DataRoot);var entries=Entries();var sb=new StringBuilder();
        sb.Append("{\n  \"schema_version\": 2,\n  \"coordinate_system\": \"unity_world\",\n  \"shelves\": [\n");
        for(int i=0;i<entries.Count;i++){var e=entries[i];if(i>0)sb.Append(",\n");sb.Append("    {\n");
          sb.Append($"      \"shelf_id\": {Q(e.shelf_id)},\n      \"parent_surface_id\": {Q(e.parent_surface_id)},\n");
          sb.Append("      \"corners_world\": [");for(int j=0;j<4;j++){if(j>0)sb.Append(",");sb.Append("\n        ");AppendVector(sb,e.corners_world[j]);}
          sb.Append("\n      ],\n      \"center_world\": ");AppendVector(sb,e.center_world);
          sb.Append(",\n      \"front_normal_world\": ");AppendVector(sb,e.front_normal_world);
          sb.Append($",\n      \"width_m\": {F(e.width_m)},\n      \"height_m\": {F(e.height_m)},\n      \"active\": {(e.active?"true":"false")}\n    }}");
        }
        sb.Append("\n  ]\n}\n");File.WriteAllText(Path.Combine(DataRoot,"store_map.json"),sb.ToString());
    }
    static string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
    static string Q(string v)=>v==null?"null":"\""+v.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    static void AppendVector(StringBuilder sb,float[] v)=>sb.Append($"[{F(v[0])}, {F(v[1])}, {F(v[2])}]");
    public void CaptureTestFrame()=>StartCoroutine(Capture());
    IEnumerator Capture(){
        if(captureCamera==null)captureCamera=Camera.main; yield return new WaitForEndOfFrame();
        string sessionRoot=Path.Combine(DataRoot,"sessions",sessionId);
        string inputRoot=Path.Combine(sessionRoot,"input"),truthRoot=Path.Combine(sessionRoot,"ground_truth");
        Directory.CreateDirectory(inputRoot);Directory.CreateDirectory(truthRoot);
        string id=$"frame_{Time.frameCount:D6}",imageName=$"{id}.png";
        var rt=new RenderTexture(captureWidth,captureHeight,24);var old=captureCamera.targetTexture;captureCamera.targetTexture=rt;
        captureCamera.Render();RenderTexture.active=rt;var tex=new Texture2D(captureWidth,captureHeight,TextureFormat.RGB24,false);
        tex.ReadPixels(new Rect(0,0,captureWidth,captureHeight),0,0);tex.Apply();
        File.WriteAllBytes(Path.Combine(inputRoot,imageName),FlipPng(tex,tex.GetPixels32()));
        Transform ct=captureCamera.transform;float fy=.5f*captureHeight/Mathf.Tan(.5f*captureCamera.fieldOfView*Mathf.Deg2Rad);
        float fx=fy*captureCamera.aspect;Matrix4x4 gpu=GL.GetGPUProjectionMatrix(captureCamera.projectionMatrix,true);
        var meta=new FrameInputData{store_id=storeId,session_id=sessionId,frame_id=id,image_path=imageName,
          timestamp=Time.realtimeSinceStartupAsDouble,image=new ImageMetadata{width=captureWidth,height=captureHeight},
          pose=new PoseMetadata{source="unity_camera_pose",quality=simulatedPoseQuality,relocalized=relocalized,scale_initialized=scaleInitialized},
          camera=new CameraMetadata{position_map=V(ct.position),rotation_xyzw=new[]{ct.rotation.x,ct.rotation.y,ct.rotation.z,ct.rotation.w},
          intrinsics=new CameraIntrinsics{fx=fx,fy=fy,cx=captureWidth*.5f,cy=captureHeight*.5f},
          vertical_fov=captureCamera.fieldOfView,near_clip=captureCamera.nearClipPlane,far_clip=captureCamera.farClipPlane,
          world_to_camera_matrix=M(captureCamera.worldToCameraMatrix),projection_matrix=M(captureCamera.projectionMatrix),gpu_projection_matrix=M(gpu)}};
        File.WriteAllText(Path.Combine(inputRoot,$"{id}.json"),JsonUtility.ToJson(meta,true));
        var gt=new FrameGroundTruth{frame_id=id};Plane[] planes=GeometryUtility.CalculateFrustumPlanes(captureCamera);
        foreach(var e in Entries()){var bounds=new Bounds(new Vector3(e.center_world[0],e.center_world[1],e.center_world[2]),Vector3.zero);
          foreach(var p in e.corners_world)bounds.Encapsulate(new Vector3(p[0],p[1],p[2]));
          if(GeometryUtility.TestPlanesAABB(planes,bounds))gt.visible_shelf_ids.Add(e.shelf_id);}
        File.WriteAllText(Path.Combine(truthRoot,$"{id}.json"),JsonUtility.ToJson(gt,true));
        captureCamera.targetTexture=old;RenderTexture.active=null;Destroy(rt);Destroy(tex);
    }
    static byte[] FlipPng(Texture2D source,Color32[] p){int w=source.width,h=source.height;var f=new Color32[p.Length];
        for(int y=0;y<h;y++)Array.Copy(p,y*w,f,(h-1-y)*w,w);var t=new Texture2D(w,h,TextureFormat.RGBA32,false);
        t.SetPixels32(f);t.Apply();byte[] b=t.EncodeToPNG();Destroy(t);return b;}
}
