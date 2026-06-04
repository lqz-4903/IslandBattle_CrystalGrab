using System.IO;
using UnityEngine;
using XLua;

/// <summary>
/// LuaMgr
/// 提供 lua解析器
/// 保证解析器的唯一性
/// </summary>
public class LuaMgr
{
    private static LuaMgr instance = new LuaMgr();

    public static LuaMgr Instance => instance;

    private LuaMgr() { }

    //执行Lua语言的函数
    //释放垃圾
    //销毁
    //重定向
    private LuaEnv luaEnv;

    /// <summary>
    /// 得到Lua中的_G
    /// </summary>
    public LuaTable Global => luaEnv.Global;


    /// <summary>
    /// 初始化解析器
    /// </summary>
    public void Init()
    {
        //已经初始化了 就返回
        if (luaEnv != null)
            return;
        //初始化
        luaEnv = new LuaEnv();
        //加载Lua脚本 重定向
        luaEnv.AddLoader(CustomLoader);
        luaEnv.AddLoader(CustomABLoader);
    }

    /// <summary>
    /// 重定向函数 自动执行
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    private byte[] CustomLoader(ref string filePath)
    {
        //通过函数中的逻辑 去加载 lua文件
        //传入的参数 是 require执行的lua脚本文件名
        //拼接一个lua文件所在路径
        string path = Path.Combine(Application.dataPath, "Lua", filePath + ".lua");

        //有路径 就去加载文件
        //判断文件是否存在
        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }
        else
        {
            Debug.Log("CustomLoader重定向失败，文件名为" + filePath);
        }
        return null;
    }

    //Lua脚本会放在AB包
    //最终我们其实 会去AB包中加载 Lua文件
    //AB包中如果要加载文本 后缀还有一定的限制 .lua 不能被识别
    //打包时 要把lua文件后缀改为txt
    public byte[] CustomABLoader(ref string filePath)
    {
        ////从AB包中加载文件
        ////加载AB包
        //string path = Path.Combine(Application.streamingAssetsPath, "lua");
        //AssetBundle ab = AssetBundle.LoadFromFile(path);

        ////加载Lua文件 返回
        //TextAsset tx = ab.LoadAsset<TextAsset>(filePath + ".lua");
        ////加载Lua文件 byte数组
        //return tx.bytes;

        //通过之前写的AB包管理器 加载的lua脚本资源
        TextAsset lua = ABMgr.Instance.LoadRes<TextAsset>("lua", filePath + ".lua");
        if (lua != null)
            return lua.bytes;
        else
            Debug.Log("CustomABLoader重定向失败，文件名为" + filePath);

        return null;
    }
    
    /// <summary>
    /// 执行Lua语言
    /// </summary>
    /// <param name="str"></param>
    public void DoString(string str)
    {
        if (luaEnv == null)
        {
            Debug.Log("解析器未初始化");
            return;
        }
        luaEnv.DoString($"require('{str}')");
    }

    /// <summary>
    /// 释放Lua 垃圾
    /// </summary>
    public void Tick()
    {
        if (luaEnv == null)
        {
            Debug.Log("解析器未初始化");
            return;
        }
        luaEnv.Tick();
    }

    /// <summary>
    /// 销毁解析器
    /// </summary>
    public void Dispose()
    {
        if (luaEnv == null)
        {
            Debug.Log("解析器未初始化");
            return;
        }
        luaEnv.Dispose();
        luaEnv = null;
    }
}
