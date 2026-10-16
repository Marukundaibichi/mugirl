using System.Text.Encodings.Web;
using System.Text.Json;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using Mono.Cecil;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. Unity 边界（ADR-006）：每个指向 UnityStubs 的类型与成员引用都必须带理由列在 <c>Baselines/unity-boundary.json</c> 中；
/// 列出但不再使用的条目同样失败。边界类型只作数据（构造、读写字段、读取数据属性）；不属于数据用法的成员（GUI、Event 等服务）即使
/// 列入白名单，也只能在 <c>Mugirl.Platform</c> 中使用（规则 3：原始 GUI 调用集中在 <c>Mugirl.Platform.Ui</c>）。
/// </summary>
/// <remarks>
/// <c>--update-baselines</c> 重写该文件：保留已有理由，新引用以 <c>REVIEW:</c> 理由加入（之后作为警告报告，直到有人写上真实理由），
/// 删除不再使用的条目。文件注释换成固定文件头。
/// </remarks>
internal static class UnityBoundaryCheck
{
    private const string FileName = "unity-boundary.json";
    private const string PlatformPrefix = "Mugirl.Platform.";

    // 构造函数与字段属于数据的值类型（ADR-006 规则 1）。
    private static readonly HashSet<string> DataStructs = new(StringComparer.Ordinal)
    {
        "UnityEngine.Color", "UnityEngine.Color32", "UnityEngine.Rect", "UnityEngine.Bounds", "UnityEngine.Vector2",
        "UnityEngine.Vector3", "UnityEngine.Vector4", "UnityEngine.Quaternion", "UnityEngine.Matrix4x4",
    };

    // ADR-006 规则 1 列出的边界类型（GlobalUsings 的别名）与 Verse API 签名中出现的引擎对象类型。
    private static readonly HashSet<string> BoundaryTypes = new(StringComparer.Ordinal)
    {
        "UnityEngine.Color", "UnityEngine.Color32", "UnityEngine.Material", "UnityEngine.MaterialPropertyBlock", "UnityEngine.Mesh",
        "UnityEngine.Rect", "UnityEngine.Shader", "UnityEngine.TextAnchor", "UnityEngine.Texture", "UnityEngine.Texture2D",
        "UnityEngine.Matrix4x4", "UnityEngine.Quaternion", "UnityEngine.Vector2", "UnityEngine.Vector3", "UnityEngine.Vector4",
        "UnityEngine.RenderTexture", "UnityEngine.Bounds", "UnityEngine.Event", "UnityEngine.Object",
    };

    public static void Run(Report report, ValidationOptions options, MetadataWorld world)
        => report.Run("B.unity-boundary", "UnityStubs references are limited to the reviewed game-API boundary", recorder => Check(recorder, options, world.Mod));

