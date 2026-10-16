using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Checks;

internal static partial class PublicApiCheck
{
    private const string ShapesFile = "public-api-shapes.json";

    private static string? CheckContract(ValidationOptions options, ModuleDefinition module, ContentModel content, JsonElement entry,
        string kind, string name)
        => kind switch
        {
            "packageId" => content.Catalog.UnderTest.PackageId == ActiveMods.Normalize(name) ? null : "About.xml packageId changed.",
            "typeShortName" => CecilNames.AllTypes(module).Any(t => t.Name == name && CecilNames.BaseTypeNames(t).Contains("Verse.Mod", StringComparer.Ordinal)) ? null : "no Mod subclass with this short name.",
            "member" => CheckMember(module, name, entry),
            "harmonyParameter" => CheckParameter(module, name, Text(entry, "owner")),
            "xpathStructure" => CheckXPath(content, name, Text(entry, "defRoot")),
            "texturePath" => TextureRules.Check(content.Textures, name, null).Match is TextureMatch.Found or TextureMatch.OptionalContentOnly ? null : "texture path cannot be resolved with its original case.",
            "tag" => OurNodes(content).Any(n => n.SelectNodes(".//tags/li|.//apparelTags/li|.//styleTags/li|.//tradeTags/li|.//weaponTags/li")!.OfType<XmlElement>().Any(e => e.InnerText.Trim() == name)) ? null : "no owned content carries this tag.",
            "scribeLabel" => CheckStamp(module, name),
            "defNamePattern" => CheckPattern(options, content, name, Text(entry, "defRoot")),
            "structure" => CheckStructure(options, content, name),
            _ => "unsupported frozen contract kind.",
        };

    private static bool RootMatches(Type type, string root)
        => type.Name == root || type.FullName == root || DefRoots.Database(type).Name == root || DefRoots.Database(type).FullName == root;

    private static IEnumerable<XmlElement> OurNodes(ContentModel content)
        => content.Corpus.Defs.Where(n => ContentModel.IsOurs(content.Corpus.SourceOf(n)?.Pack));

