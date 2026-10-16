using MugirlRace.Validation.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Legacy;

/// <summary>
/// 一个模块写进存档的 Scribe 标签，按所属对象（owner）分组：标签、Scribe 种类、值类型、LookMode、默认值、forceSave 与被存档的字段。
/// 旧构建（语料提取）与新构建（<c>C.save-upgrade</c>、<c>B.scribe-defaults</c>）用同一个分析，结果可以逐行比较。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>owner：提供标签字面量的方法所在的手写类型；不是 ExposeData 一类方法时，沿本模块调用图向上找到调用它的 ExposeData（共享的存档辅助方法）。</item>
/// <item>标签来自 <c>this</c> 的虚属性或字段时（<c>SaveKey + "_tick"</c>、抽象的标签属性），按每个具体子类求值：getter 体中的字面量，
/// 加上它读取的字符串字段的构造函数常量与 XML 中写的值；所得行的 owner 是该子类。</item>
/// <item>默认值按 Scribe 写出的文本记录（<c>0.8</c>、<c>True</c>、枚举成员名、<c>default</c>）；取自 Def/Props 的为 <c>def-dependent</c>，
/// 其他非常量为 <c>dynamic</c>。</item>
/// </list>
/// </remarks>
internal sealed class ScribeSchema
{
    private const int MaxOwnerDepth = 6;
    private const int MaxTemplateValues = 32;

    private static readonly string[] PropsRoots =
    [
        "Verse.Def", "Verse.CompProperties", "Verse.HediffCompProperties", "RimWorld.AbilityCompProperties", "Verse.DefModExtension",
    ];

    private readonly ModuleStringFlow flow;
    private readonly IFieldValueSource? xml;
    private readonly List<LabelFact> labels = [];
    private readonly List<string> dynamicSites = [];
    private readonly SortedSet<string> dynamicOwners = new(StringComparer.Ordinal);
    private readonly SortedSet<string> savedEnums = new(StringComparer.Ordinal);
    private readonly Dictionary<MethodDefinition, IReadOnlyList<TypeDefinition>> ownerCache = [];

    private ScribeSchema(ModuleStringFlow flow, IFieldValueSource? xml)
    {
        this.flow = flow;
        this.xml = xml;
    }

    /// <summary>每个（owner、标签、写法）一行；同一标签被多处以不同写法保存时有多行。</summary>
    public IReadOnlyList<LabelFact> Labels => labels;

    /// <summary>标签无法求值的 Scribe 调用点（<c>Type::Method+IL_xxxx</c>）。</summary>
    public IReadOnlyList<string> DynamicSites => dynamicSites;

    /// <summary>有无法求值的标签的 owner（存档中它的元素下可能有分析看不到的本 Mod 标签）。</summary>
    public IReadOnlySet<string> DynamicOwners => dynamicOwners;

    /// <summary>本模块中作为（或组成）Scribe 值类型的枚举。</summary>
    public IReadOnlySet<string> SavedEnums => savedEnums;

    public static ScribeSchema Analyze(ModuleStringFlow flow, IFieldValueSource? xml)
    {
        var schema = new ScribeSchema(flow, xml);
        schema.Run();
        return schema;
    }

    /// <summary><c>Verse.Scribe_*.Look</c> 或 <c>Verse.Scribe.EnterNode</c>。</summary>
    public static bool IsScribeCall(MethodReference callee)
    {
        TypeReference owner = callee.DeclaringType;
        return owner.Namespace == "Verse"
            && ((owner.Name.StartsWith("Scribe_", StringComparison.Ordinal) && callee.Name == "Look")
                || (owner.Name == "Scribe" && callee.Name == "EnterNode"));
    }

    /// <summary>ExposeData、PostExposeData、CompExposeData 一类实例方法（含其中的 lambda）。</summary>
    public static bool IsExposeMethod(MethodDefinition method)
        => !method.IsStatic && (CecilNames.UserMethodName(method).Contains("Expose", StringComparison.Ordinal) || method.Name.EndsWith(".ExposeData", StringComparison.Ordinal));

    /// <summary>值类型的可读名：<c>System.Collections.Generic.List&lt;Mugirl.Features.Corporate.CorporateOrder&gt;</c>。</summary>
    public static string Render(TypeReference type, GenericInstanceMethod? method) => type switch
    {
        ByReferenceType reference => Render(reference.ElementType, method),
        GenericParameter { Type: GenericParameterType.Method } parameter when method is not null && parameter.Position < method.GenericArguments.Count
            => Render(method.GenericArguments[parameter.Position], null),
        GenericInstanceType generic => StripArity(CecilNames.Type(generic.ElementType)) + "<" + string.Join(",", generic.GenericArguments.Select(a => Render(a, method))) + ">",
        ArrayType array => Render(array.ElementType, method) + "[]",
        _ => CecilNames.Type(type),
    };

