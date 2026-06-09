using GameProto;
using Google.Protobuf;
using System;
using System.Collections.Concurrent;
using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════
///     TickExcutor —— 帧执行器（客户端侧 MonoBehaviour）
/// ═══════════════════════════════════════════════════════════════
///
/// 【两种模式】
///   主机模式：由 TickSyncHandler 产出 InputTick → EnqueueTick 直接入队 → 立即执行
///   客户端模式：从网络收到 InputTick/CatchUpTicks → 按帧率节奏执行
///
/// 【帧追赶】
///   收到 CatchUpTicks 时批量入队，快速执行（每帧最多 MAX_CATCHUP_PER_TICK 帧）
/// ═══════════════════════════════════════════════════════════════
/// </summary>

public static class ClassForNothing4 { /* 为了避免调用时产生过长的说明 */ }

/// <summary>
/// TickExcutor —— 帧执行器（客户端侧 MonoBehaviour）
/// </summary>
public class TickExecutor : MonoBehaviour
{
    #region =============== Lua 桥接 ===============

    /// <summary>
    /// Lua 侧可设置此回调，每帧每个玩家的输入都会调用一次。
    /// 签名：function(playerInput) — playerInput 为 GameProto.PlayerInput
    /// </summary>
    public static Action<PlayerInput> OnApplyPlayerInput;

    /// <summary>
    /// Lua 侧可设置此回调，每帧执行完所有玩家输入后调用一次。
    /// 签名：function(tick) — tick 为 GameProto.InputTick
    /// </summary>
    public static Action<InputTick> OnAfterTickExecuted;

    #endregion

    #region =============== 配置 ===============

    // 默认逻辑帧率（每秒15帧）
    private const int DefaultTickRate = 15;

    // 追赶模式下每帧最多执行的帧数 （防止一帧内执行过多导致卡顿）
    private const int MaxCatchUpPreTick = 5;

    #endregion

    #region =============== 状态 ===============

    // 是否为主机模式（主机直接执行，客户端按帧率节奏执行）
    private bool _isHost;

    // 逻辑帧率（每秒执行的帧数）
    private int _tickRate;

    // 每帧的时间间隔（Fix64 确定性）
    private Fix64 _tickInterval;

    // 当前已执行到的帧号（单调递增）
    private int _localTick;

    // 帧间隔累加计时器（客户端正常模式下用于控制执行节奏）
    private Fix64 _tickTimer;

    // 是否处于追赶模式（积压帧过多时快速消化）
    private bool _isCatchingUp;

    // 是否已完成初始化
    private bool _isInitialized;

    // 追赶目标帧号（重连时由服务端告知，追赶到此帧后退出追赶模式）
    private int _targetTick;

    // 待执行的帧队列（主机模式由 HostServer 入队，客户端模式由网络事件入队）
    private ConcurrentQueue<InputTick> _pendingTicks = new();

    #endregion

    #region =============== 初始化 ===============

    /// <summary>
    /// 初始化帧执行器
    /// </summary>
    /// <param name="isHost">是否为主机模式</param>
    /// <param name="tickRate">帧率</param>
    public void Init(bool isHost, int tickRate = DefaultTickRate)
    {
        _isHost = isHost;
        _tickRate = tickRate;
        _tickInterval = Fix64.One / Fix64.FromInt(tickRate);
        _localTick = 0;
        _tickTimer = Fix64.Zero;
        _isCatchingUp = false;
        _pendingTicks.Clear();
        _isInitialized = true;

        // 客户端模式：注册网络事件，主机模式不需要
        if (!isHost)
        {
            // 监听单帧输入事件
            EventCenter.AddListener(20, OnInputTickRecvd);
            // 监听重连确定事件
            EventCenter.AddListener(41, OnReconnectAckRecvd);
            //监听批量追赶帧事件
            EventCenter.AddListener(42, OnCatchUpTickRecvd);
        }

        Debug.Log("【TickExecutor】初始化 模式" + (isHost ? "主机" : "客户端") + " 帧率：" + tickRate);
    }

    private void OnDestroy()
    {
        //  客户端模式下注销网络事件 避免内存泄漏
        if (!_isHost && _isInitialized)
        {
            EventCenter.RemoveListener(20, OnInputTickRecvd);
            EventCenter.RemoveListener(41, OnReconnectAckRecvd);
            EventCenter.RemoveListener(42, OnCatchUpTickRecvd);
        }
    }

    #endregion

    #region =============== 帧驱动 ===============

    private void Update()
    {
        if (!_isInitialized) return;

        if (_isHost)
        {
            // 主机模式：队列中有帧就立即全部执行，不等待
            while (_pendingTicks.Count > 0)
            {
                if (_pendingTicks.TryDequeue(out InputTick result))
                    ExecuteSingleTick(result);
            }
        }
        else
        {
            // 客户端模式：按帧率节奏或追赶模式执行
            ClientTickUpdate();
        }
    }

