using UnityEngine;

public class GameEntry : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        LuaMgr.Instance.Init();
        LuaMgr.Instance.DoString("Main");
        
    }
}
