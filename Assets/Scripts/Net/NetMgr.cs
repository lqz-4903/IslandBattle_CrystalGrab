using GameProto;
using Google.Protobuf;
using System;
using System.Collections.Concurrent;
using UnityEngine;

/// <summary>
/// Task 扩展方法类
/// 为 System.Threading.Tasks.Task 类型提供扩展方法，增强异步操作的便捷性和安全性
/// </summary>
/// <remarks>
/// 使用场景：
/// - 不需要等待结果的异步操作（Fire-and-Forget 模式）
/// - 避免编译器警告 CS4014（由于此调用不会等待，当前方法会继续执行）
/// - 安全地处理"即发即弃"任务中的异常，防止未观察到的异常导致程序崩溃
/// </remarks>
public static class TaskExtensions
{
    /// <summary>
    /// 忽略 Task 的等待和结果，并安全处理可能发生的异常
    /// </summary>
    /// <param name="task">要忽略的异步任务</param>
    /// <remarks>
    /// 这是一个"即发即弃"（Fire-and-Forget）模式的扩展方法。
    /// 
    /// 工作原理：
    /// 1. 如果任务已经完成且出现故障（IsFaulted），立即记录异常
    /// 2. 如果任务尚未完成，注册一个 ContinueWith 回调，等任务完成后如果出错则记录异常
    /// 3. 如果任务成功完成，不做任何处理
    /// 
    /// 为什么需要这个方法？
    /// - 直接调用异步方法而不 await 会产生编译器警告
    /// - 未观察到的 Task 异常会在 GC 时触发 UnobservedTaskException，可能导致程序崩溃
    /// - 这个方法明确表达"有意忽略"的意图，并确保异常被安全记录而非静默吞掉
    /// 
    /// 使用示例：
    /// <code>
    /// // 发送日志消息，不关心结果
    /// SendLogAsync(message).Forget();
    /// 
    /// // 触发后台保存操作
    /// SavePlayerDataAsync(playerData).Forget();
    /// 
    /// // 网络请求，出错也不影响主流程
    /// kcp.SendAsync(conv, data).Forget();
    /// </code>
    /// 
    /// 注意事项：
    /// - 不要滥用此方法，只有当你真的不关心任务结果时才使用
    /// - 对于关键操作（如数据保存、重要网络请求），应该使用 await 并处理异常
    /// - 异常会被记录到 Unity Console 中，便于调试
    /// </remarks>
    public static void Forget(this System.Threading.Tasks.Task task)
    {
        // 情况1：任务已经完成（无论成功还是失败）
        if (task.IsFaulted)
        {
            // 任务已经出错，立即记录异常信息
            // 使用 Debug.LogException 会在 Console 中显示红色错误，并包含堆栈跟踪
            UnityEngine.Debug.LogException(task.Exception);
        }
        // 情况2：任务尚未完成
        else if (!task.IsCompleted)
        {
            // 注册一个延续任务：只有当原任务出错（Faulted）时才执行
            // ContinueWith 不会阻塞当前线程
            task.ContinueWith(t =>
            {
                // 延续任务执行时，再次检查是否出错（双重保险）
                if (t.IsFaulted)
                {
                    // 记录异常到 Unity Console
                    UnityEngine.Debug.LogException(t.Exception);
                }
            }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }
        // 情况3：任务已成功完成（IsCompletedSuccessfully）
        // 无需任何处理，静默忽略
    }
}

// =================================================================================
//                      NetMgr.cs - 消息分发 + 主线程队列
// =================================================================================

public class NetMgr : MonoBehaviour
{
    private static NetMgr instance;
    public static NetMgr Instance => instance;

    private NetMgr() { }

    // ==== 跨线程队列 ====
    private readonly ConcurrentQueue<(int msgId, uint conv, IMessage msg)> _pending = new();

    /// <summary>
    /// 客户端默认使用的会话ID（客户端固定为1）
    /// 不用担心 conv唯一性问题 KcpMgr里面处理 conv == 1 是新客户端
    /// 会在再重新分配一个conv 再新的conv中完成后续逻辑
    /// </summary>
    private const uint _defaultConv = 1;

    /// <summary>
    /// 初始化
    /// </summary>
    private void Awake()
    {
        instance = this;
    }

