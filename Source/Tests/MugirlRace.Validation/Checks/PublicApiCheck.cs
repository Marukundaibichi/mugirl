using System.Text.Json;
using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. 永久冻结的公开 API（<c>tools/RenameApply/specs/public-api.json</c>，决定 §1、§5：依赖 Mod 引用的名字）：每一项都必须在当前构建与内容中存在，
/// 形态不变——XML 可见类型（public）、它们的 XML 字段、Harmony id、可打补丁的扩展点（public static、NoInlining）、defName
/// （数据库根或 Def 类）、抽象 Name、显示名。名字由 A3 扫描依赖 Mod 生成；这里只读。
/// </summary>
internal static partial class PublicApiCheck
{
    public const string FileName = "public-api.json";

    private static readonly string[] Kinds = ["type", "xmlClass", "def", "defName", "defNamePattern", "abstractName", "xmlField", "harmonyId", "method", "member", "harmonyParameter", "displayName", "packageId", "typeShortName", "scribeLabel", "tag", "texturePath", "structure", "xpathStructure"];

    private static string ApiPath(ValidationOptions options)
        => Path.Combine(options.ModRoot, "tools", "RenameApply", "specs", FileName);

    public static void Run(Report report, ValidationOptions options, MetadataWorld world, ContentModel content)
        => report.Run("C.public-api", "Every frozen public API name (tools/RenameApply/specs/public-api.json) still exists with the same shape", recorder => Check(recorder, options, world, content));

