using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Buffers;
using System.Net.Sockets.Kcp;
using System.Collections.Generic;

/// <summary>
/// 最简帧同步Demo — 主机模式 / KCP+UDP / 2人
/// 场景中创建一个空物体挂此脚本
/// 主机实例勾 IsHost，客户端实例不勾并填 ServerIP
/// 运行后用 WASD / 方向键移动
/// </summary>
public class FrameSyncServerDemo : MonoBehaviour, IKcpCallback
{
    [Header("网络")]
    public bool IsHost = true;
    public string ServerIP = "127.0.0.1";
    public int Port = 9000;

    [Header("帧同步")]
    public int LogicFrameRate = 20;   // 逻辑帧率
    public float MoveSpeed = 5f;

    // ======== KCP / UDP ========
    Socket _sock;
    SimpleSegManager.Kcp _kcp;
    EndPoint _remote;
    byte[] _rawBuf = new byte[65536];  // UDP原始接收
    byte[] _kcpBuf = new byte[65536];  // KCP解包后

    // ======== 帧同步状态 ========
    bool _ready;
    int _curFrame;
    float _tickAccum;

    // 主机端: 缓存客户端最新输入
    float _cH, _cV;

    // 客户端端: 帧缓冲, frame -> [h0,v0,h1,v1]
    Dictionary<int, float[]> _frameBuf = new Dictionary<int, float[]>();

    // ======== 表现层 ========
    Vector2[] _pos = new Vector2[2];
    GameObject[] _go = new GameObject[2];

    // ====================================================================
    //  KCP 回调 — KCP内部组装好数据包后回调此函数, 完成真正的UDP发送
    // ====================================================================
    public void Output(IMemoryOwner<byte> buffer, int avalidLength)
    {
        byte[] data = buffer.Memory.Span.Slice(0, avalidLength).ToArray();
        _sock.SendTo(data, _remote);
        buffer.Dispose();
    }

    // ====================================================================
    //  启动: 初始化Socket + KCP
    // ====================================================================
    void Start()
    {
        _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _sock.Blocking = false;

        _kcp = new SimpleSegManager.Kcp(1, this);   // conv=1, 双方必须一致
        _kcp.NoDelay(1, 10, 2, 1);                  // 无延迟模式

        if (IsHost)
        {
            _sock.Bind(new IPEndPoint(IPAddress.Any, Port));
            _remote = new IPEndPoint(IPAddress.Any, 0);  // 等客户端发数据时自动获取
            _ready = true;
            SpawnPlayers();
        }
        else
        {
            _sock.Bind(new IPEndPoint(IPAddress.Any, 0));
            _remote = new IPEndPoint(IPAddress.Parse(ServerIP), Port);
            _kcp.Send(new byte[] { 0x01 });  // 连接请求
        }
    }

    void SpawnPlayers()
    {
        Color[] colors = { Color.red, Color.blue };
        for (int i = 0; i < 2; i++)
        {
            _go[i] = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _go[i].name = "Player" + i;
            _go[i].GetComponent<Renderer>().material.color = colors[i];
            _pos[i] = new Vector2(i * 3, 0);
        }
    }

    // ====================================================================
    //  主循环: KCP驱动 + 逻辑帧 + 渲染
    // ====================================================================
    void Update()
    {
        _kcp.Update(DateTime.UtcNow);   // 驱动KCP内部状态机(重传/拥塞/刷新发送队列)
        PollReceive();
        if (!_ready) return;

        // 按固定逻辑帧率执行
        _tickAccum += Time.deltaTime;
        float interval = 1f / LogicFrameRate;
        while (_tickAccum >= interval)
        {
            _tickAccum -= interval;
            LogicTick();
        }

        // 渲染: 逻辑位置 → 物体位置
        for (int i = 0; i < 2; i++)
            _go[i].transform.position = new Vector3(_pos[i].x, 0, _pos[i].y);
    }

    void LogicTick()
    {
        if (IsHost) HostTick(); else ClientTick();
    }

