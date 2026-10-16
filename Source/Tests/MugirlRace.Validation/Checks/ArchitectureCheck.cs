using System.Text.Json;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 项目架构约束（根 README“代码与内容约束”、ADR-003），按编译结果检查，不依赖源码写法：
/// <list type="bullet">
/// <item><c>B.architecture</c>：<c>Baselines/architecture-rules.json</c> 中的每条规则限定某些游戏 API、字段或字符串字面量只能出现在
/// 指定类型或源码目录中（例如 <c>Verse.Log</c> 只由 <c>MugirlLog</c> 调用、<c>AccessTools.TypeByName</c> 只在 <c>Compatibility/</c>）。</item>
/// <item><c>B.base-calls</c>：重写 <c>Baselines/base-calls.json</c> 列出的生命周期方法时必须调用被重写的基类实现。</item>
/// <item><c>B.empty-types</c>：没有任何成员的类型只能是 <c>Baselines/empty-types.allow.txt</c> 中写明理由的 XML 标记类型。</item>
/// </list>
/// </summary>
internal static class ArchitectureCheck
{
    public const string RulesFile = "architecture-rules.json";
    public const string BaseCallsFile = "base-calls.json";
    public const string EmptyTypesFile = "empty-types.allow.txt";

    public static void Run(Report report, ValidationOptions options, MetadataWorld world)
    {
        report.Run("B.architecture", "Confined game APIs and literals appear only where the architecture allows them", recorder => CheckRules(recorder, options, world));
        report.Run("B.base-calls", "Overrides of lifecycle methods call the base implementation", recorder => CheckBaseCalls(recorder, options, world.Mod));
        report.Run("B.empty-types", "Types without members are reviewed XML marker types", recorder => CheckEmptyTypes(recorder, options, world.Mod));
    }

