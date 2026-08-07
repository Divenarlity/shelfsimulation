using UnityEngine;

public class ShelfIdentity : MonoBehaviour
{
    [Header("Shelf Identity")]
    public string aisleId = "A";
    public string side = "Left";
    public string rackId = "A-L-01";

    [Header("References")]
    public Transform viewAnchor;
    public Renderer targetRenderer;
    [Tooltip("Bu görsel yüzey birden fazla fiziksel raf içeriyorsa işaretleyin ve child KnownShelfRegion ekleyin.")]
    public bool expectsMultiplePhysicalShelves;

    public Vector3 TargetPoint
    {
        get
        {
            if (viewAnchor != null)
                return viewAnchor.position;

            if (targetRenderer != null)
                return targetRenderer.bounds.center;

            return transform.position;
        }
    }

    public Vector3 FrontDirection
    {
        get
        {
            if (viewAnchor != null)
                return viewAnchor.forward;

            return -transform.forward;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 point = TargetPoint;

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(point, 0.08f);
        Gizmos.DrawLine(point, point + FrontDirection);
        Renderer r = targetRenderer != null ? targetRenderer : GetComponentInChildren<Renderer>();
        if (r != null)
        {
            Vector3 right = r.transform.right * r.localBounds.extents.x;
            Vector3 up = r.transform.up * r.localBounds.extents.y;
            Vector3 c = r.bounds.center;
            Vector3[] p = { c-right-up, c-right+up, c+right+up, c+right-up };
            Gizmos.color = Color.yellow;
            for (int i=0;i<4;i++) Gizmos.DrawLine(p[i],p[(i+1)%4]);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(c + up, rackId);
#endif
        }
    }
}
