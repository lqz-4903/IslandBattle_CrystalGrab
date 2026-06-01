using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using UnityEngine;


public class KcpMgr 
{
    private static KcpMgr instance = new KcpMgr();
    public static KcpMgr Instance => instance;

    // KCP实例字典：会话ID -> KCP实例
    // 服务器模式下，每个客户端对应一个独立的KCP实例（不同的会话ID）
    // 客户端模式下，只有一个KCP实例
    private ConcurrentDictionary<uint, SimpleSegManager.Kcp> kcpDic = new ConcurrentDictionary<uint, SimpleSegManager.Kcp>();

    // 客户端连接映射：端点字符串(如"127.0.0.1:8080") -> 会话ID
    // 用于快速根据UDP数据包的来源地址找到对应的KCP实例
    private ConcurrentDictionary<string, uint> endPointToConvDic = new ConcurrentDictionary<string, uint>();

    // 会话ID到端点的映射
    // 用于在发送数据时，知道目标IP和端口
    private ConcurrentDictionary<uint, EndPoint> convToEndPointDic = new ConcurrentDictionary<uint, EndPoint>();

    // 接收队列：会话ID + 业务数据（已解包、已重组完成的原始消息）
    // 主线程通过Update()读取这个队列，触发回调
    private ConcurrentQueue<(uint conv, byte[] data)> recvQuene = new ConcurrentQueue<(uint conv, byte[] data)>();

    // 发送通道：异步发送队列，使用Channel实现生产者-消费者模式
    // 业务线程调用SendAsync -> 写入通道 -> 发送循环读取 -> 实际发送
    private Channel<(uint conv, byte[] data)> sendChannel = Channel.CreateUnbounded<(uint conv, byte[] data)>();

    // UDP Socket
    private Socket socket;

    // 缓存区复用
    private byte[] recvBuffer = new byte[65535]; // UDP原始数据缓存区
    private byte[] kcpBuffer = new byte[65535];  // KCP解包后业务数据缓存区

    // 服务器/客户端配置
    private bool isRunning = false;
    private bool isServerMode = true;
    private EndPoint serverEndPoint;            //客户端模式下的服务器地址

    // 下一个可用的会话ID（服务器模式下使用）
    private uint nextConvID = 1000;             // 从1000开始，避免与客户端默认的1冲突

    // 线程控制
    private CancellationTokenSource cts;        // 用于优雅停止三个后台线程
    private Task udpRecvTask;                   // UDP接收线程
    private Task kcpSendTask;                   // KCP发送处理线程
    private Task kcpUpdateTask;                 // KCP状态更新线程（处理超时重传，拥塞控制）

    // 回调事件
    public Action<uint, byte[]> onRecvData;            // 接收原始字符串
    public Action<uint, string> onRecvMsg;             // 接收字符串消息
    public Action<uint> onClientConnected;             // 新客户连接时触发
    public Action<uint, string> onClentDisConnected;   // 客户端断开时触发

    // KC配置参数 默认值
    private int noDelay = 1;
    private int interval = 10;
    private int resend = 2;
    private int nc = 1;
    private int sendWin = 128;
    private int recvWin = 128;
    private int mtu = 1400;

    // 内部私有声明 防止外部创建
    private KcpMgr() { }

    #region 配置方法 延迟模式 窗口大小 MTU
    /// <summary>
    /// 异步设置 KCP延迟模式 可动态调整，不调用的话 就使用默认值,
    /// </summary>
    /// <param name="noDelay"></param>
    /// <param name="interval"></param>
    /// <param name="resend"></param>
    /// <param name="nc"></param>
    /// <returns></returns>
    public async Task SetNoDelayAsync(int noDelay, int interval, int resend, int nc)
    {
        this.noDelay = noDelay;
        this.interval = interval;
        this.resend = resend;
        this.nc = nc;

        // 应用到所有已存在的KCP实例中（包括已连接的客户端）
        foreach (var kcp in kcpDic.Values)
        {
            kcp.NoDelay(noDelay, interval, resend, nc);
        }

        await Task.CompletedTask; // 异步方法但实际同步执行 返回已完成的Task
    }

    /// <summary>
    /// 设置窗口大小
    /// </summary>
    /// <param name="sendWin"></param>
    /// <param name="recvWin"></param>
    /// <returns></returns>
    public async Task SetWinSizeAsync(int sendWin, int recvWin)
    {
        this.sendWin = sendWin;
        this.recvWin = recvWin;

        foreach (var kcp in kcpDic.Values)
        {
            kcp.WndSize(sendWin, recvWin);
        }

        await Task.CompletedTask;
    }

    public async Task SetMTUAsync(int mtu)
    {
        this.mtu = mtu;

        foreach (var kcp in kcpDic.Values)
        {
            kcp.SetMtu(mtu);    
        }

        await Task.CompletedTask;
    }

    #endregion

    #region 启动与停止

    /// <summary>
    /// 开启服务器
    /// </summary>
    /// <param name="port"></param>
    /// <returns></returns>
    public async Task StartAsServerAsync(int port = 8888)
    {
        if (isRunning)
        {
            Debug.Log("【KCPMgr】已经在运行");
        }
        // 创建UDP Socket并绑定到指定端口，监听所有IP地址
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, port));   // 这的IPAddress.Any 用于监听所有网卡，谁都能连

        // 创建取消令牌 用于停止线程
        cts = new CancellationTokenSource();
        isRunning = true;

        // 启动三个后台线程
        //udpRecvTask = Task.Run();
        //kcpSendTask = Task.Run();
        //kcpUpdateTask = Task.Run();
    }

    public async Task StartAsClientAsync(string serverIP, int serverPort, uint convID = 0, int localPort = 0)
    {
        isServerMode = false;

        // 创建UDP Socket
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        if (localPort > 0) socket.Bind(new IPEndPoint(IPAddress.Any, localPort));

        // 记录服务器地址
        serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);

        // 创建客户端唯一的KCP实例（会话默认为1）
        uint conv = convID == 0 ? 1 : convID;
        


        // 启动三个后台线程
        //udpRecvTask = Task.Run();
        //kcpSendTask = Task.Run();
        //kcpUpdateTask = Task.Run();
    }


    #endregion











    // 初始化创建KCP



    // 设置延迟模式

    // 设置窗口大小

    // 设置MTU大小

    // 发送

    // 接收

    // 销毁

}
