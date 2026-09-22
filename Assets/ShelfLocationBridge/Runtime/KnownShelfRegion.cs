using UnityEngine;

public class KnownShelfRegion : MonoBehaviour {
    [Header("Static known-shelf identity")]
    public string shelfId;
    public string parentSurfaceId;
    public bool activeRegion = true;

    [Header("World corners: bottom-left, top-left, top-right, bottom-right")]
    public Vector3 bottomLeftWorld;
    public Vector3 topLeftWorld;
    public Vector3 topRightWorld;
    public Vector3 bottomRightWorld;

    public Vector3[] WorldCorners() => new[] {
        bottomLeftWorld, topLeftWorld, topRightWorld, bottomRightWorld
    };

    public Vector3 Center =>
        (bottomLeftWorld + topLeftWorld + topRightWorld + bottomRightWorld) * .25f;

    public Vector3 FrontNormal =>
        Vector3.Cross(topLeftWorld - bottomLeftWorld, bottomRightWorld - bottomLeftWorld).normalized;

    public float Width =>
        ((bottomRightWorld - bottomLeftWorld).magnitude +
         (topRightWorld - topLeftWorld).magnitude) * .5f;

    public float Height =>
        ((topLeftWorld - bottomLeftWorld).magnitude +
         (topRightWorld - bottomRightWorld).magnitude) * .5f;

    void OnDrawGizmos() {
        if (!activeRegion) return;
        Vector3[] corners = WorldCorners();
        Gizmos.color = Color.yellow;
        for (int i = 0; i < corners.Length; i++)
            Gizmos.DrawLine(corners[i], corners[(i + 1) % corners.Length]);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(Center, Center + FrontNormal * .5f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(Center, shelfId);
#endif
    }
}
