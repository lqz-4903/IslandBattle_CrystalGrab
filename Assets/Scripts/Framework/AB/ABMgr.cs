using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// AssetBundle 管理器（单例）
/// 负责 AB 包的加载、资源读取、依赖管理以及卸载。
/// 外部通过 ABMgr.Instance 访问，无需手动创建实例。
/// </summary>
public class ABMgr
{
    #region 单例与协程宿主

    /// <summary>
    /// 协程宿主：异步加载需要挂在 MonoBehaviour 上才能跑协程。
    /// 外部可通过 Init() 手动指定，否则在首次异步加载时自动创建。
    /// </summary>
    private MonoBehaviour MB;

    /// <summary>
    /// 饿汉式单例实例，在类加载时就完成初始化。
    /// </summary>
    private static ABMgr instance = new ABMgr();

    /// <summary>
    /// 对外暴露的单例访问属性。
    /// </summary>
    public static ABMgr Instance => instance;

    /// <summary>
    /// 私有构造函数，防止外部 new。
    /// </summary>
    private ABMgr() { }

    /// <summary>
    /// 可选初始化方法：手动指定一个 MonoBehaviour 作为协程宿主。
    /// 通常传入你的 GameManager 或任何不会被销毁的 MonoBehaviour。
    /// 不调用也没关系，ABMgr 会在第一次异步加载时自动创建一个宿主 GameObject。
    /// </summary>
    /// <param name="coroutineRunner">协程宿主 MonoBehaviour</param>
    public void Init(MonoBehaviour coroutineRunner)
    {
        MB = coroutineRunner;
    }

    /// <summary>
    /// 确保协程宿主可用。如果尚未初始化，则自动创建一个 DontDestroyOnLoad 的
    /// 空 GameObject 挂载一个内部 MonoBehaviour（ABMgrRunner）来承载协程。
    /// </summary>
    /// <returns>可用的协程宿主</returns>
    private MonoBehaviour EnsureRunner()
    {
        if (MB != null) return MB;

        // 自动创建协程宿主 GameObject，挂上一个空的 MonoBehaviour
        var go = new GameObject(nameof(ABMgr) + "_Runner");
        Object.DontDestroyOnLoad(go);   // 切换场景时不销毁
        MB = go.AddComponent<ABMgrRunner>();
        return MB;
    }

    /// <summary>
    /// 内部空 MonoBehaviour，仅用于承载协程，无其他逻辑。
    /// </summary>
    private sealed class ABMgrRunner : MonoBehaviour { }

    #endregion

    #region 异步安全计数器

    /// <summary>
    /// 当前正在进行的异步加载数量。
    /// 用于在 ClearAB() 时判断是否可以安全卸载——
    /// 如果异步加载还在跑就直接 UnloadAll 会导致 Unity 报警告甚至崩溃。
    /// </summary>
    private int _pendingAsyncLoads = 0;

    /// <summary>
    /// 标记是否已经有延迟卸载的协程在排队。
    /// 防止多次调用 ClearAB() 时重复启动 ClearABWhenSafe 协程。
    /// </summary>
    private bool _clearQueued = false;

    #endregion

    #region 主包与依赖管理

    /// <summary>
    /// 主包（Manifest 包），名称根据平台不同而不同（Android / iOS / PC）。
    /// 通过主包可以获取所有 AB 包之间的依赖关系。
    /// </summary>
    private AssetBundle mainAB = null;

    /// <summary>
    /// 从主包中加载出来的依赖配置文件。
    /// 通过它能查询某个 AB 包依赖了哪些其他 AB 包。
    /// </summary>
    private AssetBundleManifest manifest = null;

    /// <summary>
    /// 缓存已加载的 AB 包。
    /// AssetBundle 不允许重复加载同一个包（会报错），所以用字典做去重。
    /// Key = AB 包名，Value = AssetBundle 实例。
    /// </summary>
    private Dictionary<string, AssetBundle> abDic = new Dictionary<string, AssetBundle>();

    #endregion

    #region 路径与平台配置

    /// <summary>
    /// AB 包存放的根路径，默认放在 StreamingAssets 目录下。
    /// 集中管理方便后续切换到其他路径（如 PersistentDataPath 热更新场景）。
    /// </summary>
    private string PathUrl
    {
        get
        {
            return Application.streamingAssetsPath + "/";
        }
    }

    /// <summary>
    /// 主包名称，根据当前运行平台返回对应的主包文件名。
    /// 需要与你打包 AB 时设置的主包名保持一致。
    /// </summary>
    private string MainABName
    {
        get
        {
#if UNITY_IOS
            return "IOS";
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
            return "PC";
#else
            return "Android";
#endif
        }
    }

    #endregion

    #region AB 包加载（同步）

