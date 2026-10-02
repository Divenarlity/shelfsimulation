using System;
using System.Linq;
using UnityEngine;

public enum DualShelfScanState {
    Initializing,
    Moving,
    Aligning,
    Settling,
    CapturingDualView,
    DualInference,
    DualResultReady,
    StationComplete,
    MovingToNextAisle,
    Completed,
    Error
}

[Serializable]
public sealed class DualShelfScanStation {
    public string stationId;
    public Transform pose;
    [TextArea] public string expectedLeftShelves;
    [TextArea] public string expectedRightShelves;
}

[Serializable]
public sealed class DualShelfAislePlan {
    public string aisleId;
    public DualShelfScanStation[] stations;
    public Transform[] transitionToNextAisle;
}

[Serializable]
public sealed class DualShelfSideRecord {
    public string sourceId;
    public string frameId;
    public string parentShelfIds;
    public string shelfLevelIds;
    public int emptySpaceCount;
    public int attempts;
    public float durationSeconds;
    public bool succeeded;
    public bool visualizationDisplayed;
    public string message;
}

[DisallowMultipleComponent]
[RequireComponent(typeof(ShelfLocationBridge), typeof(ShelfInferenceClient))]
public sealed class DualShelfScanController : MonoBehaviour {
    // One station owns one frozen-pose dual capture, one HTTP request, and one
    // completion event. The controller never starts independent side requests.
    [Header("Rig cameras")]
    public Transform cameraMount;
    public Camera leftCamera;
    public Camera rightCamera;
    public ShelfInferenceClient inferenceClient;

    [Header("Semantic route")]
    public DualShelfAislePlan[] aisles;
    public bool autoRun = true;
    public bool loopAfterComplete;
    [Min(0)] public int startAisleIndex;
    [Min(0)] public int startStationIndex;
    public bool pauseSimulation;

    [Header("Motion")]
    [Min(.05f)] public float moveSpeed = 1.4f;
    [Min(1f)] public float turnSpeedDegrees = 150f;
    [Min(.001f)] public float positionTolerance = .015f;
    [Min(.1f)] public float angleToleranceDegrees = .5f;
    [Min(0f)] public float settleSeconds = .45f;

    [Header("Presentation performance")]
    [Min(1)] public int targetFrameRate = 60;

    [Header("Recovery")]
    [Min(0)] public int maxInferenceRetries = 2;
    public bool continueAfterSideFailure = true;

    public DualShelfScanState State { get; private set; } = DualShelfScanState.Initializing;
    public bool IsPaused => pauseSimulation;
    public int CurrentAisleIndex { get; private set; }
    public int CurrentStationIndex { get; private set; }
    public DualShelfSideRecord LastLeftResult { get; private set; }
    public DualShelfSideRecord LastRightResult { get; private set; }
    public string SessionId { get; private set; }
    public int SessionGenerationCount { get; private set; }
    public bool SessionCompletionRequested { get; private set; }

    float fixedPlatformY;
    float settleUntil;
    int transitionIndex;
    int currentAttempt;
    Vector3 heldPosition;
    Quaternion heldRotation;
    Vector3 mountLocalPosition;
    Quaternion mountLocalRotation;
    Vector3 leftLocalPosition;
    Quaternion leftLocalRotation;
    Vector3 rightLocalPosition;
    Quaternion rightLocalRotation;
    bool scanRequestPending;
    bool stationPoseStable;
    DualShelfInferenceResponse lastDualResponse;

    DualShelfScanStation CurrentStation =>
        RouteValid && CurrentStationIndex >= 0 &&
        CurrentStationIndex < aisles[CurrentAisleIndex].stations.Length
            ? aisles[CurrentAisleIndex].stations[CurrentStationIndex] : null;
    bool RouteValid => aisles != null && aisles.Length > 0 &&
        CurrentAisleIndex >= 0 && CurrentAisleIndex < aisles.Length &&
        aisles[CurrentAisleIndex] != null && aisles[CurrentAisleIndex].stations != null;

    void Awake() {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Mathf.Max(1, targetFrameRate);
        Debug.Log($"[DualShelfScan] targetFrameRate={Application.targetFrameRate} vSyncCount={QualitySettings.vSyncCount}");
        if (inferenceClient == null) inferenceClient = GetComponent<ShelfInferenceClient>();
        fixedPlatformY = transform.position.y;
        if (cameraMount != null) {
            mountLocalPosition = cameraMount.localPosition;
            mountLocalRotation = cameraMount.localRotation;
        }
        if (leftCamera != null) {
            leftLocalPosition = leftCamera.transform.localPosition;
            leftLocalRotation = leftCamera.transform.localRotation;
        }
        if (rightCamera != null) {
            rightLocalPosition = rightCamera.transform.localPosition;
            rightLocalRotation = rightCamera.transform.localRotation;
        }
    }