    /// <summary>清单中 kind 为 type 的名字（根命名空间白名单与名字复用豁免）。文件缺失或格式错误时为空。</summary>
    public static IReadOnlySet<string> TypeNames(ValidationOptions options)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement entry in SafeEntries(ApiPath(options)) ?? [])
        {
            if (Text(entry, "kind") is "type" or "xmlClass" && Text(entry, "name") is { } name)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static void Check(CheckRecorder recorder, ValidationOptions options, MetadataWorld world, ContentModel content)
    {
        string path = ApiPath(options);
        List<JsonElement>? entries;
        try
        {
            entries = Entries(path);
        }
        catch (JsonException exception)
        {
            recorder.Fail(path, "Malformed JSON: " + exception.Message);
            return;
        }

        if (entries is null)
        {
            recorder.Fail(path, "Missing: the frozen public API list (kind, name, defRoot, usedBy).");
            return;
        }

        ModuleDefinition module = world.Mod;
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonElement entry in entries)
        {
            string kind = Text(entry, "kind") ?? "";
            string name = Text(entry, "name") ?? "";
            string subject = $"{kind} {name}";
            if (Array.IndexOf(Kinds, kind) < 0 || name.Length == 0)
            {
                recorder.Fail(path, $"Entry '{entry.GetRawText()}' needs a kind ({string.Join(", ", Kinds)}) and a name.");
                continue;
            }

            if ((!entry.TryGetProperty("usedBy", out JsonElement usedBy) || usedBy.ValueKind != JsonValueKind.Array || usedBy.GetArrayLength() == 0) && string.IsNullOrWhiteSpace(Text(entry, "frozenBy")))
            {
                recorder.Fail(subject, "Entry without usedBy evidence or an explicit frozenBy decision.");
            }

            counts[kind] = counts.GetValueOrDefault(kind) + 1;
            string? problem = kind switch
            {
                "type" or "xmlClass" => CheckType(module, name),
                "xmlField" => CheckField(module, Text(entry, "owner") is { } owner ? owner + "." + name : name),
                "method" => CheckMethod(module, name, entry.TryGetProperty("noInlining", out JsonElement inline) && inline.ValueKind == JsonValueKind.True),
                "harmonyId" => CheckHarmonyId(module, name),
                "displayName" => content.Catalog.UnderTest.Name == name ? null : $"About.xml name is '{content.Catalog.UnderTest.Name}'.",
                "def" or "defName" => CheckDef(content, name, Text(entry, "defRoot")),
                "abstractName" => CheckAbstractName(content, name),
                _ => CheckContract(options, module, content, entry, kind, name),
            };
            if (problem is not null)
            {
                recorder.Fail(subject, "Frozen public API broken: " + problem + " Dependent mods reference this name (" + string.Join("; ", Used(entry)) + ").");
            }
        }

        recorder.Summary = $"{entries.Count} frozen name(s): " + string.Join(", ", counts.Select(pair => $"{pair.Key} {pair.Value}"));
    }

    private static string? CheckType(ModuleDefinition module, string name)
        => module.GetType(name.Replace('+', '/')) is { } type
            ? (type.IsPublic || type.IsNestedPublic ? null : "the type is not public.")
            : "the type does not exist.";

    // Type.field：字段可以声明在基类上（公开别名子类继承基类的 XML 字段）。
    private static string? CheckField(ModuleDefinition module, string name)
    {
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || module.GetType(name[..dot].Replace('+', '/')) is not { } type)
        {
            return "the declaring type does not exist.";
        }

        string field = name[(dot + 1)..];
        for (TypeDefinition? current = type; current is not null; current = CecilNames.TryResolve(current.BaseType))
        {
            if (current.Fields.FirstOrDefault(f => f.Name == field && !f.IsStatic) is { } found)
            {
                return found.IsPublic ? null : "the field is not public (XML cannot set it).";
            }
        }

        return $"{name[..dot]} has no field '{field}' (XML that sets it fails to load).";
    }

    // Owner::Name(ParamType,...)
    private static string? CheckMethod(ModuleDefinition module, string name, bool noInlining)
    {
        int separator = name.IndexOf("::", StringComparison.Ordinal);
        int open = name.IndexOf('(', StringComparison.Ordinal);
        if (separator <= 0 || open < separator || !name.EndsWith(')'))
        {
            return "the name must be Owner::Method(ParameterTypes).";
        }

        if (module.GetType(name[..separator].Replace('+', '/')) is not { } type)
        {
            return "the declaring type does not exist.";
        }

        string method = name[(separator + 2)..open];
        string[] parameters = name[(open + 1)..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        MethodDefinition? found = type.Methods.FirstOrDefault(m => m.Name == method
            && m.Parameters.Select(p => CecilNames.Type(p.ParameterType)).SequenceEqual(parameters, StringComparer.Ordinal));
        if (found is null)
        {
            return "no method with this name and these parameter types.";
        }

        if (!found.IsPublic || !found.IsStatic)
        {
            return "the method must stay public static.";
        }

        if (!noInlining)
        {
            return null;
        }

        if ((found.ImplAttributes & MethodImplAttributes.NoInlining) == 0)
        {
            return "the method must carry [MethodImpl(MethodImplOptions.NoInlining)] (dependents Harmony-patch it; an inlined body would bypass the patch).";
        }

        // 可打补丁的扩展点必须仍是真正的决策点：本 Mod 自己的代码（扩展点所在类型之外）调用它，否则依赖 Mod 的补丁不起作用。
        bool called = CecilNames.AllTypes(module).Where(t => t != type).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .Any(m => m.Body.Instructions.Any(i => i.Operand is MethodReference callee && CecilNames.TryResolve(callee) == found));
        return called ? null : "no Mugirl code outside the declaring type calls it any more: dependents' patches on it would have no effect.";
    }

    // new Harmony(id) 的实参必须是这个字面量：直接 ldstr（const 字段被编译器内联），或者静态构造函数中以它初始化的 static readonly 字段。
    private static string? CheckHarmonyId(ModuleDefinition module, string id)
    {
        foreach (MethodDefinition method in CecilNames.AllTypes(module).SelectMany(t => t.Methods).Where(m => m.HasBody))
        {
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code == Code.Newobj && instruction.Operand is MethodReference { DeclaringType.FullName: "HarmonyLib.Harmony" } ctor
                    && ctor.Parameters.Count == 1 && IsLiteral(IlArguments.Producer(method, instruction, 0), id))
                {
                    return null;
                }
            }
        }

        return $"no new Harmony(\"{id}\") in the assembly (dependents order their patches against this id with [HarmonyAfter]).";
    }

    private static bool IsLiteral(Instruction? producer, string id)
    {
        switch (producer)
        {
            case { OpCode.Code: Code.Ldstr, Operand: string text }:
                return text == id;
            case { OpCode.Code: Code.Ldsfld, Operand: FieldReference field } when CecilNames.TryResolve(field) is { } definition:
                MethodDefinition? cctor = definition.DeclaringType.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic && m.HasBody);
                return cctor is not null && cctor.Body.Instructions.Any(i => i.OpCode.Code == Code.Stsfld && i.Operand is FieldReference stored
                    && CecilNames.TryResolve(stored) == definition && IlArguments.Producer(cctor, i, 0) is { OpCode.Code: Code.Ldstr, Operand: string text } && text == id);
            default:
                return false;
        }
    }

    private static string? CheckDef(ContentModel content, string name, string? root)
    {
        List<DefEntry> defs = content.Defs.FindAny(name).Where(e => ContentModel.IsOurs(e.Pack) && !e.Implied).ToList();
        if (defs.Count == 0)
        {
            return "no def of this name in the mod's content.";
        }

        return root is null || defs.Any(e => RootMatches(e.Type, root))
            ? null
            : $"defined as {string.Join(", ", defs.Select(e => e.Type.Name).Distinct())}, not {root}.";
    }

    private static string? CheckAbstractName(ContentModel content, string name)
    {
        foreach (XmlElement node in content.Corpus.Root.ChildNodes.OfType<XmlElement>())
        {
            if (node.GetAttribute("Name") == name && ContentModel.IsOurs(content.Corpus.SourceOf(node)?.Pack))
            {
                return null;
            }
        }

        return "no def of the mod declares this Name (ParentName references of dependents fail).";
    }

    private static IEnumerable<string> Used(JsonElement entry)
        => entry.TryGetProperty("usedBy", out JsonElement usedBy) && usedBy.ValueKind == JsonValueKind.Array
            ? usedBy.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : $"{Text(e, "mod")} {Text(e, "file")} ({Text(e, "use")})").Take(3)
            : [Text(entry, "frozenBy") ?? "unspecified"];

    private static List<JsonElement>? SafeEntries(string path)
    {
        try { return Entries(path); }
        catch (JsonException) { return null; }
    }

    private static List<JsonElement>? Entries(string path)
    {
        if (!File.Exists(path)) { return null; }
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("entries", out JsonElement entries)) { root = entries; }
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) { throw new JsonException("Expected a non-empty entries array."); }
        return root.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static string? Text(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
