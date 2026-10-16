using System.Reflection;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Reporting;
using MugirlRace.Validation.Runtime;
using HarmonyLib;
using Verse;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 按 Harmony 3 自己的规则（<c>PatchClassProcessor</c>，3.0.0-rc.2）解析每个补丁类的目标：先不带原方法调用 <c>Prepare()</c>，
/// 再逐个原方法调用；<c>[HarmonyPatchAll]</c>；<c>TargetMethods()</c>（返回 null 时继续往下，元素为 null 失败）；<c>TargetMethod()</c>
/// （null 失败）；否则用合并后的特性做 Harmony 的 <em>declared</em> 查找（游戏更新删掉的重写是错误，而不是静默改补基类方法；重载歧义
/// 也是错误）。结果必须等于 <c>Baselines/patch-targets.txt</c>。
/// </summary>
/// <remarks>
/// 先用游戏、依赖与 Mod 的类型填充 <c>GenTypes.AllTypes</c>：离线时它只含 Assembly-CSharp，借它枚举子类的 <c>TargetMethods()</c>
/// 会比游戏中少找到目标。绝不调用 <c>GenTypes.ClearCache</c>（它会碰 AlertsReadout）。
/// </remarks>
internal static class PatchTargetCheck
{
    public const string GoldenFile = "patch-targets.txt";

    private static readonly string[] Roles = ["Prefix", "Postfix", "Transpiler", "Finalizer", "ReversePatch", "InnerPrefix", "InnerPostfix", "InnerFinalizer"];

    public static PatchPlan Run(Report report, ValidationOptions options, GameRuntime runtime)
    {
        var plan = new PatchPlan();
        report.Run("B.patch-targets", "Harmony patch targets resolve (Harmony 3 semantics) and match the golden list", recorder => Check(recorder, options, runtime, plan));
        return plan;
    }

    /// <summary>Mod 的补丁类，筛选与排序和 <c>Mugirl.Core.HarmonyBootstrap</c> 完全相同（任何 HarmonyAttribute，inherit: true；按完整名序数排序）。</summary>
    public static IReadOnlyList<Type> PatchClasses(GameRuntime runtime)
        => runtime.ModTypes.Where(type => type.IsClass && type.GetCustomAttributes(inherit: true).Any(a => a is HarmonyAttribute))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

    private static void Check(CheckRecorder recorder, ValidationOptions options, GameRuntime runtime, PatchPlan plan)
    {
        SeedAllTypes(recorder, runtime);
        var harmony = new Harmony("mugirl.validation.resolve");
        var lines = new List<string>();
        IReadOnlyList<Type> classes = PatchClasses(runtime);
        foreach (Type type in classes)
        {
            try
            {
                ResolveClass(recorder, type, harmony, plan, lines);
            }
            catch (Exception exception) when (exception is TargetInvocationException or HarmonyException or ArgumentException
                or AmbiguousMatchException or TypeLoadException or MissingMemberException or InvalidOperationException or NullReferenceException)
            {
                Exception cause = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
                recorder.Fail(RuntimeNames.Type(type), "Harmony would fail on this patch class (and HarmonyBootstrap would disable it): "
                    + cause.GetType().Name + ": " + cause.Message);
            }
        }

        plan.Resolved = true;
        GoldenList.Compare(options.BaselineDir, GoldenFile, lines, options.UpdateBaselines, recorder,
            "Patch targets of every MugirlRace patch class, resolved with Harmony 3 semantics against RimWorld 1.7.\n"
            + "PatchClass<TAB>Role PatchMethod<TAB>Target. Regenerate with --update-baselines and review the diff.");
        recorder.Summary = $"{classes.Count} patch classes → {plan.Patches.Count} patches ({plan.Skipped.Count} skipped by Prepare())";
    }

    private static void ResolveClass(CheckRecorder recorder, Type type, Harmony harmony, PatchPlan plan, List<string> lines)
    {
        string name = RuntimeNames.Type(type);
        HarmonyMethod container = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(type));
        container.methodType ??= MethodType.Normal;

