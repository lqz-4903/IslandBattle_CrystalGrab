using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameEventHandler : MonoBehaviour
{
    private HostServer _host;
    public GameEventHandler(HostServer host)
    {
        _host = host;
    }
}
