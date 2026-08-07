using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ShelfLocationTools {
    public static void ValidateMarketSceneBatch(){EditorSceneManager.OpenScene("Assets/Market_01.unity",OpenSceneMode.Single);Validate();}
    public static void ExportMarketSceneBatch(){EditorSceneManager.OpenScene("Assets/Market_01.unity",OpenSceneMode.Single);Export();}
    static ShelfLocationBridge Bridge(bool create=false){
        var b=UnityEngine.Object.FindFirstObjectByType<ShelfLocationBridge>();
        if(b==null&&create){var go=new GameObject("ShelfLocationBridge");b=go.AddComponent<ShelfLocationBridge>();
            b.captureCamera=Camera.main;b.resolver=UnityEngine.Object.FindFirstObjectByType<ShelfResolver>();
            Undo.RegisterCreatedObjectUndo(go,"Shelf Location Auto Setup");EditorUtility.SetDirty(go);}
        return b;
    }
    [MenuItem("Tools/Shelf Location/Auto Setup")]public static void Setup(){Bridge(true);Debug.Log("ShelfLocationBridge hazır. Sahneyi kaydedin.");}
    static string ValidateGeometry(ShelfMapEntry e){
        if(e.corners_world==null||e.corners_world.Length!=4)return "dört köşe eksik";
        Vector3[] p=new Vector3[4];for(int i=0;i<4;i++)p[i]=new Vector3(e.corners_world[i][0],e.corners_world[i][1],e.corners_world[i][2]);
        Vector3 up=p[1]-p[0],right=p[3]-p[0],normal=Vector3.Cross(up,right);
        if(up.magnitude<.001f||right.magnitude<.001f||normal.magnitude<.001f)return "sıfır alanlı geometri";
        float plane=Mathf.Abs(Vector3.Dot((p[2]-p[0]),normal.normalized));if(plane>.005f)return "düzlemsel olmayan köşeler";
        if(Vector3.Dot(p[2]-p[1],right)<=0||Vector3.Dot(p[2]-p[3],up)<=0)return "köşe sırası BL,TL,TR,BR değil";
        if(e.front_normal_world==null||new Vector3(e.front_normal_world[0],e.front_normal_world[1],e.front_normal_world[2]).sqrMagnitude<.5f)return "ön normal geçersiz";
        return null;
    }
    [MenuItem("Tools/Shelf Location/Validate Scene")]public static void Validate(){
        var errors=new List<string>();var ids=new HashSet<string>();var signatures=new HashSet<string>();var b=Bridge(true);
        List<ShelfMapEntry> entries;
        try{entries=b.Entries();}catch(Exception e){errors.Add(e.Message);entries=new();}
        foreach(var e in entries){
            if(string.IsNullOrWhiteSpace(e.shelf_id)||!ids.Add(e.shelf_id))errors.Add($"Boş/yinelenen ID: {e.shelf_id}");
            string geometry=ValidateGeometry(e);if(geometry!=null)errors.Add($"{e.shelf_id}: {geometry}");
            if(e.corners_world!=null){string signature="";foreach(var p in e.corners_world)signature+=$"{p[0]:F3},{p[1]:F3},{p[2]:F3};";
              if(!signatures.Add(signature))errors.Add($"{e.shelf_id}: başka raf bölgesiyle aynı geometri");}
        }
        foreach(var id in UnityEngine.Object.FindObjectsByType<ShelfIdentity>(FindObjectsSortMode.None)){
            int child=id.GetComponentsInChildren<KnownShelfRegion>(true).Length;
            if(id.expectsMultiplePhysicalShelves&&child<2)errors.Add($"{id.rackId}: birden fazla fiziksel raf bekleniyor fakat iki KnownShelfRegion yok.");
            else if(child==1)Debug.LogWarning($"{id.rackId}: yalnızca bir KnownShelfRegion var; görüntü birden fazla fiziksel raf içeriyorsa ayrı bölgeler ekleyin.");
        }
        Camera c=Camera.main;if(c==null)errors.Add("MainCamera eksik.");
        else if(c.fieldOfView<=0||c.nearClipPlane<=0||c.farClipPlane<=c.nearClipPlane)errors.Add("Kamera parametreleri geçersiz.");
        try{Directory.CreateDirectory(b.DataRoot);string p=Path.Combine(b.DataRoot,".write_test");File.WriteAllText(p,"ok");File.Delete(p);}
        catch(Exception e){errors.Add($"Çıktı yazılamıyor: {e.Message}");}
        if(errors.Count==0)Debug.Log($"Shelf Location doğrulaması başarılı: {entries.Count} atanabilir mantıksal raf bölgesi.");
        else Debug.LogError("Shelf Location doğrulama hataları:\n- "+string.Join("\n- ",errors));
    }
    [MenuItem("Tools/Shelf Location/Export Store Map")]public static void Export(){Validate();Bridge(true).ExportStoreMap();AssetDatabase.Refresh();}
    [MenuItem("Tools/Shelf Location/Capture Test Frame")]public static void Capture(){
        if(!EditorApplication.isPlaying){Debug.LogError("Capture Test Frame için Play moduna geçin.");return;}Bridge(true).CaptureTestFrame();}
    [MenuItem("Tools/Shelf Location/Open Data Folder")]public static void Open(){var b=Bridge(true);Directory.CreateDirectory(b.DataRoot);EditorUtility.RevealInFinder(b.DataRoot);}
    [MenuItem("Tools/Shelf Location/Add Known Shelf Region")]public static void AddRegion(){
        GameObject parent=Selection.activeGameObject;if(parent==null){Debug.LogError("Önce raf yüzeyi veya child nesnesini seçin.");return;}
        var go=new GameObject("KnownShelfRegion");Undo.RegisterCreatedObjectUndo(go,"Add Known Shelf Region");go.transform.SetParent(parent.transform,false);
        var region=go.AddComponent<KnownShelfRegion>();region.sourceRenderer=parent.GetComponentInChildren<Renderer>();
        var identity=parent.GetComponentInParent<ShelfIdentity>();region.parentSurfaceId=identity!=null?identity.rackId:parent.name;
        Selection.activeGameObject=go;Debug.Log("KnownShelfRegion eklendi. Inspector'da mevcut shelfId ve normalized bölge/anchor alanlarını atayın.");
    }
    [MenuItem("Tools/Shelf Location/Create Two Regions From Selected Surface")]public static void CreateTwoRegions(){
        GameObject selected=Selection.activeGameObject;if(selected==null){Debug.LogError("Önce ShelfIdentity içeren raf yüzeyini seçin.");return;}
        var identity=selected.GetComponentInParent<ShelfIdentity>();if(identity==null){Debug.LogError("Seçimde ShelfIdentity bulunamadı.");return;}
        identity.expectsMultiplePhysicalShelves=true;Renderer renderer=identity.targetRenderer!=null?identity.targetRenderer:identity.GetComponentInChildren<Renderer>();
        var top=new GameObject("KnownShelfRegion_Top");Undo.RegisterCreatedObjectUndo(top,"Create Shelf Regions");top.transform.SetParent(identity.transform,false);
        var tr=top.AddComponent<KnownShelfRegion>();tr.shelfId=identity.rackId;tr.parentSurfaceId=identity.rackId;tr.sourceRenderer=renderer;
        tr.normalizedMin=new Vector2(0,.5f);tr.normalizedMax=Vector2.one;
        var bottom=new GameObject("KnownShelfRegion_Bottom");Undo.RegisterCreatedObjectUndo(bottom,"Create Shelf Regions");bottom.transform.SetParent(identity.transform,false);
        var br=bottom.AddComponent<KnownShelfRegion>();br.shelfId="";br.parentSurfaceId=identity.rackId;br.sourceRenderer=renderer;
        br.normalizedMin=Vector2.zero;br.normalizedMax=new Vector2(1,.5f);br.activeRegion=false;
        EditorUtility.SetDirty(identity);Selection.objects=new UnityEngine.Object[]{top,bottom};
        Debug.Log("İki oriented region oluşturuldu. Top mevcut parent ID ile hazır. Bottom shelfId alanına önceden tanımlı ikinci ID'yi girip Active Region'ı açın; sonra Validate Scene çalıştırın.");
    }
    [MenuItem("Tools/Shelf Location/Select Unassigned Regions")]public static void SelectUnassigned(){
        var found=new List<GameObject>();foreach(var r in UnityEngine.Object.FindObjectsByType<KnownShelfRegion>(FindObjectsSortMode.None))
            if(string.IsNullOrWhiteSpace(r.shelfId))found.Add(r.gameObject);
        Selection.objects=found.ToArray();Debug.Log($"{found.Count} ID atanmamış bölge seçildi.");
    }
}
