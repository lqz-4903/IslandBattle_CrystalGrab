using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class LuaCopyEditor : Editor
{
    [MenuItem("XLua/自动生成txt后缀的Lua")]
    public static void CopyLuaToTxt()
    {
        string luaRoot = Path.Combine(Application.dataPath, "Lua");
        string txtRoot = Path.Combine(Application.dataPath, "LuaTxt");

        if (!Directory.Exists(luaRoot))
            return;

        // 确保目标目录存在
        if (!Directory.Exists(txtRoot))
            Directory.CreateDirectory(txtRoot);

        // 清除目标目录中所有旧的.txt文件（保留.meta）
        ClearTxtFiles(txtRoot);

        // 递归拷贝所有lua文件，保留目录结构
        List<string> newFileNames = new List<string>();
        string[] luaFiles = Directory.GetFiles(luaRoot, "*.lua", SearchOption.AllDirectories);

        for (int i = 0; i < luaFiles.Length; i++)
        {
            string srcPath = luaFiles[i];
            // 计算相对路径：Lua\Libs\PlayerData.lua → Libs\PlayerData.lua
            string relativePath = srcPath.Substring(luaRoot.Length + 1);
            // 目标路径：LuaTxt\Libs\PlayerData.lua.txt
            string dstPath = Path.Combine(txtRoot, relativePath + ".txt");

            // 确保子目录存在
            string dstDir = Path.GetDirectoryName(dstPath);
            if (!Directory.Exists(dstDir))
                Directory.CreateDirectory(dstDir);

            File.Copy(srcPath, dstPath, true);
            newFileNames.Add(dstPath);
        }

        AssetDatabase.Refresh();

        // 刷新后设置AB包名
        for (int i = 0; i < newFileNames.Count; i++)
        {
            string assetPath = newFileNames[i].Substring(newFileNames[i].IndexOf("Assets"));
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer != null)
                importer.assetBundleName = "lua";
        }
    }

    /// <summary>
    /// 递归清除目录中所有.txt文件，保留.meta
    /// </summary>
    private static void ClearTxtFiles(string dir)
    {
        // 删除当前目录的.txt文件
        string[] txtFiles = Directory.GetFiles(dir, "*.txt");
        for (int i = 0; i < txtFiles.Length; i++)
        {
            File.Delete(txtFiles[i]);
            // 同时删除对应的.meta
            string meta = txtFiles[i] + ".meta";
            if (File.Exists(meta))
                File.Delete(meta);
        }

        // 递归子目录
        string[] subDirs = Directory.GetDirectories(dir);
        for (int i = 0; i < subDirs.Length; i++)
        {
            ClearTxtFiles(subDirs[i]);
            // 如果子目录空了（只剩.meta），删掉目录和.meta
            if (Directory.GetFiles(subDirs[i]).Length == 0 && Directory.GetDirectories(subDirs[i]).Length == 0)
            {
                string meta = subDirs[i] + ".meta";
                if (File.Exists(meta))
                    File.Delete(meta);
                Directory.Delete(subDirs[i], true);
            }
        }
    }
}
