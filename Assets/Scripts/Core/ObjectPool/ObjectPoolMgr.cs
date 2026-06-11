using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 池子容器
/// </summary>
public class PoolData
{
    // 抽屉中 对象挂载的父节点
    public GameObject fatherObj;
    // 对象的容器
    public Stack<GameObject> poolStack;

    public PoolData(GameObject obj, GameObject poolObj)
    {
        // 给我们的抽屉 创建一个父对象 并且把它作为我们pool对象的子物体
        fatherObj = new GameObject(obj.name);
        fatherObj.transform.parent = poolObj.transform;
        poolStack = new Stack<GameObject>();
    }

    public GameObject GetObj()
    {
        GameObject obj;
        // 取出对象
        obj = poolStack.Pop();        
        // 激活对象
        obj.SetActive(true);
        // 断开父子关系
        obj.transform.parent = null;

        return obj;
    }


    public void PushObj(GameObject obj)
    {
        // 失活对象 之后再用
        obj.SetActive(false);
        // 往抽屉当中放入对象
        poolStack.Push(obj);
        // 设置父对象为根节点
        obj.transform.parent = fatherObj.transform;
    }
}

/// <summary>
/// 对象池管理器
/// </summary>
public class ObjectPoolMgr
{
    private static ObjectPoolMgr instance = new ObjectPoolMgr();
    public static ObjectPoolMgr Instance => instance;

    private ObjectPoolMgr() { }

    // 对象池容器（衣柜）
    private Dictionary<string, PoolData> poolDic = new Dictionary<string, PoolData>();
    // 对象池父类根节点
    private GameObject poolObj;
    
    /// <summary>
    /// 拿东西方法
    /// </summary>
    /// <param name="name">抽屉容器的名字</param>
    /// <returns>从缓存池中取出对象</returns>
    public GameObject GetObj_Res(string name)
    {
        GameObject obj;
        // 判断有抽屉 有对象 去拿
        if (poolDic.ContainsKey(name) && poolDic[name].poolStack.Count > 0)
        {
            // 弹出对象
            obj = poolDic[name].GetObj();
        }
        //  否则创建
        else
        {
            // 没有通过资源加载创建一个
            obj = GameObject.Instantiate(Resources.Load<GameObject>(name));
            // 重命名为了防止新创建的对象 后面跟一个(Clone)
            obj.name = name;
        }
        return obj;
    }

    /// <summary>
    /// 往对象池中放入对象
    /// </summary>
    /// <param name="name">对象名字</param>
    /// <param name="obj">希望放入的对象</param>
    public void PushObj(GameObject obj)
    {
        if (poolObj == null)
            poolObj = new GameObject("ObjectPool");

        // 没有抽屉 创建抽屉
        if (!poolDic.ContainsKey(obj.name))
            poolDic.Add(obj.name, new PoolData(obj, poolObj));

        // 往抽屉当中放入对象
        poolDic[obj.name].PushObj(obj);        
    }

    /// <summary>
    /// 预热方法 先加载一批出来
    /// </summary>
    /// <param name="name"></param>
    /// <param name="count"></param>
    public void Preload(string name, int count)
    {
        List<GameObject> temp = new List<GameObject>();
        for (int i = 0; i < count; i++)
        {
            GameObject obj = CreateNewObject(name);
            obj.SetActive(false);
            temp.Add(obj);
        }
        foreach (var obj in temp)
            PushObj(obj);
    }

    private GameObject CreateNewObject(string name)
    {
        // 这里可以改为从AB包加载
        GameObject prefab = Resources.Load<GameObject>(name);
        GameObject obj = GameObject.Instantiate(prefab);
        obj.name = name;
        return obj;
    }

    /// <summary>
    /// 用于清除池子中的数量
    /// 使用场景 主要是 切换场景
    /// </summary>
    public void ClearPool()
    {
        // Unity 切场景已经销毁了 GameObjects，
        // 这里只需要把 C# 侧的引用清干净
        poolDic.Clear();
        poolObj = null;
    }

    // ======================== 新增 AB包加载方法 ========================
    // 方法                                                     说明
    // GetObj_AB(abName, resName)                               同步，从池或AB包泛型加载
    // GetObj_AB_NonGeneric(abName, resName)                    同步，从池或AB包非泛型加载
    // Preload_AB(abName, resName, count)                       同步预热，先调 LoadAB 确保依赖，再循环实例化入池
    // GetObj_ABAsync(abName, resName, callBack)                异步，池中有直接回调，没有则走 LoadResAsync<GameObject>
    // Preload_ABAsync(abName, resName, count, onAllDone)       异步预热，全部完成后统一入池并回调
    // PushObj_Safe(obj)                                        与原 PushObj 逻辑一致，供异步场景单独调用

    // ---------- 同步 ----------

