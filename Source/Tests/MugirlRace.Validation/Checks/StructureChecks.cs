using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 结构约定（设计 §3.1、决定 §1 D4、§2、§4 C25），按编译结果检查：
/// <list type="bullet">
/// <item><c>B.namespaces</c>：根命名空间 <c>Mugirl</c> 中只能有 <c>Source/MugirlRace/PublicApi/</c> 下、列在 public-api.json 的公开别名类型；
/// 全局命名空间没有类型；命名空间段不能是会遮蔽游戏名字的词（UI、Text、Log、Find、Gen、Widgets、Scribe、AlienRace、HarmonyLib、
/// 原版忽略命名空间的段）或 Mod 代码用到的 Verse/RimWorld 类型短名（例如 Apparel）。</item>
/// <item><c>B.component-ctors</c>：GameComponent/MapComponent/WorldComponent 有游戏使用的 public 构造函数，且构造函数（含字段初始化与
/// 它调用的本模块方法）不碰 DefDatabase、Find、Current，也不写静态字段：游戏为每个游戏（包括加到旧存档时）调用它。</item>
/// <item><c>B.save-confinement</c>：给存档读取内部（Scribe 与 Scribe_*、ScribeLoader、ScribeSaver、ScribeExtractor、ScribeMetaHeaderUtility、
/// BackCompatibility、CrossRefHandler、PostLoadIniter、LoadedObjectDirectory，以及 Game.LoadGame、Game.ExposeSmallComponents）打补丁的类只能在
/// <c>SaveUpgrade/</c> 下：[HarmonyPatch] 特性、TargetMethod(s) 与手写 Harmony.Patch 调用都算（IL 中的调用由 architecture-rules.json 限定）。</item>
/// </list>
/// 现有违规列在 <c>Baselines/structure-debt.allow.txt</c>（只能减少：不再违规的条目报为过期）。
/// </summary>
internal static class StructureChecks
{
    public const string DebtFile = "structure-debt.allow.txt";
    public const string RootNamespace = "Mugirl";
    public const string PublicApiFolder = "PublicApi/";
    public const string UpgradeFolder = "SaveUpgrade/";

    private static readonly string[] ForbiddenSegments = ["UI", "Text", "Log", "Find", "Gen", "Widgets", "Scribe", "AlienRace", "HarmonyLib", "Harmony"];

    private static readonly (string Root, string Parameter)[] Components =
    [
        ("Verse.GameComponent", "Verse.Game"), ("Verse.MapComponent", "Verse.Map"), ("RimWorld.Planet.WorldComponent", "RimWorld.Planet.World"),
    ];

    private static readonly string[] ConfinedPatchTargets =
    [
        "Verse.Scribe", "Verse.ScribeLoader", "Verse.ScribeSaver", "Verse.ScribeExtractor", "Verse.ScribeMetaHeaderUtility", "Verse.BackCompatibility",
        "Verse.Scribe_Values", "Verse.Scribe_Deep", "Verse.Scribe_Collections", "Verse.Scribe_References", "Verse.Scribe_Defs", "Verse.Scribe_TargetInfo",
        "Verse.CrossRefHandler", "Verse.PostLoadIniter", "Verse.LoadedObjectDirectory", "Verse.LoadIDsWantedBank",
        "Verse.Game::LoadGame", "Verse.Game::ExposeSmallComponents",
    ];

    public static void Run(Report report, ValidationOptions options, MetadataWorld world)
    {
        AllowList debt = AllowList.Load(options.BaselineDir, DebtFile);
        report.Run("B.namespaces", "Namespaces: no root-namespace types outside PublicApi, no shadowing segments",
            recorder => CheckNamespaces(recorder, options, world, debt));
        report.Run("B.component-ctors", "Game/Map/World components have the engine constructor and a side-effect-free constructor",
            recorder => CheckComponents(recorder, world, debt));
        report.Run("B.save-confinement", "Patches on save-loading internals live only under SaveUpgrade/",
            recorder => CheckConfinement(recorder, options, world, debt));
    }

