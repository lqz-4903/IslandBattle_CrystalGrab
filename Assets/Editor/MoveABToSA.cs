using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public class MoveABToSA
{
    [MenuItem("AB包工具/移动选中资源到StreamingAssets中", false, 4)]
    private static void MoveABToStreamingAssets()
    {
        // 通过编辑器Selection类中的方法 获取到Project窗口中选中的资源
        Object[] selectedAsset = Selection.GetFiltered(typeof(Object), SelectionMode.DeepAssets);
        // 如果一个资源都没有选中 就没有必要处理后面的逻辑 
        if (selectedAsset.Length == 0)
            return;

        // 用于拼接本地默认AB包资源信息的字符串
        string abCompareInfo = "";

        // 遍历选中的资源对象
        foreach (Object asset in selectedAsset)
        {
            // 通过AssetDataBase类 获取 资源的路径
            string assetPath = AssetDatabase.GetAssetPath(asset);
            // 截取路径当中的文件名 用于作为 StreamingAssets中的文件名
            string fileName = assetPath.Substring(assetPath.LastIndexOf("/"));

            // 判断是否有.符号 如果有 证明有后缀 不处理
            if (fileName.IndexOf('.') != -1)
                continue;
            
            // 利用AssetDataBase中的API 将选中文件 复制到目标路径
            AssetDatabase.CopyAsset(assetPath, "Assets/StreamingAssets" + fileName);

            // 获取拷贝到StreamingAssets文件夹中的文件的全部信息
            FileInfo fileInfo = new FileInfo(Application.streamingAssetsPath + fileName);
            // 拼接AB包信息到字符串中
            abCompareInfo += fileInfo.Name + " " + fileInfo.Length + " " + GetMD5(fileInfo.FullName);
            //用一个符号隔开多个AB包信息
            abCompareInfo += "|";

        }
        // 因为循环完毕后 最后会加一个 | ，所以要截取
        abCompareInfo = abCompareInfo.Substring(0, abCompareInfo.Length - 1);
        //存储拼接好的 AB包资源信息
        File.WriteAllText(Application.streamingAssetsPath + "/ABCompareInfo.txt", abCompareInfo);

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
