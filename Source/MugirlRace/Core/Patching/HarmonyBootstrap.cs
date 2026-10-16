using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Mugirl.Core;

/// <summary>
/// 逐类应用本程序集的全部 Harmony 补丁。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Harmony.PatchAll(Assembly)"/> 没有隔离：第一个抛异常的类（目标被删、重载歧义、Prepare 返回值错误等）
/// 会让后面所有类都不打补丁。这里逐类调用 <c>CreateClassProcessor(type).Patch()</c>，失败只影响该类所属的功能，
/// 并记录是哪个类失败。
/// </para>
/// <para>
/// 筛选条件与 <c>PatchAll</c> 相同（任何 <see cref="HarmonyAttribute"/>，inherit: true），否则 JobDriver、QuestPart
/// 等子类继承的公有 <c>Cleanup()</c> 会被当成 Harmony 辅助方法。登记顺序按完整类型名的序数排序（与编译顺序无关），
/// <see cref="MugirlPatchAttribute.Deferred"/> 的类在其余类之后登记。
/// </para>
/// <para>
/// 补丁执行顺序不能依赖登记顺序：改类名就会改变它。同一目标上有多个同类（Prefix、Postfix……）Mugirl 补丁时，
/// 每个都用 <see cref="HarmonyPriority"/> 写明互不相同的优先级。<see cref="HarmonyBefore"/>/<see cref="HarmonyAfter"/>
/// 只匹配其他 Harmony id，本 Mod 的补丁共用 <see cref="HarmonyId"/>，彼此之间只能靠优先级排序。
/// </para>
/// <para>
/// 离线验证程序通过反射调用 <see cref="PatchClasses"/> 与 <see cref="Apply"/>，这两个成员的形状不能改。
/// </para>
/// </remarks>
public static class HarmonyBootstrap
{
    /// <summary>Harmony 实例 id，属于公开 API：其他 Mod 的 HarmonyBefore/HarmonyAfter 依赖它，不能改名。</summary>
    public const string HarmonyId = "MugirlMod.Mod";

    private const string FailureTranslationKey = "Mugirl.Bootstrap.HarmonyPatchFailed";

    /// <summary>一个未能应用的补丁类及 Harmony 抛出的异常。</summary>
    public sealed record PatchFailure(Type PatchClass, Exception Exception);

    // StaticCacheLifecycle: 进程级不可变的补丁类清单与类型扫描异常；程序集加载后不再变化，无需清理。
    private static readonly Discovery discovered = Discover();

    // StaticCacheLifecycle: 进程级启动结果，只在 Mod 构造与加载长事件结束时写入，供开发者模式汇总使用。
    private static readonly object startupGate = new();
    private static readonly List<PatchFailure> startupFailures = [];
    private static bool startupApplied;

    /// <summary>带 Harmony 特性的全部类，按完整类型名的序数排序。</summary>
    public static IReadOnlyList<Type> PatchClasses => discovered.Classes;

    /// <summary>
    /// 同步应用全部补丁类，每个类单独捕获异常，返回失败的类。失败也会写入日志。
    /// 供离线验证使用；登记顺序与游戏内的 <see cref="ApplyAtStartup"/> 相同（延迟类排在其余类之后）。
    /// </summary>
    public static IReadOnlyList<PatchFailure> Apply(Harmony harmony)
    {
        ArgumentNullException.ThrowIfNull(harmony);

        List<PatchFailure> failures = [.. discovered.Problems];
        ReportDiscoveryProblems();
        foreach (Type patchClass in discovered.Immediate.Concat(discovered.Deferred))
        {
            if (TryPatch(harmony, patchClass) is { } failure)
            {
                failures.Add(failure);
            }
        }

        return failures;
    }