    /// <summary>类型的源码文件（相对 Source/MugirlRace），没有 PDB 时为空。</summary>
    public static string? SourceOf(MetadataWorld world, ValidationOptions options, TypeDefinition type)
        => world.Sources.DocumentsOf(type).Select(d => SourceMap.ProjectRelative(d, options.SourceDir)).OfType<string>().Order(StringComparer.Ordinal).FirstOrDefault();

    private static void CheckNamespaces(CheckRecorder recorder, ValidationOptions options, MetadataWorld world, AllowList debt)
    {
        IReadOnlySet<string> publicTypes = PublicApiCheck.TypeNames(options);
        var shadowed = new HashSet<string>(ForbiddenSegments, StringComparer.Ordinal);
        foreach (string ns in TypeResolver.IgnoredNamespaces)
        {
            shadowed.UnionWith(ns.Split('.'));
        }

        // Mod 代码引用的 Verse/RimWorld 类型短名：同名的命名空间段会让这些简单名解析到命名空间。
        var gameTypeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (TypeReference reference in world.Mod.GetTypeReferences())
        {
            if (reference.Namespace.StartsWith("Verse", StringComparison.Ordinal) || reference.Namespace.StartsWith("RimWorld", StringComparison.Ordinal))
            {
                gameTypeNames.Add(reference.Name.Split('`')[0]);
            }
        }

        int rootTypes = 0;
        var namespaces = new SortedSet<string>(StringComparer.Ordinal);
        foreach (TypeDefinition type in world.Mod.Types.Where(t => t.Name != "<Module>" && !CecilNames.IsCompilerGenerated(t)))
        {
            string name = CecilNames.Type(type);
            namespaces.Add(type.Namespace);
            if (type.Namespace.Length == 0)
            {
                Report(recorder, debt, "global: " + name, "Type in the global namespace; use Mugirl.<Area>[.<Feature>].", null);
            }
            else if (type.Namespace == RootNamespace)
            {
                rootTypes++;
                string? source = SourceOf(world, options, type);
                bool allowed = publicTypes.Contains(name) && source is not null && source.StartsWith(PublicApiFolder, StringComparison.Ordinal);
                if (!allowed)
                {
                    Report(recorder, debt, "root: " + name, $"Type in the root namespace {RootNamespace}: only the public alias types listed in tools/RenameApply/specs/public-api.json, "
                        + $"declared under Source/MugirlRace/{PublicApiFolder}, may live there (design §3.1, decisions D4).", source);
                }
            }
        }

        foreach (string ns in namespaces.Where(n => n.StartsWith(RootNamespace + ".", StringComparison.Ordinal)))
        {
            foreach (string segment in ns.Split('.').Skip(1))
            {
                if (shadowed.Contains(segment) || gameTypeNames.Contains(segment))
                {
                    Report(recorder, debt, $"segment: {ns} ({segment})", shadowed.Contains(segment)
                        ? $"Namespace segment '{segment}' shadows a game or library name inside Mugirl code."
                        : $"Namespace segment '{segment}' equals the game type {segment} that Mugirl code uses: the simple name would bind to the namespace.", null);
                }
            }
        }

        debt.ReportProblems(recorder, key => key.StartsWith("global: ", StringComparison.Ordinal) || key.StartsWith("root: ", StringComparison.Ordinal) || key.StartsWith("segment: ", StringComparison.Ordinal));
        recorder.Summary = $"{namespaces.Count} namespace(s), {rootTypes} type(s) in the root namespace";
    }

