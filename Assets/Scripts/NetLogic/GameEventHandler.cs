using GameProto;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════
///     GameEventHandler —— 游戏事件处理器（服务端权威）
/// ═══════════════════════════════════════════════════════════════
///
/// 【职责】
///   - 127s 全局计时器
///   - 5 区域水晶生成（每区域 2s 间隔，总共 45 颗上限——只算区域生成，不含死亡掉落）
///   - 全程可攻击 + 全程可采集（无阶段限制）
///   - 水晶拾取验证 + 分数（持有数×6）
///   - 死亡掉落（ceil(持有×0.3)）+ 重生
///   - 时间结束按分数判定胜负（并列获胜）
///
/// 【原则】
///   所有事件由服务端权威判定，客户端只负责上报和表现
/// ═══════════════════════════════════════════════════════════════
/// </summary>
public static class ClassForNothing5 { /* 为了避免调用时产生过长的说明 */ }

public class GameEventHandler
{
    private HostServer _host;

    #region =============== 配置 ===============

    // ★ 水晶分值
    private const int CrystalScoreValue = 6;
    // ★ 各区生成间隔（秒）
    private static readonly Fix64 ZoneSpawnInterval = Fix64.FromFloat(1.5f);
    // ★ 场上同时存在上限（只算区域生成，不算死亡掉落）。拾取后释放配额，新水晶继续生成。
    private const int MaxCrystalsOnField = 35;
    // ★ 游戏总时长（秒）
    private static readonly Fix64 GameDuration = Fix64.FromFloat(127f);
    // ★ 初始 HP
    private const int InitialHP = 100;
    private const int DamageMin = 7;
    private const int DamageMax = 20;  // Next(min, max) 返回 [min, max)，所以 21 → [7, 20]
    // ★ 掉落比例
    private static readonly Fix64 DropRatio = Fix64.FromFloat(0.3f);

    #endregion

    #region =============== 状态 ===============

    private bool _isRunning;
    private bool _zonesInitialized;  // 延迟扫描标志（等 GameScene 加载完毕）
    private int _zoneRetryAttempts;  // 重试计数器（首次 Tick 时 GameScene 可能尚未加载完）

    // --- 计时器 ---
    private Fix64 _elapsed;           // 对局已过时间

    // --- 死亡计时器（3 秒后重生）---
    private Dictionary<int, Fix64> _deathTimers = new();
    private static readonly Fix64 DeathDelay = Fix64.FromFloat(3.0f);

    // --- 区域生成 ---
    private struct SpawnZone
    {
        public Fix64 CenterX, CenterY, CenterZ;
        public Fix64 Radius;
    }
    private SpawnZone[] _spawnZones;
    private Fix64[] _zoneTimers;      // 5 个区域的各自计时器

    // --- 水晶 ---
    private int _nextCrystalId = 1;
    private int _zoneSpawnedCount;  // ★ 五个区域已生成总数（只算区域，不算死亡掉落，仅用于统计）
    private Dictionary<int, CrystalData> _activeCrystals = new();

    // --- 玩家 ---
    private Dictionary<int, int> _playerHoldings = new();  // 持有水晶数
    private Dictionary<int, int> _playerHPs = new();
    private Dictionary<int, Vector3> _playerBirthPos = new(); // 出生点
    private Dictionary<int, Vector3> _playerLastPos = new();  // 最近位置（用于掉落）

    // --- 确定性随机 ---
    private DeterministicRandom _rng;

    // --- 定时器 ---
    private Fix64 _lastTimerBroadcast;

    #endregion

    #region =============== 内部数据结构 ===============

    private struct CrystalData
    {
        public int CrystalId;
        public Fix64 PosX, PosY, PosZ;
    }

    #endregion

    #region =============== 构造 ===============

    public GameEventHandler(HostServer host)
    {
        _host = host;
    }

    #endregion

    #region =============== 启动 / 停止 ===============

