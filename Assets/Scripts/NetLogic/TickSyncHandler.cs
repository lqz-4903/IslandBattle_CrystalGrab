using GameProto;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════
///     TickSyncHandler —— 帧同步处理器（服务端）
/// ═══════════════════════════════════════════════════════════════
///
/// 【定位】
///   运行在主机（服务器）上的帧同步核心。
///   负责收集所有玩家的输入，组装成 InputTick 后分发。
///
/// 【帧同步流程】
///   1. 每帧时间间隔到达 → 开始新一帧
///   2. 等待所有活跃玩家提交 PlayerInput
///   3. 全部收齐 或 超时 → 组装 InputTick
///   4. 回调 HostServer.OnTickReady → 广播 + 本地执行
///   5. 帧号 +1，进入下一帧
///
/// 【超时策略】
///   某帧等待超过 maxWaitTime 后，未提交的玩家使用空输入（不动）
///   连续超时 N 次后可判定该玩家掉线
///
/// 【帧历史】
///   保存最近 N 帧的 InputTick，用于断线重连时发送 CatchUpTicks
/// ═══════════════════════════════════════════════════════════════
/// </summary>
public static class ClassForNothing3 { /* 为了避免调用时产生过长的说明 */ }

/// <summary>
/// TickSyncHandler —— 帧同步处理器（服务端）
/// </summary>
public class TickSyncHandler
{
    private HostServer _host;

    #region =============== 配置 ===============

    // 逻辑帧率（帧/秒）
    private const int DefaultTickRate = 15;

    // 等待单帧输入的最大时间（秒），超时用空输入填充
    private const float MaxWaitTime = 0.1f;

    // 保留的帧历史数量（用于断线重连）
    private const int MaxTickHistory = 1500; // 约100秒 @ 15fps

    #endregion

    #region =============== 状态 ===============

    // 当前逻辑帧号
    private int _currentTick;

    // 每帧时间间隔
    private float _tickInterval;

    // 帧累计计时器
    private float _tickTimer;

    // 当前帧等待输入的计时器
    private float _waitTimer;

    // 是否正在运行
    private bool _isRunning;

    // 是否正在等待当前帧的输入
    private bool _isWaitingForInput;

    /// <summary>
    /// 活跃玩家集合（playerId）
    /// </summary>
    private HashSet<int> _activePlayers = new();

    /// <summary>
    /// 当前帧已收到的输入：playerId -> PlayerInput
    /// </summary>
    private Dictionary<int, PlayerInput> _currentTickInputs = new();

    /// <summary>
    /// 帧历史：用于断线重连时发送 CatchUpTicks
    /// </summary>
    private List<InputTick> _tickHistory = new();

    #endregion

    #region =============== 构造 ===============
    public TickSyncHandler(HostServer host)
    {
        _host = host;
    }

    #endregion

    #region =============== 启动 / 停止 ===============

    /// <summary>
    /// 启动帧同步循环
    /// </summary>
    /// <param name="playerIds"></param>
    public void StartTickLoop(int[] playerIds, int tickRate = DefaultTickRate)
    {
        _tickInterval = 1f / tickRate;
        _currentTick = 0;
        _tickTimer = 0f;
        _waitTimer = 0f;
        _isWaitingForInput = false;
        _activePlayers.Clear();
        _currentTickInputs.Clear();
        _tickHistory.Clear();

        foreach (int id in playerIds)
            _activePlayers.Add(id);

        _isRunning = true;

        Debug.Log("【TickSyncHandler】帧同步启动，帧率：" + tickRate + "fps, 玩家数：" + _activePlayers.Count);
    }

