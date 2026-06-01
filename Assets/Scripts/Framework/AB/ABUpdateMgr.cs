using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;


/// <summary>
/// 拓展部分
/// 可以添加回调函数，用于显示进度条
/// </summary>

public class ABUpdateMgr : MonoBehaviour
{
    private static ABUpdateMgr instance;
    public static ABUpdateMgr Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject obj = new GameObject("ABUpdateMgr");
                instance = obj.AddComponent<ABUpdateMgr>();
            }
            return instance;
        }
    }

    // 用于存储远端AB包信息的字典 之后 和本地进行对比即可完成 更新 下载相关逻辑
    private Dictionary<string, ABInfo> remoteABInfoDic = new Dictionary<string, ABInfo>();

    // 用于存储本地AB包信息的字典 主要用于和远端信息对比
    private Dictionary<string, ABInfo> localABInfoDic = new Dictionary<string, ABInfo>();

    // 这是待下载的AB包列表文件 存储AB包的名字
    private List<string> downloadList = new List<string>();

    // 资源服务器IP
    private string serverIP = "ftp://127.0.0.1";

    /// <summary>
    /// 用于检测热更新的函数
    /// </summary>
    /// <param name="overCallBack"></param>
    /// <param name="updateInfoCallBack"></param>
    public void CheckUpdate(UnityAction<bool> overCallBack, UnityAction<string> updateInfoCallBack)
    {
        // 为了避免上一次报错 而残留信息 所以清空它
        remoteABInfoDic.Clear();
        localABInfoDic.Clear();
        downloadList.Clear();

        // 1.加载远端资源对比文件
        DownloadABCompareFile((isOver) =>
        {
            updateInfoCallBack("开始更新资源");
            if (isOver)
            {
                updateInfoCallBack("对比文件下载结束");
                string remoteInfo = File.ReadAllText(Application.persistentDataPath + "/ABCompareInfo_Temp.txt");
                updateInfoCallBack("解析远端对比文件");
                GetABCompareFileInfo(remoteInfo, remoteABInfoDic);
                updateInfoCallBack("解析远端对比文件完成");

                // 2.加载本地资源对比文件
                GetLocalABCompareFileInfo((isOver) =>
                {
                    if (isOver)
                    {
                        updateInfoCallBack("解析本地对比文件完成");
                        // 3.对比他们 然后进行AB包下载
                        updateInfoCallBack("开始对比");

                        foreach (string abName in remoteABInfoDic.Keys)
                        {
                            // 1）.判断 哪些资源是新的 然后记录 之后用于下载
                            // 由于本地对比信息中没有叫这个名字的AB包 是新的 所以记录下来下载
                            if (!localABInfoDic.ContainsKey(abName))
                                downloadList.Add(abName);
                            // 发现本地有同名AB包 然后继续处理
                            else
                            {
                                // 2）.判断 哪些资源是需要更新的 然后记录 之后用于下载
                                // 对比md5 判断是否需要更新
                                if (localABInfoDic[abName].md5 != remoteABInfoDic[abName].md5)
                                    downloadList.Add(abName);
                                // 如果md5码相等 证明是同一个资源 不需要更新

                                // 3）.判断 哪些资源需要删除
                                // 每次检测完一个名字的AB包 就移除本地的信息 那么本地剩下来的信息 就是远端没有的内容
                                // 我们就可以把他们删了
                                localABInfoDic.Remove(abName);
                            }
                        }
                        updateInfoCallBack("对比文件完成");

                        updateInfoCallBack("删除无用的AB包");
                        // 上面对比完了 那么就先删除没用的内容 再下载AB包
                        // 删除无用的AB包
                        foreach (string abName in localABInfoDic.Keys)
                        {
                            // 如果可读写文件夹中有内容 就删除它
                            // 默认资源中 信息 没法删
                            if (File.Exists(Application.persistentDataPath + "/" + abName))
                                File.Delete(Application.persistentDataPath + "/" + abName);
                        }
                                                
                        updateInfoCallBack("下载和更新AB包文件");
                        // 下载待更新列表中的所有AB包
                        DownloadABFile((isOver) =>
                        {
                            if (isOver)
                            {
                                // 下载完所有AB包文件后
                                // 把本地AB包对比文件 更新为最新
                                // 把之前读取出来的 远端对比文件信息 存储到 本地
                                File.WriteAllText(Application.persistentDataPath + "/ABCompareInfo.txt", remoteInfo);
                                updateInfoCallBack("更新本地AB包对比文件为最新");
                            }
                            overCallBack(isOver);
                        }, updateInfoCallBack);

                    }
                    else
                        overCallBack(false);
                });          
            }
            else
            {
                overCallBack(false);
            }
        });        
    }

    /// <summary>
    ///  下载AB包对比文件
    /// </summary>
    /// <param name="overCallBack"></param>
    public async void DownloadABCompareFile(UnityAction<bool> overCallBack)
    {
        // 1.从资源服务器下载资源对比文件
        // 本地存储的路径 由于多线程不能访问Unity相关的一些内容比如Application 所以声明在外部
        string localPath = Application.persistentDataPath + "/ABCompareInfo_Temp.txt";
        // 是否下载成功标识
        bool isOver = false;
        // 重新下载的最大次数
        int reDownloadNumMaxNum = 5;
        while (!isOver && reDownloadNumMaxNum > 0)
        {
            await Task.Run(() =>
            {
                isOver = DownloadFile("ABCompareInfo.txt", localPath);
            });
            --reDownloadNumMaxNum;
        }

        // 告诉外部成功与否
        overCallBack?.Invoke(isOver);
    }

    /// <summary>
    /// 获取下载下来的AB对比文件中的信息
    /// 原本是下载远端的 现在修改为通用解析函数，通过参入两个参数进行解析
    /// </summary>
    public void GetABCompareFileInfo(string info, Dictionary<string, ABInfo> ABInfo)
    {
        // 2.就是获取资源对比文件中的 字符出信息 进行拆分
        //string info = File.ReadAllText(Application.persistentDataPath + "/ABCompareInfo_Temp.txt");
        string[] strs = info.Split("|"); // 拆分
        string[] infos = null;
        for (int i = 0; i < strs.Length; i++)
        {
            infos = strs[i].Split(" "); // 拆分
                                        // 记录每一个远端AB包的信息 之后 好用来对比 
            ABInfo.Add(infos[0], new ABInfo(infos[0], infos[1], infos[2]));
        }
    }
    
    /// <summary>
    /// 获取本地的AB对比文件中的信息 加解析信息
    /// </summary>
    public void GetLocalABCompareFileInfo(UnityAction<bool> OverCallBack)
    {
        // 如果可读可写文件中 存在对比文件 说明之前已经下载更新过了
        if (File.Exists(Application.persistentDataPath + "/ABCompareInfo.txt"))
        {
            StartCoroutine(GetLocalABCompareFileInfo(Application.persistentDataPath + "/ABCompareInfo.txt", OverCallBack));
        }
        // 如果可读可写文件中 没有对比文件 那么就去默认只读文件夹中读取
        else if (File.Exists(Application.streamingAssetsPath + "/ABCompareInfo.txt"))
        {
            StartCoroutine(GetLocalABCompareFileInfo(Application.streamingAssetsPath + "/ABCompareInfo.txt", OverCallBack));
        }
        // 如果两个if都不进 证明是第一次进入并且没有默认资源
        else
            OverCallBack(true);
    }

    /// <summary>
    /// 协同函数 加载本地信息 并且解析存入字段
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    private IEnumerator GetLocalABCompareFileInfo(string filePath, UnityAction<bool> OverCallBack)
    {
        // 通过 UnityWebRequest 去加载本地文件
        UnityWebRequest req = UnityWebRequest.Get(filePath);
        yield return req.SendWebRequest();
        // 获取文件成功 继续往下执行
        if (req.result == UnityWebRequest.Result.Success)
        {
            GetABCompareFileInfo(req.downloadHandler.text, localABInfoDic);
            OverCallBack(true);
        }
        else
            OverCallBack(false);
    }

    public async void DownloadABFile(UnityAction<bool> overCallBack, UnityAction<string> updatePro)
    {
        //// 1.遍历字典中的键 根据文件名 去下载AB包到本地
        //foreach (string name in remoteABInfoDic.Keys)
        //{
        //    // 直接放入待下载列表
        //    downloadList.Add(name);
        //}

        // 本地存储的路径 由于多线程不能访问Unity相关的一些内容比如Application 所以声明在外部
        string localPath = Application.persistentDataPath + "/";
        // 是否下载成功标识
        bool isOver = false;
        // 下载成功的列表 之后用于移除下载成功的内容
        List<string> tempList = new List<string>();
        // 重新下载的最大次数
        int reDownloadNumMaxNum = 5;
        // 下载成功的资源数
        int downloadOverNum = 0;
        // 这一次下载需要下载多少个资源
        int downloadMaxNum = downloadList.Count;
        // while循环的目的 是进行n次重新下载 避免网络异常时 下载失败
        while (downloadList.Count > 0 && reDownloadNumMaxNum > 0)
        {
            for (int i = 0; i < downloadList.Count; i++)
            {
                await Task.Run(() =>
                {
                    isOver = DownloadFile(downloadList[i], localPath + downloadList[i]);
                });
                if (isOver)
                {
                    tempList.Add(downloadList[i]); //下载成功记录下来
                    // 2.要知道现在下载了多少 结束与否
                    updatePro(++downloadOverNum + "/" + downloadMaxNum);
                }
            }

            // 把下载成功的文件名 从待下载列表中移除
            for (int i = 0; i < tempList.Count; i++)
                downloadList.Remove(tempList[i]);

            --reDownloadNumMaxNum;
        }

        // 所有内容都下载完了 告诉外部是否下载完了
        overCallBack(downloadList.Count == 0);
    }

    private bool DownloadFile(string fileName, string localPath)
    {
        try
        {
            string platformInfo =
#if UNITY_IOS
    "IOS";
#elif UNITY_ANDROID
    "Android";
#else
    "PC";
#endif

            // 1.创建一个FTP连接 用于下载
            FtpWebRequest req = FtpWebRequest.Create(new Uri(serverIP + "/AB/" + platformInfo + "/" + fileName)) as FtpWebRequest;
            // 2.设置一个通信凭证 这样才能下载
            NetworkCredential n = new NetworkCredential("Lqz", "123456");
            req.Credentials = n;
            // 3.其他设置
            //   设置代理为null
            req.Proxy = null;
            //   请求完毕后 是否关闭控制连接
            req.KeepAlive = false;
            //   操作命令-下载
            req.Method = WebRequestMethods.Ftp.DownloadFile;
            //   指定传输的类型 二进制
            req.UseBinary = true;
            // 4.下载文件
            //   FTP的流对象
            FtpWebResponse res = req.GetResponse() as FtpWebResponse;
            Stream downloadStream = res.GetResponseStream();

            // 读取文件信息 写入该流对象
            using (FileStream file = File.Create(localPath))
            {
                // 一点一点的下载内容
                byte[] bytes = new byte[2048];
                // 返回值 代表读取了多少个字节
                int contentLength = downloadStream.Read(bytes, 0, bytes.Length);

                //循环下载文件中的数据
                while (contentLength != 0)
                {
                    // 写入到本地文件流中
                    file.Write(bytes, 0, contentLength);
                    // 写完再度
                    contentLength = downloadStream.Read(bytes, 0, bytes.Length);
                }

                // 循环完毕后证明下载结束
                file.Close();
                downloadStream.Close();

                Debug.Log(fileName + "下载成功");
                return true;
            }
        }
        catch (Exception e)
        {
            Debug.Log("下载失败" + e.Message);
            return false;
        }
    }

    private void OnDestroy()
    {
        instance = null;
    }

    public class ABInfo
    {
        public string name; // AB包名字
        public long size;   // AB包大小
        public string md5;  // AB包MD5

        public ABInfo(string name, string size, string md5)
        {
            this.name = name;
            this.size = long.Parse(size);
            this.md5 = md5;
        }
    }
}



