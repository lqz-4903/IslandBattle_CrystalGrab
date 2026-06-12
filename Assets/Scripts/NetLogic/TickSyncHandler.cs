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
    private const int DefaultTickRate = 30;

    // 等待单帧输入的最大时间（使用 Fix64 确定性比较）
    private static readonly Fix64 MaxWaitTime = Fix64.FromFloat(0.05f);

    // 保留的帧历史数量（用于断线重连）
    private const int MaxTickHistory = 3000; // 约100秒 @ 30fps

    #endregion

    #region =============== 状态 ===============

    // 当前逻辑帧号
    private int _currentTick;

    // 每帧时间间隔（Fix64 确定性）
    private Fix64 _tickInterval;

    // 帧累计计时器（Fix64 确定性）
    private Fix64 _tickTimer;

    // 当前帧等待输入的计时器（Fix64 确定性）
    private Fix64 _waitTimer;

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
    /// Phase 2: 上一 tick 执行后的服务端权威位置。
    /// 由 Lua PlayerManager 在主机 OnFrameEnd 后通过 HostServer 桥接填入。
    /// 在组装下一 tick 的 InputTick 时附加到 PlayerInput.ResultPosX/Y/Z 中。
    /// </summary>
    private Dictionary<int, (long x, long y, long z)> _previousTickPositions = new();

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
        _tickInterval = Fix64.One / Fix64.FromInt(tickRate);
        _currentTick = 0;
        _tickTimer = Fix64.Zero;
        _waitTimer = Fix64.Zero;
        _isWaitingForInput = false;
        _activePlayers.Clear();
        _currentTickInputs.Clear();
        _previousTickPositions.Clear();
        _tickHistory.Clear();

        foreach (int id in playerIds)
            _activePlayers.Add(id);

        _isRunning = true;

        Debug.Log("【TickSyncHandler】帧同步启动，帧率：" + tickRate + "fps, 间隔：" + _tickInterval.ToFloat() + "s, 玩家数：" + _activePlayers.Count);
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
    /// <summary>每帧驱动，deltaTime 为 Fix64 确定性时间增量</summary>
    public void Tick(Fix64 deltaTime)
    {
        if (!_isRunning) return;

        // — 阶段1：等待帧间隔 —
        if (!_isWaitingForInput)
        {
            _tickTimer += deltaTime;
            if (_tickTimer >= _tickInterval)
            {
                _tickTimer -= _tickInterval;
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
        // ★ 不在此处清除 _previousTickPositions — 位置由 _CaptureAuthPositions 在
        //   tick N 执行后写入，需要保留到 FinalizeTick(N+1) 才能附加到 InputTick。
        //   清除统一在 FinalizeTick() 读取后执行。
        _waitTimer = Fix64.Zero;
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
            PlayerInput input;
            if (_currentTickInputs.TryGetValue(playerId, out PlayerInput existing))
            {
                input = existing;
            }
            else
            {
                // 超时：填充空输入
                input = new PlayerInput
                {
                    PlayerId = playerId,
                    Tick = _currentTick,
                    MoveDir = 0,
                    Jump = false,
                    Attack = false,
                    Skill = false,
                    CameraYaw = 0L,
                    ChargeTime = 0L,
                    CameraPitch = 0L
                };
            }

            // Phase 2: 附加上一 tick 执行后的服务端权威位置
            if (_previousTickPositions.TryGetValue(playerId, out var authPos))
            {
                input.ResultPosX = authPos.x;
                input.ResultPosY = authPos.y;
                input.ResultPosZ = authPos.z;
            }

            inputTick.Inputs.Add(input);
        }

        // 清空上一 tick 位置（仅使用一次）
        _previousTickPositions.Clear();

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
    /// <summary>
    /// 主机玩家提交本地输入。cameraYaw/chargeTime/cameraPitch 已由调用方转为 Fix64。
    /// ★ Proto 改为 sfixed64 后直接传 Fix64.Raw（long），不再丢失精度。
    /// </summary>
    public void SubmitLocalInput(int playerId, uint moveDir, bool jump, bool attack, bool skill, Fix64 cameraYaw, Fix64 chargeTime, Fix64 cameraPitch)
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
            CameraYaw = cameraYaw.Raw,
            ChargeTime = chargeTime.Raw,
            CameraPitch = cameraPitch.Raw
        };
    }

    /// <summary>
    /// Phase 2: 记录单个玩家在上一 tick 执行后的权威位置。
    /// 由 HostServer.SubmitAuthPosition() 桥接，供 Lua PlayerManager 调用。
    /// xRaw/yRaw/zRaw 均为 Fix64.Raw（long），直接来自 Unity Transform。
    /// </summary>
    public void RecordPlayerAuthPosition(int playerId, long xRaw, long yRaw, long zRaw)
    {
        _previousTickPositions[playerId] = (xRaw, yRaw, zRaw);
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
