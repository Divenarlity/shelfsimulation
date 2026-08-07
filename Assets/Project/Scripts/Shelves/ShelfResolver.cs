using UnityEngine;

public class ShelfResolver : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private Camera robotCamera;

    [Header("Selection Settings")]
    [SerializeField] private float maxDistance = 6f;

    [SerializeField, Range(1f, 89f)]
    private float maxViewAngle = 35f;

    [SerializeField, Range(1f, 89f)]
    private float maxFrontAngle = 80f;

    [Header("Visibility")]
    [SerializeField] private bool checkLineOfSight = false;
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Header("Debug")]
    [SerializeField] private bool showOverlay = true;

    private ShelfIdentity[] shelves;

    public ShelfIdentity CurrentShelf { get; private set; }
    public float CurrentScore { get; private set; }
    public float CurrentDistance { get; private set; }

    private void Awake()
    {
        if (robotCamera == null)
            robotCamera = GetComponentInChildren<Camera>();

        RefreshShelves();
    }

    public void RefreshShelves()
    {
        shelves = FindObjectsByType<ShelfIdentity>(
            FindObjectsSortMode.None
        );
    }

    private void Update()
    {
        ResolveShelf();
    }

    private void ResolveShelf()
    {
        CurrentShelf = null;
        CurrentScore = float.NegativeInfinity;
        CurrentDistance = 0f;

        if (robotCamera == null || shelves == null)
            return;

        Vector3 cameraPosition = robotCamera.transform.position;
        Vector3 cameraForward = robotCamera.transform.forward;

        float minimumViewDot =
            Mathf.Cos(maxViewAngle * Mathf.Deg2Rad);

        float minimumFrontDot =
            Mathf.Cos(maxFrontAngle * Mathf.Deg2Rad);

        Plane[] cameraPlanes =
            GeometryUtility.CalculateFrustumPlanes(robotCamera);

        foreach (ShelfIdentity shelf in shelves)
        {
            if (shelf == null)
                continue;

            Vector3 toShelf =
                shelf.TargetPoint - cameraPosition;

            float distance = toShelf.magnitude;

            if (distance < 0.01f || distance > maxDistance)
                continue;

            Vector3 directionToShelf = toShelf / distance;

            // Kamera rafa yeterince dönük mü?
            float viewDot = Vector3.Dot(
                cameraForward,
                directionToShelf
            );

            if (viewDot < minimumViewDot)
                continue;

            // Rafın ön yüzü kameraya bakıyor mu?
            Vector3 shelfToCamera =
                (cameraPosition - shelf.TargetPoint).normalized;

            float frontDot = Vector3.Dot(
                shelf.FrontDirection,
                shelfToCamera
            );

            if (frontDot < minimumFrontDot)
                continue;

            // Raf kamera görüş alanında mı?
            if (shelf.targetRenderer != null)
            {
                bool insideFrustum =
                    GeometryUtility.TestPlanesAABB(
                        cameraPlanes,
                        shelf.targetRenderer.bounds
                    );

                if (!insideFrustum)
                    continue;
            }

            Vector3 viewportPoint =
                robotCamera.WorldToViewportPoint(
                    shelf.TargetPoint
                );

            if (viewportPoint.z <= 0f ||
                viewportPoint.x < 0f ||
                viewportPoint.x > 1f ||
                viewportPoint.y < 0f ||
                viewportPoint.y > 1f)
            {
                continue;
            }

            // İsteğe bağlı görüş hattı kontrolü
            if (checkLineOfSight)
            {
                bool hitSomething = Physics.Raycast(
                    cameraPosition,
                    directionToShelf,
                    out RaycastHit hit,
                    distance,
                    obstacleMask,
                    QueryTriggerInteraction.Ignore
                );

                if (hitSomething)
                {
                    bool belongsToShelf =
                        hit.transform == shelf.transform ||
                        hit.transform.IsChildOf(shelf.transform);

                    if (!belongsToShelf)
                        continue;
                }
            }

            float angleScore = Mathf.InverseLerp(
                minimumViewDot,
                1f,
                viewDot
            );

            float distanceScore =
                1f - Mathf.Clamp01(distance / maxDistance);

            float frontScore = Mathf.InverseLerp(
                minimumFrontDot,
                1f,
                frontDot
            );

            float totalScore =
                angleScore * 0.65f +
                distanceScore * 0.25f +
                frontScore * 0.10f;

            if (totalScore > CurrentScore)
            {
                CurrentShelf = shelf;
                CurrentScore = totalScore;
                CurrentDistance = distance;
            }
        }

        if (CurrentShelf != null)
        {
            Debug.DrawLine(
                cameraPosition,
                CurrentShelf.TargetPoint,
                Color.green
            );
        }
    }

    private void OnGUI()
    {
        if (!showOverlay)
            return;

        GUIStyle style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleLeft
        };

        style.normal.textColor = Color.white;

        string output;

        if (CurrentShelf == null)
        {
            output =
                "Koridor: -\n" +
                "Raf: BULUNAMADI\n" +
                "Mesafe: -";
        }
        else
        {
            output =
                $"Koridor: {CurrentShelf.aisleId}\n" +
                $"Raf: {CurrentShelf.rackId}\n" +
                $"Mesafe: {CurrentDistance:F2} m";
        }

        GUI.Box(
            new Rect(15, 15, 360, 100),
            output,
            style
        );
    }
}