    private static void CheckComponents(CheckRecorder recorder, MetadataWorld world, AllowList debt)
    {
        int count = 0;
        foreach (TypeDefinition type in CecilNames.AllTypes(world.Mod).Where(t => !t.IsAbstract && !CecilNames.IsCompilerGenerated(t)))
        {
            List<string> bases = CecilNames.BaseTypeNames(type).ToList();
            if (Components.FirstOrDefault(c => bases.Contains(c.Root, StringComparer.Ordinal)) is not { Root: { } root } component)
            {
                continue;
            }

            count++;
            string name = CecilNames.Type(type);
            if (!type.Methods.Any(m => m.IsConstructor && !m.IsStatic && m.IsPublic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == component.Parameter))
            {
                Report(recorder, debt, $"ctor: {name}", $"{root} subclass without a public ({component.Parameter}) constructor: the game creates it with Activator.CreateInstance(type, {component.Parameter.Split('.')[^1].ToLowerInvariant()}).", null);
            }

            foreach (MethodDefinition ctor in type.Methods.Where(m => m.IsConstructor && !m.IsStatic && m.HasBody))
            {
                foreach (string effect in SideEffects(ctor, depth: 0, []))
                {
                    Report(recorder, debt, $"ctor: {name}: {effect}", "Component constructors must be side-effect free (no DefDatabase, Find, Current, static writes): "
                        + "the game constructs them for every game, including when the component is added to an old save, before any data is loaded.", null);
                }
            }
        }

        debt.ReportProblems(recorder, key => key.StartsWith("ctor: ", StringComparison.Ordinal));
        recorder.Summary = $"{count} component type(s) checked";
    }

    // 构造函数（及它调用的本模块方法：call 与 callvirt 的已解析方法、newobj 的构造函数，以及首次访问静态成员时触发的静态构造函数，
    // 三层以内）中的全局状态访问。
    private static IEnumerable<string> SideEffects(MethodDefinition method, int depth, HashSet<MethodDefinition> seen)
    {
        if (!seen.Add(method) || !method.HasBody)
        {
            yield break;
        }

        foreach (Instruction instruction in method.Body.Instructions)
        {
            switch (instruction.Operand)
            {
                case FieldReference field when instruction.OpCode.Code is Code.Stsfld:
                    if (!(method.IsConstructor && method.IsStatic && CecilNames.TryResolve(field.DeclaringType) == method.DeclaringType))
                    {
                        yield return "writes static " + CecilNames.MemberShort(field);
                    }

                    break;
                case MemberReference member when member is MethodReference or FieldReference:
                    TypeReference owner = member.DeclaringType is GenericInstanceType generic ? generic.ElementType : member.DeclaringType;
                    string ownerName = CecilNames.Type(owner);
                    if (ownerName is "Verse.DefDatabase`1" or "Verse.Find" or "Verse.Current")
                    {
                        yield return "uses " + ownerName + "::" + member.Name;
                        break;
                    }

                    if (member is FieldReference && instruction.OpCode.Code is Code.Ldsfld or Code.Ldsflda)
                    {
                        foreach (string nested in StaticConstructorEffects(owner, method, depth, seen))
                        {
                            yield return nested;
                        }
                    }

                    if (member is MethodReference callee && depth < 3 && instruction.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj
                        && CecilNames.TryResolve(callee) is { } target && target.Module == method.Module
                        && (instruction.OpCode.Code == Code.Newobj || !target.IsConstructor))
                    {
                        string via = target.IsConstructor ? "new " + target.DeclaringType.Name : target.Name;
                        IEnumerable<string> cctor = target.IsStatic || instruction.OpCode.Code == Code.Newobj ? StaticConstructorEffects(target.DeclaringType, method, depth, seen) : [];
                        foreach (string nested in SideEffects(target, depth + 1, seen).Concat(cctor))
                        {
                            yield return nested + " (via " + via + ")";
                        }
                    }

                    break;
            }
        }
    }

    // 首次访问本模块类型的静态成员时运行它的静态构造函数。
    private static IEnumerable<string> StaticConstructorEffects(TypeReference type, MethodDefinition from, int depth, HashSet<MethodDefinition> seen)
    {
        if (depth >= 3 || CecilNames.TryResolve(type) is not { } definition || definition.Module != from.Module
            || definition.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic) is not { } cctor)
        {
            return [];
        }

