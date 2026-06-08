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
        // 1. 跳过 xlua 系统脚本，不打印日志
        if (filePath.StartsWith("xlua."))
            return null;

        // 2. 把 . 换成路径符号
        string realPath = filePath.Replace(".", "/");

        // 3. 只定义【根目录】，不要把文件名拼进去！
        string[] searchDirs =
        {
        Path.Combine(Application.dataPath, "Lua"),
        Path.Combine(Application.dataPath, "Lua/Libs"),
        Path.Combine(Application.dataPath, "Lua/UI"),
    };

        // 4. 遍历所有目录去找 realPath + .lua
        foreach (string dir in searchDirs)
        {
            string fullPath = Path.Combine(dir, realPath + ".lua");

            // 找到就直接返回，不打印任何日志
            if (File.Exists(fullPath))
            {
                return File.ReadAllBytes(fullPath);
            }
        }

        // 5. 文件系统找不到，返回null让CustomABLoader兜底
        // 不打印日志，避免AB模式下刷屏
        return null;
    }

    //Lua脚本会放在AB包
    //最终我们其实 会去AB包中加载 Lua文件
    //AB包中如果要加载文本 后缀还有一定的限制 .lua 不能被识别
    //打包时 要把lua文件后缀改为txt
    public byte[] CustomABLoader(ref string filePath)
    {
        //把.换成/，和CustomLoader保持一致
        string realPath = filePath.Replace(".", "/");

        // 取文件名部分（去掉目录前缀），因为AB包中资源路径带Assets/LuaTxt/前缀
        // 直接用文件名加载，Lua文件名都是唯一的
        string fileName = realPath;
        int lastSlash = realPath.LastIndexOf('/');
        if (lastSlash >= 0)
            fileName = realPath.Substring(lastSlash + 1);

        //通过之前写的AB包管理器 加载的lua脚本资源
        TextAsset lua = ABMgr.Instance.LoadRes<TextAsset>("lua", fileName + ".lua.txt");
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
            return;
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