    private static string StripArity(string name) => name.IndexOf('`', StringComparison.Ordinal) is var tick and >= 0 ? name[..tick] : name;

    private void Run()
    {
        foreach (MethodStringFlow method in flow.Methods)
        {
            foreach (StringCall call in method.Calls.Where(call => IsScribeCall(call.Callee)))
            {
                Instruction? instruction = method.Method.Body.Instructions.FirstOrDefault(i => i.Offset == call.Offset);
                if (instruction is null)
                {
                    continue;
                }

                SiteShape shape = Describe(method.Method, instruction, call.Callee);
                StringSet label = call.FirstStringArgument ?? StringSet.Unknown;
                foreach ((string location, MethodDefinition supplier, StringSet value) in flow.Expand(call.Location, method.Method, label))
                {
                    foreach (TypeDefinition owner in OwnersOf(supplier))
                    {
                        AddLabels(owner, value, shape, location);
                    }
                }
            }
        }
    }

    private void AddLabels(TypeDefinition owner, StringSet value, SiteShape shape, string location)
    {
        if (value.Values is { } literals && !value.IsType)
        {
            foreach (string literal in literals)
            {
                labels.Add(shape.ToFact(CecilNames.Type(owner), literal, location));
            }

            return;
        }

        if (value.IsTemplate)
        {
            bool any = false;
            foreach (TypeDefinition concrete in ConcreteSelfAndSubtypes(owner))
            {
                foreach (string literal in EvaluateTemplate(value, concrete))
                {
                    any = true;
                    labels.Add(shape.ToFact(CecilNames.Type(concrete), literal, location));
                }
            }

            if (any)
            {
                return;
            }
        }

        dynamicSites.Add(location);
        dynamicOwners.Add(CecilNames.Type(owner));
    }

    // 标签字面量所在方法的 owner：ExposeData 一类方法的类型，或者经调用图找到的、调用它的那些 ExposeData 的类型。
    private IReadOnlyList<TypeDefinition> OwnersOf(MethodDefinition supplier)
    {
        if (ownerCache.TryGetValue(supplier, out IReadOnlyList<TypeDefinition>? cached))
        {
            return cached;
        }

        var owners = new List<TypeDefinition>();
        var seen = new HashSet<MethodDefinition>();
        var pending = new Queue<(MethodDefinition Method, int Depth)>();
        pending.Enqueue((supplier, 0));
        while (pending.Count > 0)
        {
            (MethodDefinition current, int depth) = pending.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            if (IsExposeMethod(current))
            {
                TypeDefinition type = CecilNames.UserType(current.DeclaringType);
                if (!owners.Contains(type))
                {
                    owners.Add(type);
                }

                continue;
            }

            if (depth >= MaxOwnerDepth)
            {
                continue;
            }

            foreach (StringCall caller in flow.CallersOf(current))
            {
                pending.Enqueue((caller.Caller, depth + 1));
            }
        }

        if (owners.Count == 0)
        {
            owners.Add(CecilNames.UserType(supplier.DeclaringType));
        }

        IReadOnlyList<TypeDefinition> result = owners.OrderBy(CecilNames.Type, StringComparer.Ordinal).ToList();
        ownerCache[supplier] = result;
        return result;
    }

    private IEnumerable<TypeDefinition> ConcreteSelfAndSubtypes(TypeDefinition owner)
    {
        foreach (TypeDefinition type in CecilNames.AllTypes(flow.Module).Where(t => !t.IsAbstract && !t.IsInterface && !CecilNames.IsCompilerGenerated(t)))
        {
            for (TypeDefinition? current = type; current is not null && current.Module == flow.Module; current = CecilNames.TryResolve(current.BaseType))
            {
                if (current == owner)
                {
                    yield return type;
                    break;
                }
            }
        }
    }

    private List<string> EvaluateTemplate(StringSet value, TypeDefinition concrete)
    {
        var results = new List<string> { "" };
        foreach (string part in value.Template)
        {
            IReadOnlyCollection<string> options = part.StartsWith(StringSet.HoleMarker)
                ? HoleValues(concrete, part[1..], depth: 0)
                : [part];
            if (options.Count == 0)
            {
                return [];
            }

            results = results.SelectMany(prefix => options.Select(option => prefix + option)).Distinct(StringComparer.Ordinal).ToList();
            if (results.Count > MaxTemplateValues)
            {
                return [];
            }
        }

        return results;
    }

