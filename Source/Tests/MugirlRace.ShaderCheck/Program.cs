using System.Text;

namespace Mugirl.ShaderCheck;

/// <summary>入口。全部通过返回 0，有失败返回 1，用法或环境错误返回 2。</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (!CheckPaths.TryResolve(args, out CheckPaths? paths, out string? problem))
        {
            Console.Error.WriteLine(problem);
            Console.Error.WriteLine("Usage: MugirlRace.ShaderCheck [modDir] [gameDir]");
            return 2;
        }

        // 必须在任何引用引擎类型的方法被 JIT 之前安装：引擎程序集不复制到本程序旁边，而是从游戏目录加载。
        GameAssemblyResolver.Install(paths.GameDir);
        return ShaderCheckRunner.Run(paths);
    }
}
