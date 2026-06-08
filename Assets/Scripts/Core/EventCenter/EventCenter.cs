using Google.Protobuf;
using System.Collections.Generic;
using UnityEngine.Events;
using System;

/// <summary>
/// 封装了UnityAction<T>, 避免传入object类产生开装箱的消耗
/// </summary>
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
/// 通用事件中心（主线程安全）
/// 不需要用单例，直接写成静态类就好了
/// 因为只是 C# 逻辑和数据 没有涉及到 Unity 生命周期方法（Awake/Update/OnDestroy）或者需要挂载组件
/// </summary>
public static class EventCenter 
{
    // key - 事件的名字（比如：怪物死亡，玩家死亡，通过等等）
    // value - 对应的是 监听这个事件 对应的委托函数们
    private static readonly Dictionary<string, IUnityAction_package> _eventStringDic = new();

    // 网络事件字典 
    private static readonly Dictionary<int, UnityAction<IMessage>> _eventIntDic = new();

    // 带conv的网络事件字典
    private static readonly Dictionary<int, UnityAction<uint, IMessage>> _eventIntConvDic = new();

    #region 事件监听

    /// <summary>
    /// 添加事件监听
    /// </summary>
    /// <param name="name">事件的名字</param>
    /// <param name="action">准备用来处理的事件 的 委托函数</param>
    public static void AddListener<T>(string name, UnityAction<T> action)
    {
        if (string.IsNullOrEmpty(name) || action == null) return;

        lock (_eventStringDic)
        {
            if (_eventStringDic.TryGetValue(name, out var wrapper) &&
                wrapper is UnityAction_package<T> typedWrapper)
            {
                typedWrapper.actions += action;
            }
            else
            {
                _eventStringDic[name] = new UnityAction_package<T>(action);
            }
        }
    }

    /// <summary>
    /// 监听无参的事件
    /// </summary>
    /// <param name="name"></param>
    /// <param name="action"></param>
    public static void AddListener(string name, UnityAction action)
    {
        if (string.IsNullOrEmpty(name) || action == null) return;

        lock (_eventStringDic)
        {
            if (_eventStringDic.TryGetValue(name, out var wrapper) &&
                wrapper is UnityAction_package typedWrapper)
            {
                typedWrapper.actions += action;
            }
            else
            {
                _eventStringDic[name] = new UnityAction_package(action);
            }
        }
    }

    /// <summary>
    /// 用于网络通信监听事件
    /// </summary>
    /// <param name="msgId">消息ID</param>
    /// <param name="callback">处理消息的回调函数</param>
    public static void AddListener(int msgId, UnityAction<IMessage> callback)
    {
        if (callback == null)
        {
            UnityEngine.Debug.Log("AddListener error, callback is null, msgId is " + msgId);
            return;
        }

        lock (_eventIntDic)
        {
            if (_eventIntDic.ContainsKey(msgId))
                _eventIntDic[msgId] += callback;
            else
                _eventIntDic[msgId] = callback;
        }
    }

    /// <summary>
    /// 注册网络事件（带conv 可获取消息来源客户端）
    /// </summary>
    /// <param name="msgId"></param>
    /// <param name="callback"></param>
    public static void AddListener(int msgId, UnityAction<uint, IMessage> callback)
    {
        if (callback == null)
        {
            UnityEngine.Debug.Log("AddListener error, callback is null, msgId is " + msgId);
            return;
        }

        lock (_eventIntConvDic)
        {
            if (_eventIntConvDic.ContainsKey(msgId))
                _eventIntConvDic[msgId] += callback;
            else
                _eventIntConvDic[msgId] = callback;
        }
    }

    #endregion

    #region 事件移除

    /// <summary>
    /// 移除事件
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="name">事件名字</param>
    /// <param name="action">对应之前添加的委托函数</param>
    public static void RemoveListener<T>(string name, UnityAction<T> action)
    {
        if (string.IsNullOrEmpty(name) || action == null) return;

        lock (_eventStringDic)
        {
            if (_eventStringDic.TryGetValue(name, out var wrapper) &&
                wrapper is UnityAction_package<T> typedWrapper)
            {
                typedWrapper.actions -= action;
                if (typedWrapper.actions == null)
                    _eventStringDic.Remove(name);
            }
        }
    }

    /// <summary>
    /// 移除无参的事件
    /// </summary>
    /// <param name="name"></param>
    /// <param name="action"></param>
    public static void RemoveListener(string name, UnityAction action)
    {
        if (string.IsNullOrEmpty(name) || action == null) return;

        lock (_eventStringDic)
        {
            if (_eventStringDic.TryGetValue(name, out var wrapper) &&
                wrapper is UnityAction_package typedWrapper)
            {
                typedWrapper.actions -= action;
                if (typedWrapper.actions == null)
                    _eventStringDic.Remove(name);
            }
        }
    }

