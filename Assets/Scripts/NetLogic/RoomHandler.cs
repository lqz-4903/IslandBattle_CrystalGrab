using GameProto;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════
//                      数据结构
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// 服务端玩家数据（区别于 Protobuf 的 PlayerInfo，包含网络层信息）
/// </summary>
public class ServerPlayer
{
    public int PlayerId;
    public string PlayerName;
    public uint Conv;
    public bool IsHost;

    // 转换为Protobuf PlayerInfo（用于网络发送）
    public PlayerInfo ToProto()
    {
        return new PlayerInfo
        {
            PlayerId = PlayerId,
            PlayerName = PlayerName,
            IsHost = IsHost
        };
    }
}

/// <summary>
/// 房间数据
/// </summary>
public class RoomData
{
    public string RoomId;

    /// <summary>
    /// conv → 玩家映射（线程安全）。
    /// 读写可能来自主线程（Update/事件处理）和 KCP 后台线程（OnClientDisconnected 回调），
    /// 使用 ConcurrentDictionary 避免 Dictionary 并发崩溃。
    /// </summary>
    public ConcurrentDictionary<uint, ServerPlayer> ConvToPlayer = new();

    /// <summary>playerId → 玩家映射（线程安全，原因同上）</summary>
    public ConcurrentDictionary<int, ServerPlayer> IdToPlayer = new();

    public int NextPlayerId = 1;
    public int HostPlayerId = -1;

    /// <summary>主机玩家的特殊 Conv 值</summary>
    public const uint HostConv = 0;

    /// <summary>获取所有玩家 ID</summary>
    public int[] GetAllPlayerIds()
    {
        return IdToPlayer.Keys.ToArray();
    }

    /// <summary>获取所有玩家的 Protobuf 列表</summary>
    public List<PlayerInfo> GetAllPlayerProtos()
    {
        return IdToPlayer.Values.Select(p => p.ToProto()).ToList();
    }

    /// <summary>分配下一个玩家 ID（调用方需确保线程安全）</summary>
    public int AllocPlayerId()
    {
        return Interlocked.Increment(ref NextPlayerId) - 1;
    }
}

// ═══════════════════════════════════════════════════════════════
//                      RoomHandler
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// ═══════════════════════════════════════════════════════════════
///     RoomHandler —— 房间管理处理器
/// ═══════════════════════════════════════════════════════════════
///
/// 【职责】
///   - 创建房间、加入房间、开始游戏
///   - 维护玩家列表与 conv ↔ playerId 双向映射
///   - 广播 PlayerList 更新
///   - 处理玩家断开连接
///
/// 【房间ID生成】
///   4位大写字母+数字随机字符串（如 "AB3K"）
/// ═══════════════════════════════════════════════════════════════
/// </summary>
public static class ClassForNothing2 { /* 为了避免调用时产生过长的说明 */ }

/// <summary>
/// RoomHandler —— 房间管理处理器
/// </summary>
public class RoomHandler
{
    private readonly HostServer _host;

    public RoomHandler(HostServer host)
    {
        _host = host;
    }

    #region =============== 创建房间 ===============

    /// <summary>
    /// 主机本地创建房间（不走网络）
    /// </summary>
    /// <param name="hostPlayerName"></param>
    public void CreateLocalRoom(string hostPlayerName)
    {
        var room = new RoomData
        {
            RoomId = GenerateRoomId()
        };

        // 分配玩家
        int hostId = room.AllocPlayerId();
        var hostPlayer = new ServerPlayer
        {
            PlayerId = hostId,
            PlayerName = hostPlayerName,
            Conv = RoomData.HostConv,
            IsHost = true
        };

        room.ConvToPlayer[RoomData.HostConv] = hostPlayer;
        room.IdToPlayer[hostId] = hostPlayer;
        room.HostPlayerId = hostId;

        _host.CurrentRoom = room;

        Debug.Log("【RoomHandler】房间创建创建成功ID：" + room.RoomId + "主机玩家：" + hostPlayerName);
    }

    #endregion

    #region =============== 加入房间 ===============

