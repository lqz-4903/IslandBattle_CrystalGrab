using System.Collections.Generic;
using UnityEngine.Events;

public interface IUnityAction_package{ }

public class UnityAction_package<T> : IUnityAction_package
{
    public UnityAction<T> actions;
    public UnityAction_package(UnityAction<T> action)
    {
        actions += action;
    }
}

public class UnityAction_package : IUnityAction_package
{
    public UnityAction actions;
    public UnityAction_package(UnityAction action)
    {
        actions += action;
    }
}

/// <summary>
/// 事件中心
/// </summary>
public class EventCenter 
{
    private static EventCenter instance = new EventCenter();
    public static EventCenter Instance => instance;

    private EventCenter() { }

    // key - 事件的名字（比如：怪物死亡，玩家死亡，通过等等）
    // value - 对应的是 监听这个事件 对应的委托函数们
    private Dictionary<string, IUnityAction_package> eventDic = new Dictionary<string, IUnityAction_package>();

    /// <summary>
    /// 添加事件监听
    /// </summary>
    /// <param name="name">事件的名字</param>
    /// <param name="action">准备用来处理的事件 的 委托函数</param>
    public void AddEventListener<T>(string name, UnityAction<T> action)
    {
        // 有没有对应的事件监听
        // 有的情况
        if (eventDic.ContainsKey(name))
        {
            (eventDic[name] as UnityAction_package<T>).actions += action;
        }
        // 没有的情况
        else
        {
            eventDic.Add(name, new UnityAction_package<T>(action));
        }
    }

    /// <summary>
    /// 监听不需要参数的事件
    /// </summary>
    /// <param name="name"></param>
    /// <param name="action"></param>
    public void AddEventListener(string name, UnityAction action)
    {
        // 有没有对应的事件监听
        // 有的情况
        if (eventDic.ContainsKey(name))
        {
            (eventDic[name] as UnityAction_package).actions += action;
        }
        // 没有的情况
        else
        {
            eventDic.Add(name, new UnityAction_package(action));
        }
    }

    /// <summary>
    /// 移除事件
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="name">事件名字</param>
    /// <param name="action">对应之前添加的委托函数</param>
    public void RemoveEventListener<T>(string name, UnityAction<T> action)
    {
        if (eventDic.ContainsKey(name))
            (eventDic[name] as UnityAction_package<T>).actions -= action;

    }

    /// <summary>
    /// 移除不需要参数的事件
    /// </summary>
    /// <param name="name"></param>
    /// <param name="action"></param>
    public void RemoveEventListener(string name, UnityAction action)
    {
        if (eventDic.ContainsKey(name))
            (eventDic[name] as UnityAction_package).actions -= action;

    }

    /// <summary>
    /// 事件触发
    /// </summary>
    /// <param name="name">哪一个名字的事件触发了</param>
    public void EventTrigger<T>(string name, T info)
    {
        // 有没有对应的事件监听
        // 有的情况
        if (eventDic.ContainsKey(name))
        {
            if ((eventDic[name] as UnityAction_package<T>).actions != null)            
                (eventDic[name] as UnityAction_package<T>).actions.Invoke(info);           
        }      
    }

    /// <summary>
    /// 触发不需要参数的事件
    /// </summary>
    /// <param name="name"></param>
    public void EventTrigger(string name)
    {
        // 有没有对应的事件监听
        // 有的情况
        if (eventDic.ContainsKey(name))
        {
            if ((eventDic[name] as UnityAction_package).actions != null)
                (eventDic[name] as UnityAction_package).actions.Invoke();
        }
    }

    /// <summary>
    /// 清空事件中心 用于切场景用
    /// </summary>
    public void Clear()
    {
        eventDic.Clear();
    }
}