    /// <summary>
    /// 停止帧同步
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        Debug.Log("【TickSyncHandler】帧同步停止， 最终帧号：" + _currentTick);
    }

    #endregion

    #region =============== 帧驱动（由 HostServer.Update 驱动） ===============

    /// <summary>
    /// 每帧调用，驱动帧同步逻辑
    /// </summary>
    /// <param name="deltaTime"></param>
    public void Tick(float deltaTime)
    {
        if (!_isRunning) return;

        // — 阶段1：等待帧间隔 —
        if (!_isWaitingForInput)
        {
            _tickTimer += deltaTime;
            if (_tickTimer >= _tickInterval)
            {
                _tickTimer -= _tickInterval;
                // 开启新的一帧
                BeginNewTick();
            }
            return;
        }

        // — 阶段2：等待当前 tick 的输入
        _waitTimer += deltaTime;

        bool allRecved = true;
        foreach (int playerId in _activePlayers)
        {
            if (!_currentTickInputs.ContainsKey(playerId))
            {
                allRecved = false;
                break;
            }
        }

        if (allRecved || _waitTimer >= MaxWaitTime)
            // 结束当前帧
            FinalizeTick();
    }


    #endregion

    #region =============== 帧逻辑 =============== 

    /// <summary>
    /// 开始新的一帧
    /// </summary>
    private void BeginNewTick()
    {
        _currentTick++;
        _currentTickInputs.Clear();
        _waitTimer = 0f;
        _isWaitingForInput = true;
    }

    /// <summary>
    /// 结束当前帧：组装 InputTick 并分发
    /// 超时未提交的玩家使用空输入（0，0，false，false）
    /// </summary>
    private void FinalizeTick()
    {
        _isWaitingForInput = false;

        // 组装 InputTick
        var inputTick = new InputTick
        {
            Tick = _currentTick
        };

        foreach (int playerId in _activePlayers)
        {
            if (_currentTickInputs.TryGetValue(playerId, out PlayerInput input))
            {
                inputTick.Inputs.Add(input);
            }
            else
            {
                // 超时：填充空输入
                inputTick.Inputs.Add(new PlayerInput
                {
                    PlayerId = playerId,
                    Tick = _currentTick,
                    MoveDir = 0,
                    Jump = false,
                    Attack = false,
                    Skill = false,
                    CameraYaw = 0f,
                    ChargeTime = 0f
                });
            }
        }

        // 保存到帧历史
        _tickHistory.Add(inputTick);
        if (_tickHistory.Count > MaxTickHistory)
            _tickHistory.RemoveAt(0);

        // 回调 HostServer：本地执行 + 广播
        _host.OnTickReady(inputTick);
    }

    #endregion

    #region =============== 输入接收 ===============

    /// <summary>
    /// 处理远程客户端发来的 PlayerInput（通过网络）
    /// </summary>
    /// <param name="input"></param>
    public void HandlePlayerInput(PlayerInput input)
    {
        if (!_isRunning || !_isWaitingForInput) return;
        if (!_activePlayers.Contains(input.PlayerId)) return;

        _currentTickInputs[input.PlayerId] = input;
    }

    /// <summary>
    /// 主机玩家提交本地输入（不走网络）
    /// 由PlayerController 等游戏逻辑调用
    /// </summary>
    /// <param name="playerId"></param>
    /// <param name="moveDir"></param>
    /// <param name="attack"></param>
    /// <param name="skill"></param>
    /// <param name="cameraYaw"></param>
    /// <param name="chargeTime"></param>
    public void SubmitLocalInput(int playerId, uint moveDir, bool jump, bool attack, bool skill, float cameraYaw, float chargeTime)
    {
        if (!_isRunning || !_isWaitingForInput) return;
        if (!_activePlayers.Contains(playerId)) return;

        _currentTickInputs[playerId] = new PlayerInput
        {
            PlayerId = playerId,
            Tick = _currentTick,
            MoveDir = moveDir,
            Jump = jump,
            Attack = attack,
            Skill = skill,
            CameraYaw = cameraYaw,
            ChargeTime = chargeTime
        };
    }

    #endregion

    #region =============== 玩家管理 ===============

    /// <summary>
    /// 从活跃玩家中移除玩家
    /// </summary>
    /// <param name="playerId"></param>
    public void RemovePlayer(int playerId)
    {
        _activePlayers.Remove(playerId);
        Debug.Log("【TickSyncHandler】移除玩家" + playerId + "剩余：" + _activePlayers.Count);
    }
    public int CurrentTick => _currentTick;

    #endregion

    #region =============== 断线重连 ===============

    /// <summary>
    /// 处理断线重连请求
    /// </summary>
    /// <param name="conv"></param>
    /// <param name="request"></param>
    public void HandleReconnect(uint conv, Reconnect request)
    {
        int playerId = request.PlayerId;
        int lastTick = request.LastTick;

        // 1.发送 ReconnectAck（多带一个服务端当前帧号）
        var ack = new ReconnectAck
        {
            Success = true,
            CatchUpFrom = lastTick + 1,
            CurrentServerTick = _currentTick
        };

        _host.SendToClient(conv, new NetMessage { ReconnectAck = ack });

        // 2.发送 CatchUpTicks
        SendCatchUpTicks(conv, lastTick);

        // 3.重新加入活跃集合
        _activePlayers.Add(playerId);

        Debug.Log("【TickSyncHandler】玩家" + playerId + "重连lastTick：" + lastTick + " 当前tick：" + _currentTick);
    }

    private void SendCatchUpTicks(uint conv, int lastTick)
    {
        var catchUp = new CatchUpTicks
        {
            FromTick = lastTick + 1,
            ToTick = _currentTick
        };

        foreach (var tick in _tickHistory)
        {
            if (tick.Tick > lastTick && tick.Tick <= _currentTick)
                catchUp.Ticks.Add(tick);
        }

        if (catchUp.Ticks.Count > 0)
        {
            _host.SendToClient(conv, new NetMessage { CatchUpTicks = catchUp });
            Debug.Log("【TickSyncHandler】发送追赶帧" + catchUp.Ticks.Count + "帧");
        }
    }

    #endregion
}
// DONE
