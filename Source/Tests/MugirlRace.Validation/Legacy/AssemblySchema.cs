using MugirlRace.Validation.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Legacy;

/// <summary>
/// 一个 Mod 程序集（旧构建或新构建）写进存档的全部名字：类型与其存档根、按 owner 的 Scribe 标签、存档图、存档枚举、
/// <c>GetUniqueLoadID</c> 前缀、写进存档字符串字段的字面量、设置类的标签、JobDriver/LordJob 类型。
/// </summary>
internal sealed class AssemblySchema
{
    private AssemblySchema(ModuleDefinition module, ModuleStringFlow flow, ScribeSchema scribe, SaveRoots roots)
    {
        Module = module;
        Flow = flow;
        Scribe = scribe;
        Roots = roots;
    }

    public ModuleDefinition Module { get; }

    public ModuleStringFlow Flow { get; }

    public ScribeSchema Scribe { get; }

    /// <summary>游戏与本模块合起来的存档根。</summary>
    public SaveRoots Roots { get; }

    /// <summary>（类型，种类，存档根，本模块中的直接基类，游戏基类，标志）。</summary>
    public List<string[]> Types { get; } = [];

    /// <summary>（owner，标签，Scribe 种类，值类型，LookMode，默认值，forceSave）。</summary>
    public List<string[]> Labels { get; } = [];

    /// <summary>（owner，标签，元素类型）：Deep 保存的本 Mod 类型在存档树中的位置。</summary>
    public List<string[]> Graph { get; } = [];

    /// <summary>（枚举，成员，值）。</summary>
    public List<string[]> Enums { get; } = [];

    /// <summary>（类型，GetUniqueLoadID 中的字面量，按出现顺序以 + 连接）。</summary>
    public List<string[]> LoadIds { get; } = [];

    /// <summary>（字段 Owner::name，JSON 引号字面量，literal|keyed）。</summary>
    public List<string[]> SavedValues { get; } = [];

    /// <summary>（设置类，标签，值类型，默认值）。</summary>
    public List<string[]> Settings { get; } = [];

    /// <summary>（JobDriver|LordJob，类型，abstract|concrete）。</summary>
    public List<string[]> JobsAndLords { get; } = [];

    /// <summary>每个类型自身与本模块中的祖先（由近到远）：存档观测标签按它找写标签的 owner。</summary>
    public Dictionary<string, List<string>> Chains { get; } = new(StringComparer.Ordinal);

    /// <summary>有无法求值的 Scribe 标签的 owner（<see cref="ScribeSchema.DynamicOwners"/>）。</summary>
    public HashSet<string> DynamicOwners { get; } = new(StringComparer.Ordinal);

    /// <param name="module">被分析的程序集。</param>
    /// <param name="gameRoots">该游戏版本 Assembly-CSharp 的存档根（<see cref="TypeUniverse.GameRoots"/>）。</param>
    /// <param name="xml">同一构建的 XML 字段值（标签求值用），没有内容时为空。</param>
    /// <param name="keyedKeys">同一构建的 Keyed 键（标记存档中的翻译键），没有内容时为空。</param>
    public static AssemblySchema Analyze(ModuleDefinition module, SaveRoots gameRoots, IFieldValueSource? xml, ISet<string>? keyedKeys)
        => Analyze(new ModuleStringFlow(module), gameRoots, xml, keyedKeys);

    /// <summary>同上，复用已经算好的字符串流分析。</summary>
    public static AssemblySchema Analyze(ModuleStringFlow flow, SaveRoots gameRoots, IFieldValueSource? xml, ISet<string>? keyedKeys)
    {
        ModuleDefinition module = flow.Module;
        ScribeSchema scribe = ScribeSchema.Analyze(flow, xml);
        SaveRoots roots = gameRoots.With(module);
        var schema = new AssemblySchema(module, flow, scribe, roots);
        schema.Collect(keyedKeys ?? new HashSet<string>(StringComparer.Ordinal));
        return schema;
    }

    /// <summary>类型的种类：class、struct、enum、interface 或 delegate。</summary>
    public static string KindOf(TypeDefinition type)
        => type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct"
            : type.BaseType?.FullName == "System.MulticastDelegate" ? "delegate" : "class";

    /// <summary>第一个定义在本模块之外的基类（游戏基类）；没有时为 <c>-</c>。</summary>
    public static string GameBase(TypeDefinition type)
    {
        for (TypeReference? current = type.BaseType; current is not null;)
        {
            TypeReference element = current is GenericInstanceType generic ? generic.ElementType : current;
            if (CecilNames.TryResolve(element) is not { } definition || definition.Module != type.Module)
            {
                return CecilNames.Type(element);
            }

            current = definition.BaseType;
        }

        return "-";
    }