    public void StartGameLoop(int randomSeed, Fix64 gameDuration = default)
    {
        if (gameDuration.Raw == 0) gameDuration = GameDuration;

        _isRunning = true;
        _elapsed = Fix64.Zero;
        _nextCrystalId = 1;
        _zoneSpawnedCount = 0;  // ★ 重置区域生成计数器
        _rng = new DeterministicRandom(randomSeed);
        _activeCrystals.Clear();
        _playerHoldings.Clear();
        _playerHPs.Clear();
        _playerBirthPos.Clear();
        _playerLastPos.Clear();
        _deathTimers.Clear();

        // ★ 延迟到 GameScene 加载后再扫描 CrystalSpawnZone
        //   Tick 在场景切换期间就会开始运行，EnsureZonesInitialized 会重试直到对象可用
        _zonesInitialized = false;
        _zoneRetryAttempts = 0;

        // --- 初始化玩家 ---
        var room = _host.CurrentRoom;
        if (room != null)
        {
            foreach (int playerId in room.GetAllPlayerIds())
            {
                _playerHoldings[playerId] = 0;
                _playerHPs[playerId] = InitialHP;
                _playerBirthPos[playerId] = Vector3.zero;
                _playerLastPos[playerId] = Vector3.zero;
            }
        }

        _lastTimerBroadcast = Fix64.Zero;

        Debug.Log("【GameEventHandler】启动 seed:" + randomSeed +
                  " 时长:" + gameDuration.ToFloat() + "s" +
                  " 分数:每水晶" + CrystalScoreValue + "分");

        BroadcastTimerUpdate();
    }

    public void Stop()
    {
        _isRunning = false;
        _activeCrystals.Clear();
    }

    #endregion

    #region =============== Tick ===============

    public void Tick(Fix64 deltaTime)
    {
        if (!_isRunning) return;

        // ★ 延迟扫描：Tick 在 LoadScene 完成前就会开始，EnsureZonesInitialized 内部重试直到 CPS 对象可用
        if (!_zonesInitialized) EnsureZonesInitialized();

        _elapsed += deltaTime;

        // === 游戏结束 ===
        if (_elapsed >= GameDuration)
        {
            OnTimerEnd();
            return;
        }

        // === 死亡计时器：3 秒后重生 ===
        if (_deathTimers.Count > 0)
        {
            var deadPlayers = new List<int>(_deathTimers.Keys);
            foreach (int pid in deadPlayers)
            {
                _deathTimers[pid] += deltaTime;
                if (_deathTimers[pid] >= DeathDelay)
                {
                    _deathTimers.Remove(pid);
                    RespawnPlayer(pid);
                }
            }
        }

        // === 每秒广播倒计时 ===
        if (_elapsed - _lastTimerBroadcast >= Fix64.One)
        {
            _lastTimerBroadcast = _elapsed;
            BroadcastTimerUpdate();
        }

        // === 水晶生成（全程可生成，区域已初始化，且未达场上同时存在上限）===
        if (_spawnZones != null && _activeCrystals.Count < MaxCrystalsOnField)
        {
            for (int z = 0; z < _spawnZones.Length; z++)
            {
                if (_activeCrystals.Count >= MaxCrystalsOnField) break;
                _zoneTimers[z] += deltaTime;
                while (_zoneTimers[z] >= ZoneSpawnInterval)
                {
                    _zoneTimers[z] -= ZoneSpawnInterval;
                    SpawnCrystalInZone(z);
                    if (_activeCrystals.Count >= MaxCrystalsOnField) break;
                }
            }
        }
    }

    #endregion

    #region =============== 水晶生成 ===============

