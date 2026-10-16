using System.Globalization;
using System.Reflection;
using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Reporting;
using Verse;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. 原生 Shader（RimWorld 1.7 的 ShaderDef、ShaderTypeDef、MaterialDef 与 GLSL，ADR-006 规则 5）中不需要引擎的全部规则。需要引擎的
/// 规则（注册、材质布局、阶段接口、场景抓取、Slang 编译、MaterialDef 与 <c>ShaderIds.For</c> 属性对照布局）由 MugirlRace.ShaderCheck 负责：
/// <list type="bullet">
/// <item>每个 ShaderTypeDef 的 <c>shaderPath</c>/<c>uiShaderPath</c> 以某个 ShaderDef 名结尾（1.7 按最后一段解析，找不到时静默使用引擎默认）；</item>
/// <item>Mod 的 ShaderDef、ShaderTypeDef、MaterialDef 带前缀，且不与其他内容包的同类 Def 重名；ShaderDef 有程序，其 GLSL 源与 include
/// 能解析，带前缀的源名解析到 Mod 内、其他源名不解析到 Mod 内；</item>
/// <item>MaterialDef 指向存在的 Shader，渲染队列有效，属性值可解析（<c>colors</c> 下没有 HDR 颜色：Verse 会按 0-255 读取）；</item>
/// <item><c>shaderType</c> 引用与 <c>ShaderDatabase.LoadShader</c> 字面量指向存在的 Shader，可选 DLC 定义的 <c>shaderType</c> 有 <c>MayRequire</c>；</item>
/// <item><see cref="GlslSourceRules"/> 的 GLSL 文件规则。</item>
/// </list>
/// 前缀从被测程序集读取（<c>ShaderSelfTest.ShaderNamePrefix</c>），不复制常量。移植自 hk 的 ShaderCheck。
/// </summary>
internal static class ShaderCheck
{
    /// <summary>保存 Mod 着色器名前缀的正式常量。</summary>
    public const string PrefixSource = "Mugirl.Platform.Rendering.ShaderSelfTest.ShaderNamePrefix";

    public static void Run(Report report, ValidationOptions options, ContentModel content, IReadOnlyList<LookupSite> sites, AllowList dynamic, Assembly mod)
        => report.Run("C.shaders", "ShaderDefs, ShaderTypeDefs, MaterialDefs and GLSL sources are consistent", recorder => Check(recorder, options, content, sites, dynamic, mod));

