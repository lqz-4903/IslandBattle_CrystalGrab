using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// ═══════════════════════════════════════════════════════════════
///     KcpMgr —— 基于 KCP 协议的 UDP 网络管理器（单例）
/// ═══════════════════════════════════════════════════════════════
///
/// 【定位】
///   封装 KCP 可靠 UDP 通信，对外提供简洁的收发 API。
///   支持两种运行模式：服务器模式（一主多从）和客户端模式（一对一）。
///
/// 【核心架构】
///
///   ┌──────────────────────────────
///   │                  KcpMgr (单例)                            │
///   │                                                           │  
///   │  ┌───────┐  ───────┐  ────────   │
///   │  │ UdpRecvLoop  │ │ KcpSendLoop │ │KcpUpdateLoop │  │
///   │  │  (后台线程)  │ │  (后台线程) │ │  (后台线程)  │  │
///   │  │              │ │             │ │              │  │
///   │  │ Socket →    │ │ Channel →  │ │ 定时驱动:    │  │
///   │  │ KCP.Input → │ │ KCP.Send → │ │ 超时重传     │  │
///   │  │ recvQueue    │ │ KCP.Update  │ │ 拥塞控制     │  │
///   │  └───────┘  ───────┘ └───────┘  │
///   │         │                │                              │
///   │         ▼                ▼                              │
///   │    recvQueue        sendChannel                           │
///   │  (ConcurrentQueue)  (Channel)                             │
///   └───────────────────────────── ┼
///             │                │
///             ▼                ▼
///   ┌────────────────────
///   │       业务层 (GameNetManager)         │
///   │  Update() 中调用 TryRecv() 取消息     │
///   │  业务逻辑中调用 SendAsync() 发消息    │
///   └────────────────────
///
/// 【三种后台线程】
///
///   1. UdpRecvLoop  —— 接收线程
///      Socket.ReceiveFrom → KCP.Input → KCP.Recv → recvQueue
///
///   2. KcpSendLoop  —— 发送线程
///      sendChannel → KCP.Send → KCP.Update → Output → Socket.SendTo
///
///   3. KcpUpdateLoop —— 心跳线程 (每10ms)
///      遍历所有KCP实例 → Update → 超时重传/拥塞控制/ACK发送
///
/// 【KCP 实例管理 + 握手协议（conv 冲突解决）】
///
///   客户端启动 → 发送 conv=0 + "KCPC" 魔数 → 服务器分配唯一 conv
///   → 返回 "KCPA" + 分配的 conv → 客户端用分配的 conv 创建 KCP 实例
///   后续所有通信通过 KCP 进行，conv 不再变化
///   彻底消除了传统方案中"服务器重分配 conv 后客户端不知情"的问题
///
/// 【使用方式】
///   await KcpMgr.Instance.StartAsServerAsync(8888);
///   while (KcpMgr.Instance.TryRecv(out uint conv, out byte[] data)) { ... }
///   await KcpMgr.Instance.SendAsync(conv, data);
///
/// 【两种消息消费模式（二选一，不可同时使用）】
///   模式A：调用 KcpMgr.Instance.Update() → 触发 OnRecvData/OnRecvMsg 事件
///   模式B：调用 KcpMgr.Instance.TryRecv() → 业务主动拉取 ★推荐★
///
/// ═══════════════════════════════════════════════════════════════
/// </summary>

public static class ClassForNothing { /* 为了避免调用时产生过长的说明 */ }

/// <summary>
/// KcpMgr - 基于 KCP 协议的 UDP 网络管理器（单例）
/// </summary>
public class KcpMgr : IKcpCallback
{
    #region ==================== 单例 ====================

    /// <summary>唯一实例（饿汉式，类加载时即创建）</summary>
    private static readonly KcpMgr instance = new KcpMgr();

    /// <summary>全局访问点</summary>
    public static KcpMgr Instance => instance;

    /// <summary>私有构造，防止外部 new</summary>
    private KcpMgr() { }

    #endregion

    #region ==================== 核心数据结构 ====================

    // ───────── KCP 实例管理 ─────────

    /// <summary>
    /// KCP 实例字典：会话ID(conv) → KCP 实例
    /// 服务器：每客户端一个独立实例，conv 各不相同
    /// 客户端：只有一个实例
    /// 多线程安全（ConcurrentDictionary）
    /// </summary>
    private ConcurrentDictionary<uint, SimpleSegManager.Kcp> _kcpDic = new ConcurrentDictionary<uint, SimpleSegManager.Kcp>();

    // ───────── 地址 ↔ 会话 双向映射 ─────────

    /// <summary>
    /// 正向映射：端点字符串("IP:Port") → 会话ID(conv)
    /// 用途：收到 UDP 包时根据来源地址找 KCP 实例
    /// </summary>
    private ConcurrentDictionary<string, uint> _endPointToConvDic = new ConcurrentDictionary<string, uint>();

    /// <summary>
    /// 反向映射：会话ID(conv) → 端点(EndPoint)
    /// 用途：发送数据时知道目标 IP:Port
    /// </summary>
    private ConcurrentDictionary<uint, EndPoint> _convToEndPointDic = new ConcurrentDictionary<uint, EndPoint>();

    // ───────── 收发队列 ─────────