    void OnEnable() {
        if (inferenceClient != null)
            inferenceClient.DualInferenceCycleCompleted += OnDualInferenceCycleCompleted;
    }

    void OnDisable() {
        if (inferenceClient != null)
            inferenceClient.DualInferenceCycleCompleted -= OnDualInferenceCycleCompleted;
    }

    void Start() {
        if (inferenceClient != null && inferenceClient.hud != null)
            inferenceClient.hud.EnableDualCameraMode(true);
        if (autoRun) RestartRoute();
    }

    public void RestartRoute() {
        if (!ValidateConfiguration(out string error)) {
            Fail(error);
            return;
        }
        CurrentAisleIndex = Mathf.Clamp(startAisleIndex, 0, aisles.Length - 1);
        CurrentStationIndex = Mathf.Clamp(
            startStationIndex, 0, aisles[CurrentAisleIndex].stations.Length - 1);
        transitionIndex = 0;
        currentAttempt = 0;
        scanRequestPending = false;
        SessionId = CreateSessionId(DateTime.Now, Guid.NewGuid().ToString("N")[..8]);
        SessionGenerationCount++;
        SessionCompletionRequested = false;
        LastLeftResult = null;
        LastRightResult = null;
        lastDualResponse = null;
        SetState(DualShelfScanState.Initializing);
    }

    public void PauseRoute() => pauseSimulation = true;
    public void ResumeRoute() => pauseSimulation = false;

    void Update() {
        EnforceFixedCameraMounts();
        if (State == DualShelfScanState.Error || State == DualShelfScanState.Completed) return;
        if (State == DualShelfScanState.CapturingDualView ||
            State == DualShelfScanState.DualInference ||
            State == DualShelfScanState.DualResultReady)
            HoldScanPose();
        if (IsPaused) return;

        switch (State) {
            case DualShelfScanState.Initializing:
                SetState(DualShelfScanState.Moving);
                break;
            case DualShelfScanState.Moving:
                if (MoveTo(CurrentStation.pose, true))
                    SetState(DualShelfScanState.Aligning);
                break;
            case DualShelfScanState.Aligning:
                if (AlignTo(CurrentStation.pose.rotation)) {
                    settleUntil = Time.realtimeSinceStartup + settleSeconds;
                    SetState(DualShelfScanState.Settling);
                }
                break;
            case DualShelfScanState.Settling:
                if (Time.realtimeSinceStartup >= settleUntil) BeginStation();
                break;
            case DualShelfScanState.CapturingDualView:
                if (!scanRequestPending) RequestDualCycle();
                break;
            case DualShelfScanState.DualResultReady:
                LogStationReport(stationPoseStable);
                Debug.Log($"[DualScan] station complete id={CurrentStation.stationId}");
                SetState(DualShelfScanState.StationComplete);
                break;
            case DualShelfScanState.StationComplete:
                AdvanceAfterStation();
                break;
            case DualShelfScanState.MovingToNextAisle:
                UpdateAisleTransition();
                break;
        }
    }

    void BeginStation() {
        heldPosition = transform.position;
        heldRotation = transform.rotation;
        stationPoseStable = true;
        LastLeftResult = null;
        LastRightResult = null;
        lastDualResponse = null;
        currentAttempt = 0;
        scanRequestPending = false;
        SetState(DualShelfScanState.CapturingDualView);
    }

    void RequestDualCycle() {
        currentAttempt++;
        string cycleId = $"scan_cycle_{CurrentStation.stationId}_{currentAttempt:D2}";
        scanRequestPending = inferenceClient.RequestDualInferenceOnce(
            leftCamera, rightCamera, cycleId, SessionId, CurrentStation.stationId);
        if (!scanRequestPending) {
            currentAttempt--;
            return;
        }
        Debug.Log(
            $"[DualShelfScan] station={CurrentStation.stationId} dualAttempt=" +
            $"{currentAttempt}/{maxInferenceRetries + 1} request=started");
        SetState(DualShelfScanState.DualInference);
    }

