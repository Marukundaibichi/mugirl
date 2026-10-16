using System.Reflection;
using MugirlRace.Validation.Content;

namespace MugirlRace.Validation.Infrastructure;

/// <summary>
/// 从 RimWorld 1.7 实际加载的位置解析游戏、Harmony 与依赖 Mod 的程序集；验证程序旁不复制任何文件，
/// 因此同一份构建能在标准 .NET 10 和游戏运行时上运行。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>0Harmony</c> 总是解析为游戏 1.7 加载的 Harmony 3。当前 HAR 1.7 的引用仍标记 0Harmony 2.3.6；
/// 这里与游戏一样绑定已经加载的 Harmony 3，不据此选择旧目录中的 DLL。</item>
/// <item>Unity 程序集名按 RimWorld.runtimeconfig.json 的 <c>WaylithClr.AssemblyNameRedirects</c> 重定向。
/// 是否需要重定向取决于依赖的实际引用，不由版本目录名推断；当前 HAR 1.7 直接引用 UnityStubs。</item>
/// <item>依赖 Mod 解析到游戏 LoadFolders 逻辑选中的确切文件（<see cref="ModCatalog.DependencyAssemblies"/>），
/// 不能按目录探测顺序猜测，否则可能选中同名的旧版程序集。</item>
/// <item>每个简单名只加载一次；同名从两个路径加载会在默认上下文中冲突。</item>
/// </list>
/// 必须用 <c>AppDomain.AssemblyResolve</c>：<c>AssemblyLoadContext.Resolving</c> 拒绝返回名字不同的程序集（0x80131509）。
/// </remarks>
internal static class GameAssemblyResolver
{
    // WaylithClr.AssemblyNameRedirects 的有序规则；目标为空表示“不存在”。
    private static readonly (string Pattern, string Target)[] Redirects =
    [
        ("UnityEngine*", "UnityStubs"),
        ("Unity.Newtonsoft.Json", ""),
        ("Unity.*", "UnityStubs"),
        ("Assembly-CSharp-firstpass", "UnityStubs"),
    ];

    private static readonly Dictionary<string, Assembly?> Resolved = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> paths = new(StringComparer.OrdinalIgnoreCase);

    public static void Install(ValidationOptions options, ModCatalog catalog)
    {
        var map = new Dictionary<string, string>(catalog.DependencyAssemblies(), StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(options.GameDir, "*.dll"))
        {
            map.TryAdd(Path.GetFileNameWithoutExtension(file), file);
        }

        string harmony = Path.Combine(options.HarmonyDir, "0Harmony.dll");
        if (!File.Exists(harmony))
        {
            throw new EnvironmentException($"Harmony 3 not found at {harmony} (pass --harmony).");
        }

        map["0Harmony"] = harmony;
        paths = map;
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }

    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        string requested = new AssemblyName(args.Name).Name ?? args.Name;
        lock (Resolved)
        {
            if (Resolved.TryGetValue(requested, out Assembly? cached))
            {
                return cached;
            }

            string name = Redirect(requested);
            Assembly? assembly = null;
            if (name.Length > 0)
            {
                // 同名程序集可能已经加载（验证程序自己的 Harmony 引用，或按路径加载的被测 Mod）。
                assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(loaded => string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
                if (assembly is null && paths.TryGetValue(name, out string? path))
                {
                    assembly = Assembly.LoadFrom(path);
                }
            }

            Resolved[requested] = assembly;
            return assembly;
        }
    }

    private static string Redirect(string name)
    {
        foreach ((string pattern, string target) in Redirects)
        {
            bool match = pattern.EndsWith('*')
                ? name.StartsWith(pattern[..^1], StringComparison.Ordinal)
                : string.Equals(name, pattern, StringComparison.Ordinal);
            if (match)
            {
                return target;
            }
        }

        return name;
    }
}
