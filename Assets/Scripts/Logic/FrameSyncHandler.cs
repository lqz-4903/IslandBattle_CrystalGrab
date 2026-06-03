using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FrameSyncHandler : MonoBehaviour
{
    private HostServer _host;
    public FrameSyncHandler(HostServer host)
    {
        _host = host;
    }
}