    /// <summary>
    /// 接收队列：已通过 KCP 重组完成的完整业务消息
    /// 生产者：UdpRecvLoop（KCP.Recv 解出消息后入队）
    /// 消费者：Unity 主线程 Update() 中 TryRecv() 出队
    /// </summary>
    private ConcurrentQueue<(uint conv, byte[] data)> _recvQuene = new ConcurrentQueue<(uint conv, byte[] data)>();

    /// <summary>
    /// 发送通道：生产者-消费者模式
    /// 生产者：业务线程 SendAsync() 写入
    /// 消费者：KcpSendLoop ReadAsync() 读取
    /// 无界队列，写入永不阻塞
    /// </summary>
    private Channel<(uint conv, byte[] data)> _sendChannel = Channel.CreateUnbounded<(uint conv, byte[] data)>();

    // ───────── UDP Socket 与缓冲区 ─────────

    /// <summary>UDP Socket 实例</summary>
    private Socket _socket;

    /// <summary>
    /// UDP 原始数据接收缓冲区（复用，避免 GC）
    /// 大小 65535 = UDP 单包理论最大值
    /// 仅 UdpRecvLoop 单线程读写
    /// </summary>
    private byte[] _recvBuffer = new byte[65535];

    /// <summary>
    /// KCP 解包后的业务数据缓冲区（复用）
    /// KCP.Recv() 将重组好的消息写入此缓冲区
    /// 仅 UdpRecvLoop 单线程读写
    /// </summary>
    private byte[] _kcpBuffer = new byte[65535];

    // ───────── 运行状态 ─────────

    /// <summary>
    /// 是否正在运行
    /// Start → true，Stop → false
    /// 三个后台循环线程以此作为退出条件
    /// </summary>
    private bool _isRunning = false;

    /// <summary>
    /// 运行模式
    /// true = 服务器（可接受多客户端），false = 客户端（连一台服务器）
    /// </summary>
    private bool _isServerMode = true;

    /// <summary>
    /// 客户端模式下服务器的地址
    /// StartAsClientAsync 中设置，Output 回调时用于 SendTo
    /// </summary>
    private EndPoint _serverEndPoint;

    /// <summary>
    /// 客户端模式下手握手成功后分配的 conv（供 NetMgr 等业务层使用）。
    /// 服务器模式下此值为 0。
    /// </summary>
    public uint ClientConv { get; private set; }

    /// <summary>
    /// 下一个可分配的会话 ID（原子递增，线程安全）。
    /// 起始 1000：预留 1~999 给特殊用途，同时避免与旧版客户端默认值冲突。
    /// 服务端握手时通过 Interlocked.Increment 分配，保证全局唯一。
    /// </summary>
    private int _nextConvID = 1000;

    // ───────── 连接握手协议 ─────────

    /// <summary>
    /// 握手请求中表示"请求分配会话"的特殊 conv 值。
    /// 客户端发送 conv=0 的原始 UDP 包（不经 KCP），
    /// 服务端识别后分配唯一 conv 并返回应答。
    /// </summary>
    private const uint HandshakeConv = 0;

    /// <summary>
    /// 连接请求魔数 "KCPC"（KCP Connect 的 ASCII）。
    /// 紧跟在 conv=0 之后的 4 字节标识，用于区分握手包和普通 KCP 数据包。
    /// 字节: 0x4B='K', 0x43='C', 0x50='P', 0x43='C'
    /// </summary>
    private static readonly byte[] HandshakeRequestMagic = { 0x4B, 0x43, 0x50, 0x43 };

    /// <summary>
    /// 连接应答魔数 "KCPA"（KCP Ack 的 ASCII）。
    /// 服务端在握手应答包中携带此魔数，客户端据此识别应答包。
    /// 字节: 0x4B='K', 0x43='C', 0x50='P', 0x41='A'
    /// </summary>
    private static readonly byte[] HandshakeAckMagic = { 0x4B, 0x43, 0x50, 0x41 };

    /// <summary>握手包固定大小：4 字节 conv（小端序）+ 4 字节魔数 = 8 字节</summary>
    private const int HandshakePacketSize = 8;

    /// <summary>握手单次超时 5000ms，超时后进入重试</summary>
    private const int HandshakeTimeoutMs = 5000;

    /// <summary>握手最多重试 3 次，全部失败则返回 conv=0 表示握手失败</summary>
    private const int HandshakeMaxRetries = 3;

    // ───────── 内存池（减少 GC） ─────────

    /// <summary>
    /// 发送端 ArrayPool 缓冲区。
    /// Output 回调中 Rent → CopyTo → SendTo → Return，
    /// 避免每次 KCP 发送都 new byte[] 产生 GC 压力。
    /// </summary>
    private readonly ArrayPool<byte> _sendBufferPool = ArrayPool<byte>.Shared;

    // ───────── 异步接收信号 ─────────

    /// <summary>
    /// 数据到达信号量（初始计数 0）。
    /// UdpRecvLoop 收到数据入队后 Release()，
    /// RecvAsync 通过 WaitAsync() 等待，不再忙等轮询。
    /// </summary>
    private readonly SemaphoreSlim _recvSignal = new SemaphoreSlim(0);

    // ───────── 线程控制 ─────────

    /// <summary>取消令牌源，Cancel() 后三个后台线程优雅退出</summary>
    private CancellationTokenSource _cts;

    /// <summary>UDP 接收线程句柄</summary>
    private Task _udpRecvTask;

    /// <summary>KCP 发送处理线程句柄</summary>
    private Task _kcpSendTask;

