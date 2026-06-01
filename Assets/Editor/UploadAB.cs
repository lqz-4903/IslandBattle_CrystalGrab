using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class UploadAB 
{
    [MenuItem("AB包工具/上传AB包和对比文件", false, 2)]
    private static void UploadAllABFile()
    {
        // 获取文件夹信息
        DirectoryInfo directory = Directory.CreateDirectory(Application.dataPath + "/ArtRes/AB/PC/");
        // 获取该目录下的所有文件信息
        FileInfo[] fileInfos = directory.GetFiles();

        foreach (FileInfo info in fileInfos)
        {
            // 没有后缀的 才是AB包 我们只想要AB包的信息
            // 还有需要获取 资源对比文件 格式是txt
            if (info.Extension == "" || 
                info.Name == "ABCompareInfo.txt")
            {
                // 上传该文件
                FtpUploadFileAsync(info.FullName, info.Name);
            }
        }
    }

    private async static void FtpUploadFileAsync(string filePath, string fileName)
    {
        await Task.Run(() =>
        {
            try
            {
                // 1.创建一个FTP连接 用于上传
                FtpWebRequest req = FtpWebRequest.Create(new Uri("ftp://127.0.0.1/AB/PC/" + fileName)) as FtpWebRequest;
                // 2.设置一个通信凭证 这样才能上传
                NetworkCredential n = new NetworkCredential("Lqz", "123456");
                req.Credentials = n;
                // 3.其他设置
                //   设置代理为null
                req.Proxy = null;
                //   请求完毕后 是否关闭控制连接
                req.KeepAlive = false;
                //   操作命令-上传
                req.Method = WebRequestMethods.Ftp.UploadFile;
                //   指定传输的类型 二进制
                req.UseBinary = true;
                // 4.上传文件
                //   FTP的流对象
                Stream uploadStream = req.GetRequestStream();
                //   读取文件信息 写入该流对象
                using (FileStream file = File.OpenRead(filePath))
                {
                    // 一点一点的上传内容
                    byte[] bytes = new byte[2048];
                    // 返回值 代表读取了多少个字节
                    int contentLength = file.Read(bytes, 0, bytes.Length);

                    //循环上传文件中的数据
                    while (contentLength != 0)
                    {
                        // 写入到上传流中
                        uploadStream.Write(bytes, 0, contentLength);
                        // 写完再度
                        contentLength = file.Read(bytes, 0, bytes.Length);
                    }

                    // 循环完毕后证明上传结束
                    file.Close();
                    uploadStream.Close();
                }
                Debug.Log(fileName + "上传成功");
            }
            catch (Exception e)
            {
                throw new Exception("上传失败" + e.Message);
            }
        });        
    }
}