    void OnDualInferenceCycleCompleted(DualShelfInferenceCycleResult result) {
        if (State != DualShelfScanState.DualInference) return;
        scanRequestPending = false;
        stationPoseStable &= PoseIsHeld() && result.stationaryPosePreserved;
        if (!result.succeeded && currentAttempt <= maxInferenceRetries) {
            Debug.LogWarning(
                $"[DualShelfScan] station={CurrentStation.stationId} attempt={currentAttempt} " +
                $"failed; retrying the complete dual scan | {result.message}");
            SetState(DualShelfScanState.CapturingDualView);
            return;
        }
        if (!result.succeeded && !continueAfterSideFailure) {
            Fail($"{CurrentStation.stationId} dual scan failed after {currentAttempt} attempts: {result.message}");
            return;
        }
        LastLeftResult = BuildRecord(
            "LEFT", result.leftMetadata, result.response != null ? result.response.left : null,
            currentAttempt, result.durationSeconds, result.succeeded,
            result.leftVisualizationDisplayed, result.message);
        LastRightResult = BuildRecord(
            "RIGHT", result.rightMetadata, result.response != null ? result.response.right : null,
            currentAttempt, result.durationSeconds, result.succeeded,
            result.rightVisualizationDisplayed, result.message);
        lastDualResponse = result.response;
        SetState(DualShelfScanState.DualResultReady);
    }

    static DualShelfSideRecord BuildRecord(
        string sourceId, FrameInputData metadata, ShelfInferenceResponse response,
        int attempts, float durationSeconds, bool succeeded,
        bool visualizationDisplayed, string message) {
        ShelfInferenceShelf[] shelves = response != null && response.shelves != null
            ? response.shelves.Where(shelf => shelf != null && shelf.shelf_id != "UNKNOWN_SHELF").ToArray()
            : Array.Empty<ShelfInferenceShelf>();
        return new DualShelfSideRecord {
            sourceId = sourceId,
            frameId = response != null ? response.frame_id : metadata != null ? metadata.frame_id : null,
            parentShelfIds = string.Join(",", shelves
                .Select(shelf => shelf.parent_shelf_id)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().OrderBy(value => value)),
            shelfLevelIds = string.Join(",", shelves
                .Select(shelf => !string.IsNullOrWhiteSpace(shelf.shelf_level_id)
                    ? shelf.shelf_level_id : shelf.shelf_id)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().OrderBy(value => value)),
            emptySpaceCount = shelves.Sum(shelf => shelf.empty_space_count),
            attempts = attempts,
            durationSeconds = durationSeconds,
            succeeded = succeeded,
            visualizationDisplayed = visualizationDisplayed,
            message = message
        };
    }

    void AdvanceAfterStation() {
        DualShelfAislePlan aisle = aisles[CurrentAisleIndex];
        if (CurrentStationIndex + 1 < aisle.stations.Length) {
            CurrentStationIndex++;
            SetState(DualShelfScanState.Moving);
            return;
        }
        if (CurrentAisleIndex + 1 < aisles.Length) {
            transitionIndex = 0;
            SetState(DualShelfScanState.MovingToNextAisle);
            return;
        }
        if (loopAfterComplete) {
            CompleteSessionOnce();
            CurrentAisleIndex = 0;
            CurrentStationIndex = 0;
            SessionId = CreateSessionId(DateTime.Now, Guid.NewGuid().ToString("N")[..8]);
            SessionGenerationCount++;
            SessionCompletionRequested = false;
            SetState(DualShelfScanState.Moving);
        } else SetState(DualShelfScanState.Completed);
    }

    void UpdateAisleTransition() {
        Transform[] route = aisles[CurrentAisleIndex].transitionToNextAisle;
        if (route != null && transitionIndex < route.Length) {
            if (MoveTo(route[transitionIndex], false)) transitionIndex++;
            return;
        }
        CurrentAisleIndex++;
        CurrentStationIndex = 0;
        SetState(DualShelfScanState.Moving);
    }

