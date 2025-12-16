using UnityEngine;

public class DualGrasperTool : MonoBehaviour
{
    [Header("Plugin")]
    public HapticPlugin haptic;

    [Header("Jaws")]
    public Transform upperJaw;   // grasper_up
    public Transform lowerJaw;   // grasper_down

    [Header("Jaw motion")]
    public Vector3 localAxis = Vector3.forward; // change if needed
    public float openAngleDeg = 45f;
    public float closedAngleDeg = 0f;
    public float speed = 16f;

    [Header("External joystick control (Legacy Input Manager)")]
    public string joystickAxisName = "Grip";  // Project Settings > Input Manager axis name
    public bool invertAxis = false;
    [Range(0f, 1f)] public float deadzone = 0.05f;

    [Header("Grab gating")]
    public float grabAngleThresholdDeg = 30f; // under this = grab
    public float releaseAngleThresholdDeg = 33f; // hysteresis to avoid chatter

    [Header("Logging")]
    public PegTransferHapticLogger3DS logger;
    public int deviceIndex; // 0 or 1

    bool wasGrabbingActive = false;
    bool grabCommanded = false;

    float currentAngle;
    GameObject lastGrabbed;

    void Awake()
    {
        if (!haptic) haptic = GetComponent<HapticPlugin>();
        currentAngle = openAngleDeg;
    }

    void Update()
    {
        DriveJawsFromJoystick();
        DriveGrabFromAngle();
        EnforceExclusiveGrab();
    }

    float ReadGrip01()
    {
        float v = 0f;
        if (!string.IsNullOrEmpty(joystickAxisName))
        {
            v = Input.GetAxis(joystickAxisName); // expected [-1..+1] or [0..1] depending on setup
        }

        if (invertAxis) v = -v;

        // normalize to [0..1] robustly
        // if axis is [-1..+1], map -> [0..1]; if already [0..1], this still behaves ok
        float grip01 = Mathf.InverseLerp(-1f, 1f, v);

        // deadzone
        if (grip01 < deadzone) grip01 = 0f;
        if (grip01 > 1f - deadzone) grip01 = 1f;

        return grip01;
    }

    void DriveJawsFromJoystick()
    {
        if (!haptic || !upperJaw || !lowerJaw) return;

        float grip01 = ReadGrip01(); // 0=open, 1=closed
        float desiredAngle = Mathf.Lerp(openAngleDeg, closedAngleDeg, grip01);

        currentAngle = Mathf.MoveTowards(currentAngle, desiredAngle, speed * Time.deltaTime * 60f);

        Quaternion qUp = Quaternion.AngleAxis(+currentAngle, localAxis.normalized);
        Quaternion qDn = Quaternion.AngleAxis(-currentAngle, localAxis.normalized);

        upperJaw.localRotation = qUp;
        lowerJaw.localRotation = qDn;
    }

    void DriveGrabFromAngle()
    {
        if (!haptic) return;

        // Hysteresis: grab below grabAngleThresholdDeg, release above releaseAngleThresholdDeg
        bool wantGrab;
        if (!grabCommanded)
            wantGrab = (currentAngle <= grabAngleThresholdDeg);
        else
            wantGrab = (currentAngle <= releaseAngleThresholdDeg);

        if (wantGrab == grabCommanded) return;
        grabCommanded = wantGrab;

        if (grabCommanded)
        {
            // Try common plugin entry points without hard dependency on exact method name
            SendMessage("Hold_Object", SendMessageOptions.DontRequireReceiver);
            SendMessage("Grab_Object", SendMessageOptions.DontRequireReceiver);
            // Some plugins expose a public method on haptic directly; keep Release_Object usage symmetric
            // If your plugin uses a different method name, add another SendMessage here.
        }
        else
        {
            // You already use this in your current script
            haptic.Release_Object();
        }
    }

    void EnforceExclusiveGrab()
    {
        if (!haptic) return;

        bool isGrabbingNow = haptic.bIsGrabbingActive && haptic.GrabObject != null;

        // --- GRAB BEGIN ---
        if (isGrabbingNow && !wasGrabbingActive)
        {
            GameObject obj = haptic.GrabObject;
            int ringId = obj.GetInstanceID();

            if (logger)
            {
                logger.SetHolding(deviceIndex, true, ringId);
                logger.LogEvent(deviceIndex, PegTransferHapticLogger3DS.EventCode.GRAB_BEGIN);
            }
        }

        // --- GRAB END ---
        if (!isGrabbingNow && wasGrabbingActive)
        {
            if (logger)
            {
                logger.SetHolding(deviceIndex, false, -1);
                logger.LogEvent(deviceIndex, PegTransferHapticLogger3DS.EventCode.GRAB_END);
            }
        }

        wasGrabbingActive = isGrabbingNow;

        // ---------------- EXISTING EXCLUSIVE-LOCK LOGIC ----------------
        if (!isGrabbingNow)
        {
            if (lastGrabbed != null)
            {
                var lockComp = lastGrabbed.GetComponent<SharedGrabLock>();
                if (lockComp && lockComp.holder == haptic)
                    lockComp.holder = null;

                lastGrabbed = null;
            }
            return;
        }

        GameObject objNow = haptic.GrabObject;

        var grabLock = objNow.GetComponent<SharedGrabLock>();
        if (!grabLock) grabLock = objNow.AddComponent<SharedGrabLock>();

        if (grabLock.holder != null && grabLock.holder != haptic)
        {
            grabLock.holder.Release_Object();
            grabLock.holder = null;
        }

        grabLock.holder = haptic;
        lastGrabbed = objNow;
    }
}
