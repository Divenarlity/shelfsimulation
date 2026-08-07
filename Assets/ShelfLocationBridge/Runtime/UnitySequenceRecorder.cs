using UnityEngine;

public class UnitySequenceRecorder : MonoBehaviour {
    public ShelfLocationBridge bridge;
    [Min(1)] public int captureEveryNFrames=15;
    public bool recordSequence;
    int nextFrame;

    void Awake(){if(bridge==null)bridge=FindFirstObjectByType<ShelfLocationBridge>();}
    void Update(){
        if(!recordSequence||bridge==null||Time.frameCount<nextFrame)return;
        nextFrame=Time.frameCount+captureEveryNFrames;
        bridge.CaptureTestFrame();
    }
    public void StartMappingPass(){bridge.sessionId="mapping_pass_01";recordSequence=true;}
    public void StartLocalizationPass(){bridge.sessionId="localization_pass_01";recordSequence=true;}
    public void StopRecording(){recordSequence=false;}
}
