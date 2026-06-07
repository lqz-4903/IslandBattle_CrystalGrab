using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class GameEntry : MonoBehaviour
{
    private XLua.LuaFunction luaUpdate;
    private XLua.LuaFunction luaOnSceneLoaded;

    [Header("持久化UI引用")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private EventSystem eventSystem;
    [SerializeField] private Camera uiCamera;

    void Awake()
    {
        // 标记自身跨场景不销毁
        DontDestroyOnLoad(gameObject);

        // 初始化场景管理器，标记持久UI对象
        SceneMgr.Instance.Init(canvas, eventSystem, uiCamera);
    }

    void Start()
    {
        LuaMgr.Instance.Init();
        LuaMgr.Instance.DoString("Main");

        // 获取Lua侧函数引用
        luaOnSceneLoaded = LuaMgr.Instance.Global.Get<XLua.LuaFunction>("OnSceneLoaded");
        luaUpdate = LuaMgr.Instance.Global.Get<XLua.LuaFunction>("Update");

        // 注册场景加载回调（切场景时自动调用Lua侧OnSceneLoaded）
        SceneManager.sceneLoaded += OnSceneLoadedCallback;

        // 首次加载也触发一次
        if (luaOnSceneLoaded != null)
            luaOnSceneLoaded.Call();
    }

    /// <summary>
    /// 场景加载完成回调
    /// </summary>
    void OnSceneLoadedCallback(Scene scene, LoadSceneMode mode)
    {
        if (luaOnSceneLoaded != null)
            luaOnSceneLoaded.Call();
    }

    void Update()
    {
        // 每帧驱动Lua侧的Update，传入deltaTime
        if (luaUpdate != null)
            luaUpdate.Action(Time.deltaTime);
    }

    void OnDestroy()
    {
        // 注销场景回调
        SceneManager.sceneLoaded -= OnSceneLoadedCallback;

        if (luaUpdate != null)
        {
            luaUpdate.Dispose();
            luaUpdate = null;
        }
        if (luaOnSceneLoaded != null)
        {
            luaOnSceneLoaded.Dispose();
            luaOnSceneLoaded = null;
        }
    }
}