    public void HandleJoinRoom(uint conv,JoinRoom request)
    {
        var room = _host.CurrentRoom;

        // 校验：房间是否存在
        if (room == null)
        {
            SendJoinRoomAck(conv, false, 0, "房间不存在", "", null);
            return;
        }

        // 校验：房间ID是否存在匹配
        if (request.RoomId != room.RoomId)
        {
            SendJoinRoomAck(conv, false, 0, "房间ID错误", "", null);
            return;
        }

        // 校验：房间是否已满（最多4人 可调整）
        if (room.ConvToPlayer.Count >= 4)
        {
            SendJoinRoomAck(conv, false, 0, "房间已满，拒绝加入", "", null);
            return;
        }

        // 如果该 conv 已有关联玩家（断线重连/重复加入场景），先清理旧映射
        // 避免 IdToPlayer 中残留孤儿条目导致 PlayerList 出现重复
        if (room.ConvToPlayer.TryGetValue(conv, out ServerPlayer oldPlayer))
        {
            room.IdToPlayer.TryRemove(oldPlayer.PlayerId, out _);
            Debug.Log("【RoomHandler】清理旧玩家映射 conv=" + conv + " oldPlayerId=" + oldPlayer.PlayerId);
        }

        // 分配玩家ID
        int playerId = room.AllocPlayerId();

        var newPlayer = new ServerPlayer
        {
            PlayerId = playerId,
            PlayerName = request.PlayerName,
            Conv = conv,
            IsHost = false
        };

        room.ConvToPlayer[conv] = newPlayer;
        room.IdToPlayer[playerId] = newPlayer;

        // 1.向新玩家发送 JoinRoomAck
        SendJoinRoomAck(conv, true, playerId, "", room.RoomId, room.GetAllPlayerProtos());

        // 2.向房间内所有玩家广播最新的 PlayerList（含主机本地）
        BroadcastPlayerList();

        Debug.Log("【RoomHandler】玩家加入：" + request.PlayerName + "PlayerId：" + playerId + "Conv：" + conv);
    }

    /// <summary>
    /// 处理创建房间请求
    /// 客户端本地调用 启动KCP服务器后创建房间
    /// </summary>
    /// <param name="conv"></param>
    /// <param name="request"></param>
    public void HandleCreateRoom(uint conv, CreateRoom request)
    {
        // 本地创建房间，调用者就是房主
        CreateLocalRoom(request.HostName);
    }

    #endregion

    #region =============== 开始游戏 ===============

    /// <summary>
    /// 处理开始游戏后的请求（仅主机玩家可发起）
    /// </summary>
    /// <param name="request"></param>
    public void HandleGameStart(uint conv, GameStart request)
    {
        var room = _host.CurrentRoom;
        if (room == null) return;

        // 校验：只有房主才能发起
        if (!room.ConvToPlayer.TryGetValue(conv, out ServerPlayer player) || !player.IsHost)
        {
            Debug.Log("【RoomHandler】非房主 无法开始游戏");
            return;
        }

        // 校验：至少2名玩家
        if (room.ConvToPlayer.Count < 2)
        {            
            Debug.Log("【RoomHandler】玩家不足 无法开始游戏");
            return;
        }

        Debug.Log("【RoomHandler】游戏开始，玩家数：" + room.ConvToPlayer.Count);

        //1.向所有远程客户端发送 GameStart
        var gameStart = new GameStart
        {
            RandomSeed = request.RandomSeed != 0 ? request.RandomSeed : UnityEngine.Random.Range(0, int.MaxValue),
            PlayerCount = room.ConvToPlayer.Count,
            GameDuration = request.GameDuration > 0 ? request.GameDuration : 120f,
            TargetScore = request.TargetScore > 0 ? request.TargetScore : 10,
            TickRate = request.TickRate > 0 ? request.TickRate : 15
        };

        var envelope = new NetMessage { GameStart = gameStart };
        _host.BroadcastToAll(envelope);

        // 通知 HostServer 游戏开始（传递游戏时长）
        _host.OnStartGame(gameStart.RandomSeed, gameStart.GameDuration);

        // 2.房主本地也要收到 GameStart（不走网络，直接派发）
        EventCenter.Dispatch(16, RoomData.HostConv, gameStart);
    }

    #endregion

    #region =============== 玩家离开 / 断开 ===============

    // 游戏结束后已返回房间的玩家ID集合（用于显示"还在游戏中"状态）
    private readonly HashSet<int> _playersReturnedToRoom = new();

