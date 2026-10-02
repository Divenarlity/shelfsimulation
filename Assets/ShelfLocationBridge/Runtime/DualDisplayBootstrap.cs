using UnityEngine;

[DisallowMultipleComponent]
public sealed class DualDisplayBootstrap : MonoBehaviour {
    static bool startupHandled;
    public static int StartupAttemptCount { get; private set; }
    public static bool DisplayTwoActivated { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() {
        startupHandled = false;
        StartupAttemptCount = 0;
        DisplayTwoActivated = false;
    }

    void Awake() {
        ActivateDisplayTwoOnce();
    }

    public static void ActivateDisplayTwoOnce() {
        if (startupHandled) return;
        startupHandled = true;
        StartupAttemptCount++;
        if (Display.displays.Length > 1) {
            Display.displays[1].Activate();
            DisplayTwoActivated = true;
            Debug.Log("[DualDisplay] Display 2 activated for RightCamera.");
        } else {
            Debug.LogWarning(
                "[DualDisplay] Display 2 is unavailable; LeftCamera continues on Display 1.");
        }
    }
}
