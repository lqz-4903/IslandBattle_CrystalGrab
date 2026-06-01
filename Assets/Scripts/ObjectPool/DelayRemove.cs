using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DelayRemove : MonoBehaviour
{
    private void OnEnable()
    {
        Invoke("RemoveObj", 1f);
    }

    private void RemoveObj()
    {
        ObjectPoolMgr.Instance.PushObj(this.gameObject);
    }
}
