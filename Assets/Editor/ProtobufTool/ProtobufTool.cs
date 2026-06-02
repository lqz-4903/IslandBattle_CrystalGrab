using System.Diagnostics;
using System.IO;
using UnityEditor;

public class ProtobufTool
{
    private static string PROTO_PATH = @"D:\UnityProgram\Unity_ProjectDemo\Protobuf\proto";
    private static string PROTOC_PATH = @"D:\UnityProgram\Unity_ProjectDemo\Protobuf\protoc.exe";
    private static string CSHARP_PATH = @"D:\UnityProgram\Unity_ProjectDemo\Protobuf\csharp";
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

    public static void Generate(string outCmd, string outPath)
    {
        //自动创建输出目录
        if (!Directory.Exists(outPath))
        {
            Directory.CreateDirectory(outPath);
            UnityEngine.Debug.Log($"已创建目录:{outPath}");
        }

        FileInfo[] protoFiles = new DirectoryInfo(PROTO_PATH).GetFiles("*.proto");
        foreach (var file in protoFiles)
        {
            Process cmd = new Process();
            cmd.StartInfo.FileName = PROTOC_PATH;
            //路径加引号防空格
            cmd.StartInfo.Arguments = $"-I=\"{PROTO_PATH}\" --{outCmd}=\"{outPath}\" \"{file.FullName}\"";

            //【核心配置：捕获cmd报错】
            cmd.StartInfo.UseShellExecute = false;
            cmd.StartInfo.RedirectStandardError = true; //捕获错误
            cmd.StartInfo.RedirectStandardOutput = true; //捕获正常日志
            cmd.StartInfo.CreateNoWindow = true;

            cmd.Start();
            cmd.WaitForExit();

            //读取错误信息，红字打印
            string errorMsg = cmd.StandardError.ReadToEnd();
            string outMsg = cmd.StandardOutput.ReadToEnd();

            //正常输出白字
            if (!string.IsNullOrEmpty(outMsg))
                UnityEngine.Debug.Log($"{file.Name} 输出信息：{outMsg}");
            //错误红字
            if (!string.IsNullOrEmpty(errorMsg))
                UnityEngine.Debug.LogError($"{file.Name}【protoc报错】：{errorMsg}");
            else
                UnityEngine.Debug.Log($"{file.Name} 生成成功！");

            cmd.Close();
        }
        UnityEngine.Debug.Log("===全部生成处理完成===");
    }
}