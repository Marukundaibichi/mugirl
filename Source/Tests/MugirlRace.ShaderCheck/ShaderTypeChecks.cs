using System.Xml.Linq;
using Waylith.Rendering;

namespace Mugirl.ShaderCheck;

/// <summary>
/// 本模组 1.7 XML 中的 ShaderTypeDef 与 <c>&lt;shaderType&gt;</c> 引用。1.7 中 shader type 解析为其 shaderPath 最后一段命名的
/// ShaderDef；缺失的 ShaderDef 加载时不报错，首次绘制时回退到默认着色器。只由某个 DLC 定义的 ShaderTypeDef，在没有该 DLC 的玩家那里是
/// 加载错误，除非引用处有 MayRequire，或所在目录只在该 DLC 启用时加载（LoadFolders.xml 的 IfModActive）。
/// </summary>
internal static class ShaderTypeChecks
{
    private const string LudeonPrefix = "Ludeon.RimWorld.";

    /// <summary>运行检查。</summary>
    public static void Run(CheckPaths paths, DefXmlIndex index, IReadOnlyDictionary<string, Shader> shaders, CheckReport report)
    {
        List<ShaderTypeDefEntry> all = [.. index.OfType("ShaderTypeDef").Select(ShaderTypeDefEntry.Read)];
        Dictionary<string, string> packageByType = new(StringComparer.Ordinal);
        foreach (ShaderTypeDefEntry type in all.Where(type => !type.Source.IsOwn))
        {
            packageByType.TryAdd(type.DefName, type.Source.Package);
        }

        CheckReport.Section("The mod's ShaderTypeDefs");
        foreach (ShaderTypeDefEntry type in all.Where(type => type.Source.IsOwn))
        {
            if (!packageByType.TryAdd(type.DefName, DefXmlIndex.OwnPackage))
            {
                report.Fail($"{type.Source.Location}: ShaderTypeDef {type.DefName} is already defined by {packageByType[type.DefName]}.");
            }

            if (!type.DefName.StartsWith(GlslSourceChecks.Prefix, StringComparison.Ordinal))
            {
                report.Fail($"{type.Source.Location}: the mod's ShaderTypeDefs must be named {GlslSourceChecks.Prefix}*.");
            }

            CheckShaderPath(type, "shaderPath", type.ShaderPath, shaders, report);
            if (type.UiShaderPath is not null)
            {
                CheckShaderPath(type, "uiShaderPath", type.UiShaderPath, shaders, report);
            }
        }

        CheckReport.Section("shaderType references in the mod's 1.7 XML");
        HashSet<string> required = RequiredPackages(paths);
        Dictionary<string, int> uses = new(StringComparer.Ordinal);
        foreach ((XDocument document, string file, string? activeWith) in index.OwnDocuments)
        {
            string? folderPackage = activeWith is null ? null : ExpansionOf(activeWith);
            foreach (XElement element in document.Descendants("shaderType"))
            {
                string name = element.Value.Trim();
                uses[name] = uses.GetValueOrDefault(name) + 1;
                if (!packageByType.TryGetValue(name, out string? package))
                {
                    report.Fail($"{XmlDef.Describe(element, file)}: shaderType '{name}' is not defined by Core, an expansion or this mod.");
                }
                else if (package != DefXmlIndex.OwnPackage && !required.Contains(package)
                         && !string.Equals(package, folderPackage, StringComparison.OrdinalIgnoreCase) && !MayRequire(element).Contains(package))
                {
                    report.Fail($"{XmlDef.Describe(element, file)}: shaderType '{name}' is defined by {package}, which the mod does not require (no MayRequire).");
                }
            }
        }

        CheckReport.Pass(uses.Count == 0
            ? "no shaderType references."
            : string.Join(", ", uses.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} x{pair.Value} ({packageByType.GetValueOrDefault(pair.Key, "undefined")})")));
    }

    private static void CheckShaderPath(ShaderTypeDefEntry type, string field, string? path, IReadOnlyDictionary<string, Shader> shaders, CheckReport report)
    {
        string shaderDef = string.IsNullOrEmpty(path) ? string.Empty : ShaderTypeDefEntry.ShaderDefNameFor(path);
        if (shaders.TryGetValue(shaderDef, out Shader? shader))
        {
            CheckReport.Pass($"{type.DefName}: {field} '{path}' -> ShaderDef {shader.Key}.");
        }
        else
        {
            report.Fail($"{type.Source.Location}: {type.DefName}: {field} '{path}' names no registered ShaderDef ('{shaderDef}').");
        }
    }

    // Core 与 About.xml 中列为依赖的 DLC 总是与本模组一起加载。
    private static HashSet<string> RequiredPackages(CheckPaths paths)
    {
        HashSet<string> required = new(StringComparer.Ordinal) { "Core" };
        XDocument about = XDocument.Load(Path.Combine(paths.ModDir, "About", "About.xml"));
        foreach (XElement id in about.Descendants("modDependencies").Elements("li").Elements("packageId"))
        {
            if (ExpansionOf(id.Value.Trim()) is { } package)
            {
                required.Add(package);
            }
        }

        return required;
    }

    private static HashSet<string> MayRequire(XElement element)
    {
        HashSet<string> packages = new(StringComparer.OrdinalIgnoreCase);
        for (XElement? current = element; current is not null; current = current.Parent)
        {
            string? value = current.Attribute("MayRequire")?.Value;
            foreach (string id in value?.Split(',') ?? [])
            {
                if (ExpansionOf(id.Trim()) is { } package)
                {
                    packages.Add(package);
                }
            }
        }

        return packages;
    }

    private static string? ExpansionOf(string packageId)
        => packageId.StartsWith(LudeonPrefix, StringComparison.OrdinalIgnoreCase) ? packageId[LudeonPrefix.Length..] : null;
}