    // call:Owner::get_X 或 field:Owner::name 在具体类型 concrete 上的可能取值。
    private SortedSet<string> HoleValues(TypeDefinition concrete, string hole, int depth)
    {
        var values = new SortedSet<string>(StringComparer.Ordinal);
        int separator = hole.IndexOf(':', StringComparison.Ordinal);
        string kind = hole[..separator];
        string member = hole[(separator + 1)..];
        string name = member[(member.LastIndexOf("::", StringComparison.Ordinal) + 2)..];
        if (kind == "field")
        {
            values.UnionWith(FieldValues(concrete, name));
            return values;
        }

        if (depth > 3 || FindImplementation(concrete, name) is not { HasBody: true } getter)
        {
            return values;
        }

        foreach (Instruction instruction in getter.Body.Instructions)
        {
            switch (instruction.Operand)
            {
                case string literal when instruction.OpCode.Code == Code.Ldstr:
                    values.Add(literal);
                    break;
                case FieldReference field when instruction.OpCode.Code is Code.Ldfld or Code.Ldsfld && field.FieldType.FullName == "System.String":
                    if (CecilNames.TryResolve(field.DeclaringType) is { } declaring)
                    {
                        values.UnionWith(FieldValues(declaring, field.Name));
                    }

                    break;
                case MethodReference callee when instruction.OpCode.Code is Code.Call or Code.Callvirt && callee.HasThis && callee.Parameters.Count == 0
                    && callee.ReturnType.FullName == "System.String" && instruction.Previous?.OpCode.Code == Code.Ldarg_0:
                    values.UnionWith(HoleValues(concrete, "call:" + CecilNames.MemberShort(callee), depth + 1));
                    break;
            }
        }

        return values;
    }

    // 字符串字段的取值：声明类型（及其子类型）构造函数中的字面量赋值，加上 XML 中为该类写的值。
    private IEnumerable<string> FieldValues(TypeDefinition type, string field)
    {
        for (TypeDefinition? current = type; current is not null; current = CecilNames.TryResolve(current.BaseType))
        {
            foreach (MethodDefinition constructor in current.Methods.Where(m => m.IsConstructor && !m.IsStatic && m.HasBody))
            {
                foreach (Instruction instruction in constructor.Body.Instructions)
                {
                    if (instruction.OpCode.Code == Code.Stfld && instruction.Operand is FieldReference stored && stored.Name == field
                        && instruction.Previous is { OpCode.Code: Code.Ldstr, Operand: string literal })
                    {
                        yield return literal;
                    }
                }
            }

            if (xml is not null)
            {
                foreach (string value in xml.ValuesOf(CecilNames.Type(current), field))
                {
                    yield return value;
                }
            }

            if (current.Module != flow.Module)
            {
                yield break;
            }
        }
    }

