using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestDataLogger : MonoBehaviour
{

    void Update()
    {
        Vector3 pos = transform.position;
        DataLogger.Instance.Log("position.csv",
            pos.x.ToString("F3"),
            pos.y.ToString("F3"),
            pos.z.ToString("F3"));

        Vector3 rot = transform.rotation.eulerAngles;
        DataLogger.Instance.Log("rotation.csv",
            rot.x.ToString("F3"),
            rot.y.ToString("F3"),
            rot.z.ToString("F3"));
    }
}
