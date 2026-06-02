using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

public static class ThreadUtil
{
    // 用来记住游戏启动时，主线程的身份证号（ID）
    private static int _mainThreadId;
    // 存放那个专门负责干活的隐藏组件
    private static MainThreadDispatcher _dispatcher;
    // 创建一个线程安全的动作队列，Action 代表一段可以执行的代码（任务）                                                 
    private static readonly ConcurrentQueue<Action> _actionQueue = new();

    [RuntimeInitializeOnLoadMethod] // Unity 启动时自动调用此方法
    private static void Init()
    {
        // 1. 记录当前线程ID。因为 Unity 启动时肯定是主线程，所以这里记下的就是主线程ID。
        _mainThreadId = Thread.CurrentThread.ManagedThreadId;

        // 2. 动态创建一个专门用来调度的 GameObject
        if (_dispatcher == null)
        {
            // 在内存中创建一个叫 [ThreadUtil_Dispatcher] 的空物体
            var go = new GameObject("[ThreadUtil_Dispatcher]");
            // 给它挂载上下面定义的 MainThreadDispatcher 脚本组件
            _dispatcher = go.AddComponent<MainThreadDispatcher>();
            // 关键！让这个物体“永生”，切换场景时不会被 Unity 自动销毁
            MonoBehaviour.DontDestroyOnLoad(go);
        }
    }

    // 判断当前代码是不是运行在主线程里
    public static bool IsMainThread() => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

    // 核心方法！任何子线程都可以调用它，把任务扔给主线程去执行
    public static void RunOnMainThread(Action action)
    {
        if (action == null) return; // 防御性编程，防止传入空任务
        _actionQueue.Enqueue(action); // 把任务打包，扔进安全队列里排队
    }

    private class MainThreadDispatcher : MonoBehaviour
    {
        // 单例模式的二次保险：确保全局只有一个调度器组件
        private void Awake()
        {
            if (_dispatcher == null)
            {
                _dispatcher = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject); // 如果已经有一个了，就把新创建的多余物体销毁掉
            }
        }

        // Update 每帧都会被 Unity 自动调用（且一定是在主线程中！）
        private void Update()
        {
            // 只要队列里还有任务，就不断地取出来执行
            while (_actionQueue.TryDequeue(out var action))
            {
                try
                {
                    action.Invoke(); // 执行这段代码（比如修改 UI 文字）
                }
                catch (Exception e)
                {
                    // 捕获任务执行中的报错，防止某个任务崩溃导致整个调度器罢工
                    Debug.LogError($"主线程任务异常: {e}");
                }
            }
        }
    }
}