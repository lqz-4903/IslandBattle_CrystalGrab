using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// Protobuf 序列化/反序列化工具类
/// ★ 缓存反射结果，避免每次反序列化都做 typeof().GetProperty().GetMethod().Invoke()
///   将 ParseFrom 方法缓存为强类型委托，彻底消除反射开销
/// </summary>
public static class SerAndDeserPBTool
{
    // 缓存每个类型对应的 ParseFrom 委托，首次反射后直接调用
    private static readonly Dictionary<Type, Func<byte[], IMessage>> _parserCache = new();
    private static readonly object _lock = new();

    /// <summary>
    /// 序列化 Protobuf 消息为字节数组
    /// </summary>
    public static byte[] GetProtoBytes(IMessage msg)
    {
        return msg.ToByteArray();
    }

    /// <summary>
    /// 反序列化字节数组为 Protobuf 消息（首次反射后走缓存委托，零反射开销）
    /// </summary>
    public static T GetProtoMsg<T>(byte[] bytes) where T : class, IMessage
    {
        Type type = typeof(T);

        Func<byte[], IMessage> parseFunc;
        if (!_parserCache.TryGetValue(type, out parseFunc))
        {
            lock (_lock)
            {
                if (!_parserCache.TryGetValue(type, out parseFunc))
                {
                    // 首次：反射获取静态 Parser 属性 → 获取 ParseFrom 方法 → 创建委托 → 缓存
                    PropertyInfo pInfo = type.GetProperty("Parser");
                    object parserObj = pInfo.GetValue(null, null);
                    Type parserType = parserObj.GetType();
                    MethodInfo mInfo = parserType.GetMethod("ParseFrom", new Type[] { typeof(byte[]) });
                    // 创建强类型委托，后续调用零反射开销
                    parseFunc = (Func<byte[], IMessage>)Delegate.CreateDelegate(
                        typeof(Func<byte[], IMessage>), parserObj, mInfo);
                    _parserCache[type] = parseFunc;
                }
            }
        }

        return parseFunc(bytes) as T;
    }
}
