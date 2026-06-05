using UnityEngine;
using UnityEngine.EventSystems;

public class GameEntry : MonoBehaviour
{
    private XLua.LuaFunction luaUpdate;

    [Header("持久化UI引用")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private EventSystem eventSystem;
    [SerializeField] private Camera uiCamera;

    void Awake()
    {
        // 初始化场景管理器，标记持久UI对象
        SceneMgr.Instance.Init(canvas, eventSystem, uiCamera);
    }

    void Start()
    {
        LuaMgr.Instance.Init();
        LuaMgr.Instance.DoString("Main");

        // 获取Lua侧的全局Update函数
        luaUpdate = LuaMgr.Instance.Global.Get<XLua.LuaFunction>("Update");
    }

    void Update()
    {
        // 每帧驱动Lua侧的Update，传入deltaTime
        if (luaUpdate != null)
            luaUpdate.Action(Time.deltaTime);
    }

    void OnDestroy()
    {
        if (luaUpdate != null)
        {
            luaUpdate.Dispose();
            luaUpdate = null;
        }
    }
}
