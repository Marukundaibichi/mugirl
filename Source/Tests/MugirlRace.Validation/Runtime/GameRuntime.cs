using System.Reflection;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Reporting;
using HarmonyLib;
using Verse;

namespace MugirlRace.Validation.Runtime;

/// <summary>
/// 按 RimWorld 1.7 的方式加载进本进程的游戏、Harmony、依赖 Mod 与被测 Mod。这里只碰元数据：游戏或 Mod 类型的静态构造函数都不运行。
/// </summary>
internal sealed class GameRuntime
{
    private GameRuntime(Assembly game, IReadOnlyList<Assembly> dependencies, Assembly mod, IReadOnlyList<Type> modTypes, IReadOnlyList<string> modTypeErrors)
    {
        Game = game;
        Dependencies = dependencies;
        Mod = mod;
        ModTypes = modTypes;
        ModTypeErrors = modTypeErrors;
    }

    /// <summary>RimWorld 1.7 的 Assembly-CSharp。</summary>
    public Assembly Game { get; }

    /// <summary>按加载顺序排列的可用依赖 Mod 程序集（Harmony 在前），与 <c>GenTypes.AllActiveAssemblies</c> 一致。</summary>
    public IReadOnlyList<Assembly> Dependencies { get; }

    /// <summary>被测的 MugirlRace.dll。</summary>
    public Assembly Mod { get; }

    /// <summary>成功加载的 Mod 类型（<see cref="ModTypeErrors"/> 为空时即全部类型）。</summary>
    public IReadOnlyList<Type> ModTypes { get; }

    /// <summary>对 Mod 调用 <c>Assembly.GetTypes()</c> 时的加载异常；出现时 RimWorld 会整体拒绝该程序集。</summary>
    public IReadOnlyList<string> ModTypeErrors { get; }

    /// <summary>RimWorld 的 <c>GenTypes</c> 看到的程序集：游戏、按加载顺序的各 Mod、被测 Mod。</summary>
    public IEnumerable<Assembly> ActiveAssemblies => Dependencies.Prepend(Game).Append(Mod);

    /// <summary>
    /// 加载全部内容。日志捕获紧跟游戏程序集安装，早于任何 Mod 或依赖程序集加载，它们写的日志一条都不会漏。
    /// </summary>
    public static GameRuntime Load(ValidationOptions options, ModCatalog catalog, Report report)
    {
        Assembly game = typeof(Log).Assembly;
        LogCapture.Install();
        OfflineModState.Install(catalog.Full);
        report.Environment("Assembly-CSharp", $"{game.GetName().Version} ({game.Location})");
        report.Environment("Harmony", $"{typeof(Harmony).Assembly.GetName().Version} ({typeof(Harmony).Assembly.Location})");

        var dependencies = new List<Assembly> { typeof(Harmony).Assembly };
        foreach (ContentPack pack in catalog.LoadOrder.Where(pack => !pack.IsOfficial && !pack.IsUnderTest))
        {
            foreach (LoadedFile file in LoadFolderResolver.Files(catalog.FoldersFor(pack, catalog.Full), "Assemblies", ModCatalog.IsDll))
            {
                Assembly assembly;
                try
                {
                    assembly = LoadByName(file.FullPath);
                }
                catch (Exception exception) when (exception is FileLoadException or BadImageFormatException or FileNotFoundException)
                {
                    // ModAssemblyHandler.ReloadAll 在第一个失败处停止加载该 Mod 剩余的程序集。
                    report.Environment($"dependency {pack.PackageId}", $"{file.RelativePath} failed to load: {exception.Message}");
                    break;
                }

                if (dependencies.Contains(assembly))
                {
                    continue;
                }

                if (TryGetTypes(assembly, out _, out string? error))
                {
                    dependencies.Add(assembly);
                    report.Environment($"dependency {assembly.GetName().Name}", file.FullPath);
                }
                else
                {
                    // ModAssemblyHandler.AssemblyIsUsable：类型没有全部加载成功的程序集完全不被使用。
                    report.Environment($"dependency {assembly.GetName().Name}", $"{file.FullPath} is unusable on this runtime: {error}");
                }
            }
        }

        if (!File.Exists(options.ModAssembly))
        {
            throw new EnvironmentException($"The assembly to validate does not exist: {options.ModAssembly}");
        }

        Assembly mod = Assembly.LoadFrom(options.ModAssembly);
        report.Environment("mod under test", $"{mod.GetName().Name} {mod.GetName().Version} ({mod.Location})");
        LoadTypes(mod, out IReadOnlyList<Type> modTypes, out IReadOnlyList<string> errors);
        return new GameRuntime(game, dependencies, mod, modTypes, errors);
    }

    /// <summary><paramref name="assembly"/> 中能加载的类型（部分失败时只含成功的）。</summary>
    public static IReadOnlyList<Type> LoadableTypes(Assembly assembly)
    {
        LoadTypes(assembly, out IReadOnlyList<Type> types, out _);
        return types;
    }

    /// <summary><c>Assembly.GetTypes()</c>，部分失败时保留成功加载的类型。</summary>
    public static bool TryGetTypes(Assembly assembly, out IReadOnlyList<Type> types, out string? error)
    {
        bool ok = LoadTypes(assembly, out types, out IReadOnlyList<string> errors);
        error = errors.Count == 0 ? null : string.Join("; ", errors.Take(3));
        return ok;
    }

    private static bool LoadTypes(Assembly assembly, out IReadOnlyList<Type> types, out IReadOnlyList<string> errors)
    {
        try
        {
            types = assembly.GetTypes();
            errors = [];
            return true;
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types.OfType<Type>().ToList();
            errors = exception.LoaderExceptions.OfType<Exception>().Select(e => e.Message).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            return false;
        }
    }

    // 依赖 DLL 按路径加载，除非同简单名的程序集已经加载（HAR 私带的 0Harmony 2.3.6 解析为已加载的 Harmony 3，与游戏运行时一致）。
    private static Assembly LoadByName(string path)
    {
        string? name = AssemblyName.GetAssemblyName(path).Name;
        return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
            ?? Assembly.LoadFrom(path);
    }
}
