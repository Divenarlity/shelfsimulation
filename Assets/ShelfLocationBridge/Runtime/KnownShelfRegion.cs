using System;
using UnityEngine;

public class KnownShelfRegion : MonoBehaviour {
    [Header("Static identity (never generated at runtime)")]
    public string shelfId;
    public string parentSurfaceId;
    public bool activeRegion=true;

    [Header("Preferred explicit corners: BL, TL, TR, BR")]
    public Transform bottomLeft,topLeft,topRight,bottomRight;

    [Header("Oriented mesh/local-bounds fallback")]
    public Renderer sourceRenderer;
    [Tooltip("Normalized X/Y rectangle inside the renderer local bounds.")]
    public Vector2 normalizedMin=Vector2.zero;
    public Vector2 normalizedMax=Vector2.one;
    [Tooltip("Use +local Z as front; disable for -local Z.")]
    public bool positiveLocalZFront=true;

    public bool HasAnchors => bottomLeft&&topLeft&&topRight&&bottomRight;

    public Vector3[] WorldCorners() {
        if(HasAnchors) return new[]{bottomLeft.position,topLeft.position,topRight.position,bottomRight.position};
        Renderer r=sourceRenderer!=null?sourceRenderer:GetComponentInParent<Renderer>();
        if(r==null) throw new InvalidOperationException($"{name}: corner anchor veya Renderer gerekli.");
        Bounds b=r.localBounds;
        float x0=Mathf.Lerp(b.min.x,b.max.x,normalizedMin.x), x1=Mathf.Lerp(b.min.x,b.max.x,normalizedMax.x);
        float y0=Mathf.Lerp(b.min.y,b.max.y,normalizedMin.y), y1=Mathf.Lerp(b.min.y,b.max.y,normalizedMax.y);
        float z=positiveLocalZFront?b.max.z:b.min.z;
        Transform t=r.transform;
        return new[]{t.TransformPoint(x0,y0,z),t.TransformPoint(x0,y1,z),
                     t.TransformPoint(x1,y1,z),t.TransformPoint(x1,y0,z)};
    }
    public Vector3 Center {
        get { var p=WorldCorners(); return (p[0]+p[1]+p[2]+p[3])*.25f; }
    }
    public Vector3 FrontNormal {
        get { var p=WorldCorners(); return Vector3.Cross(p[1]-p[0],p[3]-p[0]).normalized; }
    }
    public float Width { get { var p=WorldCorners(); return ((p[3]-p[0]).magnitude+(p[2]-p[1]).magnitude)*.5f; } }
    public float Height { get { var p=WorldCorners(); return ((p[1]-p[0]).magnitude+(p[2]-p[3]).magnitude)*.5f; } }

    void OnDrawGizmos() {
        if(!activeRegion)return;
        try {
            var p=WorldCorners(); Gizmos.color=Color.yellow;
            for(int i=0;i<4;i++)Gizmos.DrawLine(p[i],p[(i+1)%4]);
            Gizmos.color=Color.cyan; Gizmos.DrawLine(Center,Center+FrontNormal*.5f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(Center,shelfId);
#endif
        } catch(Exception) { }
    }
}
