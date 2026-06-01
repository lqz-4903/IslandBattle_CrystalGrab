using System.Diagnostics;
using System.IO;
using UnityEditor;

/// <summary>
/// Protobuf协议生成工具
/// 用Protobuf生成对应的C#代码
/// </summary>

public class ProtobufTool
{
    //协议配置文件所在路径
    private static string PROTO_PATH = @"D:\UnityProgram\Unity_ProjectDemo\Protobuf\proto";
    //协议生成可执行文件路径
    private static string PROTOC_PATH = @"D:\UnityProgram\Unity_ProjectDemo\Protobuf\protoc.exe";
    //C#文件生成的路径
    private static string CSHARP_PATH = @"D:\UnityProgram\Unity_ProjectDemo\Protobuf\csharp";
    //CPP文件生成的路径
    private static string CPP_PATH = @"D:\UnityProgram\Unity_ProjectDemo\Protobuf\cpp";


    [MenuItem("ProtobufTool/通过proto.exe生成C#代码")]
    public static void GenerateCSharp()
    {
        Generate("csharp_out", CSHARP_PATH);
    }

    [MenuItem("ProtobufTool/通过proto.exe生成CPP代码")]
    public static void GenerateCPP()
    {
        Generate("cpp_out", CPP_PATH);
    }

    //生成对应脚本的方法
    public static void Generate(string outCmd, string outPath)
    {
        // 第一步：遍历对应协议配置文件夹 得到所有的配置文件
        DirectoryInfo directoryInfo = Directory.CreateDirectory(PROTO_PATH);
        //获取对应文件夹下所有文件信息
        FileInfo[] files = directoryInfo.GetFiles();
        //遍历所有的文件 为其生成协议脚本
        for (int i = 0; i < files.Length; i++)
        {
            //后缀的判断 只有是 配置文件才能用于生成
            if (files[i].Extension == ".proto")
            {
                // 第二步：根据文件内容 来生成对应的C#脚本 （需要使用C#当中的Process类）
                Process cmd = new Process();
                // protoc.exe的路径
                cmd.StartInfo.FileName = PROTOC_PATH;
                //命令
                cmd.StartInfo.Arguments = $"-I={PROTO_PATH} --{outCmd}={outPath} {files[i]}";
                //执行
                cmd.Start();
                //告诉外部 某一个文件生成结束
                UnityEngine.Debug.Log(files[i] + "生成结束");
            }
        }
        UnityEngine.Debug.Log("所有内容生成结束");
    }
}
