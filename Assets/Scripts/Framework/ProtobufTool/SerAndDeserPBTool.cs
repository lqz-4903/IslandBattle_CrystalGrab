using Google.Protobuf;
using System;
using System.Reflection;

/// <summary>
/// 这是一个工具类 
/// 用于快速地对Protobuf生成的内容进行序列化和反序列化
/// </summary>

public static class SerAndDeserPBTool
{
    /// <summary>
    /// 序列化
    /// Protobuf生成的对象
    /// 官方为 .WriteTo
    /// </summary>
    /// <param name="msg"></param>
    /// <returns></returns>
    public static byte[] GetProtoBytes( IMessage msg )
    {
        //拓展方法、里氏替换、接口 这些知识点 都在 C#相关的内容当中

        //基础写法 基于上节课学习的知识点
        //byte[] bytes = null;
        //using (MemoryStream ms = new MemoryStream())
        //{
        //    msg.WriteTo(ms);
        //    bytes = ms.ToArray();
        //}
        //return bytes;

        //通过该拓展方法 就可以直接获取对应对象的 字节数组了
        return msg.ToByteArray();
    }

    /// <summary>
    /// 反序列化
    /// 字节数组为Protobuf相关的对象
    /// 官方为 .Parser.ParserFrom(data);
    /// </summary>
    /// <typeparam name="T">想要获取的消息类型</typeparam>
    /// <param name="bytes">对应的字节数组 用于反序列化</param>
    /// <returns></returns>
    public static T GetProtoMsg<T>(byte[] bytes) where T:class, IMessage
    {
        //泛型 C#进阶
        //反射 C#进阶
        //得到对应消息的类型 通过反射得到内部的静态成员 然后得到其中的 对应方法
        //进行反序列化
        Type type = typeof(T);
        //通过反射 得到对应的 静态成员属性对象
        PropertyInfo pInfo = type.GetProperty("Parser");
        object parserObj = pInfo.GetValue(null, null);
        //已经得到了对象 那么可以得到该对象中的 对应方法 
        Type parserType = parserObj.GetType();
        //这是指定得到某一个重载函数
        MethodInfo mInfo = parserType.GetMethod("ParseFrom", new Type[] { typeof(byte[]) });
        //调用对应的方法 反序列化为指定的对象
        object msg = mInfo.Invoke(parserObj, new object[] { bytes });
        return msg as T;
    }
}