    /// <summary>
    /// 从AB包加载 获取对象（同步）
    /// 优先从池中取，池中没有则通过ABMgr从AB包实例化
    /// </summary>
    /// <param name="abName">AB包名</param>
    /// <param name="resName">资源名（同时也是池抽屉的key）</param>
    public GameObject GetObj_AB(string abName, string resName)
    {
        GameObject obj;
        // 抽屉里有缓存就复用
        if (poolDic.ContainsKey(resName) && poolDic[resName].poolStack.Count > 0)
        {
            obj = poolDic[resName].GetObj();
        }
        else
        {
            // 通过AB包同步加载并实例化
            obj = ABMgr.Instance.LoadRes<GameObject>(abName, resName);
            if (obj != null)
                obj.name = resName;
        }
        return obj;
    }

    /// <summary>
    /// 从AB包加载 获取对象（同步，不指定泛型）
    /// </summary>
    public GameObject GetObj_AB_NonGeneric(string abName, string resName)
    {
        GameObject obj;
        if (poolDic.ContainsKey(resName) && poolDic[resName].poolStack.Count > 0)
        {
            obj = poolDic[resName].GetObj();
        }
        else
        {
            Object loaded = ABMgr.Instance.LoadRes(abName, resName);
            obj = loaded as GameObject;
            if (obj != null)
                obj.name = resName;
        }
        return obj;
    }

    /// <summary>
    /// 预热：从AB包预加载一批对象到池中（同步）
    /// </summary>
    /// <param name="abName">AB包名</param>
    /// <param name="resName">资源名</param>
    /// <param name="count">数量</param>
    public void Preload_AB(string abName, string resName, int count)
    {
        // 先确保AB包已加载，避免循环中重复加载依赖
        ABMgr.Instance.LoadAB(abName);

        List<GameObject> temp = new List<GameObject>();
        for (int i = 0; i < count; i++)
        {
            GameObject obj = CreateNewObject_AB(abName, resName);
            obj.SetActive(false);
            temp.Add(obj);
        }
        foreach (var obj in temp)
            PushObj(obj);
    }

    /// <summary>
    /// 通过AB包创建单个新对象（同步，不入池）
    /// </summary>
    private GameObject CreateNewObject_AB(string abName, string resName)
    {
        GameObject obj = ABMgr.Instance.LoadRes<GameObject>(abName, resName);
        if (obj != null)
            obj.name = resName;
        return obj;
    }

    // ---------- 异步 ----------

    /// <summary>
    /// 从AB包异步加载 获取对象
    /// 优先从池中取；池中没有则通过ABMgr异步加载，完成后回调返回
    /// </summary>
    /// <param name="abName">AB包名</param>
    /// <param name="resName">资源名</param>
    /// <param name="callBack">拿到对象后的回调</param>
    public void GetObj_ABAsync(string abName, string resName, UnityAction<GameObject> callBack)
    {
        // 池中有就直接给
        if (poolDic.ContainsKey(resName) && poolDic[resName].poolStack.Count > 0)
        {
            if (callBack != null)
                callBack(poolDic[resName].GetObj());
            return;
        }

        // 池中没有，异步从AB包加载
        ABMgr.Instance.LoadResAsync<GameObject>(abName, resName, (obj) =>
        {
            if (obj != null)
                obj.name = resName;
            if (callBack != null)
                callBack(obj);
        });
    }

    /// <summary>
    /// 预热：从AB包异步预加载一批对象到池中
    /// 全部加载完成后回调通知
    /// </summary>
    /// <param name="abName">AB包名</param>
    /// <param name="resName">资源名</param>
    /// <param name="count">数量</param>
    /// <param name="onAllDone">全部加载完成回调</param>
    public void Preload_ABAsync(string abName, string resName, int count, UnityAction onAllDone = null)
    {
        // 先同步加载AB包本身（包的加载是同步的，资源加载才异步）
        ABMgr.Instance.LoadAB(abName);

        // 用一个临时列表收集，全部加载完再统一入池
        List<GameObject> temp = new List<GameObject>();
        int loaded = 0;

        for (int i = 0; i < count; i++)
        {
            ABMgr.Instance.LoadResAsync<GameObject>(abName, resName, (obj) =>
            {
                if (obj != null)
                {
                    obj.name = resName;
                    obj.SetActive(false);
                    temp.Add(obj);
                }
                loaded++;

                // 全部加载完成
                if (loaded >= count)
                {
                    foreach (var go in temp)
                        PushObj(go);

                    if (onAllDone != null)
                        onAllDone();
                }
            });
        }

        // count 为 0 时直接回调
        if (count <= 0 && onAllDone != null)
            onAllDone();
    }

    /// <summary>
    /// 只放入对象到池中（按对象name作为key），并确保poolObj根节点存在
    /// 适用于异步加载完成后、对象尚未入池的场景
    /// </summary>
    public void PushObj_Safe(GameObject obj)
    {
        if (poolObj == null)
            poolObj = new GameObject("ObjectPool");

        if (!poolDic.ContainsKey(obj.name))
            poolDic.Add(obj.name, new PoolData(obj, poolObj));

        poolDic[obj.name].PushObj(obj);
    }
}