        return SideEffects(cctor, depth + 1, seen).Select(effect => effect + " (via static constructor of " + definition.Name + ")");
    }

    private static void CheckConfinement(CheckRecorder recorder, ValidationOptions options, MetadataWorld world, AllowList debt)
    {
        if (!world.Sources.Available)
        {
            recorder.Warn("(no PDB)", "Without a PDB the source folder of patch classes is unknown; every confined patch is reported.");
        }

        int patches = 0;
        foreach (TypeDefinition type in CecilNames.AllTypes(world.Mod))
        {
            foreach (string target in PatchTargets(type).Distinct(StringComparer.Ordinal))
            {
                if (!ConfinedPatchTargets.Any(confined => target == confined || target.StartsWith(confined + "::", StringComparison.Ordinal)))
                {
                    continue;
                }

                patches++;
                string? source = SourceOf(world, options, type);
                if (source is null || !source.StartsWith(UpgradeFolder, StringComparison.Ordinal))
                {
                    Report(recorder, debt, $"patch: {CecilNames.Type(type)} -> {target}", $"Patches of save-loading internals belong to Source/MugirlRace/{UpgradeFolder} (the only layer that knows legacy save shapes).", source);
                }
            }
        }

        debt.ReportProblems(recorder, key => key.StartsWith("patch: ", StringComparison.Ordinal));
        recorder.Summary = $"{patches} patch(es) of save-loading internals";
    }

    // 补丁目标：类上的 [HarmonyPatch] 特性合并成一个目标，方法上的特性在其上覆盖（Harmony 的规则）；TargetMethod/TargetMethods 与手写的
    // Harmony.Patch 调用按方法体中的 typeof 与字符串字面量推出（X 本身与 X::M 的每种组合）。
    private static IEnumerable<string> PatchTargets(TypeDefinition type)
    {
        (string? classType, string? classMethod) = Merge(type.CustomAttributes, null, null);
        bool methodTargets = false;
        foreach (MethodDefinition method in type.Methods)
        {
            if (method.CustomAttributes.Any(IsHarmonyPatch))
            {
                methodTargets = true;
                (string? declaring, string? name) = Merge(method.CustomAttributes, classType, classMethod);
                if (declaring is not null)
                {
                    yield return name is null ? declaring : declaring + "::" + name;
                }
            }

            bool provider = method.Name is "TargetMethod" or "TargetMethods"
                || method.CustomAttributes.Any(a => a.AttributeType.FullName is "HarmonyLib.HarmonyTargetMethod" or "HarmonyLib.HarmonyTargetMethods");
            if (!method.HasBody || !(provider || method.Body.Instructions.Any(i => i.Operand is MethodReference { Name: "Patch", DeclaringType.FullName: "HarmonyLib.Harmony" })))
            {
                continue;
            }

            List<string> types = method.Body.Instructions.Where(i => i.OpCode.Code == Code.Ldtoken).Select(i => i.Operand).OfType<TypeReference>().Select(CecilNames.Type).Distinct().ToList();
            List<string> names = method.Body.Instructions.Where(i => i.OpCode.Code == Code.Ldstr).Select(i => i.Operand).OfType<string>().Distinct().ToList();
            foreach (string target in types)
            {
                yield return target;
                foreach (string name in names)
                {
                    yield return target + "::" + name;
                }
            }
        }

        if (!methodTargets && classType is not null)
        {
            yield return classMethod is null ? classType : classType + "::" + classMethod;
        }
    }

    private static bool IsHarmonyPatch(CustomAttribute attribute) => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch";

    private static (string? Type, string? Method) Merge(IEnumerable<CustomAttribute> attributes, string? type, string? method)
    {
        foreach (CustomAttribute attribute in attributes.Where(IsHarmonyPatch))
        {
            foreach (CustomAttributeArgument argument in attribute.ConstructorArguments)
            {
                switch (argument.Value)
                {
                    case TypeReference target:
                        type = CecilNames.Type(target);
                        break;
                    case string name:
                        method = name;
                        break;
                }
            }
        }

        return (type, method);
    }

    private static void Report(CheckRecorder recorder, AllowList debt, string key, string message, string? location)
    {
        if (!debt.Allows(key))
        {
            recorder.Fail(key, message, location);
        }
    }
}
