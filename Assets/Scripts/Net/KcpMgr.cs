using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using System.Net.Sockets.Kcp;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UI;


public class KcpMgr : IKcpCallback
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
    private byte[] sendData;                     // KCP封装好发送的业务数据缓存区

    // 服务器/客户端配置
    private bool isRunning = false;
    private bool isServerMode = true;
    private EndPoint serverEndPoint;            //客户端模式下的服务器地址

    // 下一个可用的会话ID（服务器模式下使用）
    private int nextConvID = 1000;             // 从1000开始，避免与客户端默认的1冲突

    // 线程控制
    private CancellationTokenSource cts;        // 用于优雅停止三个后台线程
    private Task udpRecvTask;                   // UDP接收线程
    private Task kcpSendTask;                   // KCP发送处理线程
    private Task kcpUpdateTask;                 // KCP状态更新线程（处理超时重传，拥塞控制）

    // 回调事件
    public Action<uint, byte[]> onRecvData;            // 接收原始字符串
    public Action<uint, string> onRecvMsg;             // 接收字符串消息
    public Action<uint> onClientConnected;             // 新客户连接时触发
    public Action<uint, string> onClientDisConnected;   // 客户端断开时触发

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
    /// 启动服务器
    /// </summary>
    /// <param name="port"></param>
    /// <returns></returns>
    public async Task StartAsServerAsync(int port = 8888)
    {
        if (isRunning)
        {
            Debug.Log("【KCPMgr】已经在运行");
        }

        isServerMode = true;

        try
        {
            // 创建UDP Socket并绑定到指定端口，监听所有IP地址
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));   // 这的IPAddress.Any 用于监听所有网卡，谁都能连

            // 创建取消令牌 用于停止线程
            cts = new CancellationTokenSource();
            isRunning = true;

            // 启动三个后台线程
            //udpRecvTask = Task.Run();         // 启动UDP接收循环
            //kcpSendTask = Task.Run();         // 启动KCP发送循环
            //kcpUpdateTask = Task.Run();       // 启动KCP更新循环

            Debug.Log("【kcpMgr】服务器启动成功，端口：" + port);
            await Task.CompletedTask;
        }
        catch (SocketException se)
        {
            Debug.Log("【KcpMgr】启动服务器失败：" + se.SocketErrorCode + se.Message);
        }       
    }

    /// <summary>
    /// 启动客户端
    /// </summary>
    /// <param name="serverIP"></param>
    /// <param name="serverPort"></param>
    /// <param name="convID"></param>
    /// <param name="localPort"></param>
    /// <returns></returns>
    public async Task StartAsClientAsync(string serverIP, int serverPort, uint convID = 0, int localPort = 0)
    {
        if (isRunning)
        {
            Debug.Log("【KCPMgr】已经在运行");
        }

        isServerMode = false;

        try
        {
            // 创建UDP Socket
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            // 绑定本地接口
            if (localPort > 0) socket.Bind(new IPEndPoint(IPAddress.Any, localPort));

            // 记录服务器地址
            serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);

            // 创建客户端唯一的KCP实例（会话默认为1）
            uint conv = convID == 0 ? 1 : convID;
            CreateKcpInstance(conv, serverEndPoint, true);

            cts = new CancellationTokenSource();
            isRunning = true;


            // 启动三个后台线程
            //udpRecvTask = Task.Run();         // 启动UDP接收循环
            //kcpSendTask = Task.Run();         // 启动KCP发送循环
            //kcpUpdateTask = Task.Run();       // 启动KCP更新循环

            Debug.Log("【kcpMgr】客户端启动成功，连接服务器：" + serverIP + serverPort);
            await Task.CompletedTask;
        }
        catch (SocketException se)
        {
            Debug.Log("【KcpMgr】启动客户端失败：" + se.SocketErrorCode + se.Message);
        }
    }

    /// <summary>
    /// 停止服务
    /// </summary>
    /// <returns></returns>
    public async Task StopAsync()
    {
        if (!isRunning) return;

        isRunning = false;
        cts?.Cancel();

        // 等待所有任务完成
        if (udpRecvTask != null)
            await Task.WhenAny(udpRecvTask, Task.Delay(1000));
        if (kcpSendTask != null)        
            await Task.WhenAny(kcpSendTask, Task.Delay(1000));        
        if (kcpUpdateTask != null)        
            await Task.WhenAny(kcpUpdateTask, Task.Delay(1000));

        // 销毁所有KCP实例
        foreach (var kcp in kcpDic.Values)
        {
            kcp?.Dispose();
        }
        kcpDic.Clear();
        endPointToConvDic.Clear();
        convToEndPointDic.Clear();

        // 关闭socket
        socket?.Close();
        socket = null;

        Debug.Log("【KcpMgr】服务已停止");
    }

    #endregion

    #region KCP实例管理

    /// <summary>
    /// 创建KCP实例
    /// </summary>
    /// <param name="conv"></param>
    /// <param name="remoteEndPoint"></param>
    /// <param name="isClient"></param>
    private void CreateKcpInstance(uint conv, EndPoint remoteEndPoint, bool isClient = false)
    {
        if (kcpDic.ContainsKey(conv))
        {
            Debug.Log("【KcpMgr】Kcp实例已存在 Conv：" + conv);
            return;
        }

        SimpleSegManager.Kcp kcp = new SimpleSegManager.Kcp(conv, this);

        kcp.NoDelay(noDelay, interval, resend, nc);
        kcp.WndSize(sendWin, recvWin);
        kcp.SetMtu(mtu);

        // 存储
        kcpDic[conv] = kcp;

        string endPointKey = GetEndPointKey(remoteEndPoint);
        endPointToConvDic[endPointKey] = conv;
        convToEndPointDic[conv] = remoteEndPoint;

        if (!isClient && isServerMode)
        {
            // 服务器模式下，新连接触发回调
            onClientConnected?.Invoke(conv);
            //Debug.Log("【KcpMgr】新客户端连接 Conv" + conv + "EndPoint" + endPointKey);
        }
    }

    /// <summary>
    /// 获取指定会话的 KCP实例
    /// </summary>
    /// <param name="conv"></param>
    /// <returns></returns>
    private SimpleSegManager.Kcp GetKcp(uint conv)
    {
        kcpDic.TryGetValue(conv, out SimpleSegManager.Kcp kcp);
        return kcp;
    }

    /// <summary>
    /// 根据端点获取会话ID
    /// </summary>
    /// <param name="endPoint"></param>
    /// <returns></returns>
    private uint GetConvByEndPoint(EndPoint endPoint)
    {
        string key = GetEndPointKey(endPoint);
        if (endPointToConvDic.TryGetValue(key, out uint conv))
            return conv;
        return 0;
    }

    /// <summary>
    /// 得到端点键
    /// </summary>
    /// <param name="endPoint"></param>
    /// <returns></returns>
    private string GetEndPointKey(EndPoint endPoint)
    {
        IPEndPoint iPEndPoint = endPoint as IPEndPoint;
        return $"{iPEndPoint.Address}:{iPEndPoint.Port}";
    }
    #endregion

    #region IKcpCallback 实现

    public void Output(IMemoryOwner<byte> buffer, int avalidLength)
    {
        try
        {
            // 获取有效数据
            sendData = buffer.Memory.Span.Slice(0, avalidLength).ToArray();


            // 根据会话ID找到目标端点
            // 注意：Output回调无法直接传递conv，需要从上下文获取
            // 简化处理：遍历查找对应的端点，或者使用队列方式

            // 方案：直接对所有客户端广播（服务器模式）或发送到服务器（客户端模式）
            if (isServerMode)
            {
                // 服务器模式：需要知道是发给哪个客户端，这里简化处理
                // 实际使用中需要更好的映射机制
                foreach (var kcp in convToEndPointDic)
                {
                    socket.SendTo(sendData, kcp.Value);
                }
            }
            else
            {
                socket.SendTo(sendData, serverEndPoint);
            }

            buffer.Dispose();
        }
        catch (Exception e)
        {
            Debug.Log("【KcpMgr】Output发送失败" + e.Message);
        }
    }

    #endregion

    #region 核心循环

    // 原始消息长度
    int udpLen;
    // 解包消息长度
    int msgLen;
    // 解包消息数组
    byte[] msgData;

    /// <summary>
    /// UDP接收循环
    /// </summary>
    /// <returns></returns>
    private async Task UdpRecvLoop()
    {
        while(isRunning && socket != null)
        {
            try
            {
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                udpLen = await Task.Run(() => socket.ReceiveFrom(recvBuffer, ref remoteEndPoint));

                if (udpLen > 0)
                {
                    // 获取或获取KCP实例
                    uint conv = GetConvByEndPoint(remoteEndPoint);

                    if (conv == 0 && isServerMode)
                    {
                        // 新客户端连接 分配新的会话ID
                        conv = (uint)Interlocked.Increment(ref nextConvID);
                        CreateKcpInstance(conv, remoteEndPoint);
                    }
                    else if (conv == 0 && !isServerMode)
                    {
                        // 客户端模式 使用默认会话ID
                        conv = 1;
                        if (!kcpDic.ContainsKey(conv))
                        {
                            CreateKcpInstance(conv, remoteEndPoint, true);
                        }
                    }

                    SimpleSegManager.Kcp kcp = GetKcp(conv);
                    if (kcp != null)
                    {
                        // 喂给KCP
                        kcp.Input(recvBuffer.AsSpan(0, udpLen));

                        // 循环取出完整消息
                        while (true)
                        {
                            msgLen = kcp.Recv(kcpBuffer);
                            if (msgLen <= 0) break;

                            msgData = new byte[msgLen];
                            Array.Copy(kcpBuffer, msgData, msgLen);

                            // 放入接收队列
                            recvQuene.Enqueue((conv, msgData));
                        }
                    }
                }
            }
            catch (SocketException se)
            {
                if (isRunning)
                    Debug.Log("【KcpMgr】UDP接收异常" + se.ErrorCode + se.Message);
            }
            catch (Exception e)
            {
                if (isRunning)
                    Debug.Log("【KcpMgr】UDP接收异常"  + e.Message);
            }
        }
    }

    /// <summary>
    /// KCP发送循环 - 处理发送队列
    /// </summary>
    /// <returns></returns>
    private async Task KcpSendLoop()
    {
        var reader = sendChannel.Reader;

        while (isRunning)
        {
            try
            {
                var result = await reader.ReadAsync(cts.Token);
                var (conv, data) = result;

                SimpleSegManager.Kcp kcp = GetKcp(conv);
                if (kcp != null)
                {
                    kcp.Send(data);
                    //立即触发更新 加速发送
                    kcp.Update(DateTime.UtcNow);
                }
                else
                    Debug.Log("【KcpMgr】KCP实例不存在 Conv" + conv);   
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.Log("【KcpMgr】发送异常" + e.Message);
            }
        }
    }

    /// <summary>
    /// KCP更新循环 - 处理超时重传、窗口更新等
    /// </summary>
    /// <returns></returns>
    private async Task KcpUpdateLoop()
    {
        const int updateIntervalMs = 10;

        while (isRunning)
        {
            try
            {
                await Task.Delay(updateIntervalMs, cts.Token);                

                foreach (var kcp in kcpDic.Values)
                {
                    kcp.Update(DateTimeOffset.UtcNow);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.Log("【KcpMgr】KCP更新异常" + e.Message);
            }
        }
    }

    #endregion

    #region 对外API

    /// <summary>
    /// 发送字节数组
    /// </summary>
    /// <param name="conv"></param>
    /// <param name="data"></param>
    /// <returns></returns>
    public async Task SendAsync(uint conv, byte[] data)
    {
        if (!isRunning)
        {
            Debug.Log("【KcpMgr】服务器未运行");
            return;
        }

        await sendChannel.Writer.WriteAsync((conv, data));
    }

    /// <summary>
    /// 发送字符串消息
    /// </summary>
    /// <param name="conv"></param>
    /// <param name="msg"></param>
    /// <returns></returns>
    public async Task SendMsgAsync(uint conv, string msg)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(msg);
        await SendAsync(conv, bytes);
    }

    /// <summary>
    /// 接收数据（非阻塞）
    /// </summary>
    /// <param name="conv"></param>
    /// <param name="data"></param>
    /// <returns></returns>
    public bool TryRecv(out uint conv, out byte[] data)
    {
        if (recvQuene.TryDequeue(out var item))
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
    /// 接受字符串消息（非阻塞）
    /// </summary>
    /// <param name="conv"></param>
    /// <param name="msg"></param>
    /// <returns></returns>
    public bool TryRecvMsg(out uint conv, out string msg)
    {
        if (recvQuene.TryDequeue(out var item)) 
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
    /// 异步接收数据
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<(uint conv, byte[] data)> recvAsync(CancellationToken token = default)
    {
        var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token);

        while (isRunning)
        {
            if (recvQuene.TryDequeue(out var item))
                return item;
            await Task.Delay(1, combinedCts.Token);
        }

        return (0, null);
    }





    #endregion

}