    /// <summary>
    /// 延迟初始化水晶生成区域（等到 GameScene 加载完成后扫描 CPS 对象）
    /// StartGameLoop 在 LoadScene 之前调用，Tick 在场景切换期间就开始运行，
    /// 所以需要重试直到场景中的 CPS 对象可用。
    /// </summary>
    private void EnsureZonesInitialized()
    {
        var zoneObjects = GameObject.FindObjectsOfType<CrystalSpawnZone>();
        if (zoneObjects != null && zoneObjects.Length > 0)
        {
            // ★ 找到了！用场景中的 CPS 对象初始化
            _zonesInitialized = true;

            int count = Mathf.Min(zoneObjects.Length, 5);
            _spawnZones = new SpawnZone[count];
            for (int i = 0; i < count; i++)
            {
                var z = zoneObjects[i];
                _spawnZones[i] = new SpawnZone
                {
                    CenterX = Fix64.FromFloat(z.Center.x),
                    CenterY = Fix64.FromFloat(z.Center.y),
                    CenterZ = Fix64.FromFloat(z.Center.z),
                    Radius = Fix64.FromFloat(z.Radius),
                };
            }
            Debug.Log("【GameEventHandler】已扫描 " + count + " 个水晶生成区域");

            _zoneTimers = new Fix64[_spawnZones.Length];
            for (int i = 0; i < _zoneTimers.Length; i++)
                _zoneTimers[i] = Fix64.Zero;
        }
        else
        {
            _zoneRetryAttempts++;
            // 最多重试 90 tick（≈3 秒 @30fps），之后降级到默认区域
            if (_zoneRetryAttempts >= 90)
            {
                _zonesInitialized = true;
                Debug.LogWarning("【GameEventHandler】场景中未找到 CrystalSpawnZone（" +
                                _zoneRetryAttempts + " 次重试后放弃），使用默认区域");
                _spawnZones = new SpawnZone[5];
                for (int i = 0; i < 5; i++)
                {
                    _spawnZones[i] = new SpawnZone
                    {
                        CenterX = Fix64.FromFloat((i - 2) * 15f),
                        CenterY = Fix64.Zero,
                        CenterZ = Fix64.FromFloat(20f),
                        Radius = Fix64.FromFloat(8f),
                    };
                }
                _zoneTimers = new Fix64[_spawnZones.Length];
                for (int i = 0; i < _zoneTimers.Length; i++)
                    _zoneTimers[i] = Fix64.Zero;
            }
            // 否则：_zonesInitialized 保持 false，下一 Tick 继续重试
        }
    }

    /// <summary>在指定区域内生成一颗水晶</summary>
    private void SpawnCrystalInZone(int zoneIndex)
    {
        if (zoneIndex < 0 || zoneIndex >= _spawnZones.Length) return;

        int crystalId = _nextCrystalId++;
        _zoneSpawnedCount++;  // ★ 累计区域生成数（仅统计，不作为限制条件）
        var zone = _spawnZones[zoneIndex];

        // 确定性随机：在圆形区域内随机位置
        Fix64 angle = _rng.NextFix64() * Fix64.TwoPi;
        // dist = R * sqrt(rand)  — 面积均匀分布
        Fix64 dist = zone.Radius * Fix64.Sqrt(_rng.NextFix64());
        Fix64 posX = zone.CenterX + dist * Fix64.Cos(angle);
        Fix64 posZ = zone.CenterZ + dist * Fix64.Sin(angle);
        // 地面高度：使用生成区域的实际 Y 坐标
        Fix64 posY = zone.CenterY;

        // 存储
        _activeCrystals[crystalId] = new CrystalData
        {
            CrystalId = crystalId,
            PosX = posX,
            PosY = posY,
            PosZ = posZ,
        };

        // 广播
        var msg = new CrystalSpawn
        {
            CrystalId = crystalId,
            PosX = posX.Raw,
            PosY = posY.Raw,
            PosZ = posZ.Raw,
        };
        _host.BroadcastToAll(new NetMessage { CrystalSpawn = msg });
        // ★ 主机本地也需要创建水晶 GameObject
        EventCenter.Dispatch(30, msg);
    }

    /// <summary>在指定位置生成水晶（用于死亡掉落）</summary>
    private void SpawnCrystalAt(Fix64 posX, Fix64 posY, Fix64 posZ)
    {
        int crystalId = _nextCrystalId++;
        _activeCrystals[crystalId] = new CrystalData
        {
            CrystalId = crystalId,
            PosX = posX,
            PosY = posY,
            PosZ = posZ,
        };

        var msg = new CrystalSpawn
        {
            CrystalId = crystalId,
            PosX = posX.Raw,
            PosY = posY.Raw,
            PosZ = posZ.Raw,
        };
        _host.BroadcastToAll(new NetMessage { CrystalSpawn = msg });
        // ★ 主机本地也需要创建水晶 GameObject
        EventCenter.Dispatch(30, msg);
    }

    #endregion

    #region =============== 广播消息 ===============

    private void BroadcastTimerUpdate()
    {
        Fix64 remaining = GameDuration - _elapsed;
        if (remaining.Raw < 0) remaining = Fix64.Zero;
        var msg = new GameTimerUpdate { RemainingTime = remaining.ToFloat() };
        _host.BroadcastToAll(new NetMessage { GameTimerUpdate = msg });
        EventCenter.Dispatch(36, msg);
    }

