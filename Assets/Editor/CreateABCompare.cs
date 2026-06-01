using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public class CreateABCompare
{
    [MenuItem("AB包工具/创建对比文件", false, 3)]
    private static void CreateABCompareFile()
    {
        // 获取文件夹信息
        DirectoryInfo directory = Directory.CreateDirectory(Application.dataPath + "/ArtRes/AB/PC/");
        // 获取该目录下的所有文件信息
        FileInfo[] fileInfos = directory.GetFiles();

        // 用于存储信息的 字符串
        string abCompareInfo = "";

        foreach (FileInfo info in fileInfos)
        {
            // 没有后缀的 才是AB包 我们只想要AB包的信息
            if (info.Extension == "")
            {
                // 拼接一个AB包的信息
                abCompareInfo += info.Name + " " + info.Length + " " + GetMD5(info.FullName);
                //用一个分隔符分开不同文件之间的信息
                abCompareInfo += "|";
            }
        }
        // 因为循环完毕后 最后会加一个 | ，所以要截取
        abCompareInfo = abCompareInfo.Substring(0, abCompareInfo.Length - 1);
        //存储拼接好的 AB包资源信息
        File.WriteAllText(Application.dataPath + "/ArtRes/AB/PC/ABCompareInfo.txt", abCompareInfo);
        Debug.Log("AB包对比资源文件生成成功");
        AssetDatabase.Refresh();
    }

    private static string GetMD5(string filePath)
    {
        // 将文件以流的形式打开
        using (FileStream file = new FileStream(filePath, FileMode.Open))
        {
            // 声明一个MD5对象 用于生成MD5码
            MD5 md5 = new MD5CryptoServiceProvider();
            // 利用API 得到数据的MD5码 16个字节 数组
            byte[] md5Info = md5.ComputeHash(file);

            // 关闭文件流 
            file.Close();

            // 把16个字节转换为 16进制 拼接成字符串 为了减少md5码额长度
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < md5Info.Length; i++)
                sb.Append(md5Info[i].ToString("x2"));

            return sb.ToString();
        }
    }
}