    private static MethodDefinition? FindImplementation(TypeDefinition type, string name)
    {
        for (TypeDefinition? current = type; current is not null; current = CecilNames.TryResolve(current.BaseType))
        {
            if (current.Methods.FirstOrDefault(m => m.Name == name && m.Parameters.Count == 0 && !m.IsAbstract) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private SiteShape Describe(MethodDefinition method, Instruction call, MethodReference callee)
    {
        string ownerName = callee.DeclaringType.Name;
        string kind = ownerName == "Scribe" ? "EnterNode" : ownerName["Scribe_".Length..];
        var generic = callee as GenericInstanceMethod;
        MethodReference element = generic?.ElementMethod ?? callee;
        int offset = element.HasThis ? 1 : 0;
        string valueType = "-";
        TypeReference? valueTypeReference = null;
        if (kind != "EnterNode" && element.Parameters.Count > 0)
        {
            TypeReference first = element.Parameters[0].ParameterType;
            valueType = Render(first, generic);
            valueTypeReference = generic is { GenericArguments.Count: 1 } single ? single.GenericArguments[0] : null;
            foreach (TypeReference component in generic?.GenericArguments.SelectMany(CecilNames.Components) ?? [])
            {
                if (component is TypeDefinition { IsEnum: true } enumType && enumType.Module == flow.Module)
                {
                    savedEnums.Add(CecilNames.Type(enumType));
                }
            }
        }

        string lookMode = kind switch
        {
            "Values" => "Value",
            "Defs" => "Def",
            "References" => "Reference",
            "Deep" => "Deep",
            "TargetInfo" => "TargetInfo",
            "BodyParts" => "BodyPart",
            "EnterNode" => "-",
            _ => "",
        };
        if (kind == "Collections")
        {
            var modes = new List<string>();
            int index = 0;
            for (int i = 0; i < element.Parameters.Count; i++)
            {
                if (element.Parameters[i].ParameterType.FullName != "Verse.LookMode")
                {
                    continue;
                }

                TypeReference? argument = generic is null ? null : generic.GenericArguments[Math.Min(index, generic.GenericArguments.Count - 1)];
                index++;
                Instruction? producer = IlArguments.Producer(method, call, offset + i);
                int? mode = producer is null ? null : IlArguments.IntConstant(producer);
                modes.Add(mode switch
                {
                    null => "dynamic",
                    0 => "Undefined=>" + EffectiveLookMode(argument),
                    _ => LookModeName(mode.Value),
                });
            }

            lookMode = modes.Count == 0 ? "-" : string.Join(",", modes);
        }

        string defaultValue = "-";
        string forceSave = "-";
        if (kind == "Values")
        {
            // 成员引用的签名不含参数名：按解析到的定义取名字，读不到时按 Look(ref T, string, T, bool) 的位置。
            MethodReference named = CecilNames.TryResolve(element) ?? element;
            int defaultIndex = IndexOf(named, "defaultValue") is var d and >= 0 ? d : element.Parameters.Count >= 3 ? 2 : -1;
            int forceIndex = IndexOf(named, "forceSave") is var f and >= 0 ? f : element.Parameters.Count >= 4 ? 3 : -1;
            if (defaultIndex >= 0)
            {
                Instruction? producer = IlArguments.Producer(method, call, offset + defaultIndex);
                defaultValue = IlArguments.Constant(method, producer, valueTypeReference) ?? (IsDefDependent(producer) ? "def-dependent" : "dynamic");
            }

            if (forceIndex >= 0)
            {
                Instruction? producer = IlArguments.Producer(method, call, offset + forceIndex);
                forceSave = producer is not null && IlArguments.IntConstant(producer) is int flag ? (flag != 0 ? "True" : "False") : "dynamic";
            }
        }

        string? field = null;
        if (kind != "EnterNode" && IlArguments.Producer(method, call, offset) is { } target)
        {
            field = target.OpCode.Code switch
            {
                Code.Ldflda when target.Operand is FieldReference f => CecilNames.MemberShort(f),
                Code.Ldsflda when target.Operand is FieldReference f => "static " + CecilNames.MemberShort(f),
                _ => null,
            };
        }

        return new SiteShape(kind, valueType, lookMode, defaultValue, forceSave, field);
    }

    private static int IndexOf(MethodReference method, string parameter)
    {
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            if (method.Parameters[i].Name == parameter)
            {
                return i;
            }
        }

        return -1;
    }

    private static string LookModeName(int value) => value switch
    {
        1 => "Value",
        2 => "Deep",
        3 => "Reference",
        4 => "Def",
        5 => "LocalTargetInfo",
        6 => "TargetInfo",
        7 => "GlobalTargetInfo",
        8 => "BodyPart",
        _ => "LookMode(" + value + ")",
    };

    // Scribe_Universal.TryResolveLookMode 的推断：可解析的值类型 → Value，Def → Def，IExposable → Deep。
    private static string EffectiveLookMode(TypeReference? type)
    {
        if (type is null)
        {
            return "?";
        }

        if (type.IsPrimitive || type.FullName is "System.String" or "System.Type" || CecilNames.TryResolve(type) is { IsEnum: true })
        {
            return "Value";
        }

        TypeDefinition? definition = CecilNames.TryResolve(type is GenericInstanceType generic ? generic.ElementType : type);
        if (definition is null)
        {
            return "?";
        }

        if (CecilNames.BaseTypeNames(definition).Contains("Verse.Def", StringComparer.Ordinal))
        {
            return "Def";
        }

        for (TypeDefinition? current = definition; current is not null; current = CecilNames.TryResolve(current.BaseType))
        {
            if (current.Interfaces.Any(i => i.InterfaceType.FullName == "Verse.IExposable"))
            {
                return "Deep";
            }
        }

        return definition.IsValueType ? "Value" : "?";
    }

    private static bool IsDefDependent(Instruction? producer)
    {
        TypeReference? declaring = producer?.Operand switch
        {
            FieldReference field => field.DeclaringType,
            MethodReference method => method.DeclaringType,
            _ => null,
        };
        return CecilNames.TryResolve(declaring) is { } type
            && CecilNames.BaseTypeNames(type).Prepend(CecilNames.Type(type)).Any(name => PropsRoots.Contains(name, StringComparer.Ordinal));
    }

    private sealed record SiteShape(string Kind, string ValueType, string LookMode, string Default, string ForceSave, string? Field)
    {
        public LabelFact ToFact(string owner, string label, string location)
            => new(owner, label, Kind, ValueType, LookMode, Default, ForceSave, Field, location);
    }
}