    #endregion

    #region =============== 事件处理 ===============

    /// <summary>
    /// 处理水晶拾取（客户端上报，服务端验证后广播权威结果）
    /// </summary>
    public void HandleCrystalPickup(CrystalPickup request)
    {
        int crystalId = request.CrystalId;
        int playerId = request.PlayerId;

        if (!_activeCrystals.ContainsKey(crystalId)) return;
        if (!_playerHoldings.ContainsKey(playerId)) return;

        // 移除水晶
        _activeCrystals.Remove(crystalId);

        // 加持有数 → 分数 = 持有数 × 6
        _playerHoldings[playerId]++;
        int newScore = _playerHoldings[playerId] * CrystalScoreValue;

        // 广播权威结果
        var pickup = new CrystalPickup
        {
            CrystalId = crystalId,
            PlayerId = playerId,
            NewScore = newScore,
        };
        _host.BroadcastToAll(new NetMessage { CrystalPickup = pickup });
        // ★ 主机本地也需要处理拾取（移除水晶 + 更新分数）
        EventCenter.Dispatch(31, pickup);

        Debug.Log(string.Format("【GameEventHandler】玩家{0}拾取水晶{1} 持有{2} 分数={3}",
            playerId, crystalId, _playerHoldings[playerId], newScore));
    }

    /// <summary>处理玩家受击（服务端权威：生成随机伤害、更新 HP、广播结果）</summary>
    public void HandlePlayerHit(PlayerHit request)
    {
        int attackerId = request.AttackerId;
        int victimId = request.VictimId;

        if (!_playerHPs.ContainsKey(victimId)) return;
        if (_playerHPs[victimId] <= 0) return;  // 已死亡，忽略

        // ★ 服务端权威随机伤害 7-20
        int damage = _rng.Next(DamageMin, DamageMax + 1);  // [7, 21) → [7, 20]
        int newHp = Mathf.Max(0, _playerHPs[victimId] - damage);
        _playerHPs[victimId] = newHp;

        // ★ 广播权威结果（含 damage 和 new_hp）
        var hit = new PlayerHit
        {
            AttackerId = attackerId,
            VictimId = victimId,
            DroppedCount = 0,       // 普通受击不掉水晶（死亡时由 HandlePlayerDeath 处理）
            Damage = damage,
            NewHp = newHp,
        };
        _host.BroadcastToAll(new NetMessage { PlayerHit = hit });
        // ★ 主机本地也需要处理受击（更新 HP + 播放动画）
        EventCenter.Dispatch(32, hit);

        Debug.Log($"[GameEventHandler] 玩家{victimId}受击 攻击者{attackerId} 伤害={damage} HP={newHp}/{InitialHP}");

        // ★ 死亡判定
        if (newHp <= 0)
            HandlePlayerDeath(victimId, attackerId);
    }

    /// <summary>处理玩家坠落</summary>
    public void HandlePlayerFall(PlayerFall request)
    {
        int playerId = request.PlayerId;
        int droppedCount = request.DroppedCount;

        if (!_playerHPs.ContainsKey(playerId)) return;

        _playerHPs[playerId]--;

        if (_playerHoldings.ContainsKey(playerId))
        {
            _playerHoldings[playerId] = Mathf.Max(0, _playerHoldings[playerId] - droppedCount);
        }

        var fall = new PlayerFall
        {
            PlayerId = playerId,
            DroppedCount = droppedCount,
        };
        _host.BroadcastToAll(new NetMessage { PlayerFall = fall });
        // ★ 主机本地也需要处理坠落（更新 HP + 分数）
        EventCenter.Dispatch(33, fall);

        Debug.Log("【GameEventHandler】玩家" + playerId + "坠落 掉落" + droppedCount +
                  "颗 HP:" + _playerHPs[playerId]);

        if (_playerHPs[playerId] <= 0)
            HandlePlayerDeath(playerId, -1);
    }

