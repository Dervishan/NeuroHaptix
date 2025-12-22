// DualGrasperTool.cs
// - Drives jaws from an InputAction float
// - Full open = 60 deg (total opening between jaws), full closed = 0 deg
// - When opening angle < 30 deg: emulate "button held" by setting haptic.bIsGrabbing=true continuously
// - When opening angle >= 30 deg: emulate "button released" by setting haptic.bIsRelease=true
// - Keeps exclusive-grab logic

using UnityEngine;
using UnityEngine.InputSystem;

public class DualGrasperTool : MonoBehaviour
{
    [Header("Plugin")]
    public HapticPlugin haptic;

    [Header("Input (float action)")]
    public InputActionReference jawInput;

    [Tooltip("Raw input value at fully OPEN. Example: -1 for a joystick axis.")]
    public float inputAtOpen = -1f;

    [Tooltip("Raw input value at fully CLOSED. Example: +1 for a joystick axis.")]
    public float inputAtClosed = +1f;

    [Tooltip("If your action is already 0..1, set inputAtOpen=0, inputAtClosed=1.")]
    [Range(0f, 0.2f)] public float inputDeadzone = 0.02f;

    [Header("Jaws")]
    public Transform upperJaw;
    public Transform lowerJaw;

    [Header("Jaw motion")]
    public Vector3 localAxis = Vector3.forward;
    public float openAngleDeg = 60f;      // total opening between jaws
    public float closedAngleDeg = 0f;     // total opening between jaws
    public float speedDegPerSec = 720f;

    [Header("Grab logic")]
    public float grabWhenBelowDeg = 30f;

    float currentOpenDeg;
    GameObject lastGrabbed;

    void Awake()
    {
        if (!haptic) haptic = GetComponent<HapticPlugin>();
    }

    void OnEnable()
    {
        if (jawInput && jawInput.action != null) jawInput.action.Enable();
    }

    void OnDisable()
    {
        if (jawInput && jawInput.action != null) jawInput.action.Disable();
    }

    void FixedUpdate()
    {
        DriveJawsFromInput_Fixed();
        DriveGrabLikeHeldButton_Fixed();
        EnforceExclusiveGrab();
    }

    float ReadNormalizedJaw()
    {
        float v = 0f;
        if (jawInput && jawInput.action != null)
            v = jawInput.action.ReadValue<float>();

        // deadzone on raw value relative to range
        float range = Mathf.Abs(inputAtClosed - inputAtOpen);
        if (range > 1e-6f)
        {
            float centered = Mathf.Abs(v - inputAtOpen) / range;
            if (centered < inputDeadzone) v = inputAtOpen;
        }

        // normalize to 0..1 where 0=open, 1=closed
        float t = Mathf.InverseLerp(inputAtOpen, inputAtClosed, v);
        return Mathf.Clamp01(t);
    }

    void DriveJawsFromInput_Fixed()
    {
        if (!upperJaw || !lowerJaw) return;

        float tClosed = ReadNormalizedJaw(); // 0=open, 1=closed
        float targetOpenDeg = Mathf.Lerp(openAngleDeg, closedAngleDeg, tClosed);

        currentOpenDeg = Mathf.MoveTowards(
            currentOpenDeg,
            targetOpenDeg,
            speedDegPerSec * Time.fixedDeltaTime
        );

        float half = /*0.5f **/ currentOpenDeg;
        Quaternion qUp = Quaternion.AngleAxis(+half, localAxis.normalized);
        Quaternion qDn = Quaternion.AngleAxis(-half, localAxis.normalized);

        upperJaw.localRotation = qUp;
        lowerJaw.localRotation = qDn;
    }

    void DriveGrabLikeHeldButton_Fixed()
    {
        if (!haptic) return;

        // Emulate plugin "hold button" behavior:
        // if held: bIsGrabbing=true, bIsRelease=false
        // else:    bIsGrabbing=false, bIsRelease=true
        bool closedEnough = currentOpenDeg < grabWhenBelowDeg;

        haptic.bIsGrabbing = closedEnough;
        haptic.bIsRelease = !closedEnough;
    }

    void EnforceExclusiveGrab()
    {
        if (!haptic) return;

        if (!haptic.bIsGrabbingActive || haptic.GrabObject == null)
        {
            if (lastGrabbed != null)
            {
                var lockComp = lastGrabbed.GetComponent<SharedGrabLock>();
                if (lockComp && lockComp.holder == haptic) lockComp.holder = null;
                lastGrabbed = null;
            }
            return;
        }

        GameObject obj = haptic.GrabObject;

        var grabLock = obj.GetComponent<SharedGrabLock>();
        if (!grabLock) grabLock = obj.AddComponent<SharedGrabLock>();

        if (grabLock.holder != null && grabLock.holder != haptic)
        {
            grabLock.holder.Release_Object();
            grabLock.holder = null;
        }

        grabLock.holder = haptic;
        lastGrabbed = obj;
    }
}