    private void Collect(ISet<string> keyedKeys)
    {
        foreach (TypeDefinition type in CecilNames.AllTypes(Module).Where(t => !CecilNames.IsCompilerGenerated(t) && t.Name != "<Module>"))
        {
            string name = CecilNames.Type(type);
            var flags = new List<string>();
            if (type.IsAbstract && type.IsSealed)
            {
                flags.Add("static");
            }
            else if (type.IsAbstract && !type.IsInterface)
            {
                flags.Add("abstract");
            }
            else if (type.IsSealed && !type.IsValueType)
            {
                flags.Add("sealed");
            }

            flags.Add(type.IsPublic || type.IsNestedPublic ? "public" : "internal");
            if (type.HasGenericParameters)
            {
                flags.Add("generic");
            }

            string root = Roots.RootOf(type) ?? "-";
            TypeDefinition? direct = CecilNames.TryResolve(type.BaseType);
            string modBase = direct is not null && direct.Module == Module ? CecilNames.Type(direct) : "-";
            Types.Add([name, KindOf(type), root, modBase, GameBase(type), string.Join(",", flags)]);
            var chain = new List<string> { name };
            for (TypeDefinition? current = CecilNames.TryResolve(type.BaseType); current is not null && current.Module == Module; current = CecilNames.TryResolve(current.BaseType))
            {
                chain.Add(CecilNames.Type(current));
            }

            Chains[name] = chain;

            List<string> bases = CecilNames.BaseTypeNames(type).ToList();
            if (bases.Contains("Verse.AI.JobDriver"))
            {
                JobsAndLords.Add(["JobDriver", name, type.IsAbstract ? "abstract" : "concrete"]);
            }

            if (bases.Contains("Verse.AI.Group.LordJob"))
            {
                JobsAndLords.Add(["LordJob", name, type.IsAbstract ? "abstract" : "concrete"]);
            }

            if (type.Methods.FirstOrDefault(m => m.Name == "GetUniqueLoadID" && m.Parameters.Count == 0 && m.HasBody) is { } loadId)
            {
                List<string> literals = loadId.Body.Instructions.Where(i => i.OpCode.Code == Code.Ldstr).Select(i => (string)i.Operand).ToList();
                LoadIds.Add([name, literals.Count == 0 ? "-" : string.Join("+", literals.Select(IlArguments.Quote))]);
            }
        }

        DynamicOwners.UnionWith(Scribe.DynamicOwners);
        foreach (string enumName in Scribe.SavedEnums)
        {
            if (Module.GetType(enumName.Replace('+', '/')) is { } enumType)
            {
                foreach (FieldDefinition member in enumType.Fields.Where(f => f.IsStatic && f.IsLiteral))
                {
                    Enums.Add([enumName, member.Name, Convert.ToString(member.Constant, System.Globalization.CultureInfo.InvariantCulture) ?? "?"]);
                }
            }
        }

        var settingsTypes = new HashSet<string>(CecilNames.AllTypes(Module)
            .Where(t => CecilNames.BaseTypeNames(t).Contains("Verse.ModSettings", StringComparer.Ordinal))
            .Select(CecilNames.Type), StringComparer.Ordinal);
        var stringFields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (LabelFact fact in Scribe.Labels)
        {
            Labels.Add([fact.Owner, fact.Label, fact.Kind, fact.ValueType, fact.LookMode, fact.Default, fact.ForceSave]);
            if (settingsTypes.Contains(fact.Owner))
            {
                Settings.Add([fact.Owner, fact.Label, fact.ValueType, fact.Default]);
            }

            foreach (string element in DeepElements(fact))
            {
                Graph.Add([fact.Owner, fact.Label, element]);
            }

            if (fact is { Kind: "Values", ValueType: "System.String", Field: { } field } && !field.StartsWith("static ", StringComparison.Ordinal))
            {
                stringFields.TryAdd(field, fact.Owner);
            }
        }

        foreach (MethodStringFlow method in Flow.Methods)
        {
            foreach (FieldStore store in method.Stores)
            {
                string field = CecilNames.MemberShort(store.Field);
                if (!stringFields.ContainsKey(field))
                {
                    continue;
                }

                foreach ((_, _, StringSet value) in Flow.Expand(store.Location, method.Method, store.Value))
                {
                    foreach (string literal in value.Values ?? [])
                    {
                        SavedValues.Add([field, IlArguments.Quote(literal), keyedKeys.Contains(literal) ? "keyed" : "literal"]);
                    }
                }
            }
        }
    }

    // Deep 或以 Deep 保存的集合中，本模块定义的元素类型。
    private IEnumerable<string> DeepElements(LabelFact fact)
    {
        if (fact.Kind == "Deep")
        {
            if (Module.GetType(fact.ValueType.Replace('+', '/')) is not null)
            {
                yield return fact.ValueType;
            }

            yield break;
        }

        if (fact.Kind != "Collections" || !fact.LookMode.Contains("Deep", StringComparison.Ordinal))
        {
            yield break;
        }

        foreach (string argument in GenericArguments(fact.ValueType))
        {
            if (Module.GetType(argument.Replace('+', '/')) is not null)
            {
                yield return argument;
            }
        }
    }

    // "A<B,C<D>>" 的顶层泛型实参（B 与 C<D>），以及嵌套的实参。
    private static IEnumerable<string> GenericArguments(string type)
    {
        int open = type.IndexOf('<', StringComparison.Ordinal);
        if (open < 0 || !type.EndsWith('>'))
        {
            yield break;
        }

        string inner = type[(open + 1)..^1];
        int depth = 0;
        int start = 0;
        for (int i = 0; i <= inner.Length; i++)
        {
            if (i == inner.Length || (inner[i] == ',' && depth == 0))
            {
                string argument = inner[start..i];
                yield return argument;
                foreach (string nested in GenericArguments(argument))
                {
                    yield return nested;
                }

                start = i + 1;
            }
            else if (inner[i] == '<')
            {
                depth++;
            }
            else if (inner[i] == '>')
            {
                depth--;
            }
        }
    }
}
