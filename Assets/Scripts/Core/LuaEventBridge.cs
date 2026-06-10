using GameProto;
using Google.Protobuf;
using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// ═══════════════════════════════════════════════════════════════
///     LuaEventBridge —— C# 游戏事件 → Lua 回调桥接器
/// ═══════════════════════════════════════════════════════════════
///
/// 【定位】
///   C# 侧的游戏网络事件通过此类桥接到 Lua 侧。
///   Lua 侧在 NetworkEventMgr 中设置这些静态回调。
///
/// 【使用方式】
///   Lua 侧：CS.LuaEventBridge.OnCrystalSpawn = function(msg) ... end
///   C# 侧：EventCenter → 本类静态回调 → Lua 函数
///
/// 【消息类型对照】
///   30 CrystalSpawn   → OnCrystalSpawn(CrystalSpawn msg)
///   31 CrystalPickup  → OnCrystalPickup(CrystalPickup msg)
///   32 PlayerHit      → OnPlayerHit(PlayerHit msg)
///   33 PlayerFall     → OnPlayerFall(PlayerFall msg)
///   34 GameEnd        → OnGameEnd(GameEnd msg)
///   35 PlayerRespawn  → OnPlayerRespawn(PlayerRespawn msg)
///   37 PlayerOffline  → OnPlayerOffline(PlayerOffline msg)
///   16 GameStart      → OnGameStart(GameStart msg)
/// ═══════════════════════════════════════════════════════════════
/// </summary>
public static class LuaEventBridge
{
    #region =============== Lua 可设置的回调 ===============

    /// <summary>水晶生成：function(crystalSpawnMsg)</summary>
    public static Action<CrystalSpawn> OnCrystalSpawn;

    /// <summary>水晶拾取：function(crystalPickupMsg)</summary>
    public static Action<CrystalPickup> OnCrystalPickup;

    /// <summary>玩家受击：function(playerHitMsg)</summary>
    public static Action<PlayerHit> OnPlayerHit;

    /// <summary>玩家坠落：function(playerFallMsg)</summary>
    public static Action<PlayerFall> OnPlayerFall;

    /// <summary>玩家重生：function(playerRespawnMsg)</summary>
    public static Action<PlayerRespawn> OnPlayerRespawn;

    /// <summary>游戏结束：function(gameEndMsg)</summary>
    public static Action<GameEnd> OnGameEnd;

    /// <summary>玩家离线：function(playerOfflineMsg)</summary>
    public static Action<PlayerOffline> OnPlayerOffline;

    /// <summary>游戏开始（客户端收到）：function(gameStartMsg)</summary>
    public static Action<GameStart> OnGameStart;

    #endregion

    #region =============== 内部：EventCenter 监听器引用（用于正确移除） ===============

    // ★ 存储监听器委托引用，以便 Shutdown() 时能正确从 EventCenter 移除
    //    之前的实现使用内联 lambda，导致无法 RemoveListener，每次 Initialize/Shutdown 循环都会泄漏
    private static UnityAction<IMessage> _onCrystalSpawnListener;
    private static UnityAction<IMessage> _onCrystalPickupListener;
    private static UnityAction<IMessage> _onPlayerHitListener;
    private static UnityAction<IMessage> _onPlayerFallListener;
    private static UnityAction<IMessage> _onPlayerRespawnListener;
    private static UnityAction<IMessage> _onGameEndListener;
    private static UnityAction<IMessage> _onPlayerOfflineListener;
    private static UnityAction<IMessage> _onGameStartListener;

    #endregion

    #region =============== 初始化 / 关闭 ===============

    private static bool _initialized;

    /// <summary>
    /// 注册所有游戏事件的 C# EventCenter 监听。
    /// 幂等——多次调用不会重复注册。
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        // ★ 保存委托引用，以便 Shutdown 时正确移除
        _onCrystalSpawnListener = (IMessage msg) =>
        { if (msg is CrystalSpawn m) OnCrystalSpawn?.Invoke(m); };
        EventCenter.AddListener(30, _onCrystalSpawnListener);

        _onCrystalPickupListener = (IMessage msg) =>
        { if (msg is CrystalPickup m) OnCrystalPickup?.Invoke(m); };
        EventCenter.AddListener(31, _onCrystalPickupListener);

        _onPlayerHitListener = (IMessage msg) =>
        { if (msg is PlayerHit m) OnPlayerHit?.Invoke(m); };
        EventCenter.AddListener(32, _onPlayerHitListener);

        _onPlayerFallListener = (IMessage msg) =>
        { if (msg is PlayerFall m) OnPlayerFall?.Invoke(m); };
        EventCenter.AddListener(33, _onPlayerFallListener);

        _onPlayerRespawnListener = (IMessage msg) =>
        { if (msg is PlayerRespawn m) OnPlayerRespawn?.Invoke(m); };
        EventCenter.AddListener(35, _onPlayerRespawnListener);

        _onGameEndListener = (IMessage msg) =>
        { if (msg is GameEnd m) OnGameEnd?.Invoke(m); };
        EventCenter.AddListener(34, _onGameEndListener);

        _onPlayerOfflineListener = (IMessage msg) =>
        { if (msg is PlayerOffline m) OnPlayerOffline?.Invoke(m); };
        EventCenter.AddListener(37, _onPlayerOfflineListener);

        _onGameStartListener = (IMessage msg) =>
        { if (msg is GameStart m) OnGameStart?.Invoke(m); };
        EventCenter.AddListener(16, _onGameStartListener);

        Debug.Log("[LuaEventBridge] 所有游戏事件监听已注册");
    }

    /// <summary>
    /// 移除所有事件监听，清空 Lua 回调引用。
    /// ★ 修复：使用存储的委托引用正确移除 EventCenter 监听器，防止泄漏
    /// </summary>
    public static void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;

        // ★ 正确移除 EventCenter 监听器（使用存储的委托引用）
        if (_onCrystalSpawnListener != null)
        { EventCenter.RemoveListener(30, _onCrystalSpawnListener); _onCrystalSpawnListener = null; }
        if (_onCrystalPickupListener != null)
        { EventCenter.RemoveListener(31, _onCrystalPickupListener); _onCrystalPickupListener = null; }
        if (_onPlayerHitListener != null)
        { EventCenter.RemoveListener(32, _onPlayerHitListener); _onPlayerHitListener = null; }
        if (_onPlayerFallListener != null)
        { EventCenter.RemoveListener(33, _onPlayerFallListener); _onPlayerFallListener = null; }
        if (_onPlayerRespawnListener != null)
        { EventCenter.RemoveListener(35, _onPlayerRespawnListener); _onPlayerRespawnListener = null; }
        if (_onGameEndListener != null)
        { EventCenter.RemoveListener(34, _onGameEndListener); _onGameEndListener = null; }
        if (_onPlayerOfflineListener != null)
        { EventCenter.RemoveListener(37, _onPlayerOfflineListener); _onPlayerOfflineListener = null; }
        if (_onGameStartListener != null)
        { EventCenter.RemoveListener(16, _onGameStartListener); _onGameStartListener = null; }

        // 清空 Lua 回调引用，防止悬挂
        OnCrystalSpawn = null;
        OnCrystalPickup = null;
        OnPlayerHit = null;
        OnPlayerFall = null;
        OnPlayerRespawn = null;
        OnGameEnd = null;
        OnPlayerOffline = null;
        OnGameStart = null;

        Debug.Log("[LuaEventBridge] 已关闭，所有 EventCenter 监听器已移除，回调已清空");
    }

    #endregion
}
