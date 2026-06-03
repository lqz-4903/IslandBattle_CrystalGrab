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
///   ├── FrameSyncHandler —— 帧同步（服务端）
///   ├── GameEventHandler —— 游戏事件处理
///   └── FrameExecutor    —— 帧执行（客户端侧，MonoBehaviour）
///
/// 【生命周期】
///   1. CreateRoom  → 初始化 KcpMgr 服务器，创建房间
///   2. JoinRoom    → 等待玩家加入
///   3. StartGame   → 启动帧同步循环
///   4. 游戏中      → 收集输入、分发帧、处理事件
///   5. GameEnd     → 结算，停止帧同步
/// ═══════════════════════════════════════════════════════════════
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
    private FrameSyncHandler _frameSyncHandler;
    // 帧执行器（客户端执行帧、推进游戏）
    private GameEventHandler _gameEventHandler;
    // 游戏事件（水晶、受伤、坠落、结算）
    private FrameExcutor _frameExcutor;

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
    private const int DEFAULT_PORT = 8888;

    #endregion

    #region =============== 生命周期 =============== 

    private void Awake()
    {
        _instance = this;

        _roomHandler = new RoomHandler(this);
        _frameSyncHandler = new FrameSyncHandler(this);
        _gameEventHandler = new GameEventHandler(this);

        _frameExcutor = GetComponent<FrameExcutor>();
        if (_frameExcutor = null)
            _frameExcutor = gameObject.AddComponent<FrameExcutor>();
    }


    #endregion

    #region =============== 事件注册 ===============

    private void SubscribeEvents()
    {
        EventCenter.AddListener(10, OnCreateRoom);
        EventCenter.AddListener(12, OnJoinRoom);
        EventCenter.AddListener(16, OnGameStart);
 
    }

    private void UnSubscribEvents()
    {

    }

    // 房间
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
        if (!_isRunning) return;
        _roomHandler.HandlerGameStart(conv, msg as GameStart);
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
