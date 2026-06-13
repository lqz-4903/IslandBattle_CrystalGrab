using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameMgr : MonoBehaviour
{
    private static GameMgr _instance;
    public static GameMgr Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("GameMgr");
                _instance = go.AddComponent<GameMgr>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    private XLua.LuaFunction luaUpdate;
    private XLua.LuaFunction luaLateUpdate;
    private XLua.LuaFunction luaOnSceneLoaded;

    void Awake()
    {
        // 单例检测：如果已存在实例（来自之前的场景），销毁当前场景中的重复对象
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        // 日志写入文件（Logs/aw.txt），方便诊断帧同步问题
        Application.logMessageReceived += OnLogMessageReceived;

        // 从代码创建 Canvas / EventSystem / UICamera（幂等，仅首次生效）
        SceneMgr.Instance.Init();
    }

    void Start()
    {
        LuaMgr.Instance.Init();
        LuaMgr.Instance.DoString("Main");

        // 提前初始化 GameTimerManager，确保场景切换后能接收服务端倒计时消息
        _ = GameTimerManager.Instance;

        // 获取Lua侧函数引用
        luaOnSceneLoaded = LuaMgr.Instance.Global.Get<XLua.LuaFunction>("OnSceneLoaded");
        luaUpdate = LuaMgr.Instance.Global.Get<XLua.LuaFunction>("Update");
        luaLateUpdate = LuaMgr.Instance.Global.Get<XLua.LuaFunction>("LateUpdate");

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

    private float _luaGCTimer;

    void Update()
    {
        // 每帧驱动Lua侧的Update，传入deltaTime
        if (luaUpdate != null)
            luaUpdate.Action(Time.deltaTime);

        // Lua GC 每秒触发一次（而非每帧），减少 GC 压力
        _luaGCTimer += Time.deltaTime;
        if (_luaGCTimer >= 1.0f)
        {
            _luaGCTimer -= 1.0f;
            LuaMgr.Instance.Tick();
        }
    }

    /// <summary>
    /// LateUpdate：在所有 Update 之后调用，用于渲染层（插值/摄像机）
    ///   确保物理回退（TickExecutor.Update）已完成，插值读到正确的 prevPos/targetPos
    /// </summary>
    void LateUpdate()
    {
        if (luaLateUpdate != null)
            luaLateUpdate.Action(Time.deltaTime);
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLogMessageReceived;
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

    private static readonly string LogPath = string.Format("Logs/aw_{0}.txt",
        System.Diagnostics.Process.GetCurrentProcess().Id);

    private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        try
        {
            string time = System.DateTime.Now.ToString("HH:mm:ss.fff");
            File.AppendAllText(LogPath, string.Format("[{0}] {1}\n", time, condition));
        }
        catch { }
    }
}
