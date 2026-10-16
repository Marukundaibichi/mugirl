using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Verse.AI;

namespace Mugirl.GameValidation.Core;

/// <summary>读取已经解析的真实 ThinkTree 图；不执行节点方法、属性、复制或键分配。</summary>
internal static class ThinkTreeSnapshot
{
    private static readonly FieldInfo Key = typeof(ThinkNode).GetField("uniqueSaveKeyInt", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(ThinkNode).FullName, "uniqueSaveKeyInt");

    public static JsonObject Capture(IEnumerable<ThinkTreeDef> definitions, Assembly? targetAssembly)
    {
        var trees = new JsonArray();
        var errors = new JsonArray();
        IReadOnlyDictionary<string, Dictionary<ThinkNode, int>> profiles = ReadProfiles(targetAssembly?.GetType("Mugirl.SaveUpgrade.LegacyThinkTreeKeys"), errors);
        int index = 0;
        foreach (ThinkTreeDef definition in definitions)
        {
            trees.Add(Tree(definition, index++, errors, profiles));
        }
        if (trees.Count > 0 && profiles.TryGetValue("Ledger", out Dictionary<ThinkNode, int>? assignments) && assignments.Count == 0)
        {
            errors.Add("The current shadow ledger has no assignments for the already resolved ThinkTrees.");
        }
        string[] mods = [.. LoadedModManager.RunningModsListForReading.Select(m => m.PackageIdPlayerFacing)];
        return new JsonObject
        {
            ["format"] = 1,
            ["gameVersion"] = VersionControl.CurrentVersionStringWithRev,
            ["gameAssemblyMvid"] = typeof(ThinkNode).Module.ModuleVersionId.ToString(),
            ["modAssemblyMvid"] = targetAssembly?.ManifestModule.ModuleVersionId.ToString(),
            ["modIds"] = new JsonArray([.. mods.Select(m => (JsonNode?)JsonValue.Create(m))]),
            ["dlcIds"] = new JsonArray([.. mods.Where(m => m.StartsWith("ludeon.rimworld.", StringComparison.OrdinalIgnoreCase)).Select(m => (JsonNode?)JsonValue.Create(m))]),
            ["trees"] = trees,
            ["shadowProfiles"] = new JsonObject([.. profiles.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, JsonValue.Create(p.Value.Count)))]),
            ["dutyDefs"] = new JsonArray([.. DefDatabase<DutyDef>.AllDefsListForReading.Select((duty, i) => (JsonNode?)new JsonObject
            {
                ["index"] = i, ["defName"] = duty.defName,
                ["thinkNodeType"] = duty.thinkNode?.GetType().FullName, ["constantThinkNodeType"] = duty.constantThinkNode?.GetType().FullName,
            })]),
            ["errors"] = errors,
        };
    }

    private static JsonObject Tree(ThinkTreeDef definition, int index, JsonArray errors, IReadOnlyDictionary<string, Dictionary<ThinkNode, int>> profiles)
    {
        var nodes = new JsonArray();
        var result = new JsonObject
        {
            ["index"] = index, ["defName"] = definition.defName, ["type"] = definition.GetType().FullName,
            ["fields"] = Fields(definition, typeof(Def), ["thinkRoot"]), ["nodes"] = nodes,
        };
        if (definition.thinkRoot is null)
        {
            errors.Add("No root: " + definition.defName);
            return result;
        }
        var paths = new Dictionary<ThinkNode, int[]>(ReferenceEqualityComparer.Instance);
        var visits = new List<(ThinkNode Node, int[] Path, int[]? Parent)>();
        var pending = new Stack<(ThinkNode Node, int[] Path, int[]? Parent)>();
        pending.Push((definition.thinkRoot, [], null));
        while (pending.TryPop(out var visit))
        {
            if (!paths.TryAdd(visit.Node, visit.Path))
            {
                nodes.Add(new JsonObject { ["path"] = Path(visit.Path), ["referencePath"] = Path(paths[visit.Node]) });
                errors.Add("Repeated node instance: " + definition.defName + "/" + string.Join('/', visit.Path));
                continue;
            }
            visits.Add(visit);
            if (visit.Node.subNodes is null)
            {
                errors.Add("Null subNodes: " + definition.defName + "/" + string.Join('/', visit.Path));
                continue;
            }
            for (int child = visit.Node.subNodes.Count - 1; child >= 0; child--)
            {
                if (visit.Node.subNodes[child] is { } next) { pending.Push((next, [.. visit.Path, child], visit.Path)); }
                else { errors.Add("Null child: " + definition.defName + "/" + string.Join('/', visit.Path) + "/" + child); }
            }
        }
        foreach ((ThinkNode node, int[] path, int[]? parent) in visits)
        {
            int[]? actualParent = node.parent is not null && paths.TryGetValue(node.parent, out int[]? found) ? found : null;
            nodes.Add(new JsonObject
            {
                ["path"] = Path(path), ["type"] = node.GetType().FullName,
                ["assemblyMvid"] = node.GetType().Module.ModuleVersionId.ToString(),
                ["uniqueSaveKey"] = (int)Key.GetValue(node)!,
                ["profileKeys"] = new JsonObject([.. profiles.Select(p => new KeyValuePair<string, JsonNode?>(p.Key,
                    p.Value.TryGetValue(node, out int key) ? JsonValue.Create(key) : null))]),
                ["structuralParentPath"] = Path(parent), ["parentPath"] = Path(actualParent),
                ["parentIsNull"] = node.parent is null,
                ["externalParentType"] = node.parent is not null && actualParent is null ? node.parent.GetType().FullName : null,
                ["fields"] = Fields(node, typeof(object), ["subNodes", "parent", "uniqueSaveKeyInt"]),
            });
        }
        return result;
    }

    internal static IReadOnlyDictionary<string, Dictionary<ThinkNode, int>> ReadProfiles(Type? owner, JsonArray errors)
    {
        var result = new Dictionary<string, Dictionary<ThinkNode, int>>(StringComparer.Ordinal);
        if (owner is null) { return result; }
        foreach (string name in new[] { "Ledger", "BeforeCorporateDuty", "FirstPublishedLayout", "CurrentLayout" })
        {
            FieldInfo? field = owner.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            // 仍能捕获尚未加入当前布局的历史被测构建；存在时按同一严格形状读取。
            if (field is null && name == "CurrentLayout") { continue; }
            object? ledger = field?.GetValue(null);
            FieldInfo? assignments = ledger?.GetType().GetField("currentAssignments", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is null || ledger?.GetType() != field.FieldType || assignments?.FieldType != typeof(Dictionary<ThinkNode, int>)
                || assignments.GetValue(ledger) is not Dictionary<ThinkNode, int> dictionary
                || !ReferenceEquals(dictionary.Comparer, ReferenceEqualityComparer.Instance))
            {
                errors.Add("Unsupported shadow ledger field shape/reference comparer: " + name);
                continue;
            }
            // 只枚举已经由自然分配回调写入的字典；不调用 KeyFor/Capture/Resolve，也不重放丢弃副本的分配。
            result.Add(name, new Dictionary<ThinkNode, int>(dictionary, ReferenceEqualityComparer.Instance));
        }
        if (owner.GetField("captureError", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)?.GetValue(null) is string problem)
        {
            errors.Add("Production ThinkTree capture error: " + problem);
        }
        return result;
    }

    private static JsonArray? Path(int[]? path) => path is null ? null : new JsonArray([.. path.Select(i => (JsonNode?)JsonValue.Create(i))]);

    private static JsonArray Fields(object owner, Type stopBefore, string[] excluded)
    {
        var fields = new JsonArray();
        for (Type? type = owner.GetType(); type is not null && type != stopBefore; type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(f => f.MetadataToken))
            {
                if (excluded.Contains(field.Name, StringComparer.Ordinal)) { continue; }
                bool cache = field.Name.Contains("cache", StringComparison.OrdinalIgnoreCase);
                fields.Add(new JsonObject
                {
                    ["name"] = field.Name, ["declaringType"] = type.FullName, ["fieldType"] = field.FieldType.FullName,
                    ["unsaved"] = field.CustomAttributes.Any(a => a.AttributeType == typeof(UnsavedAttribute)),
                    ["excluded"] = cache ? "runtime cache" : null,
                    ["value"] = cache ? null : Value(field.GetValue(owner), new HashSet<object>(ReferenceEqualityComparer.Instance)),
                });
            }
        }
        return fields;
    }

    private static JsonNode? Value(object? value, HashSet<object> active)
    {
        if (value is null) { return null; }
        Type type = value.GetType();
        if (value is string text) { return JsonValue.Create(text); }
        if (value is bool boolean) { return JsonValue.Create(boolean); }
        if (value is char character) { return JsonValue.Create(character.ToString()); }
        if (type.IsEnum) { return new JsonObject { ["enumType"] = type.FullName, ["name"] = Enum.GetName(type, value), ["number"] = Enum.Format(type, value, "D") }; }
        if (value is float single) { return new JsonObject { ["float"] = single.ToString("R", CultureInfo.InvariantCulture), ["bits"] = BitConverter.SingleToInt32Bits(single) }; }
        if (value is double number) { return new JsonObject { ["double"] = number.ToString("R", CultureInfo.InvariantCulture), ["bits"] = BitConverter.DoubleToInt64Bits(number) }; }
        if (type.IsPrimitive || value is decimal) { return JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)); }
        if (value is Type represented) { return new JsonObject { ["type"] = represented.FullName, ["assemblyMvid"] = represented.Module.ModuleVersionId.ToString() }; }
        if (value is Def definition) { return new JsonObject { ["defType"] = type.FullName, ["defName"] = definition.defName }; }
        bool collection = type.IsArray && type.GetArrayRank() == 1
            || type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) || type.GetGenericTypeDefinition() == typeof(HashSet<>));
        if (!collection) { return new JsonObject { ["unsupportedType"] = type.FullName }; }
        if (!active.Add(value)) { return new JsonObject { ["cycleType"] = type.FullName }; }
        var items = new JsonArray();
        foreach (object? item in (IEnumerable)value) { items.Add(Value(item, active)); }
        active.Remove(value);
        return new JsonObject { ["collectionType"] = type.FullName, ["items"] = items };
    }
}