    /// <summary>
    /// 客户端请求刷新玩家列表（使用 RequestPlayerList 专用消息）
    /// </summary>
    public void HandleRequestPlayerList(uint conv)
    {
        Debug.Log("【RoomHandler】收到 RequestPlayerList conv=" + conv);
        BroadcastPlayerList();
    }

    /// <summary>
    /// 客户端通知已从游戏返回房间（使用 ReturnToRoom 专用消息）
    /// </summary>
    public void HandleReturnToRoom(uint conv, ReturnToRoom request)
    {
        Debug.Log("【RoomHandler】收到 ReturnToRoom conv=" + conv);
        MarkPlayerReturnedToRoom(conv);
        BroadcastPlayerList();
    }

    /// <summary>
    /// 处理客户端主动发送的 KickOff（正常离开房间）。
    /// KickOff 仅用于「离开房间/解散房间」，不再承载其他内部协议。
    /// </summary>
    public void HandleKickOff(uint conv, KickOff request)
    {
        string reason = request != null ? request.Reason : "";
        Debug.Log("【RoomHandler】玩家主动离开 conv=" + conv + " reason=" + reason);

        // 正常的离开房间流程
        HandlePlayerDisconnect(conv);
    }

    /// <summary>
    /// 标记玩家已从游戏返回房间
    /// </summary>
    public void MarkPlayerReturnedToRoom(uint conv)
    {
        var room = _host.CurrentRoom;
        if (room == null) return;
        if (room.ConvToPlayer.TryGetValue(conv, out ServerPlayer player))
        {
            _playersReturnedToRoom.Add(player.PlayerId);
            Debug.Log("【RoomHandler】玩家" + player.PlayerName + " 已返回房间 playerId=" + player.PlayerId);
        }
    }

    /// <summary>
    /// 重置返回房间状态（游戏结束时调用，所有玩家初始为"还在游戏中"）
    /// </summary>
    public void ResetReturnedPlayers()
    {
        _playersReturnedToRoom.Clear();
    }

    /// <summary>
    /// 查询玩家是否还在游戏中（未返回房间）
    /// </summary>
    public bool IsPlayerStillInGame(int playerId)
    {
        return !_playersReturnedToRoom.Contains(playerId);
    }

    /// <summary>
    /// 处理玩家断开
    /// </summary>
    /// <param name="conv"></param>
    public void HandlePlayerDisconnect(uint conv)
    {
        var room = _host.CurrentRoom;
        if (room == null) return;

        if (room.ConvToPlayer.TryGetValue(conv, out ServerPlayer player))
        {
            room.ConvToPlayer.TryRemove(conv, out _);
            room.IdToPlayer.TryRemove(player.PlayerId, out _);

            Debug.Log("【RoomHandler】玩家断开：" + player.PlayerName + "PlayerId：" + player.PlayerId);

            // 如果游戏还没开始 广播更新的玩家列表
            if (!_host.IsGameStarted)
                BroadcastPlayerList();

            // 如果游戏已开始 由 HostServer 处理帧同步中的玩家移除
        }

    }

    #endregion

    #region =============== 断线重连 ===============

    /// <summary>
    /// 处理重连：更新 conv 绑定
    /// </summary>
    public void HandleReconnect(uint conv, int playerId)
    {
        var room = _host.CurrentRoom;
        if (room == null) return;

        // 找到这个玩家
        if (!room.IdToPlayer.TryGetValue(playerId, out ServerPlayer player))
        {
            Debug.Log("【RoomHandler】重连失败 玩家不存在：" + playerId);
            return;
        }

        // 清除旧的 conv 映射
        room.ConvToPlayer.TryRemove(player.Conv, out _);

        // 绑定新的 conv
        player.Conv = conv;
        room.ConvToPlayer[conv] = player;

        Debug.Log("【RoomHandler】玩家" + player.PlayerName + " 重连 新conv：" + conv);
    }

    #endregion

    #region =============== 查询方法 ===============

    /// <summary>
    /// 根据 conv 获取 playerId
    /// </summary>
    /// <param name="conv"></param>
    /// <returns></returns>
    public int GetPlayerIdByConv(uint conv)
    {
        var room = _host.CurrentRoom;
        if (room == null) return -1;

        if (room.ConvToPlayer.TryGetValue(conv, out ServerPlayer player))
            return player.PlayerId;
        return -1;
    }