    // ====================================================================
    //  主机: 收集所有输入 → 模拟 → 广播帧
    // ====================================================================
    void HostTick()
    {
        // 采集本地输入(玩家0) + 客户端缓存输入(玩家1)
        float[] inputs = {
            Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"),  // p0
            _cH, _cV                                                       // p1
        };

        Simulate(inputs);

        // 广播帧数据: [type=0x04][frame 4B][h0 v0 h1 v1 各4B] = 21字节
        byte[] msg = new byte[21];
        msg[0] = 0x04;
        BitConverter.GetBytes(_curFrame).CopyTo(msg, 1);
        for (int i = 0; i < 4; i++)
            BitConverter.GetBytes(inputs[i]).CopyTo(msg, 5 + i * 4);
        _kcp.Send(msg);

        _curFrame++;
    }

    // ====================================================================
    //  客户端: 发送输入 → 等帧数据 → 模拟
    // ====================================================================
    void ClientTick()
    {
        // 发送输入: [type=0x03][frame 4B][h 4B][v 4B] = 13字节
        byte[] msg = new byte[13];
        msg[0] = 0x03;
        BitConverter.GetBytes(_curFrame).CopyTo(msg, 1);
        BitConverter.GetBytes(Input.GetAxisRaw("Horizontal")).CopyTo(msg, 5);
        BitConverter.GetBytes(Input.GetAxisRaw("Vertical")).CopyTo(msg, 9);
        _kcp.Send(msg);

        // 有帧数据才推进 — 纯帧同步: 等主机"发号施令"
        if (_frameBuf.TryGetValue(_curFrame, out var inputs))
        {
            Simulate(inputs);
            _frameBuf.Remove(_curFrame);  // 用完即清
            _curFrame++;
        }
        // 没收到 → 不推进, 等下一帧再试
    }

    // ====================================================================
    //  确定性模拟: 两边执行相同输入 → 相同结果
    // ====================================================================
    void Simulate(float[] inputs)
    {
        float dt = 1f / LogicFrameRate;
        for (int i = 0; i < 2; i++)
            _pos[i] += new Vector2(inputs[i * 2], inputs[i * 2 + 1]) * MoveSpeed * dt;
    }

    // ====================================================================
    //  网络: 非阻塞接收 → 喂KCP → 取完整消息
    // ====================================================================
    void PollReceive()
    {
        while (_sock.Available > 0)
        {
            EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                int len = _sock.ReceiveFrom(_rawBuf, ref ep);
                if (IsHost) _remote = ep;   // 记住客户端地址
                _kcp.Input(_rawBuf.AsSpan(0, len));
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock) { break; }
            catch { break; }
        }

        // 从KCP取出完整消息
        while (true)
        {
            int n = _kcp.Recv(_kcpBuf);
            if (n < 0) break;
            HandleMsg(_kcpBuf, n);
        }
    }

    // ====================================================================
    //  消息处理
    // ====================================================================
    void HandleMsg(byte[] d, int n)
    {
        switch (d[0])
        {
            // ---- 连接握手 ----
            case 0x01 when IsHost:     // 客户端请求连接
                SpawnPlayers();
                _kcp.Send(new byte[] { 0x02 });
                Debug.Log("[Host] 玩家加入");
                break;

            case 0x02 when !IsHost:    // 主机确认
                SpawnPlayers();
                _ready = true;
                Debug.Log("[Client] 已连接");
                break;

            // ---- 帧同步数据 ----
            case 0x03 when IsHost:     // 客户端输入
                _cH = BitConverter.ToSingle(d, 5);
                _cV = BitConverter.ToSingle(d, 9);
                break;

            case 0x04 when !IsHost:    // 主机广播帧数据
                int f = BitConverter.ToInt32(d, 1);
                float[] inputs = new float[4];
                for (int i = 0; i < 4; i++)
                    inputs[i] = BitConverter.ToSingle(d, 5 + i * 4);
                _frameBuf[f] = inputs;
                break;
        }
    }

    void OnDestroy()
    {
        _sock?.Close();
        _kcp?.Dispose();
    }
}