    /// <summary>玩家重生请求验证</summary>
    public void HandlePlayerRespawn(PlayerRespawn request)
    {
        int playerId = request.PlayerId;
        if (!_playerHPs.ContainsKey(playerId)) return;
        if (_playerHPs[playerId] <= 0) return;

        var respawn = new PlayerRespawn
        {
            PlayerId = playerId,
            PosX = request.PosX,
            PosY = request.PosY,
            PosZ = request.PosZ,
        };
        _host.BroadcastToAll(new NetMessage { PlayerRespawn = respawn });
        // ★ 主机本地也需要处理重生（移动玩家到出生点）
        EventCenter.Dispatch(35, respawn);
        Debug.Log("【GameEventHandler】玩家" + playerId + "重生 剩余HP:" + _playerHPs[playerId]);
    }

    #endregion

    #region =============== 死亡与重生 ===============

    /// <summary>
    /// 处理玩家死亡：计算掉落、扣持有数、生成掉落水晶、触发重生
    /// </summary>
    private void HandlePlayerDeath(int playerId, int killerId)
    {
        // ★ 防止重复死亡（已在死亡计时器中等待重生）
        if (_deathTimers.ContainsKey(playerId)) return;

        Debug.Log("【GameEventHandler】玩家" + playerId + "死亡" +
                  (killerId > 0 ? " 击杀者:" + killerId : " 跌落死亡"));

        // --- 计算掉落 ---
        int holding = _playerHoldings.TryGetValue(playerId, out int h) ? h : 0;
        int dropCount = 0;
        if (holding > 0)
        {
            // ceil(holding × 0.3)
            Fix64 holdingFix = Fix64.FromInt(holding);
            Fix64 dropFix = holdingFix * DropRatio;
            dropCount = dropFix.Ceil().ToInt();
            if (dropCount > holding) dropCount = holding;
        }

        // --- 扣持有数 ---
        _playerHoldings[playerId] = Mathf.Max(0, holding - dropCount);
        int newScore = _playerHoldings[playerId] * CrystalScoreValue;

        // --- 在死亡位置生成掉落水晶 ---
        Vector3 deathPos = _playerLastPos.TryGetValue(playerId, out Vector3 dp)
            ? dp : Vector3.zero;
        // 微偏移避免重叠
        float scatterRadius = 1.5f;
        for (int i = 0; i < dropCount; i++)
        {
            float angleVal = (float)(_rng.Next(0, 1000) / 1000.0 * 2.0 * 3.1415926535);
            float scatter = (float)(_rng.Next(0, 1000) / 1000.0) * scatterRadius;
            float dxFloat = deathPos.x + Mathf.Cos(angleVal) * scatter;
            float dzFloat = deathPos.z + Mathf.Sin(angleVal) * scatter;
            Fix64 dx = Fix64.FromFloat(dxFloat);
            Fix64 dy = Fix64.FromFloat(deathPos.y);
            Fix64 dz = Fix64.FromFloat(dzFloat);
            SpawnCrystalAt(dx, dy, dz);
        }

        // --- 广播 CrystalDrop 事件 ---
        var dropMsg = new CrystalDrop
        {
            Count = dropCount,
            PlayerId = playerId,
            NewScore = newScore,
        };
        _host.BroadcastToAll(new NetMessage { CrystalDrop = dropMsg });
        // ★ 主机本地也需要处理掉落事件（更新分数）
        EventCenter.Dispatch(39, dropMsg);

        // ★ 启动死亡计时器（3 秒后重生，不再立即重生）
        _deathTimers[playerId] = Fix64.Zero;

        Debug.Log(string.Format("【GameEventHandler】玩家{0}死亡 掉落{1}颗（持有{2}→{3}）分数={4} 3秒后重生",
            playerId, dropCount, holding, _playerHoldings[playerId], newScore));
    }

    /// <summary>
    /// 重生玩家：重置HP，发送 PlayerRespawn 广播
    /// </summary>
    private void RespawnPlayer(int playerId)
    {
        _playerHPs[playerId] = InitialHP;
        Vector3 birthPos = _playerBirthPos.TryGetValue(playerId, out Vector3 bp)
            ? bp : Vector3.zero;

        var respawn = new PlayerRespawn
        {
            PlayerId = playerId,
            PosX = Fix64.FromFloat(birthPos.x).Raw,
            PosY = Fix64.FromFloat(birthPos.y).Raw,
            PosZ = Fix64.FromFloat(birthPos.z).Raw,
        };
        _host.BroadcastToAll(new NetMessage { PlayerRespawn = respawn });
        // ★ 直接调 Lua 回调，不用 EventCenter.Dispatch(35)。
        //   Dispatch(35) 会同时触发 HostServer.OnPlayerRespawn → HandlePlayerRespawn → 再次 Dispatch → ∞ 递归
        LuaEventBridge.OnPlayerRespawn?.Invoke(respawn);

        Vector3 deathPos = _playerLastPos.TryGetValue(playerId, out Vector3 dp) ? dp : Vector3.zero;
        Debug.Log($"[GameEventHandler] 玩家{playerId}重生 HP={InitialHP} " +
                  $"出生点=({birthPos.x:F2},{birthPos.y:F2},{birthPos.z:F2}) " +
                  $"死亡点=({deathPos.x:F2},{deathPos.y:F2},{deathPos.z:F2})");
    }

