using UnityEngine;

public class main : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        LuaMgr.Instance.Init();
        LuaMgr.Instance.DoString("Main");
    }
}
