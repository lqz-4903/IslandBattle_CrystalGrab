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

    private Canvas _canvasStatic;
    private Canvas _canvasMid;
    private Canvas _canvasDynamic;
    private EventSystem _eventSystem;
    private Camera _uiCamera;
    private bool _initialized = false;

    /// <summary>
    /// 从代码创建持久UI对象（3 层 Canvas / EventSystem / UICamera）
    /// 动静分离：Static(0) 永不重建的背景|Mid(1) 偶发变更的大厅|Dynamic(2) 高频 HUD
    /// 幂等：首次调用创建并标记 DontDestroyOnLoad，后续调用直接返回
    /// </summary>
    public void Init()
    {
        if (_initialized)
            return;

        _initialized = true;

        // —— 创建 3 层 Canvas（动静分离）——
        _canvasStatic  = CreateCanvas("Canvas_Static",  0);
        _canvasMid     = CreateCanvas("Canvas_Mid",     1);
        _canvasDynamic = CreateCanvas("Canvas_Dynamic", 2);

        // —— 创建 EventSystem（3 层 Canvas 共享 1 个，Unity 按 sortingOrder 降序遍历 GraphicRaycaster）——
        GameObject esGo = new GameObject("EventSystem");
        _eventSystem = esGo.AddComponent<EventSystem>();
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
    /// 创建一个 ScreenSpaceOverlay Canvas（含 CanvasScaler + GraphicRaycaster）
    /// </summary>
    private Canvas CreateCanvas(string name, int sortingOrder)
    {
        GameObject go = new GameObject(name);
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = false;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;

        GraphicRaycaster raycaster = go.AddComponent<GraphicRaycaster>();
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        DontDestroyOnLoad(go);
        return canvas;
    }

    // 旧重载已移除 —— 改用无参 Init() + Canvas_Static / Canvas_Mid / Canvas_Dynamic 属性

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
    /// 静态层 Canvas（sortingOrder=0）—— 永不修改的背景/信息页
    /// </summary>
    public Canvas Canvas_Static => _canvasStatic;

    /// <summary>
    /// 中间层 Canvas（sortingOrder=1）—— 偶发变更的大厅/弹窗
    /// </summary>
    public Canvas Canvas_Mid => _canvasMid;

    /// <summary>
    /// 动态层 Canvas（sortingOrder=2）—— 高频 per-frame 更新的 HUD
    /// </summary>
    public Canvas Canvas_Dynamic => _canvasDynamic;
}