    bool MoveTo(Transform target, bool leaveFinalAlignmentForNextState) {
        if (target == null) { Fail("Route contains a missing pose transform."); return false; }
        Vector3 destination = target.position;
        destination.y = fixedPlatformY;
        Vector3 delta = destination - transform.position;
        delta.y = 0f;
        if (delta.magnitude > positionTolerance) {
            Quaternion travelRotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, travelRotation, turnSpeedDegrees * Time.deltaTime);
            transform.position = Vector3.MoveTowards(
                transform.position, destination, moveSpeed * Time.deltaTime);
            transform.position = new Vector3(transform.position.x, fixedPlatformY, transform.position.z);
            return false;
        }
        transform.position = destination;
        if (!leaveFinalAlignmentForNextState)
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target.rotation, turnSpeedDegrees * Time.deltaTime);
        return leaveFinalAlignmentForNextState ||
            Quaternion.Angle(transform.rotation, target.rotation) <= angleToleranceDegrees;
    }

    bool AlignTo(Quaternion targetRotation) {
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRotation, turnSpeedDegrees * Time.deltaTime);
        return Quaternion.Angle(transform.rotation, targetRotation) <= angleToleranceDegrees;
    }

    void HoldScanPose() => transform.SetPositionAndRotation(heldPosition, heldRotation);
    bool PoseIsHeld() => Vector3.Distance(transform.position, heldPosition) <= .0001f &&
        Quaternion.Angle(transform.rotation, heldRotation) <= .001f;

    void EnforceFixedCameraMounts() {
        if (cameraMount != null)
            cameraMount.SetLocalPositionAndRotation(mountLocalPosition, mountLocalRotation);
        if (leftCamera != null)
            leftCamera.transform.SetLocalPositionAndRotation(leftLocalPosition, leftLocalRotation);
        if (rightCamera != null)
            rightCamera.transform.SetLocalPositionAndRotation(rightLocalPosition, rightLocalRotation);
    }

    void SetState(DualShelfScanState next) {
        if (State == next) return;
        if (!IsLegalTransition(State, next)) {
            Fail($"Illegal scan state transition {State} -> {next}.");
            return;
        }
        State = next;
        if (State == DualShelfScanState.Completed) CompleteSessionOnce();
        if (inferenceClient != null && inferenceClient.hud != null)
            inferenceClient.hud.SetScanStatus(
                CurrentStation != null ? $"{State} • {CurrentStation.stationId}" : State.ToString());
        Debug.Log($"[DualShelfScan] state={State} aisle={CurrentAisleIndex} station={CurrentStationIndex}");
    }

    void CompleteSessionOnce() {
        if (SessionCompletionRequested || string.IsNullOrWhiteSpace(SessionId)) return;
        SessionCompletionRequested = true;
        if (inferenceClient != null)
            inferenceClient.RequestSessionCompletionOnce(SessionId);
    }

    public static string CreateSessionId(DateTime localTime, string uniqueSuffix) {
        string suffix = string.IsNullOrWhiteSpace(uniqueSuffix)
            ? "route" : uniqueSuffix.Trim();
        return $"session_{localTime:yyyyMMdd_HHmmss}_{suffix}";
    }

    public static bool IsLegalTransition(DualShelfScanState from, DualShelfScanState to) {
        if (from == to || to == DualShelfScanState.Error || to == DualShelfScanState.Initializing)
            return true;
        return from switch {
            DualShelfScanState.Initializing => to == DualShelfScanState.Moving,
            DualShelfScanState.Moving => to == DualShelfScanState.Aligning,
            DualShelfScanState.Aligning => to == DualShelfScanState.Settling,
            DualShelfScanState.Settling => to == DualShelfScanState.CapturingDualView,
            DualShelfScanState.CapturingDualView => to == DualShelfScanState.DualInference,
            DualShelfScanState.DualInference =>
                to == DualShelfScanState.CapturingDualView ||
                to == DualShelfScanState.DualResultReady,
            DualShelfScanState.DualResultReady => to == DualShelfScanState.StationComplete,
            DualShelfScanState.StationComplete =>
                to == DualShelfScanState.Moving ||
                to == DualShelfScanState.MovingToNextAisle ||
                to == DualShelfScanState.Completed,
            DualShelfScanState.MovingToNextAisle => to == DualShelfScanState.Moving,
            _ => false
        };
    }

    void LogStationReport(bool poseStable) {
        DualShelfInferenceCounts batch = lastDualResponse != null
            ? lastDualResponse.counts : null;
        DualShelfInferenceTiming timing = lastDualResponse != null
            ? lastDualResponse.timing : null;
        Debug.Log(
            $"[DualShelfScanReport] station={CurrentStation.stationId} " +
            $"position={transform.position:F3} platformYaw={transform.eulerAngles.y:F2} " +
            $"leftYaw={leftCamera.transform.eulerAngles.y:F2} rightYaw={rightCamera.transform.eulerAngles.y:F2} " +
            $"poseStable={poseStable} | {FormatRecord(LastLeftResult)} | {FormatRecord(LastRightResult)} | " +
            $"shelfBatch={(batch != null ? batch.shelf_model_inputs : 0)} " +
            $"emptyBatch={(batch != null ? batch.total_empty_inputs : 0)} " +
            $"shelfCalls={(batch != null ? batch.shelf_model_predict_calls : 0)} " +
            $"emptyCalls={(batch != null ? batch.empty_model_predict_calls : 0)} " +
            $"dualMs={(timing != null ? timing.dual_total_ms : 0f):F1} " +
            $"saveMs={(lastDualResponse?.persistence != null ? lastDualResponse.persistence.save_ms : 0f):F1} " +
            $"saved={lastDualResponse?.persistence?.saved}");
    }

    static string FormatRecord(DualShelfSideRecord value) => value == null ? "missing" :
        $"{value.sourceId}:frame={value.frameId},parents=[{value.parentShelfIds}]," +
        $"levels=[{value.shelfLevelIds}],gaps={value.emptySpaceCount}," +
        $"attempts={value.attempts},duration={value.durationSeconds:F3}s," +
        $"success={value.succeeded},visualization={value.visualizationDisplayed}";

    bool ValidateConfiguration(out string error) {
        error = null;
        if (inferenceClient == null || cameraMount == null || leftCamera == null || rightCamera == null) {
            error = "Inference client, CameraMount, LeftCamera, and RightCamera are required.";
            return false;
        }
        if (leftCamera.transform.parent != cameraMount || rightCamera.transform.parent != cameraMount) {
            error = "Both cameras must be direct children of CameraMount.";
            return false;
        }
        if (Vector3.Angle(leftCamera.transform.forward, rightCamera.transform.forward) < 179f) {
            error = "Left and right cameras are not back-to-back.";
            return false;
        }
        if (aisles == null || aisles.Length < 2 || aisles.Any(aisle => aisle == null ||
            aisle.stations == null || aisle.stations.Length < 3 ||
            aisle.stations.Any(station => station == null || station.pose == null))) {
            error = "At least two aisles with three valid semantic stations each are required.";
            return false;
        }
        return true;
    }

    void Fail(string message) {
        State = DualShelfScanState.Error;
        Debug.LogError("[DualShelfScan] " + message);
    }

    void OnDrawGizmosSelected() {
        if (leftCamera != null) {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(leftCamera.transform.position, leftCamera.transform.forward * 1.2f);
        }
        if (rightCamera != null) {
            Gizmos.color = Color.magenta;
            Gizmos.DrawRay(rightCamera.transform.position, rightCamera.transform.forward * 1.2f);
        }
        if (aisles == null) return;
        foreach (DualShelfAislePlan aisle in aisles) {
            if (aisle == null || aisle.stations == null) continue;
            Gizmos.color = new Color(.1f, .8f, 1f, .85f);
            for (int i = 0; i < aisle.stations.Length; i++) {
                Transform pose = aisle.stations[i]?.pose;
                if (pose == null) continue;
                Gizmos.DrawWireSphere(pose.position + Vector3.up * .06f, .12f);
                Gizmos.DrawRay(pose.position, pose.forward * .4f);
                if (i > 0 && aisle.stations[i - 1]?.pose != null)
                    Gizmos.DrawLine(aisle.stations[i - 1].pose.position, pose.position);
#if UNITY_EDITOR
                UnityEditor.Handles.Label(
                    pose.position + Vector3.up * .25f,
                    $"{aisle.aisleId}/{aisle.stations[i].stationId}\n" +
                    $"L: {aisle.stations[i].expectedLeftShelves}\n" +
                    $"R: {aisle.stations[i].expectedRightShelves}");
#endif
            }
            if (aisle.transitionToNextAisle == null) continue;
            Gizmos.color = new Color(1f, .72f, .1f, .9f);
            for (int i = 0; i < aisle.transitionToNextAisle.Length; i++) {
                Transform pose = aisle.transitionToNextAisle[i];
                if (pose == null) continue;
                Gizmos.DrawWireCube(pose.position + Vector3.up * .06f, Vector3.one * .18f);
                if (i > 0 && aisle.transitionToNextAisle[i - 1] != null)
                    Gizmos.DrawLine(aisle.transitionToNextAisle[i - 1].position, pose.position);
#if UNITY_EDITOR
                UnityEditor.Handles.Label(pose.position + Vector3.up * .25f, pose.name);
#endif
            }
        }
    }
}
