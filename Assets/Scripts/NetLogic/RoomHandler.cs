using GameProto;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
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
    public Dictionary<uint, ServerPlayer> ConvToPlayer = new(); // conv -> player
    public Dictionary<int, ServerPlayer> IdToPlayer = new();    // playerId -> player
    public int NextPlayerId = 1;
    public int HostPlayerId = -1;

    // 主机玩家的特殊 Conv 值
    public const uint HOST_CONV = 0;

    // 获取所有玩家ID
    public int[] GerAllPlayerIds()
    {
        return IdToPlayer.Keys.ToArray();
    }
    
    // 获取所有玩家的 Protobuf 列表
    public List<PlayerInfo> GetAllPlayerProtos()
    {
        return IdToPlayer.Values.Select(p => p.ToProto()).ToList();
    }

    // 分配下一个玩家ID
    public int AllocPlayerId()
    {
        return NextPlayerId++;
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
public static class ClassForNothing3 { /* 为了避免调用时产生过长的说明 */ }

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
            Conv = RoomData.HOST_CONV,
            IsHost = true
        };

        room.ConvToPlayer[RoomData.HOST_CONV] = hostPlayer;
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
            SendJoinRoomAck(0, false, 0, "房间不存在", "", null);
            return;
        }

        // 校验：房间ID是否存在匹配
        if (request.RoomId != room.RoomId)
        {
            SendJoinRoomAck(0, false, 0, "房间ID错误", "", null);
        }

        // 校验：房间是否已满（最多4人 可调整）
        if (room.ConvToPlayer.Count >= 4)
        {
            SendJoinRoomAck(0, false, 0, "房间已满，拒绝加入", "", null);
            return;
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
            RandomSeed = UnityEngine.Random.Range(0, int.MaxValue),
            PlayerCount = room.ConvToPlayer.Count,
            GameDuration = 120f,    
            TargetScore = 10,
            TickRate = 15
        };

        var envelope = new NetMessage { GameStart = gameStart };
        _host.BroadcastToAll(envelope);

        // 2.房主本地也要收到 GameStart（不走网络，直接派发）
        EventCenter.Dispatch(16, RoomData.HOST_CONV, gameStart);

        // 通知 HostServer 游戏开始
        _host.OnStartGame();
    }

    #endregion

    #region =============== 玩家断开 ===============

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
            room.ConvToPlayer.Remove(conv);
            room.IdToPlayer.Remove(player.PlayerId);

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
        room.ConvToPlayer.Remove(player.Conv);

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
    /// 广播 PlayerList 给所有远程客户端
    /// </summary>
    private void BroadcastPlayerList()
    {
        var room = _host.CurrentRoom;
        if (room == null)
            return;

        var playerList = new PlayerList();
        playerList.Players.AddRange(room.GetAllPlayerProtos());

        var evenlope = new NetMessage { PlayerList = playerList };
        _host.BroadcastToAll(evenlope);
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

    private string GenerateRoomId()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWSYZ23456789";
        var rng = new System.Random();
        char[] result = new char[4];
        for (int i = 0; i < 4; i++)
            result[i] = chars[rng.Next(chars.Length)];
        return new string(result);
    }

    #endregion
}