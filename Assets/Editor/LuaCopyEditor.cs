using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class LuaCopyEditor : Editor
{
    [MenuItem("XLua/自动生成txt后缀的Lua")]
    public static void CopyLuaToTxt()
    {
        // 首先要找到 我们的所有Lua文件
        string path = Path.Combine(Application.dataPath, "Lua");
        string libsPath = Path.Combine(path, "Libs");
        //判断路径是否存在
        if (!Directory.Exists(path))
            return;
        
        if (!Directory.Exists(libsPath))
            return;

        //得到每一个lua文件的路径 才能进行迁移拷贝
        string[] strs = Directory.GetFiles(path, "*.lua");
        string[] libsStrs = Directory.GetFiles(libsPath, "*.lua");

        // 然后把Lua文件拷贝到一个新的文件夹中
        // 首先定一个新路径
        string newPath = Path.Combine(Application.dataPath, "LuaTxt");

        //为了避免一些被删除的lua文件 不再使用 我们应该先清空目标路径  
        //判断新路径文件夹是否存在
        if (!Directory.Exists(newPath))
            Directory.CreateDirectory(newPath);
        else
        {
            //得到该路径中 所有后缀txt的文件 把他们都删了
            string[] oldFileStrs = Directory.GetFiles(newPath, "*.txt");
            for (int i = 0; i < oldFileStrs.Length; i++)
            {
                File.Delete(oldFileStrs[i]);
            }
        }
        
        List<string> newFileNames = new List<string>();
        string fileName;
        for (int i = 0; i < strs.Length; i++)
        {
            //得到新的文件路径 用于拷贝
            fileName = newPath + strs[i].Substring(strs[i].LastIndexOf("\\")) + ".txt";
            newFileNames.Add(fileName);
            File.Copy(strs[i], fileName);

        } 

        for (int i = 0; i < libsStrs.Length; i++)
        {
            //得到新的文件路径 用于拷贝
            fileName = newPath + libsStrs[i].Substring(libsStrs[i].LastIndexOf("\\")) + ".txt";
            newFileNames.Add(fileName);
            File.Copy(libsStrs[i], fileName);
        }

        AssetDatabase.Refresh();

        // 刷新后再来改 指定包 如果不刷新 第一次改没用
        for (int i = 0; i < newFileNames.Count; i++)
        {
            // Unity API
            AssetImporter importer = AssetImporter.GetAtPath(newFileNames[i].Substring(newFileNames[i].IndexOf("Assets")));
            if (importer != null)
                importer.assetBundleName = "lua";
        }
    }
}
