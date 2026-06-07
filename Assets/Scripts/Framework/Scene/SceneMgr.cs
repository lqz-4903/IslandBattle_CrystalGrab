using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景管理器（单例）
/// 负责场景切换、持久UI对象管理
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

    // 持久化UI对象引用
    private Canvas _canvas;
    private EventSystem _eventSystem;
    private Camera _uiCamera;

    /// <summary>
    /// 初始化持久UI对象，在游戏启动时调用一次
    /// </summary>
    public void Init(Canvas canvas, EventSystem eventSystem, Camera uiCamera)
    {
        _canvas = canvas;
        _eventSystem = eventSystem;
        _uiCamera = uiCamera;

        // 标记为跨场景不销毁
        if (_canvas != null) DontDestroyOnLoad(_canvas.gameObject);
        if (_eventSystem != null) DontDestroyOnLoad(_eventSystem.gameObject);
        if (_uiCamera != null) DontDestroyOnLoad(_uiCamera.gameObject);
    }

    /// <summary>
    /// 切换场景
    /// </summary>
    /// <param name="sceneName">目标场景名</param>
    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }

    /// <summary>
    /// 异步切换场景（可加加载界面）
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
            // TODO: 更新加载进度条
            yield return null;
        }
    }

    /// <summary>
    /// 获取持久Canvas
    /// </summary>
    public Canvas Canvas => _canvas;
}
