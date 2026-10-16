using System.Reflection;
using System.Runtime.Loader;

namespace Mugirl.ShaderCheck;

/// <summary>从游戏目录加载引擎程序集（Waylith、Brutal.*、Microsoft.Extensions.*）与原生库。</summary>
internal static class GameAssemblyResolver
{
    /// <summary>安装解析器。只调用一次，且在接触任何引擎类型之前。</summary>
    public static void Install(string gameDir)
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string candidate = Path.Combine(gameDir, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };

        // Slang 编译器（slang.dll 及其依赖）是原生库，位于游戏目录。
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable("PATH", gameDir + Path.PathSeparator + path);
    }

    /// <summary>引擎程序集的版本，用于报告头。</summary>
    public static string VersionOf(Assembly assembly)
        => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? assembly.GetName().Version?.ToString()
           ?? "unknown";
}
