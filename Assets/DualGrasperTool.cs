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

        // If not actively holding anything, clear lock if we were holder
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

        // If the other tool already holds this object, force it to release
        if (grabLock.holder != null && grabLock.holder != haptic)
        {
            grabLock.holder.Release_Object();   // calls plugin release path
            grabLock.holder = null;
        }

        // Claim ownership
        grabLock.holder = haptic;
        lastGrabbed = obj;
    }
}