    /// <summary>KCP 状态更新线程句柄（超时重传/拥塞控制）</summary>
    private Task _kcpUpdateTask;

    // ───────── 对外回调事件 ─────────

    /// <summary>
    /// 收到业务数据时触发（原始字节数组）
    /// 参数：conv(会话ID), data(业务数据)
    /// 注意：从后台线程触发，Unity 中需投递到主线程
    /// </summary>
    public Action<uint, byte[]> OnRecvData;

    /// <summary>
    /// 收到业务数据时触发（UTF-8 字符串版本）
    /// 与 OnRecvData 同时触发，同一份数据的两种形式
    /// </summary>
    public Action<uint, string> OnRecvMsg;

    /// <summary>
    /// 服务器模式下，新客户端连接时触发
    /// 参数：conv(新客户端的会话ID)
    /// </summary>
    public Action<uint> OnClientConnected;

    /// <summary>
    /// 客户端断开连接时触发
    /// 参数：conv(会话ID), reason(断开原因)
    /// </summary>
    public Action<uint, string> OnClientDisconnected;

    #endregion

    #region ==================== KCP 配置参数（默认值） ====================

    /// <summary>无延迟模式：0=关闭, 1=开启。游戏建议 1</summary>
    private int _noDelay = 1;

    /// <summary>内部刷新间隔(ms)，越小延迟越低CPU越高。游戏建议 10</summary>
    private int _interval = 10;

    /// <summary>快速重传阈值，后续包确认达此次数即重传。典型 2-5</summary>
    private int _resend = 2;

    /// <summary>流控开关：0=开启拥塞控制, 1=关闭(全速发)</summary>
    private int _nc = 1;

    /// <summary>发送窗口（同时在途最大包数）。典型 128-512</summary>
    private int _sendWin = 128;

    /// <summary>接收窗口（最大缓存乱序包数）。应 ≥ sendWin</summary>
    private int _recvWin = 128;

    /// <summary>最大传输单元(字节)。局域网1500，互联网建议1400</summary>
    private int _mtu = 1400;

    #endregion

    #region ==================== 配置方法 ====================

    /// <summary>
    /// 设置 KCP 延迟模式参数（运行时动态调整，同步到所有已存在的 KCP 实例）
    ///
    /// 预设参考：
    ///   普通模式: (0, 40, 0, 0) — 带宽友好，延迟较高
    ///   激进模式: (1, 10, 2, 1) — 低延迟，适合实时游戏
    ///   极速模式: (1,  5, 2, 1) — 最低延迟，带宽消耗大
    /// </summary>
    public async Task SetNoDelayAsync(int noDelay, int interval, int resend, int nc)
    {
        this._noDelay = noDelay;
        this._interval = interval;
        this._resend = resend;
        this._nc = nc;

        foreach (var kcp in _kcpDic.Values)
            kcp.NoDelay(noDelay, interval, resend, nc);

        await Task.CompletedTask;
    }

    /// <summary>设置收发窗口大小（运行时动态调整）</summary>
    public async Task SetWinSizeAsync(int sendWin, int recvWin)
    {
        this._sendWin = sendWin;
        this._recvWin = recvWin;

        foreach (var kcp in _kcpDic.Values)
            kcp.WndSize(sendWin, recvWin);

        await Task.CompletedTask;
    }

    /// <summary>设置最大传输单元 MTU（运行时动态调整）</summary>
    public async Task SetMTUAsync(int mtu)
    {
        this._mtu = mtu;

        foreach (var kcp in _kcpDic.Values)
            kcp.SetMtu(mtu);

        await Task.CompletedTask;
    }

    #endregion

    #region ==================== 启动与停止 ====================