    /// <summary>
    /// 同步加载指定 AB 包及其所有依赖包。
    /// 流程：
    ///   1. 如果主包未加载，先加载主包并解析依赖配置。
    ///   2. 通过 manifest 查询目标 AB 包的所有依赖，逐个同步加载（已加载则跳过）。
    ///   3. 最后加载目标 AB 包本身。
    /// </summary>
    /// <param name="abName">AB 包文件名（不含路径）</param>
    public void LoadAB(string abName)
    {
        // 第一次调用时，加载主包并取出依赖配置
        if (mainAB == null)
        {
            mainAB = AssetBundle.LoadFromFile(PathUrl + MainABName);
            manifest = mainAB.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        }

        AssetBundle ab = null;

        // 查询并加载该 AB 包的所有依赖包（递归依赖由 Unity 打包时已展开）
        string[] strs = manifest.GetAllDependencies(abName);
        for (int i = 0; i < strs.Length; i++)
        {
            // 字典中不存在说明还没加载过，执行加载并缓存
            if (!abDic.ContainsKey(strs[i]))
            {
                ab = AssetBundle.LoadFromFile(PathUrl + strs[i]);
                abDic.Add(strs[i], ab);
            }
        }

        // 加载目标资源所在的 AB 包本身
        if (!abDic.ContainsKey(abName))
        {
            ab = AssetBundle.LoadFromFile(PathUrl + abName);
            abDic.Add(abName, ab);
        }
    }

    #endregion

    #region 同步加载资源

    /// <summary>
    /// 同步加载资源（不指定类型，按名称查找）。
    /// 如果目标资源是 GameObject 类型，会自动实例化后返回实例；
    /// 否则直接返回资源对象（如材质、贴图、音频等）。
    /// </summary>
    /// <param name="abName">AB 包名</param>
    /// <param name="resName">资源名称</param>
    /// <returns>加载到的资源，GameObject 类型返回实例</returns>
    public Object LoadRes(string abName, string resName)
    {
        // 先确保 AB 包及其依赖已加载
        LoadAB(abName);

        // 从 AB 包中加载资源
        Object obj = abDic[abName].LoadAsset(resName);

        // GameObject 自动实例化，方便外部直接使用
        if (obj is GameObject)
            return Object.Instantiate(obj);
        else
            return obj;
    }

    /// <summary>
    /// 同步加载资源（通过 System.Type 指定类型）。
    /// 适用于资源名重名时需要区分类型，或者只想加载特定子资源的场景。
    /// </summary>
    /// <param name="abName">AB 包名</param>
    /// <param name="resName">资源名称</param>
    /// <param name="type">目标资源类型</param>
    /// <returns>加载到的资源，GameObject 类型返回实例</returns>
    public Object LoadRes(string abName, string resName, System.Type type)
    {
        LoadAB(abName);

        Object obj = abDic[abName].LoadAsset(resName, type);

        if (obj is GameObject)
            return Object.Instantiate(obj);
        else
            return obj;
    }

    /// <summary>
    /// 同步加载资源（泛型版本，编译期类型安全）。
    /// 推荐使用此重载，类型明确且无需额外类型转换。
    /// </summary>
    /// <typeparam name="T">目标资源类型</typeparam>
    /// <param name="abName">AB 包名</param>
    /// <param name="resName">资源名称</param>
    /// <returns>加载到的资源，GameObject 类型返回实例</returns>
    public T LoadRes<T>(string abName, string resName) where T : Object
    {
        LoadAB(abName);

        T obj = abDic[abName].LoadAsset<T>(resName);

        // 注意：泛型版本用 MonoBehaviour.Instantiate 保持返回类型为 T
        if (obj is GameObject)
            return MonoBehaviour.Instantiate(obj);
        else
            return obj;
    }

    #endregion

    #region 异步加载资源

    /*
     * 说明：这里的"异步"指的是从已加载的 AB 包中异步读取资源（AssetBundleRequest），
     * AB 包本身的加载仍然是同步的（LoadFromFile）。
     * 如果需要 AB 包级别的异步加载，可将 LoadFromFile 替换为 AssetBundle.LoadFromFileAsync。
     */

    /// <summary>
    /// 异步加载资源（不指定类型，按名称查找）。
    /// 加载完成后通过回调返回资源。如果资源是 GameObject 会自动实例化。
    /// </summary>
    /// <param name="abName">AB 包名</param>
    /// <param name="resName">资源名称</param>
    /// <param name="callBack">加载完成回调，参数为加载到的资源</param>
    public void LoadResAsync(string abName, string resName, UnityAction<Object> callBack)
    {
        EnsureRunner().StartCoroutine(LoadAsync(abName, resName, callBack));
    }

    /// <summary>
    /// 异步加载资源的协程实现（无类型指定版本）。
    /// </summary>
    private IEnumerator LoadAsync(string abName, string resName, UnityAction<Object> callBack)
    {
        // 同步加载 AB 包（确保包已在内存中）
        LoadAB(abName);

        // 进入异步加载，增加待完成计数器
        _pendingAsyncLoads++;
        AssetBundleRequest abr = abDic[abName].LoadAssetAsync(resName);
        yield return abr;   // 等待异步加载完成
        _pendingAsyncLoads--;

        // 通过回调将结果传递给外部
        if (callBack != null)
        {
            if (abr.asset is GameObject)
                callBack(MonoBehaviour.Instantiate(abr.asset));
            else
                callBack(abr.asset);
        }
    }