        var patchMethods = new List<(MethodInfo Method, string Role, HarmonyMethod Info)>();
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
        {
            string? role = RoleOf(method);
            if (role is null)
            {
                continue;
            }

            if (role != "ReversePatch" && !method.IsStatic)
            {
                throw new ArgumentException($"Patch method {method.Name} must be static.");
            }

            HarmonyMethod info = container.Merge(HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromMethod(method)));
            info.method = method;
            patchMethods.Add((method, role, info));
        }

        MethodInfo? prepare = AuxiliaryMethod(type, "HarmonyLib.HarmonyPrepare", "Prepare", typeof(bool));
        MethodInfo? targetMethods = AuxiliaryMethod(type, "HarmonyLib.HarmonyTargetMethods", "TargetMethods", typeof(IEnumerable<MethodBase>));
        MethodInfo? targetMethod = AuxiliaryMethod(type, "HarmonyLib.HarmonyTargetMethod", "TargetMethod", typeof(MethodBase));
        if (!RunPrepare(prepare, harmony, null))
        {
            plan.Skipped.Add(type);
            lines.Add($"{name}\tSKIPPED by Prepare()");
            return;
        }

        List<MethodBase> bulk = BulkMethods(type, container, targetMethods, targetMethod, harmony);
        var jobs = new List<(MethodBase Original, MethodInfo Method, string Role)>();
        if (bulk.Count > 0)
        {
            foreach ((MethodInfo method, string role, HarmonyMethod info) in patchMethods)
            {
                if (info.methodName is not null || (info.methodType.HasValue && info.methodType.Value != MethodType.Normal) || info.argumentTypes is not null)
                {
                    throw new ArgumentException($"You cannot combine TargetMethod, TargetMethods or [HarmonyPatchAll] with individual annotations ({method.Name}).");
                }

                jobs.AddRange(bulk.Select(original => (original, method, role)));
            }
        }
        else
        {
            foreach ((MethodInfo method, string role, HarmonyMethod info) in patchMethods)
            {
                MethodBase original = OriginalMethod(info) ?? throw new ArgumentException($"Undefined target method for patch method {method.Name} ({Describe(info)}).");
                jobs.Add((original, method, role));
            }
        }

        if (jobs.Count == 0)
        {
            recorder.Warn(name, "Has a Harmony attribute but no patch method; it patches nothing.");
        }

        foreach (IGrouping<MethodBase, (MethodBase Original, MethodInfo Method, string Role)> group in jobs.GroupBy(job => job.Original))
        {
            MethodBase original = group.Key;
            if (!RunPrepare(prepare, harmony, original))
            {
                lines.Add($"{name}\tSKIPPED by Prepare({RuntimeNames.Method(original)})");
                continue;
            }

            if (original.IsAbstract || original.GetMethodBody() is null)
            {
                recorder.Fail(name, $"Target {RuntimeNames.Method(original)} has no method body (abstract or extern); Harmony cannot patch it.");
                continue;
            }

            if (original.DeclaringType?.Assembly == type.Assembly)
            {
                recorder.Fail(name, "Own-assembly Harmony target " + RuntimeNames.Method(original)
                    + ": use an explicit feature extension point; compatibility must not patch the mod's own methods.");
            }

            foreach ((_, MethodInfo method, string role) in group)
            {
                var patch = new PlannedPatch(type, role, method, original);
                lines.Add(patch.Key);
                if (role.StartsWith("Inner", StringComparison.Ordinal))
                {
                    recorder.Info(name, $"Infix patch {method.Name}: the inner call site is not verified offline.");
                }

                if (role != "ReversePatch")
                {
                    plan.Patches.Add(patch);
                }
            }
        }
    }

    // 对应 PatchClassProcessor.GetBulkMethods。
    private static List<MethodBase> BulkMethods(Type type, HarmonyMethod container, MethodInfo? targetMethods, MethodInfo? targetMethod, Harmony harmony)
    {
        if (type.GetCustomAttributes(inherit: true).Any(a => a.GetType().FullName == "HarmonyLib.HarmonyPatchAll"))
        {
            Type declaring = container.declaringType ?? throw new ArgumentException("[HarmonyPatchAll] requires an additional attribute for the class/type.");
            var all = new List<MethodBase>();
            all.AddRange(AccessTools.GetDeclaredConstructors(declaring));
            all.AddRange(AccessTools.GetDeclaredMethods(declaring));
            foreach (PropertyInfo property in AccessTools.GetDeclaredProperties(declaring))
            {
                all.AddRange(new[] { property.GetGetMethod(nonPublic: true), property.GetSetMethod(nonPublic: true) }.OfType<MethodInfo>());
            }

            return all;
        }

        if (targetMethods is not null && Invoke(targetMethods, harmony, null) is IEnumerable<MethodBase> many)
        {
            List<MethodBase> list = many.ToList();
            if (list.Any(method => method is null))
            {
                throw new ArgumentException($"{targetMethods.Name}() returned an unexpected result: some element was null.");
            }

            return list;
        }

        if (targetMethod is not null)
        {
            return [Invoke(targetMethod, harmony, null) as MethodBase ?? throw new ArgumentException($"{targetMethod.Name}() returned an unexpected result: null.")];
        }

        return [];
    }

    // 对应 PatchTools.GetOriginalMethod（Harmony 3.0.0-rc.2），只用 declared 查找。
    private static MethodBase? OriginalMethod(HarmonyMethod info)
    {
        Type? type = info.declaringType;
        if (type is null || info.methodType is not { } methodType)
        {
            return null;
        }

        try
        {
            return methodType switch
            {
                MethodType.Normal => string.IsNullOrEmpty(info.methodName) ? null : AccessTools.DeclaredMethod(type, info.methodName, info.argumentTypes),
                MethodType.Getter => string.IsNullOrEmpty(info.methodName) ? AccessTools.DeclaredIndexerGetter(type, info.argumentTypes) : AccessTools.DeclaredPropertyGetter(type, info.methodName),
                MethodType.Setter => string.IsNullOrEmpty(info.methodName) ? AccessTools.DeclaredIndexerSetter(type, info.argumentTypes) : AccessTools.DeclaredPropertySetter(type, info.methodName),
                MethodType.Constructor => AccessTools.DeclaredConstructor(type, info.argumentTypes),
                MethodType.StaticConstructor => AccessTools.GetDeclaredConstructors(type).FirstOrDefault(c => c.IsStatic),
                MethodType.Enumerator => string.IsNullOrEmpty(info.methodName) ? null : AccessTools.EnumeratorMoveNext(AccessTools.DeclaredMethod(type, info.methodName, info.argumentTypes)),
                MethodType.Async => string.IsNullOrEmpty(info.methodName) ? null : AccessTools.AsyncMoveNext(AccessTools.DeclaredMethod(type, info.methodName, info.argumentTypes)),
                MethodType.Finalizer => AccessTools.DeclaredFinalizer(type),
                MethodType.EventAdd => string.IsNullOrEmpty(info.methodName) ? null : AccessTools.DeclaredEventAdder(type, info.methodName),
                MethodType.EventRemove => string.IsNullOrEmpty(info.methodName) ? null : AccessTools.DeclaredEventRemover(type, info.methodName),
                _ when methodType.ToString().StartsWith("Operator", StringComparison.Ordinal)
                    => AccessTools.DeclaredMethod(type, "op_" + methodType.ToString()["Operator".Length..], info.argumentTypes),
                _ => null,
            };
        }
        catch (AmbiguousMatchException exception)
        {
            throw new ArgumentException($"Ambiguous match for HarmonyMethod[{Describe(info)}]: give the argument types.", exception);
        }
    }

    // 对应 PatchTools.GetPatchMethod：先按特性、再按名字查找；返回类型不对时 Harmony 拒绝。
    private static MethodInfo? AuxiliaryMethod(Type type, string attributeName, string name, Type expectedReturn)
    {
        MethodInfo? method = type.GetMethods(AccessTools.all).FirstOrDefault(m => m.GetCustomAttributes(inherit: true).Any(a => a.GetType().FullName == attributeName))
            ?? type.GetMethod(name, AccessTools.all);
        if (method is not null && method.ReturnType != typeof(void) && !expectedReturn.IsAssignableFrom(method.ReturnType))
        {
            throw new ArgumentException($"Method {method.Name} has wrong return type (should be assignable to {expectedReturn.FullName}); "
                + "Harmony reserves this name (never name a helper Prepare/TargetMethod/TargetMethods/Cleanup).");
        }

        return method;
    }

    private static bool RunPrepare(MethodInfo? prepare, Harmony harmony, MethodBase? original)
        => prepare is null || Invoke(prepare, harmony, original) is not false;

    private static object? Invoke(MethodInfo method, Harmony harmony, MethodBase? original)
    {
        object[] inputs = original is null ? [harmony] : [original, harmony];
        return method.Invoke(null, AccessTools.ActualParameters(method, inputs));
    }

    // 对应 AttributePatch.GetPatchType：方法名，或 [Harmony<Role>] 特性。
    private static string? RoleOf(MethodInfo method)
    {
        var attributes = new HashSet<string>(method.GetCustomAttributes(inherit: true).Select(a => a.GetType().FullName ?? ""), StringComparer.Ordinal);
        return Roles.FirstOrDefault(role => method.Name == role || attributes.Contains("HarmonyLib.Harmony" + role));
    }

    private static string Describe(HarmonyMethod info)
        => $"{(info.declaringType is null ? "?" : RuntimeNames.Type(info.declaringType))}.{info.methodName} {info.methodType}"
            + (info.argumentTypes is null ? "" : "(" + string.Join(", ", info.argumentTypes.Select(RuntimeNames.Type)) + ")");

    private static void SeedAllTypes(CheckRecorder recorder, GameRuntime runtime)
    {
        FieldInfo? field = typeof(GenTypes).GetField("allTypesCached", BindingFlags.NonPublic | BindingFlags.Static);
        if (field is null)
        {
            recorder.Warn("GenTypes.allTypesCached", "Field not found in this game build: TargetMethods() that enumerate GenTypes.AllTypes see only Assembly-CSharp offline.");
            return;
        }

        var seen = new HashSet<Type>();
        var all = new List<Type>();
        foreach (Assembly assembly in runtime.ActiveAssemblies)
        {
            IReadOnlyList<Type> types = assembly == runtime.Mod ? runtime.ModTypes : GameRuntime.LoadableTypes(assembly);
            all.AddRange(types.Where(seen.Add));
        }

        field.SetValue(null, all);
    }
}
