using GameProto;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════
///     HostServer —— 主机服务器协调器（单例 MonoBehaviour）
/// ═══════════════════════════════════════════════════════════════
///
/// 【定位】
///   游戏主机的核心协调组件，管理整个游戏生命周期。
///   持有各子处理器，负责事件注册/路由、消息收发、游戏状态管理。
///
/// 【架构】
///   HostServer (MonoBehaviour 单例)
///   ├── RoomHandler      —— 房间管理
///   ├── TickSyncHandler —— 帧同步（服务端）
///   ├── GameEventHandler —— 游戏事件处理
///   └── TickExecutor    —— 帧执行（客户端侧，MonoBehaviour）
///
/// 【生命周期】
///   1. CreateRoom  → 初始化 KcpMgr 服务器，创建房间
///   2. JoinRoom    → 等待玩家加入
///   3. StartGame   → 启动帧同步循环
///   4. 游戏中      → 收集输入、分发帧、处理事件
///   5. GameEnd     → 结算，停止帧同步
/// ═══════════════════════════════════════════════════════════════
/// </summary>
public static class ClassForNothing5 { /* 为了避免调用时产生过长的说明 */ }

/// <summary>
/// HostServer —— 主机服务器协调器（单例 MonoBehaviour）
/// </summary>
public class HostServer : MonoBehaviour
{
    #region =============== 单例 ===============
    private static HostServer _instance;
    public static HostServer Intansce => _instance;
    private HostServer() { }

    #endregion

    #region =============== 子处理器 ===============

    // 房间管理（创建/加入/开始游戏）
    private RoomHandler _roomHandler;
    // 帧同步核心（服务端收集输入、分发帧）
    private TickSyncHandler _tickSyncHandler;
    // 帧执行器（客户端执行帧、推进游戏）
    private GameEventHandler _gameEventHandler;
    // 游戏事件（水晶、受伤、坠落、结算）
    private TickExecutor _tickExcutor;

    #endregion

    #region =============== 状态 =============== 

    // 服务器是否正在运行
    private bool _isRunning;
    public bool IsRunning => _isRunning;

    // 游戏是否已开始
    private bool _isGameStarted;
    public bool IsGameStarted => _isGameStarted;

    // 当前房间数据（由RoomHandler管理）
    public RoomData CurrentRoom;

    // 默认端口
    private const int _DEFAULT_PORT = 8888;

    #endregion

    #region =============== 生命周期 =============== 

    private void Awake()
    {
        _instance = this;

        _roomHandler = new RoomHandler(this);
        _tickSyncHandler = new TickSyncHandler(this);
        _gameEventHandler = new GameEventHandler(this);

        _tickExcutor = GetComponent<TickExecutor>();
        if (_tickExcutor = null)
            _tickExcutor = gameObject.AddComponent<TickExecutor>();
    }


    #endregion

    #region =============== 事件注册 ===============

    private void SubscribeEvents()
    {
        // — 房间消息 —
        EventCenter.AddListener(10, OnCreateRoom);
        EventCenter.AddListener(12, OnJoinRoom);
        EventCenter.AddListener(16, OnGameStart);

        // — 帧同步消息 —
        EventCenter.AddListener(21, OnPlayerInput);

        // — 游戏事件消息 —

        // — 重连消息 —
        EventCenter.AddListener(40, OnReconnect);

        // — 心跳消息 —
        EventCenter.AddListener(1, OnHeartbeat);
    }

    private void UnSubscribEvents()
    {
        EventCenter.RemoveListener(10, OnCreateRoom);
        EventCenter.RemoveListener(12, OnJoinRoom);
        EventCenter.RemoveListener(16, OnGameStart);

        EventCenter.RemoveListener(21, OnPlayerInput);


        EventCenter.RemoveListener(40, OnReconnect);
        EventCenter.RemoveListener(1, OnHeartbeat);
    }

    // — 房间 —
    private void OnCreateRoom(uint conv, IMessage msg)
    {
        if (!_isRunning) return;
        _roomHandler.HandleCreateRoom(conv, msg as CreateRoom);
    }

    private void OnJoinRoom(uint conv, IMessage msg)
    {
        if (!_isRunning) return;
        _roomHandler.HandleJoinRoom(conv, msg as JoinRoom);
    }

    private void OnGameStart(uint conv, IMessage msg)
    {
        if (!_isRunning || _isGameStarted) return;
        _roomHandler.HandleGameStart(conv, msg as GameStart);
    }

    // — 帧同步 —
    private void OnPlayerInput(uint conv, IMessage msg)
    {
        if (!_isRunning || _isGameStarted) return;
        _tickSyncHandler.HandlePlayerInput(msg as PlayerInput);
    }

    // — 游戏事件 —

    // — 重连 —
    private void OnReconnect(uint conv, IMessage msg)
    {
        if (!_isRunning) return;
        var request = msg as Reconnect;
        
        _roomHandler.HandleReconnect(conv, request.PlayerId);

        _tickSyncHandler.HandleReconnect(conv, request);
    }

    // — 心跳 —
    private void OnHeartbeat(uint conv, IMessage msg)
    {
        // 服务器收到心跳后原样回发
    }


    #endregion

    #region =============== 游戏状态管理 ===============

    /// <summary>
    /// 由 RoomHandler 调用，标记游戏开始
    /// </summary>
    public void OnStartGame()
    {
        _isGameStarted = true;

        // 将玩家集合传给帧同步和游戏事件处理器
        var playerIds = CurrentRoom.GetAllPlayerProtos();

        Debug.Log("【HostServer】游戏开始，玩家数：" + playerIds);
    }


    #endregion


    #region =============== 网络工具方法 ===============

    /// <summary>
    /// 向指定客户端发送消息
    /// </summary>
    /// <param name="conv"></param>
    /// <param name="msg"></param>
    public void SendToClient(uint conv, NetMessage msg)
    {
        byte[] data = SerAndDeserPBTool.GetProtoBytes(msg);
        KcpMgr.Instance.SendAsync(conv, data).Forget();
    }

    /// <summary>
    /// 向所有已连接客户端广播消息
    /// </summary>
    /// <param name="msg"></param>
    public void BroadcastToAll(NetMessage msg)
    {
        byte[] data = SerAndDeserPBTool.GetProtoBytes(msg);
        uint[] clients = KcpMgr.Instance.GetConnectClients();
        foreach (uint conv in clients)
        {
            KcpMgr.Instance.SendAsync(conv, data).Forget();
        }
    }

    /// <summary>
    /// 像除指定客户端外的所有客户端广播
    /// </summary>
    /// <param name="excludeConv"></param>
    /// <param name="msg"></param>
    public void BroadcastExcept(uint excludeConv, NetMessage msg)
    {
        byte[] data = SerAndDeserPBTool.GetProtoBytes(msg);
        uint[] clients = KcpMgr.Instance.GetConnectClients();
        foreach (uint conv in clients)
        {
            if (conv != excludeConv)
                KcpMgr.Instance.SendAsync(conv, data).Forget();
        }
    }

    #endregion
}
