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
    public float openAngleDeg = 45f;            // your requirement
    public float closedAngleDeg = 0f;
    public float speed = 16f;

    [Header("Logging")]
    public PegTransferHapticLogger3DS logger;
    public int deviceIndex; // 0 or 1

    bool wasGrabbingActive = false;

    float currentAngle;
    GameObject lastGrabbed;

    void Awake()
    {
        if (!haptic) haptic = GetComponent<HapticPlugin>();
    }

    void Update()
    {
        DriveJaws();
        EnforceExclusiveGrab();
    }

    void DriveJaws()
    {
        if (!haptic || !upperJaw || !lowerJaw) return;

        bool closing = haptic.bIsGrabbing || haptic.bIsGrabbingActive;
        float target = closing ? closedAngleDeg : openAngleDeg;

        currentAngle = Mathf.MoveTowards(currentAngle, target, speed * Time.deltaTime * 60f);

        Quaternion qUp = Quaternion.AngleAxis(+currentAngle, localAxis.normalized);
        Quaternion qDn = Quaternion.AngleAxis(-currentAngle, localAxis.normalized);

        upperJaw.localRotation = qUp;
        lowerJaw.localRotation = qDn;
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
