using GameProto;
using Google.Protobuf;
using System;
using System.Collections.Generic;
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
    public static HostServer Instance => _instance;
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

    // 当前房间是否已进行过游戏（用于区分"游戏前"和"游戏后"显示玩家状态）
    private bool _hasGamePlayed;
    public bool HasGamePlayed => _hasGamePlayed;

    // 供外部调用
    public GameEventHandler GameEventHandler => _gameEventHandler;

    // 当前房间数据（由RoomHandler管理）
    public RoomData CurrentRoom;

    // 默认端口
    private const int DefaultPort = 8888;

    #endregion

    #region =============== 心跳超时检测 ===============

    // 每个远程客户端的最后一次心跳时间戳（conv → Unix毫秒时间戳）
    // 仅主线程（Update/事件回调）访问，不需要 ConcurrentDictionary
    private Dictionary<uint, long> _lastHeartbeatTime = new();

    // 心跳超时阈值（毫秒），超过此时间未收到心跳即判定离线
    private const long HeartbeatTimeoutMs = 5000;

    // 心跳检查间隔（秒）
    private const float HeartbeatCheckInterval = 1f;

    // 心跳检查累计计时器
    private float _heartbeatCheckTimer = 0f;

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

    /// <summary>
    /// 生成房间号（供 Lua 层调用，由服务端统一分发）
    /// </summary>
    public string GenerateRoomId()
    {
        return _roomHandler.GenerateRoomId();
    }

    /// <summary>
    /// 获取当前房间号（供 Lua 层读取，与服务器验证的一致）
    /// </summary>
    public string GetCurrentRoomId()
    {
        return CurrentRoom != null ? CurrentRoom.RoomId : null;
    }

    private void Start()
    {
        SubscribeEvents();
    }

    private void Update()
    {
        if (!IsRunning) return;

        // ★ 心跳超时检测（始终运行，不限于游戏状态）
        CheckHeartbeatTimeouts();

        if (!_isGameStarted) return;

        // Unity Time.deltaTime 在入口处转换为 Fix64，确保 Tick 链中全部使用定点数
        Fix64 dt = Fix64.FromFloat(Time.deltaTime);
        _tickSyncHandler.Tick(dt);
        _gameEventHandler.Tick(dt);
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

            // 1.5 确保 NetMgr 存在（负责从 KcpMgr 取数据 → 解析 protobuf → 派发到 EventCenter）
            _ = NetMgr.Instance;

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

    /// <summary>
    /// 启动主机并立即开始游戏（供 Lua 层调用，一步完成）
    /// 如果已有运行中的服务（如上一局刚结束），先关闭再重启
    /// </summary>
    /// <summary>
    /// 启动主机并立即开始游戏（供 Lua 层调用）。
    /// gameDuration 参数为 float 以兼容 XLua，内部转换为 Fix64。
    /// </summary>
    public async void StartHostAndGame(string hostPlayerName, int randomSeed, float gameDuration = 120f, int port = DefaultPort)
    {
        if (_isRunning)
        {
            Debug.Log("【HostServer】已在运行中，关闭旧服务后重启...");
            // 停止游戏逻辑
            _isGameStarted = false;
            _tickSyncHandler.Stop();
            _gameEventHandler.Stop();
            // 停止 KCP 服务器
            await KcpMgr.Instance.StopAsync();
            _isRunning = false;
            CurrentRoom = null;
        }

        try
        {
            // 1.启动 KCP 服务器
            await KcpMgr.Instance.StartAsServerAsync(port);
            _isRunning = true;

            // 1.5 确保 NetMgr 存在（负责从 KcpMgr 取数据 → 解析 protobuf → 派发到 EventCenter）
            _ = NetMgr.Instance;

            // 2.注册 KcpMgr 连接 / 断开回调
            KcpMgr.Instance.OnClientConnected = OnClientConnected;
            KcpMgr.Instance.OnClientDisconnected = OnClientDisconnected;

            // 3.创建房间
            _roomHandler.CreateLocalRoom(hostPlayerName);

            // 4.初始化帧执行器（主机模式）
            _tickExcutor.Init(isHost: true);

            Debug.Log("【HostServer】主机启动成功，端口：" + port);

            // 5.启动游戏循环
            OnStartGame(randomSeed, gameDuration);
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

        try
        {
            await KcpMgr.Instance.StopAsync();
        }
        catch (Exception e)
        {
            Debug.Log("【HostServer】关闭 KCP 异常：" + e.Message);
        }

        CurrentRoom = null;
        Debug.Log("【HostServer】主机已关闭");
    }

    /// <summary>
    /// 【房主专用】广播 KickOff 通知所有客户端房间已解散，短暂延迟后关闭服务器。
    /// </summary>
    public async void BroadcastKickOffAndShutdown(string reason)
    {
        Debug.Log("【HostServer】BroadcastKickOffAndShutdown reason=" + reason);
        if (_isRunning && CurrentRoom != null)
        {
            var kickOff = new KickOff { Reason = reason };
            var netMsg = new NetMessage { KickOff = kickOff };
            BroadcastToAll(netMsg);
            Debug.Log("【HostServer】KickOff 已广播给所有客户端");

            // 短暂延迟让 KCP 刷出消息（500ms 确保 KCP 发送循环完成）
            await System.Threading.Tasks.Task.Delay(500);
        }
        Shutdown();
    }

    #endregion

    #region =============== 事件注册 ===============

    private void SubscribeEvents()
    {
        // — 房间消息 —
        EventCenter.AddListener(10, OnCreateRoom);
        EventCenter.AddListener(12, OnJoinRoom);
        EventCenter.AddListener(16, OnGameStart);

        // — 客户端请求刷新玩家列表 —
        EventCenter.AddListener(18, OnRequestPlayerList);

        // — 客户端通知已返回房间 —
        EventCenter.AddListener(19, OnReturnToRoom);

        // — 客户端离开（KickOff） —
        EventCenter.AddListener(2, OnKickOff);

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
        EventCenter.RemoveListener(18, OnRequestPlayerList);
        EventCenter.RemoveListener(19, OnReturnToRoom);
        EventCenter.RemoveListener(2, OnKickOff);
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

    private void OnRequestPlayerList(uint conv, IMessage msg)
    {
        if (!_isRunning) return;
        _roomHandler.HandleRequestPlayerList(conv);
    }

    private void OnReturnToRoom(uint conv, IMessage msg)
    {
        if (!_isRunning) return;
        _roomHandler.HandleReturnToRoom(conv, msg as ReturnToRoom);
    }

    private void OnKickOff(uint conv, IMessage msg)
    {
        if (!_isRunning) return;
        var kickOff = msg as KickOff;
        Debug.Log("【HostServer】收到 KickOff conv=" + conv + " reason=" + (kickOff != null ? kickOff.Reason : "null"));
        _roomHandler.HandleKickOff(conv, kickOff);
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
        // 更新该客户端的心跳时间戳
        _lastHeartbeatTime[conv] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 回复心跳（携带服务器时间戳，供客户端计算 RTT）
        var hb = new HeartBeat { Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        SendToClient(conv, new NetMessage { Heartbeat = hb });
    }


    #endregion

    #region =============== KcpMgr 连接回调 ===============

    private void OnClientConnected(uint conv)
    {
        Debug.Log("【HostServer】新客户端连接 Conv：" + conv);

        // ★ 初始化心跳时间戳（刚连接时视为刚收到心跳）
        _lastHeartbeatTime[conv] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private void OnClientDisconnected(uint conv, string reason)
    {
        Debug.Log("【HostServer】客户端断开 Conv：" + conv + " 原因：" + reason);

        // ★ 移除心跳追踪
        _lastHeartbeatTime.Remove(conv);

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

    #region =============== 心跳超时检测 ===============

    /// <summary>
    /// 定期检查所有远程客户端的心跳是否超时。
    /// 由 Update() 每帧调用，按 HeartbeatCheckInterval 间隔实际执行检查。
    /// </summary>
    private void CheckHeartbeatTimeouts()
    {
        _heartbeatCheckTimer += Time.deltaTime;
        if (_heartbeatCheckTimer < HeartbeatCheckInterval) return;
        _heartbeatCheckTimer = 0f;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // 收集超时的 conv（避免在遍历时修改字典）
        System.Collections.Generic.List<uint> timedOutConvs = null;

        foreach (var kvp in _lastHeartbeatTime)
        {
            uint conv = kvp.Key;
            long lastHb = kvp.Value;

            // 跳过主机（conv=0 不是 KCP 连接）
            if (conv == RoomData.HostConv) continue;

            if (now - lastHb > HeartbeatTimeoutMs)
            {
                timedOutConvs ??= new System.Collections.Generic.List<uint>();
                timedOutConvs.Add(conv);
            }
        }

        if (timedOutConvs != null)
        {
            foreach (uint conv in timedOutConvs)
            {
                HandlePlayerTimeout(conv);
            }
        }
    }

    /// <summary>
    /// 处理心跳超时的玩家：广播 PlayerOffline 通知 → 断开 KCP 连接（触发清理流程）
    /// </summary>
    private void HandlePlayerTimeout(uint conv)
    {
        // 在断开前获取玩家信息（断开后数据会被清除）
        int playerId = 0;
        string playerName = "未知玩家";
        if (CurrentRoom != null && CurrentRoom.ConvToPlayer.TryGetValue(conv, out ServerPlayer p))
        {
            playerId = p.PlayerId;
            playerName = p.PlayerName;
        }

        Debug.LogWarning("【HostServer】玩家心跳超时，即将断开 Conv：" + conv + " 玩家：" + playerName);

        // 1. 向剩余客户端广播 PlayerOffline 专用消息
        var playerOffline = new PlayerOffline { PlayerId = playerId, PlayerName = playerName };
        var notifyMsg = new NetMessage { PlayerOffline = playerOffline };
        BroadcastExcept(conv, notifyMsg);

        // 同时通知房主本地 Lua 层（BroadcastExcept 跳过了远程 conv，房主需要单独处理）
        try { NetMgr.OnPlayerOfflineCallback?.Invoke(playerOffline); }
        catch (System.Exception e) { Debug.Log("【HostServer】OnPlayerOfflineCallback 房主离线通知异常：" + e.Message); }

        // 2. 断开 KCP 连接（触发 OnClientDisconnected → 房间清理 + 帧同步移除 + PlayerList 广播）
        _ = KcpMgr.Instance.DisconClientAsync(conv);
    }

    #endregion

    #region =============== 游戏状态管理 ===============

    /// <summary>
    /// 从已有房间启动游戏（服务器必须已在运行中）。
    /// 通过 RoomHandler 广播 GameStart 到所有客户端，同时启动本地游戏循环。
    /// 与 StartHostAndGame 的区别：不重启 KCP 服务器，不重建房间。
    /// </summary>
    /// <param name="randomSeed">确定性随机种子</param>
    /// <param name="gameDuration">游戏时长（秒）</param>
    public void StartGame(int randomSeed, float gameDuration = 120f)
    {
        if (!_isRunning)
        {
            Debug.Log("【HostServer】服务器未运行，无法开始游戏");
            return;
        }
        if (_isGameStarted)
        {
            Debug.Log("【HostServer】游戏已在进行中");
            return;
        }
        if (CurrentRoom == null)
        {
            Debug.Log("【HostServer】没有房间，无法开始游戏");
            return;
        }

        // 委托给 RoomHandler（已有完整逻辑：校验房主 + 人数 + 广播 + 启动）
        var request = new GameStart
        {
            RandomSeed = randomSeed,
            GameDuration = gameDuration,
            TargetScore = 10,
            TickRate = 15
        };
        _roomHandler.HandleGameStart(RoomData.HostConv, request);
    }

    /// <summary>由 RoomHandler 调用，标记游戏开始</summary>
    public void OnStartGame(int randomSeed, float gameDuration = 120f)
    {
        _isGameStarted = true;
        _hasGamePlayed = true;

        var playerIds = CurrentRoom.GetAllPlayerIds();
        _tickSyncHandler.StartTickLoop(playerIds);

        // 将 float 转为 Fix64，游戏逻辑层全部使用定点数
        Fix64 duration = Fix64.FromFloat(gameDuration);
        _gameEventHandler.StartGameLoop(randomSeed, gameDuration: duration);

        Debug.Log("【HostServer】游戏开始，玩家数：" + playerIds.Length + " 游戏时长：" + gameDuration + "秒");
    }

    /// <summary>
    /// 由 GameEventHandler 调用，标记游戏结束
    /// </summary>
    public void OnGameEnded()
    {
        _isGameStarted = false;
        _tickSyncHandler.Stop();
        _gameEventHandler.Stop();

        // ★ 重置返回房间状态追踪（所有玩家初始为"还在游戏中"）
        _roomHandler.ResetReturnedPlayers();

        Debug.Log("【HostServer】游戏结束");
    }

    /// <summary>
    /// 刷新玩家列表（由 Lua 层调用，游戏结束后回到房间时触发）
    /// 广播当前房间的完整玩家列表给所有客户端，同时通知本地 Lua 回调
    /// </summary>
    public void RefreshPlayerList()
    {
        if (_roomHandler != null && CurrentRoom != null)
        {
            _roomHandler.BroadcastPlayerList();
        }
    }

    /// <summary>
    /// 查询玩家是否还在游戏中（未返回房间），供 Lua 层显示"还在游戏中"状态
    /// </summary>
    public bool IsPlayerStillInGame(int playerId)
    {
        return _roomHandler != null && _roomHandler.IsPlayerStillInGame(playerId);
    }

    /// <summary>
    /// 【房主专用】标记房主自身已从游戏返回房间，并广播玩家列表。
    /// 房主没有 KCP ClientConv，无法通过 NotifyReturnToRoom 走网络路径，
    /// 因此需要此本地方法直接操作 RoomHandler。
    /// </summary>
    public void MarkHostReturnedToRoom()
    {
        if (_roomHandler != null && CurrentRoom != null)
        {
            _roomHandler.MarkPlayerReturnedToRoom(RoomData.HostConv);
            _roomHandler.BroadcastPlayerList();
            Debug.Log("【HostServer】房主已标记返回房间并广播玩家列表");
        }
    }

    #endregion

    #region =============== 主机玩家输入 ===============

    /// <summary>
    /// 本机玩家提交本地输入（由 PlayerController 调用）。
    /// cameraYawRaw/chargeTimeRaw 为 Fix64.Raw（long），不再经过 float 转换丢失精度。
    /// </summary>
    public void SubmitHostInput(uint moveDir, bool jump, bool attack, bool skill, long cameraYawRaw, long chargeTimeRaw)
    {
        if (!_isRunning || !_isGameStarted || CurrentRoom == null) return;

        int hostPlayerId = CurrentRoom.HostPlayerId;
        _tickSyncHandler.SubmitLocalInput(hostPlayerId, moveDir, jump, attack, skill,
            new Fix64(cameraYawRaw), new Fix64(chargeTimeRaw));
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
        // 统一走 NetMgr，自带 conv=0 保护
        NetMgr.Instance.SendTo(conv, msg);
    }

    /// <summary>
    /// 向所有已连接客户端广播消息
    /// </summary>
    public void BroadcastToAll(NetMessage msg)
    {
        // 统一走 NetMgr，自带 conv=0 过滤
        NetMgr.Instance.BroadcastToAll(msg);
    }

    /// <summary>
    /// 向除指定客户端外的所有客户端广播
    /// </summary>
    public void BroadcastExcept(uint excludeConv, NetMessage msg)
    {
        byte[] data = SerAndDeserPBTool.GetProtoBytes(msg);
        uint[] clients = KcpMgr.Instance.GetConnectClients();
        foreach (uint conv in clients)
        {
            if (conv != excludeConv && conv != 0)
                KcpMgr.Instance.SendAsync(conv, data).Forget();
        }
    }

    #endregion
}