    /// <summary>
    /// 以服务器模式启动
    ///
    /// 流程：创建Socket → 绑定端口 → 创建Cts → 启动三个后台线程
    /// 之后等待客户端连接，新连接通过 OnClientConnected 通知
    /// </summary>
    /// <param name="port">监听端口，默认 8888</param>
    public async Task StartAsServerAsync(int port = 8888)
    {
        if (_isRunning)
        {
            Debug.Log("【KCPMgr】已经在运行");
            return;
        }

        _isServerMode = true;

        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, port));

            _cts = new CancellationTokenSource();
            _isRunning = true;

            _udpRecvTask = Task.Run(UdpRecvLoop);
            _kcpSendTask = Task.Run(KcpSendLoop);
            _kcpUpdateTask = Task.Run(KcpUpdateLoop);

            Debug.Log("【KcpMgr】服务器启动成功，端口：" + port);
        }
        catch (SocketException se)
        {
            Debug.Log("【KcpMgr】启动服务器失败：" + se.SocketErrorCode + " " + se.Message);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 以客户端模式启动
    ///
    /// 流程：创建Socket → 绑定本地端口 → 握手（请求→服务器分配conv→应答）
    ///     → 用分配的conv创建KCP实例 → 启动三个后台线程
    ///
    /// 握手协议（conv冲突解决方案）：
    ///   客户端发送 conv=0 + "KCPC" 魔数 → 服务器分配唯一conv
    ///   → 服务器返回 分配的conv + "KCPA" 魔数 → 客户端用此conv创建KCP
    /// </summary>
    /// <param name="serverIP">服务器IP</param>
    /// <param name="serverPort">服务器端口</param>
    /// <param name="localPort">本地端口，0=系统随机</param>
    public async Task StartAsClientAsync(string serverIP, int serverPort, int localPort = 0)
    {
        if (_isRunning)
        {
            Debug.Log("【KCPMgr】已经在运行");
            return;
        }

        _isServerMode = false;

        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            if (localPort > 0)
                _socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
            else
                _socket.Bind(new IPEndPoint(IPAddress.Any, 0));

            _serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);

            // ── 握手阶段：向服务器请求分配 conv ──
            uint assignedConv = await HandshakeWithServerAsync();
            if (assignedConv == 0)
            {
                Debug.Log("【KcpMgr】握手失败，无法连接到服务器");
                _socket.Close();
                _socket = null;
                return;
            }

            Debug.Log("【KcpMgr】握手成功，服务器分配 Conv：" + assignedConv);

            // 暴露给 NetMgr 等业务层使用
            ClientConv = assignedConv;
            Debug.Log("【KcpMgr】ClientConv 已设置为 " + ClientConv);

            // ── 用服务器分配的 conv 创建 KCP 实例 ──
            CreateKcpInstance(assignedConv, _serverEndPoint, true);

            _cts = new CancellationTokenSource();
            _isRunning = true;

            _udpRecvTask = Task.Run(UdpRecvLoop);
            _kcpSendTask = Task.Run(KcpSendLoop);
            _kcpUpdateTask = Task.Run(KcpUpdateLoop);

            Debug.Log("【KcpMgr】客户端启动成功，连接：" + serverIP + ":" + serverPort);
        }
        catch (SocketException se)
        {
            Debug.Log("【KcpMgr】启动客户端失败：" + se.SocketErrorCode + " " + se.Message);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 客户端握手流程：
    ///
    ///   1. 通过原始 UDP 发送连接请求包（conv=0 + "KCPC"）
    ///   2. 阻塞等待服务器应答（超时 HandshakeTimeoutMs = 5000ms）
    ///   3. 收到应答后校验魔数 "KCPA"，提取服务器分配的 conv
    ///   4. 超时或校验失败则重试，最多 HandshakeMaxRetries = 3 次
    ///   5. 重试间隔采用递增退避：500ms → 1000ms → 1500ms
    ///
    /// 为什么用原始 UDP 而非 KCP：
    ///   此时客户端还没有 conv，无法创建 KCP 实例。
    ///   必须先通过轻量握手拿到 conv，才能建立 KCP 会话。
    /// </summary>
    /// <returns>服务器分配的 conv，0 表示握手彻底失败</returns>
    private async Task<uint> HandshakeWithServerAsync()
    {
        byte[] requestPacket = BuildHandshakeRequest();
        byte[] recvBuf = new byte[HandshakePacketSize];

        for (int retry = 0; retry < HandshakeMaxRetries; retry++)
        {
            try
            {
                // 1. 发送连接请求（原始 UDP，不经 KCP）
                await Task.Run(() => _socket.SendTo(requestPacket, _serverEndPoint));
                Debug.Log($"【KcpMgr】发送连接请求（第{retry + 1}次）→ {_serverEndPoint}");

                // 2. 等待应答（带超时，超时后抛 OperationCanceledException）
                var cts = new CancellationTokenSource(HandshakeTimeoutMs);
                try
                {
                    EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    int len = await Task.Run(
                        () => _socket.ReceiveFrom(recvBuf, ref remoteEP),
                        cts.Token
                    );

                    // 3. 解析应答：长度=8 + 魔数="KCPA" + conv≠0
                    if (len == HandshakePacketSize && IsHandshakeAck(recvBuf))
                    {
                        uint assignedConv = BitConverter.ToUInt32(recvBuf, 0);
                        if (assignedConv != 0)
                            return assignedConv;
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.Log($"【KcpMgr】握手超时（第{retry + 1}次），重试中...");
                }
            }
            catch (Exception e)
            {
                Debug.Log($"【KcpMgr】握手异常（第{retry + 1}次）：{e.Message}");
            }

            // 递增退避：第1次等500ms，第2次等1000ms
            if (retry < HandshakeMaxRetries - 1)
                await Task.Delay(500 * (retry + 1));
        }

        return 0;
    }

    /// <summary>
    /// 构建连接请求包，格式（大端视角）：
    /// ┌──────────────────────┬──────────────────────┐
    /// │  conv = 0 (4B LE)   │ 魔数 "KCPC" (4B)     │
    /// └──────────────────────┴──────────────────────┘
    /// conv=0 即 HandshakeConv，表示"请求分配会话"。
    /// "KCPC" = 0x4B 0x43 0x50 0x43，用于与普通 KCP 数据包区分。
    /// </summary>
    private static byte[] BuildHandshakeRequest()
    {
        byte[] packet = new byte[HandshakePacketSize];
        // 前 4 字节：conv = HandshakeConv = 0，new byte[] 默认全零，无需额外赋值
        // 后 4 字节：魔数 "KCPC"
        Buffer.BlockCopy(HandshakeRequestMagic, 0, packet, 4, 4);
        return packet;
    }

    /// <summary>
    /// 构建连接应答包，格式（大端视角）：
    /// ┌──────────────────────┬──────────────────────┐
    /// │  assignedConv (4B LE)│ 魔数 "KCPA" (4B)     │
    /// └──────────────────────┴──────────────────────┘
    /// 服务端调用此方法，传入分配的 conv，客户端收到后提取并用于创建 KCP。
    /// "KCPA" = 0x4B 0x43 0x50 0x41，客户端据此识别握手应答包。
    /// </summary>
    private static byte[] BuildHandshakeAck(uint conv)
    {
        byte[] packet = new byte[HandshakePacketSize];
        // 前 4 字节：分配的 conv（小端序）
        byte[] convBytes = BitConverter.GetBytes(conv);
        Buffer.BlockCopy(convBytes, 0, packet, 0, 4);
        // 后 4 字节：魔数 "KCPA"
        Buffer.BlockCopy(HandshakeAckMagic, 0, packet, 4, 4);
        return packet;
    }

    /// <summary>
    /// 检查一个收到的 UDP 包是否是握手请求。
    /// 判断条件：
    ///   1. 包长度 ≥ 8 字节（HandshakePacketSize）
    ///   2. 前 4 字节 conv == 0（HandshakeConv）
    ///   3. 后 4 字节匹配 HandshakeRequestMagic（"KCPC"）
    /// </summary>
    private static bool IsHandshakeRequest(byte[] data, int offset = 0)
    {
        if (data.Length - offset < HandshakePacketSize) return false;

        // 校验前 4 字节：conv 必须为 HandshakeConv(=0)
        if (data[offset] != 0 || data[offset + 1] != 0 ||
            data[offset + 2] != 0 || data[offset + 3] != 0)
            return false;

        // 校验后 4 字节：必须匹配 "KCPC"
        for (int i = 0; i < 4; i++)
            if (data[offset + 4 + i] != HandshakeRequestMagic[i])
                return false;
        return true;
    }

    /// <summary>
    /// 检查一个收到的 UDP 包是否是握手应答。
    /// 判断条件：
    ///   1. 包长度 ≥ 8 字节（HandshakePacketSize）
    ///   2. 后 4 字节匹配 HandshakeAckMagic（"KCPA"）
    /// 不校验前 4 字节 conv（由调用方 BitConverter.ToUInt32 提取）。
    /// </summary>
    private static bool IsHandshakeAck(byte[] data, int offset = 0)
    {
        if (data.Length - offset < HandshakePacketSize) return false;

        // 校验后 4 字节：必须匹配 "KCPA"
        for (int i = 0; i < 4; i++)
            if (data[offset + 4 + i] != HandshakeAckMagic[i])
                return false;
        return true;
    }

    /// <summary>
    /// 停止服务并释放所有资源
    ///
    /// 流程：isRunning=false → cts.Cancel → 等待线程退出(1秒超时)
    ///     → 销毁所有KCP实例 → 清空映射 → 关闭Socket
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            Debug.Log("【KcpMgr】StopAsync 被调用但 _isRunning=false，跳过（可能已断开）");
            return;
        }

        Debug.Log("【KcpMgr】StopAsync 开始关闭...");
        _isRunning = false;
        _cts?.Cancel();

        // 唤醒所有等待 RecvAsync 的调用者
        try { _recvSignal.Release(); }
        catch (SemaphoreFullException) { }

        // 等待三个线程退出，最多等 1 秒
        if (_udpRecvTask != null)
            await Task.WhenAny(_udpRecvTask, Task.Delay(1000));
        if (_kcpSendTask != null)
            await Task.WhenAny(_kcpSendTask, Task.Delay(1000));
        if (_kcpUpdateTask != null)
            await Task.WhenAny(_kcpUpdateTask, Task.Delay(1000));

        // 销毁所有 KCP 实例
        foreach (var kcp in _kcpDic.Values)
            kcp?.Dispose();
        _kcpDic.Clear();
        _endPointToConvDic.Clear();
        _convToEndPointDic.Clear();

        // 清空接收队列
        while (_recvQuene.TryDequeue(out _)) { }

        // 关闭 Socket
        _socket?.Close();
        _socket = null;

        // 清除客户端 conv
        ClientConv = 0;

        Debug.Log("【KcpMgr】服务已停止");
    }

    #endregion

    #region ==================== KCP 实例管理 ====================

    /// <summary>
    /// 创建 KCP 实例并注册到管理字典
    ///
    /// 1. 创建 KCP 对象，应用当前配置
    /// 2. 写入 kcpDic / endPointToConvDic / convToEndPointDic
    /// 3. 服务器模式下触发 OnClientConnected
    ///
    /// conv 已存在则跳过（不覆盖）
    /// </summary>
    private void CreateKcpInstance(uint conv, EndPoint remoteEndPoint, bool isClient = false)
    {
        if (_kcpDic.ContainsKey(conv))
        {
            Debug.Log("【KcpMgr】Kcp实例已存在 Conv：" + conv);
            return;
        }

        SimpleSegManager.Kcp kcp = new SimpleSegManager.Kcp(conv, this);

        // 应用当前配置
        kcp.NoDelay(_noDelay, _interval, _resend, _nc);
        kcp.WndSize(_sendWin, _recvWin);
        kcp.SetMtu(_mtu);

        // 注册到三个字典
        _kcpDic[conv] = kcp;

        string endPointKey = GetEndPointKey(remoteEndPoint);
        _endPointToConvDic[endPointKey] = conv;
        _convToEndPointDic[conv] = remoteEndPoint;

        // 服务器模式下通知业务层：有新客户端连入
        if (!isClient && _isServerMode)
        {
            OnClientConnected?.Invoke(conv);
        }
    }

    /// <summary>根据 conv 获取 KCP 实例，不存在返回 null</summary>
    private SimpleSegManager.Kcp GetKcp(uint conv)
    {
        _kcpDic.TryGetValue(conv, out SimpleSegManager.Kcp kcp);
        return kcp;
    }

    /// <summary>根据 UDP 来源地址查找 conv，未找到返回 0</summary>
    private uint GetConvByEndPoint(EndPoint endPoint)
    {
        string key = GetEndPointKey(endPoint);
        if (_endPointToConvDic.TryGetValue(key, out uint conv))
            return conv;
        return 0;
    }

    /// <summary>EndPoint → "IP:Port" 字符串，用作字典键</summary>
    private string GetEndPointKey(EndPoint endPoint)
    {
        IPEndPoint ip = endPoint as IPEndPoint;
        return $"{ip.Address}:{ip.Port}";
    }

    #endregion

    #region ==================== IKcpCallback 实现 ====================

    /// <summary>
    /// KCP 内部的发送回调
    ///
    /// KCP 组装好一个完整的 UDP 包（含 KCP 头）后回调此方法，
    /// 由 KcpMgr 执行真正的 Socket.SendTo。
    ///
    /// 策略：
    ///   服务器：从包头读目标 conv → 定向发给对应客户端
    ///   客户端：直接发给服务器地址
    ///
    /// 线程安全：sendData 为局部变量
    /// </summary>
    /// <param name="buffer">KCP 分配的内存块，用完必须 Dispose</param>
    /// <param name="avalidLength">有效数据长度</param>
    public void Output(IMemoryOwner<byte> buffer, int avalidLength)
    {
        // 从内存池租用发送缓冲区（避免每次 new byte[] 造成的 GC 压力）
        byte[] sendData = _sendBufferPool.Rent(avalidLength);
        try
        {
            buffer.Memory.Span.Slice(0, avalidLength).CopyTo(sendData);

            if (_isServerMode)
            {
                // 从 KCP 包头（前4字节，小端序）读取目标 conv
                uint targetConv = 0;
                if (sendData.Length >= 4)
                    targetConv = (uint)(sendData[0]
                        | (sendData[1] << 8)
                        | (sendData[2] << 16)
                        | (sendData[3] << 24));

                // 查找目标 endpoint 并发送（指定精确长度，避免发送池化缓冲区的尾部垃圾）
                if (_convToEndPointDic.TryGetValue(targetConv, out var targetEndPoint))
                {
                    try
                    {
                        _socket.SendTo(sendData, avalidLength, SocketFlags.None, targetEndPoint);
                    }
                    catch (SocketException se)
                    {
                        // 10054 = 远程主机强迫关闭（Windows）
                        if (se.ErrorCode == 10054)
                        {
                            Debug.Log("【KcpMgr】客户端" + targetConv + "已断开");
                            Task.Run(async () => await DisconClientAsync(targetConv));
                        }
                    }
                }
            }
            else
            {
                // 客户端模式：固定发往服务器
                _socket.SendTo(sendData, avalidLength, SocketFlags.None, _serverEndPoint);
            }

            buffer.Dispose();
        }
        catch (Exception e)
        {
            Debug.Log("【KcpMgr】Output发送失败：" + e.Message);
        }
        finally
        {
            _sendBufferPool.Return(sendData);
        }
    }

    #endregion

    #region ==================== 三个核心后台循环 ====================

    /// <summary>
    /// [线程1] UDP 接收循环
    ///
    /// Socket.ReceiveFrom → 握手检测/查找KCP实例 → KCP.Input → KCP.Recv → recvQueue
    ///
    /// 握手处理（仅服务器模式）：
    ///   收到 conv=0 + "KCPC" 魔数 → 分配新 conv → 返回 "KCPA" + 分配的conv
    ///   → 创建 KCP 实例，客户端随后用分配的 conv 进行正常通信
    ///   此机制彻底消除了"服务器重分配 conv 后客户端不知情"的问题
    /// </summary>
    private async Task UdpRecvLoop()
    {
        while (_isRunning && _socket != null)
        {
            try
            {
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                // 在 Task.Run 中阻塞接收，避免卡住 async 状态机
                int udpLen = await Task.Run(() => _socket.ReceiveFrom(_recvBuffer, ref remoteEndPoint));

                if (udpLen > 0)
                {
                    // ── 第1步：检查是否握手请求（conv=0 + "KCPC" 魔数）──
                    //
                    // 这是 conv 冲突解决方案的核心：
                    //   客户端首次连接时不带 conv，发送 conv=0 + "KCPC" 的原始 UDP 包。
                    //   服务端识别后分配一个全局唯一的 conv，通过 "KCPA" 应答包告知客户端。
                    //   此后双方都用分配的 conv 创建 KCP 实例，conv 不会变更。

                    if (_isServerMode && IsHandshakeRequest(_recvBuffer))
                    {
                        // Interlocked.Increment 保证多线程安全地分配唯一 conv
                        uint newConv = (uint)Interlocked.Increment(ref _nextConvID);

                        // 通过原始 UDP 返回应答（不经过 KCP，因为 KCP 实例尚未创建）
                        byte[] ackPacket = BuildHandshakeAck(newConv);
                        await Task.Run(() => _socket.SendTo(ackPacket, remoteEndPoint));
                        Debug.Log($"【KcpMgr】握手：分配 Conv={newConv} → {GetEndPointKey(remoteEndPoint)}");

                        // 注册 endpoint ↔ conv 映射 + 创建 KCP 实例 + 触发 OnClientConnected
                        CreateKcpInstance(newConv, remoteEndPoint);
                        continue; // 握手包不进入 KCP 处理流程
                    }

                    // ── 第2步：查找已有 KCP 实例（非握手包）──

                    // 优先用 endpoint 反查 conv（已知客户端走这条路，O(1)）
                    uint conv = GetConvByEndPoint(remoteEndPoint);

                    if (conv == 0 && _isServerMode)
                    {
                        // endpoint 未知且不是握手包 → 可能是未走握手流程的旧版客户端
                        // 尝试从 KCP 包头（前4字节小端序）读取 conv 作为兜底
                        if (udpLen >= 4)
                            conv = (uint)(_recvBuffer[0]
                                | (_recvBuffer[1] << 8)
                                | (_recvBuffer[2] << 16)
                                | (_recvBuffer[3] << 24));

                        // 兜底失败则分配新 ID
                        if (conv == 0)
                            conv = (uint)Interlocked.Increment(ref _nextConvID);

                        CreateKcpInstance(conv, remoteEndPoint);
                    }
                    else if (conv == 0 && !_isServerMode)
                    {
                        // 客户端模式：未找到 conv 映射 → 丢弃该包
                        // 正常情况下客户端已在握手阶段建立了映射，不应走到这里
                        continue;
                    }

                    // ── 第3步：喂给 KCP → 取出完整业务消息 ──

                    SimpleSegManager.Kcp kcp = GetKcp(conv);
                    if (kcp != null)
                    {
                        // 将原始 UDP 数据喂入 KCP（内部分片重组）
                        kcp.Input(_recvBuffer.AsSpan(0, udpLen));

                        // 循环取出所有已重组完成的完整消息
                        while (true)
                        {
                            int msgLen = kcp.Recv(_kcpBuffer);
                            if (msgLen <= 0) break;

                            byte[] msgData = new byte[msgLen];
                            Array.Copy(_kcpBuffer, msgData, msgLen);
                            _recvQuene.Enqueue((conv, msgData));
                        }
                    }
                }
            }
            catch (SocketException se)
            {
                // 10004=WSAEINTR(socket关闭中断) 10038=WSAENOTSOCK(socket已关闭)
                // 这些都是 StopAsync 关闭 Socket 时的正常现象，不是错误
                if (_isRunning && se.ErrorCode != 10004 && se.ErrorCode != 10038)
                    Debug.Log("【KcpMgr】UDP接收异常：" + se.ErrorCode + " " + se.Message);
            }
            catch (Exception e)
            {
                if (_isRunning)
                    Debug.Log("【KcpMgr】UDP接收异常：" + e.Message);
            }

            // 有数据到达时通知 RecvAsync 等待者
            if (!_recvQuene.IsEmpty)
            {
                try { _recvSignal.Release(); }
                catch (SemaphoreFullException) { /* 已有等待者被唤醒 */ }
            }
        }
    }

    /// <summary>
    /// [线程2] KCP 发送循环
    ///
    /// sendChannel.ReadAsync → KCP.Send → KCP.Update(立即刷新) → Output → Socket
    ///
    /// Send 后立刻调用 Update 实现"即发即出"，降低首包延迟
    /// （KcpUpdateLoop 也会每 10ms 调用 Update 做兜底刷新）
    /// </summary>
    private async Task KcpSendLoop()
    {
        var reader = _sendChannel.Reader;

        while (_isRunning)
        {
            try
            {
                // 无数据时挂起等待（不占 CPU）
                var result = await reader.ReadAsync(_cts.Token);
                var (conv, data) = result;

                SimpleSegManager.Kcp kcp = GetKcp(conv);
                if (kcp != null)
                {
                    kcp.Send(data);
                    // 立即刷新，让数据尽快通过 Output 回调发出
                    kcp.Update(DateTime.UtcNow);
                }
                else
                {
                    Debug.Log("【KcpMgr】KCP实例不存在 Conv：" + conv);
                }
            }
            catch (OperationCanceledException)
            {
                break; // 服务停止，正常退出
            }
            catch (Exception e)
            {
                Debug.Log("【KcpMgr】发送异常：" + e.Message);
            }
        }
    }

    /// <summary>
    /// [线程3] KCP 状态更新循环（每 10ms）
    ///
    /// 遍历所有 KCP 实例调用 Update()，驱动内部状态机：
    ///   - 超时重传：包超过 RTT 未确认则重发
    ///   - 快速重传：后续包已确认但某包缺失达阈值，立即重发
    ///   - 拥塞控制：根据网络状况调整发送速率
    ///   - ACK 发送：累积确认信息
    ///   - 刷新发送队列：有空闲窗口时发送排队中的包
    /// </summary>
    private async Task KcpUpdateLoop()
    {
        const int UpdateIntervalMs = 10;

        while (_isRunning)
        {
            try
            {
                await Task.Delay(UpdateIntervalMs, _cts.Token);

                foreach (var kcp in _kcpDic.Values)
                {
                    kcp.Update(DateTime.UtcNow);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.Log("【KcpMgr】KCP更新异常：" + e.Message);
            }
        }
    }

    #endregion

    #region ==================== 对外 API ====================

    /// <summary>
    /// 异步发送字节数组
    ///
    /// 流程：SendAsync → sendChannel → KcpSendLoop → KCP.Send → Output → Socket
    /// 线程安全，可从业务线程调用
    /// </summary>
    /// <param name="conv">目标会话ID</param>
    /// <param name="data">业务数据</param>
    public async Task SendAsync(uint conv, byte[] data)
    {
        if (!_isRunning)
        {
            Debug.Log("【KcpMgr】服务未运行");
            return;
        }

        await _sendChannel.Writer.WriteAsync((conv, data));
    }

    /// <summary>
    /// 异步发送字符串消息（UTF-8 编码后调用 SendAsync）
    /// </summary>
    public async Task SendMsgAsync(uint conv, string msg)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(msg);
        await SendAsync(conv, bytes);
    }

    /// <summary>
    /// 非阻塞接收一条业务数据（字节数组版本）
    /// </summary>
    public bool TryRecv(out uint conv, out byte[] data)
    {
        if (_recvQuene.TryDequeue(out var item))
        {
            conv = item.conv;
            data = item.data;
            return true;
        }
        conv = 0;
        data = null;
        return false;
    }

    /// <summary>
    /// 非阻塞接收一条字符串消息（UTF-8 解码版本）
    /// </summary>
    public bool TryRecvMsg(out uint conv, out string msg)
    {
        if (_recvQuene.TryDequeue(out var item))
        {
            conv = item.conv;
            msg = Encoding.UTF8.GetString(item.data);
            return true;
        }
        conv = 0;
        msg = null;
        return false;
    }

    /// <summary>
    /// 异步等待接收一条数据（信号量唤醒，无忙等）
    /// 服务停止时返回 (0, null)
    /// </summary>
    public async Task<(uint conv, byte[] data)> RecvAsync(CancellationToken token = default)
    {
        var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, token);

        while (_isRunning)
        {
            if (_recvQuene.TryDequeue(out var item))
                return item;

            try
            {
                await _recvSignal.WaitAsync(combinedCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        return (0, null);
    }

    /// <summary>
    /// 获取所有已连接客户端的会话ID列表（仅服务器模式）
    /// </summary>
    public uint[] GetConnectClients()
    {
        if (!_isServerMode) return new uint[0];

        uint[] clients = new uint[_kcpDic.Keys.Count];
        _kcpDic.Keys.CopyTo(clients, 0);
        return clients;
    }

    /// <summary>
    /// ★ 向所有客户端广播数据（直接迭代 Keys + TryWrite，零分配）
    /// </summary>
    public void BroadcastToAll(byte[] data)
    {
        if (!_isServerMode) return;

        foreach (uint conv in _kcpDic.Keys)
        {
            if (conv != 0)
                _sendChannel.Writer.TryWrite((conv, data));
        }
    }

    /// <summary>
    /// 断开指定客户端（仅服务器模式）
    ///
    /// 操作：销毁KCP实例 → 清除双向映射 → 触发 OnClientDisconnected
    /// </summary>
    public async Task DisconClientAsync(uint conv)
    {
        if (_kcpDic.TryRemove(conv, out SimpleSegManager.Kcp kcp))
        {
            kcp?.Dispose();

            if (_convToEndPointDic.TryRemove(conv, out var endPoint))
            {
                string key = GetEndPointKey(endPoint);
                _endPointToConvDic.TryRemove(key, out _);
            }

            OnClientDisconnected?.Invoke(conv, "主动断开");
            Debug.Log("【KcpMgr】客户端断开连接 Conv：" + conv);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 获取 KCP 实例运行状态（调试用，有反射开销，勿高频调用）
    /// </summary>
    /// <returns>(conv, state, rtt毫秒, 发送队列长度, 接收队列长度)</returns>
    public (uint conv, int state, int rtt, int waitQueueSize, int recvQueueSize) GetKcpStatus(uint conv)
    {
        SimpleSegManager.Kcp kcp = GetKcp(conv);
        if (kcp != null)
        {
            var state = GetPrivateField<int>(kcp, "state");
            var rxSrtt = GetPrivateField<int>(kcp, "rxSrtt");
            var sndQueue = GetPrivateField<Queue>(kcp, "sndQueue");
            var rcvQueue = GetPrivateField<Queue>(kcp, "rcvQueue");

            return (
                conv: conv,
                state: state,
                rtt: rxSrtt,
                waitQueueSize: sndQueue?.Count ?? 0,
                recvQueueSize: rcvQueue?.Count ?? 0
            );
        }

        return (conv, -1, 0, 0, 0);
    }

    /// <summary>反射工具：读取对象私有字段值（仅 GetKcpStatus 使用）</summary>
    private T GetPrivateField<T>(object obj, string fieldName)
    {
        var type = obj.GetType();
        var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return (T)field.GetValue(obj);
    }

    #endregion

    #region ==================== Unity 生命周期函数 ====================

    /// <summary>
    /// 消费 recvQuene 并触发回调（事件模式）
    ///
    /// ★ 两种消费模式二选一 ★
    /// 模式A：调此方法 → OnRecvData/OnRecvMsg 事件回调
    /// 模式B：调 TryRecv() → 业务主动拉取（推荐，不要同时调此方法）
    /// </summary>
    public void Update()
    {
        while (_recvQuene.TryDequeue(out var item))
        {
            OnRecvData?.Invoke(item.conv, item.data);

            string msg = Encoding.UTF8.GetString(item.data);
            OnRecvMsg?.Invoke(item.conv, msg);
        }
    }

    /// <summary>销毁资源（Unity OnDestroy 中调用）</summary>
    public async void Dispose()
    {
        await StopAsync();
    }

    #endregion
}
// Done