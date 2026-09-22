using UnityEngine;

[RequireComponent(typeof(ShelfLocationBridge))]
public class UnitySequenceRecorder : MonoBehaviour {
    public ShelfLocationBridge bridge;
    [Min(1)] public int captureEveryNFrames = 15;
    public bool recordSequence;
    int nextFrame;

    void Awake() {
        if (bridge == null) bridge = GetComponent<ShelfLocationBridge>();
    }

    void Update() {
        if (!recordSequence || bridge == null || Time.frameCount < nextFrame) return;
        nextFrame = Time.frameCount + captureEveryNFrames;
        bridge.CaptureOfflineFrame();
    }

    public void StartRecording() {
        nextFrame = Time.frameCount;
        recordSequence = true;
    }

    public void StopRecording() => recordSequence = false;
}
