using GameProto;
using Google.Protobuf;
using System;
using UnityEngine;

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

    #region =============== 初始化（由 GameMgr 或 Lua 调用一次） ===============

    private static bool _initialized;

    /// <summary>
    /// 注册所有游戏事件的 C# EventCenter 监听。
    /// 幂等——多次调用不会重复注册。
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        EventCenter.AddListener(30, (IMessage msg) =>
        { if (msg is CrystalSpawn m) OnCrystalSpawn?.Invoke(m); });

        EventCenter.AddListener(31, (IMessage msg) =>
        { if (msg is CrystalPickup m) OnCrystalPickup?.Invoke(m); });

        EventCenter.AddListener(32, (IMessage msg) =>
        { if (msg is PlayerHit m) OnPlayerHit?.Invoke(m); });

        EventCenter.AddListener(33, (IMessage msg) =>
        { if (msg is PlayerFall m) OnPlayerFall?.Invoke(m); });

        EventCenter.AddListener(35, (IMessage msg) =>
        { if (msg is PlayerRespawn m) OnPlayerRespawn?.Invoke(m); });

        EventCenter.AddListener(34, (IMessage msg) =>
        { if (msg is GameEnd m) OnGameEnd?.Invoke(m); });

        EventCenter.AddListener(37, (IMessage msg) =>
        { if (msg is PlayerOffline m) OnPlayerOffline?.Invoke(m); });

        EventCenter.AddListener(16, (IMessage msg) =>
        { if (msg is GameStart m) OnGameStart?.Invoke(m); });

        Debug.Log("[LuaEventBridge] 所有游戏事件监听已注册");
    }

    /// <summary>
    /// 移除所有事件监听（切场景/退出时调用）
    /// </summary>
    public static void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;

        // EventCenter 的 RemoveListener 需要传入原始 delegate，无法直接移除 lambda。
        // 因此这里不清除单个监听器——EventCenter.Clear() 会在切场景时由 GameMgr 调用。
        // 仅清空 Lua 回调引用，防止悬挂。
        OnCrystalSpawn = null;
        OnCrystalPickup = null;
        OnPlayerHit = null;
        OnPlayerFall = null;
        OnPlayerRespawn = null;
        OnGameEnd = null;
        OnPlayerOffline = null;
        OnGameStart = null;

        Debug.Log("[LuaEventBridge] 已关闭，所有回调已清空");
    }

    #endregion
}