    private static void CheckRules(CheckRecorder recorder, ValidationOptions options, MetadataWorld world)
    {
        using JsonDocument? document = JsonBaseline.Load(recorder, options.BaselineDir, RulesFile);
        if (document is null)
        {
            return;
        }

        List<Rule> rules = ReadRules(recorder, document.RootElement);
        if (rules.Any(rule => rule.AllowedSources.Count > 0) && !world.Sources.Available)
        {
            recorder.Warn("(no PDB)", "The assembly has no portable PDB next to it: rules that allow source folders cannot see where code was written, so those sites are reported as violations.");
        }

        var known = new HashSet<string>(CecilNames.AllTypes(world.Mod).Select(CecilNames.Type), StringComparer.Ordinal);
        foreach (Rule rule in rules)
        {
            foreach (string type in rule.AllowedTypes.Where(type => !known.Contains(type)))
            {
                recorder.Warn($"{rule.Id}: allowed type {type}", $"Baselines/{RulesFile} allows a type the assembly does not define; update the rule.");
            }
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        var hits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TypeDefinition type in CecilNames.AllTypes(world.Mod))
        {
            foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody))
            {
                TypeDefinition user = CecilNames.UserType(type);
                string where = CecilNames.Type(user) + "::" + CecilNames.UserMethodName(method);
                string? source = world.Sources.DocumentOf(method);
                string? relative = source is null ? null : SourceMap.ProjectRelative(source, options.SourceDir);
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    foreach (Rule rule in rules)
                    {
                        if (Match(rule, instruction) is not { } matched)
                        {
                            continue;
                        }

                        hits[rule.Id] = hits.GetValueOrDefault(rule.Id) + 1;
                        if (IsAllowed(rule, user, relative) || !reported.Add(rule.Id + "\t" + matched + "\t" + where))
                        {
                            continue;
                        }

                        string allowed = rule.AllowedTypes.Count + rule.AllowedSources.Count == 0
                            ? "It is not allowed anywhere."
                            : "Allowed only in " + string.Join(", ", rule.AllowedTypes.Concat(rule.AllowedSources.Select(s => "Source/MugirlRace/" + s))) + ".";
                        recorder.Fail($"{rule.Id}: {matched} @ {where}", $"{rule.Reason} {allowed}", relative ?? source);
                    }
                }
            }
        }

        recorder.Summary = $"{rules.Count} rules; " + string.Join(", ", rules.Select(rule => $"{rule.Id} {hits.GetValueOrDefault(rule.Id)} site(s)"));
    }

    // 指令匹配规则时返回被匹配的成员或字面量的可读名。
    private static string? Match(Rule rule, Instruction instruction)
    {
        switch (instruction.Operand)
        {
            case string literal when instruction.OpCode.Code == Code.Ldstr && rule.Literals.Contains(literal, StringComparer.Ordinal):
                return "\"" + literal + "\"";
            case MemberReference member when member is MethodReference or FieldReference:
                TypeReference owner = member.DeclaringType is GenericInstanceType generic ? generic.ElementType : member.DeclaringType;
                string ownerName = CecilNames.Type(owner);
                foreach (string pattern in rule.Members)
                {
                    int separator = pattern.IndexOf("::", StringComparison.Ordinal);
                    string patternOwner = pattern[..separator];
                    string patternName = pattern[(separator + 2)..];
                    bool nameMatches = patternName.EndsWith('*')
                        ? member.Name.StartsWith(patternName[..^1], StringComparison.Ordinal)
                        : member.Name == patternName;
                    if (nameMatches && ownerName == patternOwner)
                    {
                        return ownerName + "::" + member.Name;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private static bool IsAllowed(Rule rule, TypeDefinition user, string? relativeSource)
    {
        for (TypeDefinition? current = user; current is not null; current = current.DeclaringType)
        {
            if (rule.AllowedTypes.Contains(CecilNames.Type(current), StringComparer.Ordinal))
            {
                return true;
            }
        }

        return relativeSource is not null && rule.AllowedSources.Any(prefix => relativeSource.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static List<Rule> ReadRules(CheckRecorder recorder, JsonElement root)
    {
        var rules = new List<Rule>();
        if (!root.TryGetProperty("rules", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            recorder.Fail("Baselines/" + RulesFile, "Expected an object \"rules\": { id: { reason, members, literals, allowedTypes, allowedSources } }.");
            return rules;
        }

        foreach (JsonProperty entry in section.EnumerateObject())
        {
            string reason = JsonBaseline.String(entry.Value, "reason")?.Trim() ?? "";
            List<string> members = JsonBaseline.Strings(entry.Value, "members");
            List<string> literals = JsonBaseline.Strings(entry.Value, "literals");
            if (reason.Length == 0)
            {
                recorder.Fail($"Baselines/{RulesFile}: {entry.Name}", "Rule without a reason.");
            }

            foreach (string member in members.Where(member => !member.Contains("::", StringComparison.Ordinal)))
            {
                recorder.Fail($"Baselines/{RulesFile}: {entry.Name}", $"Member pattern '{member}' must be Owner::Name (Name may end in *).");
            }

            if (members.Count + literals.Count == 0)
            {
                recorder.Fail($"Baselines/{RulesFile}: {entry.Name}", "Rule matches nothing (no members or literals).");
            }

            rules.Add(new Rule(entry.Name, reason, members.Where(m => m.Contains("::", StringComparison.Ordinal)).ToList(), literals,
                JsonBaseline.Strings(entry.Value, "allowedTypes"), JsonBaseline.Strings(entry.Value, "allowedSources")));
        }

        return rules;
    }

    private static void CheckBaseCalls(CheckRecorder recorder, ValidationOptions options, ModuleDefinition module)
    {
        using JsonDocument? document = JsonBaseline.Load(recorder, options.BaselineDir, BaseCallsFile);
        if (document is null)
        {
            return;
        }

        Dictionary<string, string> methods = document.RootElement.TryGetProperty("methods", out JsonElement section)
            ? JsonBaseline.ReasonMap(recorder, BaseCallsFile, section, "methods.")
            : [];
        Dictionary<string, string> exceptions = document.RootElement.TryGetProperty("exceptions", out JsonElement allowed)
            ? JsonBaseline.ReasonMap(recorder, BaseCallsFile, allowed, "exceptions.")
            : [];
        var usedExceptions = new HashSet<string>(StringComparer.Ordinal);
        int checkedOverrides = 0;
        int unresolved = 0;
        foreach (TypeDefinition type in CecilNames.AllTypes(module).Where(t => !CecilNames.IsCompilerGenerated(t)))
        {
            foreach (MethodDefinition method in type.Methods.Where(m => m.IsVirtual && !m.IsNewSlot && !m.IsAbstract && m.HasBody && methods.ContainsKey(m.Name)))
            {
                MethodDefinition? overridden = Overridden(type, method, out bool resolved);
                if (!resolved)
                {
                    unresolved++;
                    continue;
                }

                if (overridden is null || overridden.IsAbstract)
                {
                    continue;
                }

                checkedOverrides++;
                string key = CecilNames.Type(type) + "::" + method.Name;
                if (CallsBase(type, method))
                {
                    continue;
                }

                if (exceptions.ContainsKey(key))
                {
                    usedExceptions.Add(key);
                    continue;
                }

                recorder.Fail(key, $"Overrides {CecilNames.Type(overridden.DeclaringType)}.{method.Name} without calling base.{method.Name}(): {methods[method.Name]} "
                    + $"Call the base method, or list \"{key}\" with the reason under exceptions in Baselines/{BaseCallsFile}.");
            }
        }

        foreach (string stale in exceptions.Keys.Where(key => !usedExceptions.Contains(key)).Order(StringComparer.Ordinal))
        {
            recorder.Fail($"Baselines/{BaseCallsFile}: {stale}", "Stale exception: the override no longer exists or now calls the base method. Remove it.");
        }

        if (unresolved > 0)
        {
            recorder.Info("(unresolved)", $"{unresolved} override(s) whose base type could not be read were not checked.");
        }

        recorder.Summary = $"{checkedOverrides} override(s) of {methods.Count} lifecycle method name(s) checked";
    }

    // 同名、同参数个数且参数类型名一致的最近基类虚方法。resolved 为 false 表示基类程序集读不到。
    private static MethodDefinition? Overridden(TypeDefinition type, MethodDefinition method, out bool resolved)
    {
        resolved = true;
        for (TypeReference? current = type.BaseType; current is not null;)
        {
            TypeDefinition? definition = CecilNames.TryResolve(current);
            if (definition is null)
            {
                resolved = false;
                return null;
            }

            MethodDefinition? candidate = definition.Methods.FirstOrDefault(m => m.IsVirtual && m.Name == method.Name
                && m.Parameters.Count == method.Parameters.Count
                && m.Parameters.Select(p => p.ParameterType.Name).SequenceEqual(method.Parameters.Select(p => p.ParameterType.Name), StringComparer.Ordinal));
            if (candidate is not null)
            {
                return candidate;
            }

            current = definition.BaseType;
        }

        return null;
    }

    // base.X(...) 编译为对基类方法的 call（非 callvirt）；lambda 中的 base 调用经编译器生成的 <>n__N 辅助方法转发。
    private static bool CallsBase(TypeDefinition type, MethodDefinition method)
    {
        foreach (Instruction instruction in method.Body.Instructions.Where(i => i.OpCode.Code == Code.Call))
        {
            if (instruction.Operand is not MethodReference callee)
            {
                continue;
            }

            if (callee.Name == method.Name && callee.Parameters.Count == method.Parameters.Count && IsStrictBase(type, callee.DeclaringType))
            {
                return true;
            }

            if (callee.Name.StartsWith("<>n__", StringComparison.Ordinal) && CecilNames.TryResolve(callee) is { HasBody: true } helper && helper.DeclaringType == type
                && helper.Body.Instructions.Any(i => i.OpCode.Code == Code.Call && i.Operand is MethodReference inner && inner.Name == method.Name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsStrictBase(TypeDefinition type, TypeReference candidate)
    {
        string name = CecilNames.Type(candidate is GenericInstanceType generic ? generic.ElementType : candidate);
        return CecilNames.BaseTypeNames(type).Contains(name, StringComparer.Ordinal);
    }

    private static void CheckEmptyTypes(CheckRecorder recorder, ValidationOptions options, ModuleDefinition module)
    {
        AllowList allowed = AllowList.Load(options.BaselineDir, EmptyTypesFile);
        int empty = 0;
        foreach (TypeDefinition type in CecilNames.AllTypes(module).Where(IsEmpty))
        {
            empty++;
            string name = CecilNames.Type(type);
            if (!allowed.Allows(name))
            {
                recorder.Fail(name, $"Declares no members. Remove it, or list it in Baselines/{EmptyTypesFile} with the reason it must exist (for example an XML Class= or thingClass marker).");
            }
        }

        allowed.ReportProblems(recorder);
        recorder.Summary = $"{empty} empty type(s), all reviewed";
    }

    private static bool IsEmpty(TypeDefinition type)
    {
        if (type.Name == "<Module>" || CecilNames.IsCompilerGenerated(type) || type.IsEnum
            || type.BaseType?.FullName == "System.MulticastDelegate" || type.CustomAttributes.Any(a => a.AttributeType.Name == "CompilerGeneratedAttribute"))
        {
            return false;
        }

        if (type.HasFields || type.HasProperties || type.HasEvents || type.HasNestedTypes)
        {
            return false;
        }

        return type.Methods.All(IsTrivialConstructor);
    }

    // 编译器补出的默认构造函数：ldarg.0; call 基类 .ctor(); ret（Debug 构建可能夹 nop）。
    private static bool IsTrivialConstructor(MethodDefinition method)
        => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 0 && method.HasBody
            && method.Body.Instructions.Where(i => i.OpCode.Code != Code.Nop).Select(i => i.OpCode.Code).SequenceEqual([Code.Ldarg_0, Code.Call, Code.Ret]);

    private sealed record Rule(string Id, string Reason, IReadOnlyList<string> Members, IReadOnlyList<string> Literals,
        IReadOnlyList<string> AllowedTypes, IReadOnlyList<string> AllowedSources);
}
