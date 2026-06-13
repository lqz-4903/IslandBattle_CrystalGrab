using GameProto;
using Google.Protobuf;
using System;
using System.Collections.Concurrent;
using UnityEngine;

/// <summary>
/// Task 扩展方法类
/// 为 System.Threading.Tasks.Task 类型提供扩展方法，增强异步操作的便捷性和安全性
/// </summary>
public static class TaskExtensions
{
    public static void Forget(this System.Threading.Tasks.Task task)
    {
        if (task.IsFaulted)
        {
            UnityEngine.Debug.LogException(task.Exception);
        }
        else if (!task.IsCompleted)
        {
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    UnityEngine.Debug.LogException(t.Exception);
            }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}

// =================================================================================
//                      NetMgr.cs —— 网络消息管理器（客户端 / 服务端通用）
// =================================================================================
//
// 【两种使用模式】
//
//   客户端（Client）：调用方是 Lua JoinRoomPanel
//     Send(msg) → 自动使用 KcpMgr.ClientConv 发送到服务端
//     Update()  → 轮询 KcpMgr.TryRecv，收到 JoinRoomAck/PlayerList/GameStart 后回调 Lua
//
//   服务端（Server）：调用方是 C# HostServer / RoomHandler
//     SendTo(conv, msg)    → 发送给指定客户端
//     BroadcastToAll(msg)  → 广播给所有已连接客户端
//     Update()             → 轮询 KcpMgr.TryRecv → 解析 protobuf → EventCenter 派发
//                            同时触发 Lua 回调（OnJoinRoomAckCallback / OnPlayerListCallback）
//
//   NetMgr 自身不区分模式——发送方法由调用者选择合适的 API。
//   Send(msg) 仅在客户端使用（自动读 ClientConv）；
//   SendTo / BroadcastToAll 在服务端使用（显式传 conv）。
//
// =================================================================================

public class NetMgr : MonoBehaviour
{
    private static NetMgr instance;
    public static NetMgr Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<NetMgr>();
                if (instance == null)
                {
                    GameObject go = new GameObject("NetMgr");
                    instance = go.AddComponent<NetMgr>();
                    DontDestroyOnLoad(go);
                }
            }
            return instance;
        }
    }

    // ==== Lua 回调桥接（静态字段，Lua 可直接设置） ====
    public static Action<JoinRoomAck> OnJoinRoomAckCallback;
    public static Action<PlayerList> OnPlayerListCallback;
    public static Action<PlayerOffline> OnPlayerOfflineCallback;
    public static Action<KickOff> OnKickOffCallback;         // 仅用于「房间解散」通知
    public static Action<GameStart> OnGameStartCallback;

    // ==== 跨线程队列 ====
    private readonly ConcurrentQueue<(int msgId, uint conv, IMessage msg)> _pending = new();

    // ==== 心跳发送（客户端） ====
    private float _heartbeatSendTimer = 0f;
    private const float HeartbeatSendInterval = 2f;

    // 上次打印 Send 日志时的 ClientConv（避免帧同步每帧刷屏）
    private uint _lastLoggedClientConv;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    #region 每帧 Update（双端通用）

    void Update()
    {
        // ★ 心跳发送（客户端模式，每 HeartbeatSendInterval 秒发送一次）
        uint clientConv = KcpMgr.Instance.ClientConv;
        if (clientConv != 0)
        {
            _heartbeatSendTimer += Time.deltaTime;
            if (_heartbeatSendTimer >= HeartbeatSendInterval)
            {
                _heartbeatSendTimer = 0f;
                var hb = new HeartBeat { Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                SendRaw(clientConv, new NetMessage { Heartbeat = hb });
            }
        }
        else
        {
            _heartbeatSendTimer = 0f; // 未连接时重置计时器
        }

        // 1. 消费主线程消息队列 → 派发到 EventCenter + Lua 回调
        while (_pending.TryDequeue(out var item))
        {
            // 派发到三参数字典（HostServer 等需要 conv 的监听者）
            EventCenter.Dispatch(item.msgId, item.conv, item.msg);

            // 派发到二参数字典（LuaEventBridge 等不需要 conv 的监听者）
            // ★ 主机侧：游戏事件(30-39)由 GameEventHandler 验证后统一派发到 2-param，
            //   此处跳过以避免主机侧双重回调。
            //   客户端侧：HostServer 未运行，2-param 是唯一路径，必须派发。
            bool isHostRunning = HostServer.Instance != null && HostServer.Instance.IsRunning;
            bool isGameEvent = item.msgId >= 30 && item.msgId <= 39;
            if (!(isHostRunning && isGameEvent))
            {
                EventCenter.Dispatch(item.msgId, item.msg);
            }

            // Lua 回调桥接
            if (item.msgId == 13 && OnJoinRoomAckCallback != null && item.msg is JoinRoomAck ack)
            {
                try { OnJoinRoomAckCallback.Invoke(ack); }
                catch (Exception e) { Debug.Log("【NetMgr】OnJoinRoomAckCallback 异常：" + e.Message); }
            }
            else if (item.msgId == 14 && OnPlayerListCallback != null && item.msg is PlayerList playerList)
            {
                try { OnPlayerListCallback.Invoke(playerList); }
                catch (Exception e) { Debug.Log("【NetMgr】OnPlayerListCallback 异常：" + e.Message); }
            }
            else if (item.msgId == 2 && OnKickOffCallback != null && item.msg is KickOff kickOff)
            {
                try { OnKickOffCallback.Invoke(kickOff); }
                catch (Exception e) { Debug.Log("【NetMgr】OnKickOffCallback 异常：" + e.Message); }
            }
            else if (item.msgId == 37 && OnPlayerOfflineCallback != null && item.msg is PlayerOffline playerOffline)
            {
                try { OnPlayerOfflineCallback.Invoke(playerOffline); }
                catch (Exception e) { Debug.Log("【NetMgr】OnPlayerOfflineCallback 异常：" + e.Message); }
            }
            else if (item.msgId == 16 && OnGameStartCallback != null && item.msg is GameStart gameStart)
            {
                try { OnGameStartCallback.Invoke(gameStart); }
                catch (Exception e) { Debug.Log("【NetMgr】OnGameStartCallback 异常：" + e.Message); }
            }
        }

        // 2. 从 KcpMgr 取网络数据 → 解析 protobuf → 入队（下一帧派发）
        while (KcpMgr.Instance.TryRecv(out uint conv, out byte[] data))
        {
            OnRecvData(conv, data);
        }
    }

    #endregion

    #region 客户端发送：Send(msg) —— 自动用 ClientConv

    /// <summary>
    /// 【客户端专用】发送消息到服务端。
    /// 自动使用 KcpMgr.ClientConv（握手分配的 conv）。
    /// </summary>
    public void Send(NetMessage msg)
    {
        uint clientConv = KcpMgr.Instance.ClientConv;
        // 仅在 ClientConv 变化时打印（避免帧同步每 1/15s 刷屏）
        if (_lastLoggedClientConv != clientConv)
        {
            _lastLoggedClientConv = clientConv;
            Debug.Log("【NetMgr】Send 被调用 ClientConv=" + clientConv);
        }
        if (clientConv == 0)
        {
            Debug.LogWarning("【NetMgr】ClientConv 为 0，KCP 握手可能尚未完成，消息未发送");
            return;
        }
        SendRaw(clientConv, msg);
    }

    /// <summary>
    /// 【客户端专用】发送 KickOff 通知服务器（正常离开房间），短暂延迟后断开 KCP。
    /// 延迟是为了让 KCP 发送循环把 KickOff 消息刷出去。
    /// </summary>
    public async void SendKickOffAndStop(string reason)
    {
        uint clientConv = KcpMgr.Instance.ClientConv;
        Debug.Log("【NetMgr】SendKickOffAndStop reason=" + reason + " ClientConv=" + clientConv);
        if (clientConv != 0)
        {
            var kickOff = new KickOff { Reason = reason };
            var netMsg = new NetMessage { KickOff = kickOff };
            SendRaw(clientConv, netMsg);

            // 短暂延迟让 KCP 刷新队列中的消息（500ms 确保 KCP 发送循环完成）
            await System.Threading.Tasks.Task.Delay(500);
            Debug.Log("【NetMgr】KickOff 已发送，开始断开 KCP...");
        }
        await KcpMgr.Instance.StopAsync();
        Debug.Log("【NetMgr】KCP 已断开");
    }

    /// <summary>
    /// 【客户端专用】请求服务器刷新玩家列表。
    /// 使用 RequestPlayerList 专用消息，不再滥用 KickOff。
    /// </summary>
    public static void RequestPlayerListRefresh()
    {
        uint clientConv = KcpMgr.Instance.ClientConv;
        Debug.Log("【NetMgr】RequestPlayerListRefresh ClientConv=" + clientConv);
        if (clientConv == 0)
        {
            Debug.LogWarning("【NetMgr】ClientConv 为 0，无法请求刷新玩家列表");
            return;
        }
        var netMsg = new NetMessage { RequestPlayerList = new RequestPlayerList() };
        Instance.Send(netMsg);
    }

    /// <summary>
    /// 【双端通用】通知服务器本玩家已从游戏返回房间。
    /// - 客户端：通过 KCP 发送 ReturnToRoom，服务器收到后标记并广播
    /// - 房主（服务器自身）：直接调用 HostServer 本地标记，不走网络（ClientConv==0）
    /// </summary>
    public static void NotifyReturnToRoom()
    {
        uint clientConv = KcpMgr.Instance.ClientConv;
        Debug.Log("【NetMgr】NotifyReturnToRoom ClientConv=" + clientConv);

        // ★ 房主本地路径：ClientConv==0 表示当前进程就是服务器，直接调用本地方法
        if (clientConv == 0)
        {
            if (HostServer.Instance != null)
            {
                Debug.Log("【NetMgr】房主本地标记返回房间");
                HostServer.Instance.MarkHostReturnedToRoom();
            }
            else
            {
                Debug.LogWarning("【NetMgr】ClientConv 为 0 且 HostServer 不存在，无法通知返回房间");
            }
            return;
        }

        // 客户端：通过网络发送 ReturnToRoom 专用消息
        var netMsg = new NetMessage { ReturnToRoom = new ReturnToRoom() };
        Instance.Send(netMsg);
    }

    #endregion

    #region 服务端发送：SendTo(conv, msg) / BroadcastToAll(msg)

    /// <summary>
    /// 【服务端专用】发送消息给指定客户端。
    /// </summary>
    public void SendTo(uint conv, NetMessage msg)
    {
        if (conv == 0)
        {
            Debug.LogWarning("【NetMgr】SendTo 收到 conv=0，跳过（HostConv 不是 KCP 实例）");
            return;
        }
        SendRaw(conv, msg);
    }

    /// <summary>
    /// 【服务端专用】广播消息给所有已连接客户端。
    /// ★ 使用 KcpMgr.BroadcastToAll 直接迭代 Keys，不分配中间 uint[] 数组。
    /// </summary>
    public void BroadcastToAll(NetMessage msg)
    {
        byte[] data = SerAndDeserPBTool.GetProtoBytes(msg);
        KcpMgr.Instance.BroadcastToAll(data);
    }

    #endregion

    #region 底层发送

    private void SendRaw(uint conv, NetMessage msg)
    {
        try
        {
            byte[] data = SerAndDeserPBTool.GetProtoBytes(msg);
            KcpMgr.Instance.SendAsync(conv, data).Forget();
        }
        catch (Exception e)
        {
            Debug.Log("【NetMgr】发送失败 conv=" + conv + "：" + e.Message);
        }
    }

    #endregion

    #region 收消息（protobuf 解析 + 入队）

    public void OnRecvData(uint conv, byte[] data)
    {
        try
        {
            NetMessage envelope = SerAndDeserPBTool.GetProtoMsg<NetMessage>(data);

            int msgId = envelope.MsgCase switch
            {
                NetMessage.MsgOneofCase.Heartbeat          => 1,
                NetMessage.MsgOneofCase.KickOff            => 2,
                NetMessage.MsgOneofCase.CreateRoom         => 10,
                NetMessage.MsgOneofCase.CreateRoomAck      => 11,
                NetMessage.MsgOneofCase.JoinRoom           => 12,
                NetMessage.MsgOneofCase.JoinRoomAck        => 13,
                NetMessage.MsgOneofCase.PlayerList         => 14,
                NetMessage.MsgOneofCase.StartGame          => 15,
                NetMessage.MsgOneofCase.GameStart          => 16,
                NetMessage.MsgOneofCase.RequestPlayerList  => 18,
                NetMessage.MsgOneofCase.ReturnToRoom       => 19,
                NetMessage.MsgOneofCase.InputTick          => 20,
                NetMessage.MsgOneofCase.PlayerInput        => 21,
                NetMessage.MsgOneofCase.CrystalSpawn       => 30,
                NetMessage.MsgOneofCase.CrystalPickup      => 31,
                NetMessage.MsgOneofCase.PlayerHit          => 32,
                NetMessage.MsgOneofCase.PlayerFall         => 33,
                NetMessage.MsgOneofCase.GameEnd            => 34,
                NetMessage.MsgOneofCase.PlayerRespawn      => 35,
                NetMessage.MsgOneofCase.GameTimerUpdate    => 36,
                NetMessage.MsgOneofCase.PlayerOffline      => 37,
                NetMessage.MsgOneofCase.PhaseSwitch       => 38,
                NetMessage.MsgOneofCase.CrystalDrop       => 39,
                NetMessage.MsgOneofCase.Reconnect          => 40,
                NetMessage.MsgOneofCase.ReconnectAck       => 41,
                NetMessage.MsgOneofCase.CatchUpTicks       => 42,
                _ => 0
            };

            IMessage payload = envelope.MsgCase switch
            {
                NetMessage.MsgOneofCase.Heartbeat          => envelope.Heartbeat,
                NetMessage.MsgOneofCase.KickOff            => envelope.KickOff,
                NetMessage.MsgOneofCase.CreateRoom         => envelope.CreateRoom,
                NetMessage.MsgOneofCase.CreateRoomAck      => envelope.CreateRoomAck,
                NetMessage.MsgOneofCase.JoinRoom           => envelope.JoinRoom,
                NetMessage.MsgOneofCase.JoinRoomAck        => envelope.JoinRoomAck,
                NetMessage.MsgOneofCase.PlayerList         => envelope.PlayerList,
                NetMessage.MsgOneofCase.StartGame          => envelope.StartGame,
                NetMessage.MsgOneofCase.GameStart          => envelope.GameStart,
                NetMessage.MsgOneofCase.RequestPlayerList  => envelope.RequestPlayerList,
                NetMessage.MsgOneofCase.ReturnToRoom       => envelope.ReturnToRoom,
                NetMessage.MsgOneofCase.InputTick          => envelope.InputTick,
                NetMessage.MsgOneofCase.PlayerInput        => envelope.PlayerInput,
                NetMessage.MsgOneofCase.CrystalSpawn       => envelope.CrystalSpawn,
                NetMessage.MsgOneofCase.CrystalPickup      => envelope.CrystalPickup,
                NetMessage.MsgOneofCase.PlayerHit          => envelope.PlayerHit,
                NetMessage.MsgOneofCase.PlayerFall         => envelope.PlayerFall,
                NetMessage.MsgOneofCase.PlayerRespawn      => envelope.PlayerRespawn,
                NetMessage.MsgOneofCase.GameEnd            => envelope.GameEnd,
                NetMessage.MsgOneofCase.GameTimerUpdate    => envelope.GameTimerUpdate,
                NetMessage.MsgOneofCase.PlayerOffline      => envelope.PlayerOffline,
                NetMessage.MsgOneofCase.PhaseSwitch        => envelope.PhaseSwitch,
                NetMessage.MsgOneofCase.CrystalDrop        => envelope.CrystalDrop,
                NetMessage.MsgOneofCase.Reconnect          => envelope.Reconnect,
                NetMessage.MsgOneofCase.ReconnectAck       => envelope.ReconnectAck,
                NetMessage.MsgOneofCase.CatchUpTicks       => envelope.CatchUpTicks,
                _ => null
            };

            if (msgId != 0 && payload != null)
            {
                _pending.Enqueue((msgId, conv, payload));
            }
        }
        catch (Exception e)
        {
            Debug.Log("【NetMgr】解析失败：" + e.Message);
        }
    }

    #endregion
}
// Done