    /// <summary>
    /// 异步加载资源（通过 System.Type 指定类型）。
    /// </summary>
    /// <param name="abName">AB 包名</param>
    /// <param name="resName">资源名称</param>
    /// <param name="type">目标资源类型</param>
    /// <param name="callBack">加载完成回调</param>
    public void LoadResAsync(string abName, string resName, System.Type type, UnityAction<Object> callBack)
    {
        EnsureRunner().StartCoroutine(LoadAsync(abName, resName, type, callBack));
    }

    /// <summary>
    /// 异步加载资源的协程实现（Type 版本）。
    /// </summary>
    private IEnumerator LoadAsync(string abName, string resName, System.Type type, UnityAction<Object> callBack)
    {
        LoadAB(abName);

        _pendingAsyncLoads++;
        AssetBundleRequest abr = abDic[abName].LoadAssetAsync(resName, type);
        yield return abr;
        _pendingAsyncLoads--;

        if (callBack != null)
        {
            if (abr.asset is GameObject)
                callBack(MonoBehaviour.Instantiate(abr.asset));
            else
                callBack(abr.asset);
        }
    }

    /// <summary>
    /// 异步加载资源（泛型版本，编译期类型安全，推荐使用）。
    /// </summary>
    /// <typeparam name="T">目标资源类型</typeparam>
    /// <param name="abName">AB 包名</param>
    /// <param name="resName">资源名称</param>
    /// <param name="callBack">加载完成回调，参数类型为 T</param>
    public void LoadResAsync<T>(string abName, string resName, UnityAction<T> callBack) where T : Object
    {
        EnsureRunner().StartCoroutine(LoadAsync<T>(abName, resName, callBack));
    }

    /// <summary>
    /// 异步加载资源的协程实现（泛型版本）。
    /// </summary>
    private IEnumerator LoadAsync<T>(string abName, string resName, UnityAction<T> callBack) where T : Object
    {
        LoadAB(abName);

        _pendingAsyncLoads++;
        AssetBundleRequest abr = abDic[abName].LoadAssetAsync<T>(resName);
        yield return abr;
        _pendingAsyncLoads--;

        if (callBack != null)
        {
            // 泛型版本用 as T 转换，保持类型一致
            if (abr.asset is GameObject)
                callBack(MonoBehaviour.Instantiate(abr.asset) as T);
            else
                callBack(abr.asset as T);
        }
    }

    #endregion

    #region 卸载

    /// <summary>
    /// 单个 AB 包卸载。
    /// Unload(false) 表示只卸载 AB 包本身，已经加载到内存中的资源实例不会被销毁。
    /// 如果传 true，所有从此包加载的资源都会被卸载（引用这些资源的对象会丢失）。
    /// </summary>
    /// <param name="abName">要卸载的 AB 包名</param>
    public void UnLoad(string abName)
    {
        if (abDic.ContainsKey(abName))
        {
            abDic[abName].Unload(false);   // false = 保留已实例化的资源
            abDic.Remove(abName);           // 从缓存字典中移除
        }
    }

    /// <summary>
    /// 卸载所有已加载的 AB 包。
    /// 如果当前有异步加载尚未完成，不会立即执行卸载，
    /// 而是启动一个协程等待异步加载全部完成后再统一卸载，
    /// 避免 Unity 在异步操作进行中调用 UnloadAll 报错。
    /// </summary>
    public void ClearAB()
    {
        // 如果还有异步加载在进行中，延迟到安全时机再卸载
        if (_pendingAsyncLoads > 0)
        {
            // 只启动一次延迟卸载协程，避免重复排队
            if (!_clearQueued)
            {
                _clearQueued = true;
                EnsureRunner().StartCoroutine(ClearABWhenSafe());
            }
            return;
        }

        // 没有异步加载，立即执行卸载
        DoClearAB();
    }

    /// <summary>
    /// 延迟卸载协程：每帧检测异步加载计数，直到全部完成后执行实际卸载。
    /// </summary>
    private IEnumerator ClearABWhenSafe()
    {
        // 挂起等待，直到所有异步加载完成
        while (_pendingAsyncLoads > 0)
            yield return null;

        // 安全时机，执行卸载
        DoClearAB();
        _clearQueued = false;
    }

    /// <summary>
    /// 实际执行卸载逻辑。
    /// UnloadAllAssetBundles(false) 卸载所有 AB 包但保留已加载的资源实例。
    /// 同时清空缓存字典和主包引用，使管理器回到初始状态。
    /// </summary>
    private void DoClearAB()
    {
        AssetBundle.UnloadAllAssetBundles(false);   // 卸载全部 AB 包
        abDic.Clear();          // 清空缓存
        mainAB = null;          // 重置主包引用，下次加载时重新初始化
        manifest = null;        // 重置依赖配置引用
    }

    #endregion
}