    private static void Check(CheckRecorder recorder, ValidationOptions options, ModuleDefinition module)
    {
        string path = Path.Combine(options.BaselineDir, FileName);
        (Dictionary<string, string> allowedTypes, Dictionary<string, string> allowedMembers) = Load(recorder, path);

        // 引用取自真正使用它们的代码与元数据（UsageScanner），不取原始 TypeRef 表：编译器也会为
        // "global using X = UnityEngine.X" 别名的目标生成 TypeRef，只供调试信息的导入作用域使用。
        var usedTypes = new SortedSet<string>(StringComparer.Ordinal);
        var usedMembers = new SortedDictionary<string, MemberReference>(StringComparer.Ordinal);
        var nativeTypes = new SortedSet<string>(StringComparer.Ordinal);
        var where = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (Usage usage in UsageScanner.Scan(module))
        {
            string key;
            if (usage.Referenced is TypeReference type)
            {
                // 数组、引用与泛型实例经其元素类型与类型实参列出。
                if (type is TypeSpecification)
                {
                    continue;
                }

                string assembly = CecilNames.AssemblyOf(type);
                if (assembly.StartsWith("Waylith", StringComparison.Ordinal))
                {
                    nativeTypes.Add(CecilNames.Type(type));
                }

                if (assembly != "UnityStubs")
                {
                    continue;
                }

                key = CecilNames.Type(type);
                usedTypes.Add(key);
            }
            else if (CecilNames.AssemblyOf(usage.Referenced.DeclaringType) == "UnityStubs")
            {
                key = CecilNames.Member(usage.Referenced);
                usedMembers.TryAdd(key, usage.Referenced);
            }
            else
            {
                continue;
            }

            if (!where.TryGetValue(key, out List<string>? sites))
            {
                where[key] = sites = [];
            }

            if (!sites.Contains(usage.Where))
            {
                sites.Add(usage.Where);
            }
        }

        if (options.UpdateBaselines)
        {
            Update(recorder, path, allowedTypes, allowedMembers, usedTypes, usedMembers, module, where);
        }

        foreach (string type in usedTypes.Where(type => !allowedTypes.ContainsKey(type)))
        {
            recorder.Fail(type, "Unity type outside the boundary whitelist. A type is acceptable only where RimWorld's own API "
                + $"uses it; add it to Baselines/{FileName} with that reason, or remove the use.", Describe(where, type));
        }

        foreach ((string key, MemberReference member) in usedMembers.Where(pair => !allowedMembers.ContainsKey(pair.Key)))
        {
            string verdict = IsDataUse(member)
                ? $"It is data use that ADR-006 allows: add it to Baselines/{FileName} with a reason."
                : IsEngineBridge(member)
                    ? $"It reads the engine object behind a boundary type: allowed only in Mugirl.Platform.Rendering.NativeBridge; add it to Baselines/{FileName} if that is where it is used."
                    : CecilNames.Type(member.DeclaringType) is "UnityEngine.Rect" or "UnityEngine.Bounds"
                        ? "ADR-006 allows Rect/Bounds constructors and property getters only: build the rectangle with the constructor or Verse's GenUI helpers (LeftPartPixels, ContractedBy, …)."
                        : "ADR-006 forbids it outside Mugirl.Platform (UnityStubs logic or service): replace it (Mugirl.Mathematics, Mugirl.Platform, Verse), "
                            + $"or wrap it in Mugirl.Platform with a reason and list it in Baselines/{FileName}.";
            recorder.Fail(key, "Unity member outside the boundary whitelist. " + verdict, Describe(where, key));
        }

        // 引擎桥接与服务调用即使在白名单中，也只能出现在 Mugirl.Platform。
        foreach ((string key, MemberReference member) in usedMembers.Where(pair => !IsDataUse(pair.Value)))
        {
            foreach (string site in (where.GetValueOrDefault(key) ?? []).Where(site => !site.StartsWith(PlatformPrefix, StringComparison.Ordinal)))
            {
                recorder.Fail(key + " @ " + site, IsEngineBridge(member)
                    ? "Engine-object bridge used outside Mugirl.Platform: go through Mugirl.Platform.Rendering.NativeBridge."
                    : "Unity service call outside Mugirl.Platform (ADR-006 rule 3): use Verse (Widgets, GenUI, OriginalEventUtility) or a Mugirl.Platform wrapper.");
            }
        }

        foreach (string stale in allowedTypes.Keys.Where(type => !usedTypes.Contains(type)))
        {
            recorder.Fail("stale: " + stale, $"Listed in Baselines/{FileName} but no longer referenced: remove the entry.");
        }

        foreach (string stale in allowedMembers.Keys.Where(member => !usedMembers.ContainsKey(member)))
        {
            recorder.Fail("stale: " + stale, $"Listed in Baselines/{FileName} but no longer referenced: remove the entry.");
        }

        foreach ((string key, string reason) in allowedTypes.Concat(allowedMembers).Where(pair => pair.Value.StartsWith(AllowList.ReviewPrefix, StringComparison.Ordinal)))
        {
            recorder.Warn("review: " + key, $"Generated whitelist entry awaiting review: replace the reason in Baselines/{FileName} with why the reference is acceptable, or remove the use.");
        }

        foreach (string native in nativeTypes)
        {
            recorder.Info(native, "Native engine type (Waylith) used directly.");
        }

        recorder.Summary = $"{usedTypes.Count} Unity types and {usedMembers.Count} members referenced";
    }

    // ADR-006 规则 1：边界结构体的构造函数与字段、Rect/Bounds 的无参 getter、Texture.width/height、Object.name，
    // 以及 Verse 绘制 API 接受的 MaterialPropertyBlock 调用（只用 int 属性 id）。
    private static bool IsDataUse(MemberReference member)
    {
        string type = CecilNames.Type(member.DeclaringType);
        string name = member.Name;
        if (member is FieldReference)
        {
            return DataStructs.Contains(type);
        }

        if (member is not MethodReference method)
        {
            return false;
        }

        if (name == ".ctor" && (DataStructs.Contains(type) || type == "UnityEngine.MaterialPropertyBlock"))
        {
            return true;
        }

        if (type is "UnityEngine.Rect" or "UnityEngine.Bounds")
        {
            return name.StartsWith("get_", StringComparison.Ordinal) && method.Parameters.Count == 0;
        }

        return (type, name) switch
        {
            ("UnityEngine.Texture", "get_width" or "get_height") => true,
            ("UnityEngine.Object", "get_name") => true,
            ("UnityEngine.MaterialPropertyBlock", "Clear") => true,
            ("UnityEngine.MaterialPropertyBlock", "SetFloat" or "SetColor" or "SetVector" or "SetTexture") =>
                method.Parameters.Count == 2 && method.Parameters[0].ParameterType.FullName == "System.Int32",
            _ => false,
        };
    }

    // UnityStubs 为 Material、Mesh、Texture 背后的引擎对象公开的 getter（Verse 自己也用）。
    private static bool IsEngineBridge(MemberReference member)
        => (CecilNames.Type(member.DeclaringType), member.Name) is ("UnityEngine.Material", "get_engineMaterial")
            or ("UnityEngine.Mesh", "get_EngineMesh") or ("UnityEngine.Texture", "get_CoreTexture");

