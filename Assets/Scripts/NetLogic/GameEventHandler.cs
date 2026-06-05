using GameProto;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════
///     GameEventHandler —— 游戏事件处理器（服务端权威）
/// ═══════════════════════════════════════════════════════════════
///
/// 【职责】
///   - 周期性生成水晶并广播
///   - 验证并处理水晶拾取、玩家受击、玩家坠落
///   - 判定游戏结束
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

    // 水晶生成间隔（秒）
    private const float CrystalSpawnInterval = 5f;
    // 最大同时存在水晶数
    private const int MaxCrystals = 5;
    // 胜利所需分数
    private const int DefaultWinScore = 10;

    #endregion

    #region =============== 状态 ===============

    // 是否正在运行
    private bool _isRunning;
    // 晶石生成计时器
    private float _crystalSpawnTimer;
    // 下一个晶石ID
    private int _nextCrystalId = 1;
    // 获胜分数
    private int _winScore;
    // 确定性模拟随机数
    private DeterministicRandom _rng;

    // 当前存活的水晶
    private Dictionary<int, CrystalData> _activeCrystals = new();

    //  玩家分数：playerId -> score
    private Dictionary<int, int> _playerScores = new();

    // 玩家生命值：playerId -> hp
    private Dictionary<int, int> _playerHPs = new();

    #endregion

    #region =============== 内部数据结构 ===============

    private struct CrystalData
    {
        public int CrystalId;
        public float PosX, PosY, PosZ;
    }


    #endregion

    #region =============== 构造 ===============

    public GameEventHandler(HostServer host)
    {
        _host = host;
    }

    #endregion

    #region =============== 启动 / 停止 ===============

    /// <summary>
    /// 开始游戏事件循环
    /// </summary>
    /// <param name="randomSeed"> 随机种子（从GameStart开始） </param>
    /// <param name="targetScore"> 胜利分数 （从GameStart开始）</param>
    public void StartGameLoop(int randomSeed, int targetScore = DefaultWinScore)
    {
        _isRunning = true;
        _crystalSpawnTimer = 0f;
        _nextCrystalId = 1;
        _winScore = targetScore;
        _rng = new DeterministicRandom(randomSeed);
        _activeCrystals.Clear();
        _playerScores.Clear();
        _playerHPs.Clear();

        var room = _host.CurrentRoom;
        if (room != null)
        {
            foreach (int playerId in room.GetAllPlayerIds())
            {
                _playerScores[playerId] = 0;
                _playerHPs[playerId] = 3; // 默认3条命
            }
        }

        Debug.Log("【GameEventHandler】启动 seed：" + randomSeed + "目标分数：" + targetScore);
    }

    /// <summary>
    /// 停止游戏事件循环
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _activeCrystals.Clear();
    }

    #endregion

    #region =============== Tick (HostServer.Update 驱动) ===============

    public void Tick(float deltaTime)
    {
        if (!_isRunning) return;

        // 水晶定时生成
        _crystalSpawnTimer += deltaTime;
        if (_crystalSpawnTimer >= CrystalSpawnInterval)
        {
            _crystalSpawnTimer -= CrystalSpawnInterval;
            if (_activeCrystals.Count < MaxCrystals)
            {
                // 生成水晶
                SpawnCrystal();
            }
        }

    }

    #endregion

    #region =============== 水晶生成 ===============


    /// <summary>
    /// 生成一颗水晶并广播
    /// ★★★ 生成位置需要根据实际地图对接 ★★★
    /// </summary>
    private void SpawnCrystal()
    {
        int crystalId = _nextCrystalId++;

        // TODO：替换为实际地图的水晶生成点
        //float posX = 
        //float posY = 
        //float posZ = 

        _activeCrystals[crystalId] = new CrystalData
        {
            CrystalId = crystalId,
            //PosX = 
            //PosY = 
            //PosZ = 
        };

        var msg = new CrystalSpawn
        {
            CrystalId = crystalId,
            //PosX = 
            //PosY = 
            //PosZ = 
        };

        _host.BroadcastToAll(new NetMessage { CrystalSpawn = msg });
    }

    #endregion

    #region ==================== 事件处理 ====================


    /// <summary>
    /// 处理水晶拾取（客户端上报，服务端验证后广播权威结果）
    /// </summary>
    public void HandleCrystalPickup(CrystalPickup request)
    {
        int crystalId = request.CrystalId;
        int playerId = request.PlayerId;

        if (!_activeCrystals.ContainsKey(crystalId)) return;
        if (!_playerScores.ContainsKey(playerId)) return;

        // 移除水晶
        _activeCrystals.Remove(crystalId);

        // 加分
        _playerScores[playerId]++;

        // 广播权威结果（包含 new_score）
        var pickup = new CrystalPickup
        {
            CrystalId = crystalId,
            PlayerId = playerId,
            NewScore = _playerScores[playerId]
        };
        _host.BroadcastToAll(new NetMessage { CrystalPickup = pickup });

        Debug.Log("【GameEventHandler】玩家" + playerId + "拾取水晶" + crystalId + " 分数：" + _playerScores[playerId]);

        // 检查胜利
        if (_playerScores[playerId] >= _winScore)
            // 结束游戏
            EndGame(playerId);

    }

    /// <summary>
    /// 处理玩家受击
    /// </summary>
    /// <param name="request"></param>
    public void HandlePlayerHit(PlayerHit request)
    {
        int attackId = request.AttackerId;
        int victimId = request.VictimId;
        int droppedCount = request.DroppedCount; //晶石掉落数量

        if (!_playerHPs.ContainsKey(victimId)) return;

        // 扣血
        _playerHPs[victimId]--;

        // 掉落晶石 -> 扣分
        _playerScores[victimId] = Mathf.Max(0, _playerScores[victimId] - droppedCount);

        // 广播权威结果
        var hit = new PlayerHit
        {
            AttackerId = attackId,
            VictimId = victimId,
            DroppedCount = droppedCount
        };
        _host.BroadcastToAll(new NetMessage { PlayerHit = hit });

        Debug.Log("【GameEventHandler】玩家" + victimId + "受击 掉落" + droppedCount +
                  "颗 HP：" + _playerHPs[victimId]);

        // 检查淘汰
        if (_playerHPs[victimId] <= 0)
            // 淘汰玩家
            HandlePlayerEliminated(victimId, attackId);
    }

    public void HandlePlayerFall(PlayerFall request)
    {
        int playerId = request.PlayerId;
        int droppedCount = request.DroppedCount;

        if (!_playerHPs.ContainsKey(playerId)) return;

        // 扣血
        _playerHPs[playerId]--;

        // 掉落全部晶石 -> 扣分
        _playerScores[playerId] = Mathf.Max(0, _playerScores[playerId] - droppedCount);

        // 广播权威结果
        var fall = new PlayerFall
        {
            PlayerId = playerId,
            DroppedCount = droppedCount
        };
        _host.BroadcastToAll(new NetMessage { PlayerFall = fall });

        Debug.Log("【GameEventHandler】玩家" + playerId + "坠落 掉落" + droppedCount +
          "颗 HP：" + _playerHPs[playerId]);

        if (_playerHPs[playerId] <= 0)
            HandlePlayerEliminated(playerId, -1);

    }

    /// <summary>
    /// 处理玩家重生（坠落后重新站起，HP已在HandlePlayerFall中扣除）
    /// </summary>
    public void HandlePlayerRespawn(PlayerRespawn request)
    {
        int playerId = request.PlayerId;

        // 校验：玩家是否存在
        if (!_playerHPs.ContainsKey(playerId)) return;

        // 校验：HP <= 0 已被淘汰，不能重生
        if (_playerHPs[playerId] <= 0) return;

        // 广播权威重生结果（HP已在HandlePlayerFall中扣过，这里不动HP）
        var respawn = new PlayerRespawn
        {
            PlayerId = playerId,
            PosX = request.PosX,
            PosY = request.PosY,
            PosZ = request.PosZ
        };
        _host.BroadcastToAll(new NetMessage { PlayerRespawn = respawn });

        Debug.Log("【GameEventHandler】玩家" + playerId + "重生 剩余HP：" + _playerHPs[playerId]);
    }



    #endregion

    #region =============== 淘汰与结算 ===============

    /// <summary>
    /// 处理淘汰玩家
    /// </summary>
    /// <param name="eliminatedId"></param>
    /// <param name="killerId"></param>
    private void HandlePlayerEliminated(int eliminatedId, int killerId)
    {
        Debug.Log("【GameEventHandler】玩家" + eliminatedId + "被淘汰" + (killerId > 0 ? " 击杀者：" + killerId : "跌落死亡"));

        // 检查是否只剩一人
        int aliveCount = 0;
        int lastAliveId = -1;

        foreach (var kvp in _playerHPs)
        {
            if (kvp.Value > 0)
            {
                aliveCount++;
                lastAliveId = kvp.Key;
            }
        }

        if (aliveCount <= 1)
            EndGame(lastAliveId);

    }

    private void EndGame(int winnerId)
    {
        Debug.Log("【GameEventHandler】游戏结束 胜者：" + winnerId);

        var gameEnd = new GameEnd { WinnerId = winnerId };

        // scores：按 playerId 从 1 开始依次添加
        var room = _host.CurrentRoom;
        if (room != null)
        {
            foreach (int playerId in room.GetAllPlayerIds())
            {
                int score = _playerScores.TryGetValue(playerId, out int s) ? s : 0;
                gameEnd.Scores.Add(score);
            }
        }

        _host.BroadcastToAll(new NetMessage { GameEnd = gameEnd });

        _isRunning = false;
        _host.OnGameEnded();
    }

    #endregion

    #region =============== 查询 ===============

    public int GetPlayerScore(int playerId)
        => _playerScores.TryGetValue(playerId, out int s) ? s : 0;

    public int GetPlayerHP(int playerId)
        => _playerHPs.TryGetValue(playerId, out int h) ? h : 0;

    #endregion
}
// TODO 水晶生成位置待处理 156 - 174