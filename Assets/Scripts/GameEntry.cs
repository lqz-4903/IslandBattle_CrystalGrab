using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class main : MonoBehaviour
{
    private void Awake()
    {
        LuaMgr.Instance.Init();
        LuaMgr.Instance.DoString("Main");
    }
    // Start is called before the first frame update
    void Start()
    {
        
    }
}