    /// <summary>
    /// 根据 playerId 获取 conv
    /// </summary>
    /// <param name="playerId"></param>
    /// <returns></returns>
    public uint GetConvByPlayerId(int playerId)
    {
        var room = _host.CurrentRoom;
        if (room == null) return 0;

        if (room.IdToPlayer.TryGetValue(playerId, out ServerPlayer player))
            return player.Conv;
        return 0;
    }

    /// <summary>
    /// 获取主机玩家ID
    /// </summary>
    public int HostPlayerId => _host.CurrentRoom?.HostPlayerId ?? -1;

    #endregion

    #region =============== 内部方法 ===============

    /// <summary>
    /// 广播 PlayerList 给所有远程客户端。
    /// ★ 玩家名末尾带 \x01 表示"还在游戏中"（未返回房间），
    ///    客户端 Lua 解析时去掉 \x01 并根据是否存在来判断状态。
    /// </summary>
    public void BroadcastPlayerList()
    {
        var room = _host.CurrentRoom;
        if (room == null)
            return;

        var playerList = new PlayerList();
        foreach (var kvp in room.IdToPlayer)
        {
            var serverPlayer = kvp.Value;
            var playerInfo = serverPlayer.ToProto();

            // ★ 编码游戏状态：名字末尾加 \x01 表示"还在游戏中"
            //    Lua 层解析时据此显示状态，解决了客户端没有 HostServer 无法查询的问题
            //    仅在游戏已进行过后才编码状态（游戏前所有玩家都是"已在房间"）
            if (_host.HasGamePlayed && IsPlayerStillInGame(serverPlayer.PlayerId))
            {
                playerInfo.PlayerName = serverPlayer.PlayerName + "\x01";
            }

            playerList.Players.Add(playerInfo);
        }

        var evenlope = new NetMessage { PlayerList = playerList };
        _host.BroadcastToAll(evenlope);

        // 也派发给房主本机（BroadcastToAll 只发给远程客户端，房主也需要更新 UI）
        // EventCenter.Dispatch 内置了主线程保护（ThreadUtil），但这里没有监听者，保留以兼容未来扩展
        EventCenter.Dispatch(14, RoomData.HostConv, playerList);

        // 通知 Lua 层（CreateRoomPanel）—— 必须在主线程执行
        // HandlePlayerDisconnect 可能从 KCP 后台线程调用，直接 Invoke 会导致
        // XLua/Unity API 跨线程崩溃，UI 无法更新。通过 ThreadUtil 确保主线程执行。
        System.Action notifyLua = () =>
        {
            try { NetMgr.OnPlayerListCallback?.Invoke(playerList); }
            catch (Exception e) { Debug.Log("【RoomHandler】OnPlayerListCallback 异常：" + e.Message); }
        };

        if (ThreadUtil.IsMainThread())
            notifyLua();
        else
            ThreadUtil.RunOnMainThread(notifyLua);
    }

    /// <summary>
    /// 发送 JoinRoomAck uint会话号 bool是否成功 int玩家Id string错误信息 string房间Id List<PlayerInfo>玩家列表
    /// </summary>
    /// <param name="conv"> 会话号 </param>
    /// <param name="success"> 是否成功 </param>
    /// <param name="playerId"> 玩家Id </param>
    /// <param name="error"> 错误信息 </param>
    /// <param name="roomId"> 房间Id </param>
    private void SendJoinRoomAck(uint conv, bool success, int playerId, string error, string roomId, List<PlayerInfo> players)
    {
        var ack = new JoinRoomAck
        {
            Success = success,
            PlayerId = playerId,
            Error = error ?? "",
            RoomId = roomId ?? ""
        };
        if (players != null)
            ack.Players.AddRange(players);

        var envolope = new NetMessage { JoinRoomAck = ack };
        _host.SendToClient(conv, envolope);
    }

    public string GenerateRoomId()
    {
        const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ23456789";
        var rng = new System.Random();
        char[] result = new char[4];
        for (int i = 0; i < 4; i++)
            result[i] = Chars[rng.Next(Chars.Length)];
        return new string(result);
    }

    #endregion
}
// Done
