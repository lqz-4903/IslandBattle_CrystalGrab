using GameProto;
using Google.Protobuf;
using System;
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
public static class ClassForNothing1 { /* 为了避免调用时产生过长的说明 */ }

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
    // 游戏事件（水晶、受伤、坠落、结算）
    private GameEventHandler _gameEventHandler;
    // 帧执行器（客户端执行帧、推进游戏）
    private TickExecutor _tickExcutor;

    #endregion

    #region =============== 状态 =============== 

    // 服务器是否正在运行
    private bool _isRunning;
    public bool IsRunning => _isRunning;

    // 游戏是否已开始
    private bool _isGameStarted;
    public bool IsGameStarted => _isGameStarted;

    // 供外部调用
    public GameEventHandler GameEventHandler => _gameEventHandler;

    // 当前房间数据（由RoomHandler管理）
    public RoomData CurrentRoom;

    // 默认端口
    private const int DefaultPort = 8888;

    #endregion

    #region =============== 生命周期 =============== 

    private void Awake()
    {
        _instance = this;

        _roomHandler = new RoomHandler(this);
        _tickSyncHandler = new TickSyncHandler(this);
        _gameEventHandler = new GameEventHandler(this);

        _tickExcutor = GetComponent<TickExecutor>();
        if (_tickExcutor == null)
            _tickExcutor = gameObject.AddComponent<TickExecutor>();
    }

    private void Start()
    {
        SubscribeEvents();
    }

    private void Update()
    {
        if (!IsRunning || !_isGameStarted) return;

        _tickSyncHandler.Tick(Time.deltaTime);
        _gameEventHandler.Tick(Time.deltaTime);
    }

    private void OnDestroy()
    {
        UnSubscribEvents();
        Shutdown();
    }

    #endregion

    #region =============== 启动 / 关闭 ===============

    /// <summary>
    /// 以主机模式启动（服务器 + 本地玩家）
    /// UI 层调用此方法创建房间
    /// </summary>
    /// <param name="hostPlayerName"></param>
    /// <param name="port"></param>
    public async void StartHost(string hostPlayerName, int port = DefaultPort)
    {
        if (_isRunning)
        {
            Debug.Log("【HostServer】已在运行中");
            return;
        }

        try
        {
            // 1.启动 KCP 服务器
            await KcpMgr.Instance.StartAsServerAsync(port);
            _isRunning = true;

            // 2.注册 KcpMgr 连接 / 断开回调
            KcpMgr.Instance.OnClientConnected = OnClientConnected;
            KcpMgr.Instance.OnClientDisconnected = OnClientDisconnected;

            // 3.创建房间
            _roomHandler.CreateLocalRoom(hostPlayerName);

            // 4.初始化帧执行器（主机模式）
            _tickExcutor.Init(isHost: true);

            Debug.Log("【HostServer】主机启动成功，端口：" + port);
        }
        catch (Exception e)
        {
            Debug.Log("【HostServer】启动失败：" + e.Message);
        }
    }

    public async void Shutdown()
    {
        if (!_isRunning) return;

        _isGameStarted = false;
        _isRunning = false;

        _tickSyncHandler.Stop();
        _gameEventHandler.Stop();

        await KcpMgr.Instance.StopAsync();

        CurrentRoom = null;
        Debug.Log("【HostServer】主机已关闭");
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
        EventCenter.AddListener(31, OnCrystalPickup);
        EventCenter.AddListener(32, OnPlayerHit);
        EventCenter.AddListener(33, OnPlayerFall);
        EventCenter.AddListener(35, OnPlayerRespawn);
        

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
        EventCenter.RemoveListener(31, OnCrystalPickup);
        EventCenter.RemoveListener(32, OnPlayerHit);
        EventCenter.RemoveListener(33, OnPlayerFall);
        EventCenter.RemoveListener(35, OnPlayerRespawn);
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
        if (!_isRunning || !_isGameStarted) return;
        _tickSyncHandler.HandlePlayerInput(msg as PlayerInput);
    }

    // — 游戏事件 —

    private void OnCrystalPickup(uint conv, IMessage msg)
    {
        if (!_isRunning || !_isGameStarted) return;
        _gameEventHandler.HandleCrystalPickup(msg as CrystalPickup);
    }

    private void OnPlayerHit(uint conv, IMessage msg)
    {
        if (!_isRunning || !_isGameStarted) return;
        _gameEventHandler.HandlePlayerHit(msg as PlayerHit);
    }

    private void OnPlayerFall(uint conv, IMessage msg)
    {
        if (!_isRunning || !_isGameStarted) return;
        _gameEventHandler.HandlePlayerFall(msg as PlayerFall);
    }

    private void OnPlayerRespawn(uint conv, IMessage msg)
    {
        if (!_isRunning || !_isGameStarted) return;
        _gameEventHandler.HandlePlayerRespawn(msg as PlayerRespawn);
    }


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
        var hb = new HeartBeat { Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        SendToClient(conv, new NetMessage { Heartbeat = hb });
    }


    #endregion

    #region =============== KcpMgr 连接回调 ===============

    private void OnClientConnected(uint conv)
    {
        Debug.Log("【HostServer】新客户端连接 Conv：" + conv);
    }

    private void OnClientDisconnected(uint conv, string reason)
    {
        Debug.Log("【HostServer】客户端断开 Conv：" + conv + " 原因：" + reason);

        // 从房间移除
        _roomHandler.HandlePlayerDisconnect(conv);

        // 从帧同步中移除
        if (_isGameStarted)
        {
            int playerId = _roomHandler.GetPlayerIdByConv(conv);
            if (playerId > 0)
                _tickSyncHandler.RemovePlayer(playerId);
        }
    }


    #endregion

    #region =============== 游戏状态管理 ===============

    /// <summary>
    /// 由 RoomHandler 调用，标记游戏开始
    /// </summary>
    public void OnStartGame(int randomSeed)
    {
        _isGameStarted = true;

        // 将玩家集合传给帧同步和游戏事件处理器
        var playerIds = CurrentRoom.GetAllPlayerIds();
        _tickSyncHandler.StartTickLoop(playerIds);
        _gameEventHandler.StartGameLoop(randomSeed);

        Debug.Log("【HostServer】游戏开始，玩家数：" + playerIds.Length);
    }

    /// <summary>
    /// 由 GameEventHandler 调用，标记游戏结束
    /// </summary>
    public void OnGameEnded()
    {
        _isGameStarted = false;
        _tickSyncHandler.Stop();
        _gameEventHandler.Stop();
        Debug.Log("【HostServer】游戏结束");
    }

    #endregion

    #region =============== 主机玩家输入 ===============

    /// <summary>
    /// 本机玩家提交给本地输入（由PlayerController调用）
    /// 不走网络 直接交给 TickSyncHandler
    /// </summary>
    public void SubmitHostInput(uint moveDir, bool jump, bool attack, bool skill, float cameraYaw, float chargeTime)
    {
        if (!_isRunning || !_isGameStarted || CurrentRoom == null) return;

        int hostPlayerId = CurrentRoom.HostPlayerId;
        _tickSyncHandler.SubmitLocalInput(hostPlayerId, moveDir, jump, attack, skill, cameraYaw, chargeTime);
    }
    #endregion

    #region =============== 帧就绪回调 ===============

    /// <summary>
    /// 由 TickSyncHandler 在每帧输入收齐后回调
    /// 负责：1.交给本地 TickExecutor 执行 2.广播给所有远程客户端
    /// </summary>
    /// <param name="tick"></param>
    internal void OnTickReady(InputTick tick)
    {
        // 1.本地执行
        _tickExcutor.EnqueueTick(tick);

        // 2.广播给远程客户端
        var enevlope = new NetMessage { InputTick = tick };
        BroadcastToAll(enevlope);
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