    private static MethodDefinition? NamedMember(ModuleDefinition module, string name)
    {
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || module.GetType(name[..dot].Replace('+', '/')) is not { } type) { return null; }
        MethodDefinition[] methods = type.Methods.Where(m => m.Name == name[(dot + 1)..]).ToArray();
        return methods.Length == 1 ? methods[0] : null;
    }

    private static string? CheckMember(ModuleDefinition module, string name, JsonElement entry)
    {
        MethodDefinition? method = NamedMember(module, name);
        if (method is null) { return "the public member is missing or ambiguous."; }
        // 当前唯一受支持的生产 C# 扩展点；签名与返回值也属于冻结契约。
        if (name == "Mugirl.MountedPawnUtility.IsMugirl"
            && (method.ReturnType.FullName != "System.Boolean" || method.Parameters.Count != 1 || method.Parameters[0].ParameterType.FullName != "Verse.Pawn"))
        {
            return "IsMugirl must remain bool IsMugirl(Verse.Pawn pawn).";
        }

        string signature = CecilNames.Type(method.DeclaringType) + "::" + method.Name + "(" + string.Join(",", method.Parameters.Select(p => CecilNames.Type(p.ParameterType))) + ")";
        string? problem = CheckMethod(module, signature, noInlining: true);
        if (problem is not null) { return problem; }
        if (entry.TryGetProperty("harmonyBoundParameters", out JsonElement parameters))
        {
            foreach (JsonElement parameter in parameters.EnumerateArray())
            {
                if (!method.Parameters.Any(p => p.Name == parameter.GetString())) { return "Harmony-bound parameter name changed: " + parameter.GetString(); }
            }
        }

        return null;
    }

    private static string? CheckParameter(ModuleDefinition module, string name, string? owner)
    {
        int open = name.LastIndexOf('(');
        if (open <= 0 || !name.EndsWith(')') || owner != name[..open]) { return "invalid Harmony parameter evidence."; }
        MethodDefinition? method = NamedMember(module, owner);
        string parameter = name[(open + 1)..^1];
        return method?.Parameters.Any(p => p.Name == parameter) == true ? null : "Harmony-bound parameter name is missing: " + parameter;
    }

    private static string? CheckXPath(ContentModel content, string name, string? root)
    {
        int slash = name.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0) { return "expected defName/element/path."; }
        string defName = name[..slash];
        if (CheckDef(content, defName, root) is { } problem) { return problem; }
        foreach (XmlElement node in OurNodes(content).Where(n => n["defName"]?.InnerText.Trim() == defName))
        {
            string typeName = node.GetAttribute("Class") is { Length: > 0 } cls ? cls : node.Name;
            Type? type = content.Types.GetTypeInAnyAssembly(typeName);
            if ((root is null || (type is not null && RootMatches(type, root))) && content.Inheritance.Resolve(node).SelectSingleNode(name[(slash + 1)..]) is not null) { return null; }
        }

        return "dependent XPath no longer selects its required element path.";
    }

    private static string? CheckStamp(ModuleDefinition module, string name)
    {
        TypeDefinition? hub = module.GetType("Mugirl.Core.GameComponent_Mugirl");
        MethodDefinition? method = hub?.Methods.FirstOrDefault(m => m.Name == "ExposeData" && m.HasBody);
        if (method is null) { return "the schema hub has no ExposeData method."; }
        bool found = false;
        foreach (Instruction call in method.Body.Instructions)
        {
            if (call.Operand is not MethodReference target || target.DeclaringType.FullName != "Verse.Scribe_Values" || target.Name != "Look"
                || !IsLiteral(IlArguments.Producer(method, call, 1), name)) { continue; }
            found = true;
            int forceSave = CecilNames.TryResolve(target)?.Parameters.ToList().FindIndex(p => p.Name == "forceSave") ?? -1;
            if (forceSave < 0 || IlArguments.Constant(method, IlArguments.Producer(method, call, forceSave), target.Parameters[forceSave].ParameterType) != "True")
            { return "schema label is not written with forceSave: true."; }
        }

        return found ? null : "the schema hub no longer serializes the frozen label.";
    }

    private static JsonElement? Shapes(ValidationOptions options)
    {
        string path = Path.Combine(options.BaselineDir, ShapesFile);
        if (!File.Exists(path)) { return null; }
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static string? CheckPattern(ValidationOptions options, ContentModel content, string pattern, string? root)
    {
        JsonElement? shapes = Shapes(options);
        if (shapes is null || !shapes.Value.TryGetProperty("defPatterns", out JsonElement patterns) || !patterns.TryGetProperty(pattern, out JsonElement expected)
            || expected.ValueKind != JsonValueKind.Array || expected.GetArrayLength() == 0) { return "missing immutable pattern membership evidence in " + ShapesFile; }
        foreach (JsonElement item in expected.EnumerateArray())
        {
            string name = item.GetProperty("name").GetString()!;
            if (!Regex.IsMatch(name, "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$", RegexOptions.CultureInvariant))
            { return "pattern evidence contains an unrelated name: " + name; }
            if (CheckDef(content, name, root ?? Text(item, "root")) is { } problem) { return name + ": " + problem; }
        }

        return null;
    }

    private static string? CheckStructure(ValidationOptions options, ContentModel content, string name)
    {
        JsonElement? shapes = Shapes(options);
        if (shapes is null) { return "missing immutable structure evidence in " + ShapesFile; }
        if (name == "Mugirl race bodyAddons order (HAR addonVariants are saved by index; grow only at the end)")
        {
            if (!shapes.Value.TryGetProperty("bodyAddonPaths", out JsonElement expected) || expected.GetArrayLength() == 0) { return "missing bodyAddons index evidence."; }
            XmlElement? race = OurNodes(content).FirstOrDefault(n => n["defName"]?.InnerText.Trim() == "Mugirl");
            string[] actual = race is null ? [] : content.Inheritance.Resolve(race).SelectNodes("alienRace/generalSettings/alienPartGenerator/bodyAddons/li")!.OfType<XmlElement>().Select(n => n["path"]?.InnerText.Trim() ?? "").ToArray();
            string[] original = expected.EnumerateArray().Select(e => e.GetString()!).ToArray();
            return actual.Take(original.Length).SequenceEqual(original, StringComparer.Ordinal) ? null : "bodyAddons order changed; existing indices must remain a prefix.";
        }

        if (name == "texture paths under Textures/ (dependents reference Mugirl textures by path)")
        {
            if (!shapes.Value.TryGetProperty("texturePaths", out JsonElement expected) || expected.GetArrayLength() == 0) { return "missing texture path evidence."; }
            string[] missing = expected.EnumerateArray().Select(e => e.GetString()!).Where(p => content.Textures.Find(p) is not (TextureMatch.Found or TextureMatch.OptionalContentOnly)).Take(6).ToArray();
            return missing.Length == 0 ? null : "frozen texture paths missing: " + string.Join(", ", missing);
        }

        return "unknown structure contract: no check is implemented.";
    }
}