    #endregion

    #region =============== 结算 ===============

    /// <summary>时间结束：按分数决定胜者</summary>
    private void OnTimerEnd()
    {
        Debug.Log("【GameEventHandler】时间结束，按分数决定胜者");

        int maxScore = -1;
        var winners = new List<int>();

        foreach (var kvp in _playerHoldings)
        {
            int score = kvp.Value * CrystalScoreValue;
            if (score > maxScore)
            {
                maxScore = score;
                winners.Clear();
                winners.Add(kvp.Key);
            }
            else if (score == maxScore)
            {
                winners.Add(kvp.Key);
            }
        }

        if (winners.Count == 0) { EndGame(-1); return; }

        // 找胜者名（并列取第一个的名称，或拼接）
        int winnerId = winners[0];
        string winnerName = "未知";
        var room = _host.CurrentRoom;
        if (room != null && room.IdToPlayer.TryGetValue(winnerId, out var player))
            winnerName = player.PlayerName;
        if (winners.Count > 1) winnerName += " 等" + winners.Count + "人并列";

        var gameEnd = new GameEnd { WinnerId = winnerId, WinnerName = winnerName };

        if (room != null)
        {
            foreach (int pid in room.GetAllPlayerIds())
            {
                int score = _playerHoldings.TryGetValue(pid, out int hld) ? hld * CrystalScoreValue : 0;
                gameEnd.Scores.Add(score);
            }
        }

        _host.BroadcastToAll(new NetMessage { GameEnd = gameEnd });
        EventCenter.Dispatch(34, gameEnd);
        _isRunning = false;
        _host.OnGameEnded();
    }

    private void EndGame(int winnerId)
    {
        // OnTimerEnd 已处理，此处保留兼容
        OnTimerEnd();
    }

    #endregion

    #region =============== 外部接口（供 Lua 调用）===============

    /// <summary>设置玩家出生点（Lua 在 SpawnAllPlayers 时调用）</summary>
    public void SetPlayerBirthPos(int playerId, float x, float y, float z)
    {
        _playerBirthPos[playerId] = new Vector3(x, y, z);
        Debug.Log($"[GameEventHandler] 写入出生点 playerId={playerId} pos=({x:F2}, {y:F2}, {z:F2})");
    }

    /// <summary>报告玩家位置（Lua 每帧调用，用于死亡掉落位置）</summary>
    public void ReportPlayerPosition(int playerId, float x, float y, float z)
    {
        _playerLastPos[playerId] = new Vector3(x, y, z);
    }

    /// <summary>设置玩家初始持有数（Lua 调用，通常为0）</summary>
    public void SetPlayerHolding(int playerId, int holding)
    {
        _playerHoldings[playerId] = holding;
    }

    /// <summary>获取玩家持有数</summary>
    public int GetPlayerHolding(int playerId)
        => _playerHoldings.TryGetValue(playerId, out int h) ? h : 0;

    /// <summary>获取玩家分数（持有数×6）</summary>
    public int GetPlayerScore(int playerId)
        => GetPlayerHolding(playerId) * CrystalScoreValue;

    /// <summary>获取玩家 HP</summary>
    public int GetPlayerHP(int playerId)
        => _playerHPs.TryGetValue(playerId, out int hp) ? hp : 0;

    /// <summary>全程可攻击（无阶段限制）</summary>
    public bool CanAttack => _isRunning;

    /// <summary>全程可生成水晶（受 45 颗上限限制）</summary>
    public bool CrystalsSpawning => _isRunning;

    /// <summary>游戏是否运行中</summary>
    public bool IsRunning => _isRunning;

    #endregion
}
