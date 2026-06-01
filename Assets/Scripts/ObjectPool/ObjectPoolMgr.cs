using System.Collections.Generic;
using UnityEngine;

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
    public GameObject GetObj(string name)
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
    /// 用于清除池子中的数量
    /// 使用场景 主要是 切换场景
    /// </summary>
    public void ClearPool()
    {
        poolDic.Clear();
    }
}

