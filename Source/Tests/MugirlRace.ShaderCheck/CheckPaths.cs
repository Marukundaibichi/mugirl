using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Xml.Linq;

namespace Mugirl.ShaderCheck;

/// <summary>检查所用的模组与游戏目录。</summary>
/// <param name="ModDir">模组根目录（含 About/About.xml 与 1.7/）。</param>
/// <param name="GameDir">RimWorld 1.7 安装目录（含 Waylith.dll 与 Data/）。</param>
internal sealed record CheckPaths(string ModDir, string GameDir)
{
    /// <summary>模组的 1.7 内容目录。</summary>
    public string ModContentDir => Path.Combine(ModDir, "1.7");

    /// <summary>模组 GLSL 的主目录（1.7/Shaders）。</summary>
    public string ModShaderDir => Path.Combine(ModContentDir, "Shaders");

    /// <summary>1.7 的 C# 源码。</summary>
    public string SourceDir => Path.Combine(ModDir, "Source", "MugirlRace");

    /// <summary>集中声明 ShaderDef 与 MaterialDef 的 DefOf 类。</summary>
    public string RenderDefOfFile => Path.Combine(SourceDir, "Rendering", "RenderingDefOf.cs");

    /// <summary>
    /// LoadFolders.xml 中 v1.7 的全部加载目录，按加载顺序（先 "/" 与 1.7，再 1.7/Mods/*）。带 IfModActive 的目录同样列出，
    /// 并记录它依赖的 packageId（null 表示总是加载）。
    /// </summary>
    public IReadOnlyList<LoadFolder> LoadFolders()
    {
        string file = Path.Combine(ModDir, "LoadFolders.xml");
        XElement? v17 = File.Exists(file) ? XDocument.Load(file).Root?.Element("v1.7") : null;
        if (v17 is null)
        {
            return [new LoadFolder(ModDir, null), new LoadFolder(ModContentDir, null)];
        }

        return [.. v17.Elements("li").Select(li => new LoadFolder(
            Path.GetFullPath(Path.Combine(ModDir, li.Value.Trim().TrimStart('/', '\\'))),
            li.Attribute("IfModActive")?.Value.Trim()))];
    }

    /// <summary>
    /// 从命令行（<c>[modDir] [gameDir]</c>）解析目录，缺省时取 Directory.Build.props 写入本程序集的值。
    /// </summary>
    public static bool TryResolve(string[] args, [NotNullWhen(true)] out CheckPaths? paths, [NotNullWhen(false)] out string? problem)
    {
        string? modDir = args.Length > 0 ? args[0] : Metadata("Mugirl.ModDir");
        string? gameDir = args.Length > 1 ? args[1] : Metadata("Mugirl.GameDir");
        paths = null;
        if (string.IsNullOrWhiteSpace(modDir) || !File.Exists(Path.Combine(modDir, "About", "About.xml")))
        {
            problem = $"Mod folder not found: '{modDir}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(gameDir) || !File.Exists(Path.Combine(gameDir, "Waylith.dll")))
        {
            problem = $"RimWorld 1.7 folder not found (no Waylith.dll): '{gameDir}'.";
            return false;
        }

        paths = new CheckPaths(Path.GetFullPath(modDir), Path.GetFullPath(gameDir));
        problem = null;
        return true;
    }

    private static string? Metadata(string key) => typeof(CheckPaths).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == key)?.Value;
}

/// <summary>一个加载目录。</summary>
/// <param name="Path">绝对路径。</param>
/// <param name="IfModActive">LoadFolders.xml 的 IfModActive 条件（packageId），无条件时为 null。</param>
internal sealed record LoadFolder(string Path, string? IfModActive);
