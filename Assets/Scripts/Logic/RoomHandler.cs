using GameProto;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Linq;
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
    //public List<PlayerInfo>

    // 分配下一个玩家ID
}