    /// <summary>
    /// 用于网络通信移出事件
    /// </summary>
    /// <param name="msgId">消息ID</param>
    /// <param name="callback">处理消息的回调函数</param>
    public static void RemoveListener(int msgId, UnityAction<IMessage> callback)
    {
        if (callback == null)
        {
            UnityEngine.Debug.Log("RemoveListener error, callback is null, msgId is " + msgId);
            return;
        }

        lock (_eventIntDic)
        {
            if (_eventIntDic.TryGetValue(msgId, out var handlers))
            {
                handlers -= callback;
                _eventIntDic[msgId] = handlers;

                // 没有回调了就清除键，避免内存垃圾
                if (handlers == null)
                    _eventIntDic.Remove(msgId);
            }
        }
    }

    /// <summary>
    /// 移除网络事件（带conv）
    /// </summary>
    /// <param name="msgId"></param>
    /// <param name="callback"></param>
    public static void RemoveListener(int msgId, UnityAction<uint, IMessage> callback)
    {
        if (callback == null)
        {
            UnityEngine.Debug.Log("RemoveListener error, callback is null, msgId is " + msgId);
            return;
        }

        lock (_eventIntConvDic)
        {
            if (_eventIntConvDic.TryGetValue(msgId, out var handlers))
            {
                handlers -= callback;
                _eventIntConvDic[msgId] = handlers;

                // 没有回调了就清除键，避免内存垃圾
                if (handlers == null)
                    _eventIntConvDic.Remove(msgId);
            }
        }

    }
    
    #endregion

    #region 事件触发

    /// <summary>
    /// 事件触发
    /// </summary>
    /// <param name="name">哪一个名字的事件触发了</param>
    public static void Dispatch<T>(string name, T info)
    {
        UnityAction<T> actions = null;

        lock (_eventStringDic)
        {
            if (_eventStringDic.TryGetValue(name, out var wrapper) &&
                wrapper is UnityAction_package<T> typedWrapper)
            {
                actions = typedWrapper.actions;
            }
        }

        if (actions != null)
        {
            try
            {
                actions.Invoke(info);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("Event Dispatch Error (Generic), name: " + name + " | " + e.Message);
            }
        }
    }

    /// <summary>
    /// 触发无参的事件
    /// </summary>
    /// <param name="name"></param>
    public static void Dispatch(string name)
    {
        UnityAction actions = null;

        lock (_eventStringDic)
        {
            if (_eventStringDic.TryGetValue(name, out var wrapper) &&
                wrapper is UnityAction_package typedWrapper)
            {
                actions = typedWrapper.actions;
            }
        }

        if (actions != null)
        {
            try
            {
                actions.Invoke();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("Event Dispatch Error, name: " + name + " | " + e.Message);
            }
        }
    }

    /// <summary>
    /// 用于网络通信派发事件
    /// </summary>
    /// <param name="msgId">消息ID</param>
    /// <param name="message">处理消息的回调函数</param>
    public static void Dispatch(int msgId, IMessage message)
    {
        // 强制主线程执行（避免网络子线程调用）
        if (ThreadUtil.IsMainThread())
            DispatchInternal(msgId, message);
        else
            ThreadUtil.RunOnMainThread(() => DispatchInternal(msgId, message));
    }

    /// <summary>
    /// 用于网络通信派发事件 内部真正实现发送
    /// </summary>
    /// <param name="msgId">消息ID</param>
    /// <param name="message">处理消息的回调函数</param>
    private static void DispatchInternal(int msgId, IMessage message)
    {
        UnityAction<IMessage> handlers = null;

        lock (_eventIntDic)
        {
            if (_eventIntDic.TryGetValue(msgId, out var d))
                handlers = d;
        }

        if (handlers == null) return;

        try
        {
            handlers.Invoke(message);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.Log("Event Dispatch Error, msgId: " + msgId + e.Message);
        }
    }

    /// <summary>
    /// 派发网络事件（带conv版本）
    /// </summary>
    public static void Dispatch(int msgId, uint conv, IMessage message)
    {
        if (ThreadUtil.IsMainThread())
            DispatchInternal(msgId, conv, message);
        else
            ThreadUtil.RunOnMainThread(() => DispatchInternal(msgId, conv, message));
    }

    /// <summary>带conv派发内部实现</summary>
    private static void DispatchInternal(int msgId, uint conv, IMessage message)
    {
        UnityAction<uint, IMessage> handlers = null;

        lock (_eventIntConvDic)
        {
            if (_eventIntConvDic.TryGetValue(msgId, out var d))
                handlers = d;
        }

        if (handlers == null) return;

        try
        {
            handlers.Invoke(conv, message);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.Log("Event Dispatch Error, msgId: " + msgId + " | " + e.Message);
        }
    }


    #endregion

    /// <summary>
    /// 清空事件中心 用于切场景用
    /// </summary>
    public static void Clear()
    {
        lock (_eventIntDic)
        {
            _eventIntDic.Clear();
        }

        lock (_eventStringDic)
        {
            _eventStringDic.Clear();
        }

        lock (_eventIntConvDic)
        {
            _eventIntConvDic.Clear();
        }
    }

}