    /// <summary>
    /// Mod 构造函数中的入口：立即应用普通补丁类，<see cref="MugirlPatchAttribute.Deferred"/> 的类推迟到加载长事件结束。
    /// </summary>
    /// <remarks>
    /// 必须在 Mod 构造函数中调用。只返回字段的小 getter（如 <c>CompApparelVerbOwner.Wearer</c>、<c>Pawn.DrawPos</c>）
    /// 会被 JIT 内联进调用方；调用方在补丁之后才编译，补丁才对它们可见（迁移指南“补丁与 JIT 内联”）。
    /// </remarks>
    internal static void ApplyAtStartup(Harmony harmony)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        lock (startupGate)
        {
            if (startupApplied)
            {
                return;
            }

            startupApplied = true;
            startupFailures.AddRange(discovered.Problems);
        }

        ReportDiscoveryProblems();
        foreach (Type patchClass in discovered.Immediate)
        {
            RecordStartupResult(TryPatch(harmony, patchClass));
        }

        if (discovered.Deferred.Count > 0)
        {
            // 编译目标方法时可能触发目标类型的静态构造（例如 HAR 的 StylingStation 会加载 UI 贴图），
            // 这类补丁等加载长事件结束后在主线程注册。
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                foreach (Type patchClass in discovered.Deferred)
                {
                    RecordStartupResult(TryPatch(harmony, patchClass));
                }
            });
        }

        // 排在延迟补丁之后执行，汇总时包含全部实际注册结果。
        LongEventHandler.ExecuteWhenFinished(() => LogDevSummary(harmony));
    }

    internal static MugirlPatchAttribute? GetMetadata(Type patchClass) =>
        patchClass.GetCustomAttribute<MugirlPatchAttribute>(inherit: false);

    private static bool IsDeferred(Type patchClass) => GetMetadata(patchClass)?.Deferred == true;

    private static PatchFailure? TryPatch(Harmony harmony, Type patchClass)
    {
        try
        {
            harmony.CreateClassProcessor(patchClass).Patch();
            return null;
        }
        catch (Exception exception)
        {
            ReportFailure("Bootstrap.PatchClassFailed." + patchClass.FullName, patchClass.FullName ?? patchClass.Name, exception);
            return new PatchFailure(patchClass, exception);
        }
    }

    private static void RecordStartupResult(PatchFailure? failure)
    {
        if (failure is null)
        {
            return;
        }

        lock (startupGate)
        {
            startupFailures.Add(failure);
        }
    }

    private static void ReportDiscoveryProblems()
    {
        foreach (PatchFailure problem in discovered.Problems)
        {
            string name = problem.PatchClass.FullName ?? problem.PatchClass.Name;
            ReportFailure("Bootstrap.PatchClassDiscovery." + name, name, problem.Exception);
        }
    }

    private static void ReportFailure(string warningKey, string targetName, Exception exception)
    {
        // Harmony 的外层异常通常只带目标信息；保留内部异常和完整堆栈才能定位原因。
        string detail = exception.ToString();
        MugirlLog.StartupWarningOnce(warningKey,
            () => FailureTranslationKey.Translate(targetName, detail).ToString(),
            "Harmony patch skipped: " + targetName + ". " + detail);
    }

    private static Discovery Discover()
    {
        Assembly assembly = typeof(HarmonyBootstrap).Assembly;
        List<PatchFailure> problems = [];
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            // 部分类型加载失败时继续处理其余类型，并把异常作为发现阶段的失败报告出去。
            types = exception.Types;
            problems.Add(new PatchFailure(typeof(HarmonyBootstrap), exception));
        }

        List<Type> classes = [];
        foreach (Type? type in types)
        {
            if (type is null || !type.IsClass)
            {
                continue;
            }

            try
            {
                if (type.GetCustomAttributes(inherit: true).Any(attribute => attribute is HarmonyAttribute))
                {
                    classes.Add(type);
                }
            }
            catch (Exception exception)
            {
                // 读不出特性的类型可能是补丁类，不能静默跳过。
                problems.Add(new PatchFailure(type, exception));
            }
        }

        classes.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        // 登记顺序只在这里决定，离线 Apply 与游戏内 ApplyAtStartup 共用：普通类按上面的顺序，延迟类排在其后。
        List<Type> immediate = [.. classes.Where(type => !IsDeferred(type))];
        List<Type> deferred = [.. classes.Where(IsDeferred)];
        return new Discovery(classes.AsReadOnly(), immediate.AsReadOnly(), deferred.AsReadOnly(), problems.AsReadOnly());
    }

    private static void LogDevSummary(Harmony harmony)
    {
        if (!Prefs.DevMode)
        {
            return;
        }

        // Harmony 3 的 Patches 有 7 个列表；只统计本 Mod 的 owner，并记录每个补丁方法所属的类以判断哪些类实际生效。
        var appliedClasses = new HashSet<Type>();
        int[] listCounts = new int[7];
        int patchedMethods = 0;
        foreach (MethodBase original in harmony.GetPatchedMethods())
        {
            Patches? info = Harmony.GetPatchInfo(original);
            if (info is null)
            {
                continue;
            }

            patchedMethods++;
            CountOwned(info.Prefixes, 0);
            CountOwned(info.Postfixes, 1);
            CountOwned(info.Transpilers, 2);
            CountOwned(info.Finalizers, 3);
            CountOwned(info.InnerPrefixes, 4);
            CountOwned(info.InnerPostfixes, 5);
            CountOwned(info.InnerFinalizers, 6);
        }

        int withMetadata = 0;
        int deferred = 0;
        int highRisk = 0;
        int activeHighRisk = 0;
        List<string> missingMetadata = [];
        foreach (Type patchClass in PatchClasses)
        {
            MugirlPatchAttribute? metadata = GetMetadata(patchClass);
            if (metadata is null)
            {
                missingMetadata.Add(patchClass.FullName ?? patchClass.Name);
                continue;
            }

            withMetadata++;
            if (metadata.Deferred)
            {
                deferred++;
            }

            if (metadata.Risk >= PatchRisk.High)
            {
                highRisk++;
                if (appliedClasses.Contains(patchClass))
                {
                    activeHighRisk++;
                }
            }
        }

        int failed;
        lock (startupGate)
        {
            failed = startupFailures.Count;
        }

        MugirlLog.DevMessage(
            "Patch audit: classes=" + PatchClasses.Count +
            ", withMetadata=" + withMetadata +
            ", deferred=" + deferred +
            ", failed=" + failed +
            ", active=" + appliedClasses.Count +
            ", highRisk=" + highRisk +
            ", activeHighRisk=" + activeHighRisk +
            "; owned patches on " + patchedMethods + " methods: prefixes=" + listCounts[0] +
            ", postfixes=" + listCounts[1] +
            ", transpilers=" + listCounts[2] +
            ", finalizers=" + listCounts[3] +
            ", innerPrefixes=" + listCounts[4] +
            ", innerPostfixes=" + listCounts[5] +
            ", innerFinalizers=" + listCounts[6] + ".");

        if (missingMetadata.Count > 0)
        {
            MugirlLog.Warning("Patch audit: Harmony patch classes without [MugirlPatch] metadata: " + string.Join(", ", missingMetadata));
        }

        void CountOwned(IReadOnlyList<Patch> patches, int listIndex)
        {
            foreach (Patch patch in patches)
            {
                if (patch.owner != HarmonyId)
                {
                    continue;
                }

                listCounts[listIndex]++;
                if (patch.PatchMethod?.DeclaringType is { } declaringType)
                {
                    appliedClasses.Add(declaringType);
                }
            }
        }
    }

    /// <param name="Classes">全部补丁类，按完整类型名的序数排序。</param>
    /// <param name="Immediate">Mod 构造时登记的类（<paramref name="Classes"/> 中非延迟的部分，顺序不变）。</param>
    /// <param name="Deferred">加载长事件结束后登记的类，排在 <paramref name="Immediate"/> 之后。</param>
    /// <param name="Problems">发现阶段的失败。</param>
    private sealed record Discovery(IReadOnlyList<Type> Classes, IReadOnlyList<Type> Immediate, IReadOnlyList<Type> Deferred, IReadOnlyList<PatchFailure> Problems);
}