    /// <summary><paramref name="mod"/> 中的前缀常量；程序集没有它时为 <see langword="null"/>。</summary>
    public static string? ReadPrefix(Assembly mod)
    {
        int split = PrefixSource.LastIndexOf('.');
        return mod.GetType(PrefixSource[..split])
            ?.GetField(PrefixSource[(split + 1)..], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetRawConstantValue() as string;
    }

    private static void Check(CheckRecorder recorder, ValidationOptions options, ContentModel content, IReadOnlyList<LookupSite> sites, AllowList dynamic, Assembly mod)
    {
        if (ReadPrefix(mod) is not { Length: > 0 } prefix)
        {
            recorder.Fail(PrefixSource, "Not found in the assembly under test: the shader naming rules have no prefix to check.");
            return;
        }

        string root = content.Catalog.UnderTest.RootDir;
        ShaderFiles files = ShaderFiles.Collect(options.GameDir, content.Catalog);
        Dictionary<string, string> shaderTypePaths = ShaderTypePaths(content);
        var stageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int shaderDefs = 0;
        int materialDefs = 0;

        foreach (XmlElement node in content.Corpus.Defs)
        {
            NodeSource? source = content.Corpus.SourceOf(node);
            if (!ContentModel.IsOurs(source?.Pack) || node.GetAttribute("Abstract").Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Type? type = content.Types.GetTypeInAnyAssembly(node.GetAttribute("Class") is { Length: > 0 } cls ? cls : node.Name);
            string defName = node["defName"]?.InnerText.Trim() ?? "";
            string file = XmlPaths.File(root, source!.File);
            XmlElement resolved = content.Inheritance.Resolve(node);
            if (type is not null && typeof(ShaderDef).IsAssignableFrom(type))
            {
                shaderDefs++;
                CheckNaming(recorder, content, typeof(ShaderDef), $"ShaderDef[{defName}]", defName, prefix, file);
                CheckShaderDef(recorder, content, files, resolved, defName, prefix, file, stageNames);
            }
            else if (type is not null && typeof(ShaderTypeDef).IsAssignableFrom(type))
            {
                CheckNaming(recorder, content, typeof(ShaderTypeDef), $"ShaderTypeDef[{defName}]", defName, prefix, file);
                string path = resolved["shaderPath"]?.InnerText.Trim() ?? "";
                CheckShaderPath(recorder, content, $"ShaderTypeDef[{defName}] shaderPath '{path}'", path, file);
                if (resolved["uiShaderPath"]?.InnerText.Trim() is { Length: > 0 } uiPath)
                {
                    CheckShaderPath(recorder, content, $"ShaderTypeDef[{defName}] uiShaderPath '{uiPath}'", uiPath, file);
                }
            }
            else if (type is not null && typeof(MaterialDef).IsAssignableFrom(type))
            {
                materialDefs++;
                CheckNaming(recorder, content, typeof(MaterialDef), $"MaterialDef[{defName}]", defName, prefix, file);
                CheckMaterialDef(recorder, content, resolved, defName, file);
            }
        }

        CheckShaderTypeReferences(recorder, content, shaderTypePaths);
        GlslSourceRules.Check(recorder, files, root, prefix, stageNames);

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (LookupSite site in sites.Where(s => s.Kind == LookupKind.ShaderPath))
        {
            if (LookupChecks.HandleDynamic(recorder, site, dynamic, reported))
            {
                continue;
            }

            foreach (string path in site.Value.Values!)
            {
                CheckShaderPath(recorder, content, $"ShaderDatabase.LoadShader('{path}')", path, site.Location);
            }
        }

        dynamic.ReportProblems(recorder, key => key.StartsWith("shader\t", StringComparison.Ordinal));
        recorder.Summary = $"{shaderDefs} ShaderDef(s), {materialDefs} MaterialDef(s) and {files.Ours.Count} GLSL file(s) of the mod (prefix '{prefix}')";
    }

    // 这些 Def 名是全局的、按名字解析：要带前缀，且不与其他内容包的同类 Def 重名。
    private static void CheckNaming(CheckRecorder recorder, ContentModel content, Type defType, string subject, string defName, string prefix, string file)
    {
        if (!defName.StartsWith(prefix, StringComparison.Ordinal))
        {
            recorder.Fail(subject, $"{defType.Name} names are global and resolved by name: prefix it with {prefix}.", file);
        }

        List<DefEntry> sameName = [.. content.Defs.FindAny(defName).Where(e => e.Type == defType && !e.Implied)];
        List<string> owners = sameName.Where(e => !e.Pack.IsUnderTest).Select(e => e.Pack.PackageId).Distinct().ToList();
        if (owners.Count > 0)
        {
            recorder.Fail(subject, $"Another pack defines a {defType.Name} with this name: " + string.Join(", ", owners), file);
        }

        if (sameName.Count(e => e.Pack.IsUnderTest) > 1)
        {
            recorder.Fail(subject, $"The mod defines more than one {defType.Name} with this name.", file);
        }
    }

    private static void CheckShaderDef(CheckRecorder recorder, ContentModel content, ShaderFiles files, XmlElement def, string defName, string prefix, string file, HashSet<string> stageNames)
    {
        string subject = $"ShaderDef[{defName}]";
        XmlElement? program = def["shaderProgram"];
        if (program is null)
        {
            recorder.Fail(subject, "No shaderProgram: the engine never registers it and every draw logs \"not registered\" and uses the default shader.", file);
            return;
        }

        foreach ((string field, string fallback) in new[] { ("vertexShaderFileName", "SceneDefault.vert.glsl"), ("fragmentShaderFileName", "SceneDefault.frag.glsl") })
        {
            string name = program[field]?.InnerText.Trim() is { Length: > 0 } text ? text : fallback;
            stageNames.Add(Path.GetFileName(name));
            CheckSource(recorder, files, $"{subject} {field} '{name}'", name, prefix, file);
        }

        foreach (XmlElement entry in program["passShaderOverrides"]?.ChildNodes.OfType<XmlElement>() ?? [])
        {
            string name = entry["value"]?.InnerText.Trim() ?? entry.InnerText.Trim();
            if (name.Length > 0)
            {
                stageNames.Add(Path.GetFileName(name));
                CheckSource(recorder, files, $"{subject} passShaderOverrides '{name}'", name, prefix, file);
            }
        }

        foreach (string reference in new[] { "complexVersion", "iconVersion" })
        {
            if (def[reference]?.InnerText.Trim() is { Length: > 0 } other && content.Defs.Find(typeof(ShaderDef), other).Count == 0)
            {
                recorder.Fail($"{subject} {reference} '{other}'", "No such ShaderDef.", file);
            }
        }
    }

    // 源必须能解析；带前缀（Mod 所有）的名字必须解析到 Mod 内，其他名字不能解析到 Mod 内（Mod 文件遮蔽原版名字会替换整个游戏的那个程序）。
    private static void CheckSource(CheckRecorder recorder, ShaderFiles files, string subject, string name, string prefix, string file)
    {
        string? resolved = files.Resolve(name);
        bool ownName = Path.GetFileName(name).StartsWith(prefix, StringComparison.Ordinal);
        if (resolved is null)
        {
            recorder.Fail(subject, "GLSL source not found in any loaded Shaders folder or Rendering/Shaders.", file);
        }
        else if (ownName && !files.IsOurs(resolved))
        {
            recorder.Fail(subject, $"A mod-owned source name resolves outside the mod: {resolved}.", file);
        }
        else if (!ownName && files.IsOurs(resolved))
        {
            recorder.Fail(subject, $"Resolves to a mod file without the {prefix} prefix ({resolved}): it would shadow the vanilla source for every ShaderDef.", file);
        }
    }

    private static void CheckMaterialDef(CheckRecorder recorder, ContentModel content, XmlElement def, string defName, string file)
    {
        string subject = $"MaterialDef[{defName}]";
        string shader = def["shader"]?.InnerText.Trim() ?? "";
        if (shader.Length == 0 || content.Defs.Find(typeof(ShaderDef), shader).Count == 0)
        {
            recorder.Fail($"{subject} shader '{shader}'", "No such ShaderDef: the material cannot be created.", file);
        }

        if (def["renderQueue"]?.InnerText.Trim() is { Length: > 0 } queue
            && (!int.TryParse(queue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < -1))
        {
            recorder.Fail($"{subject} renderQueue '{queue}'", "Not an integer render queue of -1 (the shader's) or above.", file);
        }

        foreach (XmlElement item in def["floats"]?.ChildNodes.OfType<XmlElement>() ?? [])
        {
            string text = item["value"]?.InnerText.Trim() ?? "";
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                recorder.Fail($"{subject} floats {item["name"]?.InnerText.Trim()}", $"'{text}' is not a number.", file);
            }
        }

        foreach (string list in new[] { "colors", "vectors" })
        {
            foreach (XmlElement item in def[list]?.ChildNodes.OfType<XmlElement>() ?? [])
            {
                string text = item["value"]?.InnerText.Trim() ?? "";
                string property = $"{subject} {list} {item["name"]?.InnerText.Trim()}";
                if (!TryParseVector(text, out float[] components))
                {
                    recorder.Fail(property, $"'{text}' is not a vector of 3 or 4 numbers.", file);
                }
                else if (list == "colors" && components.Take(3).Any(component => component > 1f))
                {
                    // Verse.ParseHelper.ParseColor 在 r、g、b 任一大于 1 时把整个颜色按 0-255 字节读取。
                    recorder.Fail(property, $"HDR colour {text} under <colors> would be parsed as 0-255 bytes; use <vectors>.", file);
                }
            }
        }
    }

    // Mod 的 Def 中任何位置的 shaderType 引用：ShaderTypeDef 存在、路径能解析；只由 Mod 不依赖的 DLC 定义的要有 MayRequire
    // （否则没有该 DLC 的玩家加载时报错）。
    private static void CheckShaderTypeReferences(CheckRecorder recorder, ContentModel content, Dictionary<string, string> shaderTypePaths)
    {
        var walker = new XmlTypeWalker(content.Types, content.Catalog.Full)
        {
            DefReference = (node, field, defType, name) =>
            {
                if (defType != typeof(ShaderTypeDef) || name.Length == 0)
                {
                    return;
                }

                if (!shaderTypePaths.TryGetValue(name, out string? path))
                {
                    recorder.Fail($"shaderType '{name}'", "No such ShaderTypeDef.", XmlPaths.Of(node));
                    return;
                }

                CheckShaderPath(recorder, content, $"shaderType '{name}' (shaderPath '{path}')", path, XmlPaths.Of(node));
                if (!content.Defs.Find(typeof(ShaderTypeDef), name).Any(entry => entry.InMinimal) && !GuardedByMayRequire(node))
                {
                    string packs = string.Join(", ", content.Defs.Find(typeof(ShaderTypeDef), name).Select(entry => entry.Pack.PackageId).Distinct());
                    recorder.Fail($"shaderType '{name}'", $"Defined only by {packs}, which the mod does not require, and not guarded by MayRequire.", XmlPaths.Of(node));
                }
            },
        };
        foreach (XmlElement node in content.Corpus.Defs.Where(n => ContentModel.IsOurs(content.Corpus.SourceOf(n)?.Pack)))
        {
            walker.WalkDef(node, []);
        }
    }

    private static bool GuardedByMayRequire(XmlNode node)
    {
        for (XmlNode? current = node; current is XmlElement element; current = current.ParentNode)
        {
            if (element.HasAttribute("MayRequire") || element.HasAttribute("MayRequireAnyOf"))
            {
                return true;
            }
        }

        return false;
    }

    // ShaderDatabase.LoadShader：最后一个 '/' 之后的部分必须是 ShaderDef 名，否则绘制静默回退。
    private static void CheckShaderPath(CheckRecorder recorder, ContentModel content, string subject, string path, string? location)
    {
        string name = path[(path.LastIndexOf('/') + 1)..];
        if (name.Length == 0 || content.Defs.Find(typeof(ShaderDef), name).Count == 0)
        {
            recorder.Fail(subject, $"No ShaderDef named '{name}': RimWorld 1.7 resolves shaders by the last path segment and otherwise renders with the engine default (no load error).", location);
        }
    }

    private static bool TryParseVector(string text, out float[] components)
    {
        string[] parts = text.Trim().TrimStart('(', 'R', 'G', 'B', 'A').TrimEnd(')').Split(',');
        components = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out components[i]))
            {
                return false;
            }
        }

        return parts.Length is 3 or 4;
    }

    // 所有内容包的 ShaderTypeDef defName -> shaderPath。
    private static Dictionary<string, string> ShaderTypePaths(ContentModel content)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XmlElement node in content.Corpus.Defs)
        {
            Type? type = content.Types.GetTypeInAnyAssembly(node.GetAttribute("Class") is { Length: > 0 } cls ? cls : node.Name);
            if (type is not null && typeof(ShaderTypeDef).IsAssignableFrom(type) && node["defName"]?.InnerText.Trim() is { Length: > 0 } defName)
            {
                result[defName] = content.Inheritance.Resolve(node)["shaderPath"]?.InnerText.Trim() ?? "";
            }
        }

        return result;
    }
}
