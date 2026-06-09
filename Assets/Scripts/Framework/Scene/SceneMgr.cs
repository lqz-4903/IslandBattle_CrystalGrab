using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 场景管理器（单例）
/// 负责场景切换、持久UI对象的代码创建与管理
/// </summary>
public class SceneMgr : MonoBehaviour
{
    private static SceneMgr _instance;
    public static SceneMgr Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("SceneMgr");
                _instance = go.AddComponent<SceneMgr>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    private Canvas _canvas;
    private EventSystem _eventSystem;
    private Camera _uiCamera;
    private bool _initialized = false;

    /// <summary>
    /// 从代码创建持久UI对象（Canvas / EventSystem / UICamera）
    /// 幂等：首次调用创建并标记 DontDestroyOnLoad，后续调用直接返回
    /// 这样场景中就不需要放置这些对象，彻底杜绝切场景时产生重复
    /// </summary>
    public void Init()
    {
        if (_initialized)
            return;

        _initialized = true;

        // —— 创建 Canvas ——
        GameObject canvasGo = new GameObject("Canvas");
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.pixelPerfect = false;
        _canvas.sortingOrder = 0;

        // CanvasScaler: 随屏幕缩放，参考分辨率 1920x1080
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;

        // GraphicRaycaster: 接收 UI 事件
        GraphicRaycaster raycaster = canvasGo.AddComponent<GraphicRaycaster>();
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        DontDestroyOnLoad(canvasGo);

        // —— 创建 EventSystem ——
        GameObject esGo = new GameObject("EventSystem");
        _eventSystem = esGo.AddComponent<EventSystem>();
        // StandaloneInputModule: 处理键盘/鼠标输入
        esGo.AddComponent<StandaloneInputModule>();

        DontDestroyOnLoad(esGo);

        // —— 创建 UICamera ——
        GameObject camGo = new GameObject("UICamera");
        _uiCamera = camGo.AddComponent<Camera>();
        _uiCamera.clearFlags = CameraClearFlags.Depth;
        _uiCamera.cullingMask = LayerMask.GetMask("UI");
        _uiCamera.orthographic = false;
        _uiCamera.fieldOfView = 60f;
        _uiCamera.nearClipPlane = 0.3f;
        _uiCamera.farClipPlane = 1000f;
        _uiCamera.depth = 0f;
        _uiCamera.backgroundColor = new Color(0.192f, 0.302f, 0.475f, 0f);

        DontDestroyOnLoad(camGo);
    }

    /// <summary>
    /// 旧的 Init 重载（不再需要传参，内部转发到无参版）
    /// </summary>
    public void Init(Canvas canvas, EventSystem eventSystem, Camera uiCamera)
    {
        Init();
    }

    /// <summary>
    /// 切换场景
    /// </summary>
    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }

    /// <summary>
    /// 异步切换场景
    /// </summary>
    public void LoadSceneAsync(string sceneName)
    {
        StartCoroutine(LoadSceneAsyncCoroutine(sceneName));
    }

    private IEnumerator LoadSceneAsyncCoroutine(string sceneName)
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone)
        {
            yield return null;
        }
    }

    /// <summary>
    /// 获取持久 Canvas
    /// </summary>
    public Canvas Canvas => _canvas;
}