    private static (Dictionary<string, string> Types, Dictionary<string, string> Members) Load(CheckRecorder recorder, string path)
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        var members = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            recorder.Fail("Baselines/" + FileName, "Missing whitelist file (run with --update-baselines to create it, then review every reason).");
            return (types, members);
        }

        using JsonDocument? document = JsonBaseline.Load(recorder, Path.GetDirectoryName(path)!, FileName);
        if (document is null)
        {
            return (types, members);
        }

        foreach ((string section, Dictionary<string, string> target) in new[] { ("types", types), ("members", members) })
        {
            if (document.RootElement.TryGetProperty(section, out JsonElement entries))
            {
                foreach ((string key, string reason) in JsonBaseline.ReasonMap(recorder, FileName, entries, ""))
                {
                    target[key] = reason;
                }
            }
        }

        return (types, members);
    }

    // 只自动加入 ADR-006 允许的引用：边界类型（与枚举值）、数据用法的成员，以及只在 Mugirl.Platform 中使用的其他成员与类型。
    // 业务代码中的服务调用与 UnityStubs 逻辑不加入：它们是要修复的违规，不是待审的例外。
    private static void Update(CheckRecorder recorder, string path, Dictionary<string, string> allowedTypes, Dictionary<string, string> allowedMembers,
        SortedSet<string> usedTypes, SortedDictionary<string, MemberReference> usedMembers, ModuleDefinition module, Dictionary<string, List<string>> where)
    {
        bool PlatformOnly(string key) => where.GetValueOrDefault(key) is { Count: > 0 } sites && sites.All(site => site.StartsWith(PlatformPrefix, StringComparison.Ordinal));
        var newTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string type in usedTypes)
        {
            string? reason = allowedTypes.GetValueOrDefault(type)
                ?? (BoundaryTypes.Contains(type) ? AllowList.ReviewPrefix + " RimWorld API boundary type (ADR-006 rule 1): name the Verse API that uses it"
                    : IsEnum(module, type) ? AllowList.ReviewPrefix + " enum value exchanged with a Verse API"
                    : PlatformOnly(type) ? AllowList.ReviewPrefix + " used only inside Mugirl.Platform"
                    : null);
            if (reason is not null)
            {
                newTypes[type] = reason;
            }
        }

        var newMembers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, MemberReference member) in usedMembers)
        {
            string? reason = allowedMembers.GetValueOrDefault(key)
                ?? (IsDataUse(member) ? AllowList.ReviewPrefix + " data use (ADR-006 rule 1)"
                    : PlatformOnly(key) ? AllowList.ReviewPrefix + (IsEngineBridge(member) ? " engine bridge inside Mugirl.Platform" : " service call wrapped by Mugirl.Platform; copy the #pragma reason")
                    : null);
            if (reason is not null)
            {
                newMembers[key] = reason;
            }
        }

        int added = newTypes.Keys.Count(type => !allowedTypes.ContainsKey(type)) + newMembers.Keys.Count(key => !allowedMembers.ContainsKey(key));
        int removed = allowedTypes.Keys.Count(type => !usedTypes.Contains(type)) + allowedMembers.Keys.Count(key => !usedMembers.ContainsKey(key));

        using (FileStream stream = File.Create(path))
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            writer.WriteStartObject();
            foreach ((string section, Dictionary<string, string> entries) in new[] { ("types", newTypes), ("members", newMembers) })
            {
                writer.WriteStartObject(section);
                foreach ((string key, string reason) in entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(key, reason);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        string body = File.ReadAllText(path);
        File.WriteAllText(path, "// UnityStubs types and members the 1.7 build references, each with the reason it is allowed (ADR-006: RimWorld API\n"
            + "// boundary types as data only; services only inside Mugirl.Platform). B.unity-boundary fails on anything missing here and\n"
            + "// on entries no longer used. Member keys: Type::field, Type::Method(ParameterTypes). Regenerate with --update-baselines,\n"
            + "// then replace every \"REVIEW:\" reason.\n" + body.ReplaceLineEndings("\n") + "\n");
        allowedTypes.Clear();
        allowedMembers.Clear();
        foreach ((string key, string reason) in newTypes)
        {
            allowedTypes[key] = reason;
        }

        foreach ((string key, string reason) in newMembers)
        {
            allowedMembers[key] = reason;
        }

        recorder.Info("Baselines/" + FileName, $"Rewritten (--update-baselines): {added} entr(y/ies) added with a REVIEW reason, {removed} stale entr(y/ies) removed; violations outside Mugirl.Platform are not added.");
    }

    private static bool IsEnum(ModuleDefinition module, string typeName)
        => module.GetTypeReferences().FirstOrDefault(reference => CecilNames.Type(reference) == typeName) is { } reference && CecilNames.TryResolve(reference)?.IsEnum == true;

    private static string? Describe(Dictionary<string, List<string>> where, string key)
    {
        if (!where.TryGetValue(key, out List<string>? list) || list.Count == 0)
        {
            return null;
        }

        IEnumerable<string> sites = list.Order(StringComparer.Ordinal).Take(4);
        return string.Join("; ", sites) + (list.Count > 4 ? $"; +{list.Count - 4} more" : "");
    }
}