    /// <summary>
    /// 客户端帧驱动主循环。
    /// Time.deltaTime 在入口处转换为 Fix64，游戏逻辑层不再直接使用 float 时间。
    /// </summary>
    private void ClientTickUpdate()
    {
        if (_pendingTicks.Count == 0) return;

        // 将 Unity 的 deltaTime 转换为 Fix64（确定性模拟的理想做法是使用固定时间步长）
        Fix64 dt = Fix64.FromFloat(Time.deltaTime);

        // ============= 追赶模式：快速执行积压帧 =============
        if (_isCatchingUp)
        {
            int executed = 0;
            while (_pendingTicks.Count > 0 && executed < MaxCatchUpPreTick)
            {
                if (_pendingTicks.TryDequeue(out InputTick result))
                    ExecuteSingleTick(result);
                executed++;
            }

            if (_pendingTicks.Count == 0 && _localTick >= _targetTick)
            {
                _isCatchingUp = false;
                Debug.Log("【TickExcutor】追赶完成 当前tick：" + _localTick);
            }
        }
        // ============= 正常模式：按固定时间间隔执行帧 =============
        else
        {
            _tickTimer += dt;

            if (_tickTimer >= _tickInterval)
            {
                _tickTimer -= _tickInterval;

                if (_pendingTicks.Count > 0 && _pendingTicks.TryDequeue(out InputTick result))
                    ExecuteSingleTick(result);
            }

            if (_pendingTicks.Count > 3)
            {
                _isCatchingUp = true;
                Debug.Log("【TickExcutor】帧积压 " + _pendingTicks.Count + " 帧 进入追赶模式");
            }
        }
    }

    #endregion

    #region =============== 帧执行 ===============

    private void ExecuteSingleTick(InputTick tick)
    {
        // 更新本地帧号
        _localTick = tick.Tick;

        // 遍历该帧内所有玩家的输入 逐一应用
        foreach (PlayerInput input in tick.Inputs)
        {
            ApplyPlayerInput(input);
        }

        // 单帧逻辑执行完毕 触发后续回调（物理模拟等）
        OnTickExecuted(tick);

    }

    /// <summary>
    /// 将单个玩家的输入应用到游戏对象。
    /// 通过静态回调桥接到 Lua 侧 PlayerManager。
    /// </summary>
    private void ApplyPlayerInput(PlayerInput input)
    {
        OnApplyPlayerInput?.Invoke(input);
    }

    /// <summary>
    /// 单帧执行完毕后的回调。
    /// 通过静态回调桥接到 Lua 侧 PlayerManager.OnTickEnd。
    /// </summary>
    private void OnTickExecuted(InputTick tick)
    {
        OnAfterTickExecuted?.Invoke(tick);
    }

    #endregion

    #region ==================== 公共接口 ====================

    /// <summary>
    ///  入队一帧（主机模式由 HostServer 调用）
    /// </summary>
    /// <param name="tick"></param>
    public void EnqueueTick(InputTick tick)
    {
        _pendingTicks.Enqueue(tick);        
    }

    // 当前本地帧号（只读）
    public int LocalTick => _localTick;

    // 队列中执行的帧数量（只读）
    public int PendingTickCount => _pendingTicks.Count;

    #endregion


    #region ==================== 网络事件（客户端模式） ====================

    // 收到服务器下发的单帧输入 直接入队等待执行
    private void OnInputTickRecvd(uint conv, IMessage msg)
    {
        // 主机模式不处理网络帧
        if (_isHost) return;
        if (msg is InputTick tick)
            _pendingTicks.Enqueue(tick);
    }
    
    // 收到批量追赶帧（通常在重连或延迟较高时服务器一次性下发多帧）
    private void OnCatchUpTickRecvd(uint conv, IMessage msg)
    {
        // 主机模式不处理网络
        if (_isHost) return;

        if (msg is CatchUpTicks catchUp)
        {
            // 将所有追赶帧逐帧入队
            foreach (var tick in catchUp.Ticks)
                _pendingTicks.Enqueue(tick);

            // 有追赶帧是立即切换到追赶模式
            if (catchUp.Ticks.Count > 0)
            {
                _isCatchingUp = true;
                Debug.Log("【TickExcutor】收到追赶帧 " + catchUp.Ticks.Count + " 帧");
            }
        }
    }

    private void OnReconnectAckRecvd(uint conv, IMessage msg)
    {
        // 主机模式不处理网络
        if (_isHost) return;

        // 重连成功后，服务器会告知当前帧号，客户端需要追赶到这个帧
        if (msg is ReconnectAck ack && ack.Success)
        {
            _targetTick = ack.CurrentServerTick;
            Debug.Log("【TickExcutor】重连成功 服务端tick：" + _targetTick);

        }
    }

    #endregion

}
// 211 - 233 Todo