    #region 主线程轮询
    // Update is called once per frame
    void Update()
    {
        // 主线程取出 直接派发给事件中心
        while (_pending.TryDequeue(out var item))
        {
            EventCenter.Dispatch(item.msgId,item.conv, item.msg);
        }

        //从KcpMgr去网络数据（必须每帧调用）
        while (KcpMgr.Instance.TryRecv(out uint conv, out byte[] data))
        {
            OnRecvData(conv, data);
        }
    }

    #endregion

    #region 发消息
    public void Send(NetMessage msg)
    {
        try
        {
            byte[] data = SerAndDeserPBTool.GetProtoBytes(msg);
            KcpMgr.Instance.SendAsync(_defaultConv, data).Forget();
        }
        catch (Exception e)
        {
            Debug.Log("【NetMgr】发送失败" + e.Message);
        }
    }

    #endregion

    #region 收消息

    public void OnRecvData(uint conv, byte[] data)
    {
        try
        {
            NetMessage envelope = SerAndDeserPBTool.GetProtoMsg<NetMessage>(data);

            // msgId映射
            int msgId = envelope.MsgCase switch
            {
                NetMessage.MsgOneofCase.Heartbeat     => 1,
                NetMessage.MsgOneofCase.CreateRoom    => 10,
                NetMessage.MsgOneofCase.CreateRoomAck => 11,
                NetMessage.MsgOneofCase.JoinRoom      => 12,
                NetMessage.MsgOneofCase.JoinRoomAck   => 13,
                NetMessage.MsgOneofCase.PlayerList    => 14,
                NetMessage.MsgOneofCase.StartGame     => 15,
                NetMessage.MsgOneofCase.GameStart     => 16,
                NetMessage.MsgOneofCase.InputFrame    => 20,
                NetMessage.MsgOneofCase.PlayerInput   => 21,
                NetMessage.MsgOneofCase.CrystalSpawn  => 30,
                NetMessage.MsgOneofCase.CrystalPickup => 31,
                NetMessage.MsgOneofCase.PlayerHit     => 32,
                NetMessage.MsgOneofCase.PlayerFall    => 33,
                NetMessage.MsgOneofCase.GameEnd       => 34,
                NetMessage.MsgOneofCase.Reconnect     => 40,
                NetMessage.MsgOneofCase.ReconnectAck  => 41,
                NetMessage.MsgOneofCase.CatchUpFrames => 42,
                _ => 0
            };

            // 取出oneof里的实际消息
            IMessage payload = envelope.MsgCase switch
            {
                NetMessage.MsgOneofCase.Heartbeat       => envelope.Heartbeat,
                NetMessage.MsgOneofCase.CreateRoom      => envelope.CreateRoom,
                NetMessage.MsgOneofCase.CreateRoomAck   => envelope.CreateRoomAck,
                NetMessage.MsgOneofCase.JoinRoom        => envelope.JoinRoom,
                NetMessage.MsgOneofCase.JoinRoomAck     => envelope.JoinRoomAck,
                NetMessage.MsgOneofCase.PlayerList      => envelope.PlayerList,
                NetMessage.MsgOneofCase.StartGame       => envelope.StartGame,
                NetMessage.MsgOneofCase.GameStart       => envelope.GameStart,
                NetMessage.MsgOneofCase.InputFrame      => envelope.InputFrame,
                NetMessage.MsgOneofCase.PlayerInput     => envelope.PlayerInput,
                NetMessage.MsgOneofCase.CrystalSpawn    => envelope.CrystalSpawn,
                NetMessage.MsgOneofCase.CrystalPickup   => envelope.CrystalPickup,
                NetMessage.MsgOneofCase.PlayerHit       => envelope.PlayerHit,
                NetMessage.MsgOneofCase.PlayerFall      => envelope.PlayerFall,
                NetMessage.MsgOneofCase.GameEnd         => envelope.GameEnd,
                NetMessage.MsgOneofCase.Reconnect       => envelope.Reconnect,
                NetMessage.MsgOneofCase.ReconnectAck    => envelope.ReconnectAck,
                NetMessage.MsgOneofCase.CatchUpFrames   => envelope.CatchUpFrames,
                _ => null
            };

            if (msgId != 0 && payload != null)
                _pending.Enqueue((msgId, conv, payload));
        }
        catch (Exception e)
        {
            Debug.Log("【NetMgr】解析失败" + e.Message);
        }
    }

    #endregion
}
