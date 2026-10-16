using System.Reflection;
using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Reporting;
using RimWorld;
using Verse;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. Mod 引用的每张贴图都在游戏查找的位置、以其 Graphic 类加载的形式存在：代码字面量（<c>ContentFinder</c>、<c>GraphicDatabase</c>、
/// <c>MaterialPool</c>、<c>GraphicData.texPath</c>）与 XML（带继承 <c>graphicClass</c> 的 <c>graphicData</c>、按 <c>Graphic_Multi</c>
/// 加载的渲染节点 <c>texPath</c>/<c>texPaths</c>（及女性变体）、按体型的服装穿戴图、MaterialDef 贴图，以及名为
/// <c>…texPath</c>/<c>…iconPath</c>/<c>…texturePath</c> 的字符串字段，以及本 Mod 类型中以 <c>Path</c> 结尾的字段）。1.7 区分大小写查找 Mod 贴图，只靠忽略大小写才找到的算缺失。
/// </summary>
internal static class TextureCheck
{
    private static readonly string[] PathFieldSuffixes = ["texpath", "texpaths", "iconpath", "iconpaths", "texturepath", "texturepaths"];

    public static void Run(Report report, ContentModel content, IReadOnlyList<LookupSite> sites, AllowList dynamic)
        => report.Run("C.textures", "Texture paths in code and XML resolve", recorder => Check(recorder, content, sites, dynamic));

    private static void Check(CheckRecorder recorder, ContentModel content, IReadOnlyList<LookupSite> sites, AllowList dynamic)
    {
        int count = 0;
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (LookupSite site in sites.Where(s => s.Kind is LookupKind.TexturePath or LookupKind.TextureFolder))
        {
            if (LookupChecks.HandleDynamic(recorder, site, dynamic, reported))
            {
                continue;
            }

            foreach (string path in site.Value.Values!)
            {
                count++;
                (TextureMatch match, string form) = site.Kind == LookupKind.TextureFolder
                    ? (content.Textures.FolderHasTextures(path) ? TextureMatch.Found : TextureMatch.Missing, "folder")
                    : TextureRules.Check(content.Textures, path, site.GraphicClass);
                Report(recorder, $"texture '{path}'", match, form, site.Location, probe: !site.ReportsFailure);
            }
        }

        dynamic.ReportProblems(recorder, key => key.StartsWith("texture\t", StringComparison.Ordinal));
        count += CheckXml(recorder, content);
        recorder.Summary = $"{count} texture references checked";
    }

    private static int CheckXml(CheckRecorder recorder, ContentModel content)
    {
        int count = 0;
        string root = content.Catalog.UnderTest.RootDir;
        string[] bodyTypes = content.Defs.All.Where(e => e.Type == typeof(BodyTypeDef)).Select(e => e.DefName).Distinct().ToArray();
        string current = "";
        string file = "";
        var walker = new XmlTypeWalker(content.Types, content.Catalog.Full)
        {
            ObjectVisited = (node, type) =>
            {
                if (typeof(GraphicData).IsAssignableFrom(type) && Text(node, "texPath") is { } texPath)
                {
                    count++;
                    Type? graphicClass = Text(node, "graphicClass") is { } name ? content.Types.GetTypeInAnyAssembly(name) : null;
                    (TextureMatch match, string form) = TextureRules.Check(content.Textures, texPath, graphicClass);
                    Report(recorder, $"{current}: texture '{texPath}'", match, $"{form} ({graphicClass?.Name ?? "no graphicClass"})", file, probe: false);
                }
                else if (typeof(MaterialTextureProperty).IsAssignableFrom(type) && Text(node, "path") is { } materialPath)
                {
                    count++;
                    Report(recorder, $"{current}: texture '{materialPath}'", content.Textures.Find(materialPath), "file", file, probe: false);
                }
            },
            StringValue = (node, field, value) =>
            {
                string path = value.Trim();
                if (path.Length == 0 || (field.DeclaringType == typeof(GraphicData) && field.Name == "texPath")
                    || (field.DeclaringType == typeof(MaterialTextureProperty) && field.Name == "path"))
                {
                    return;
                }

                if (field.DeclaringType == typeof(ApparelProperties) && field.Name is "wornGraphicPath" or "wornGraphicPaths")
                {
                    count++;
                    Report(recorder, $"{current}: worn graphic '{path}'", TextureRules.CheckWorn(content.Textures, path, bodyTypes), "multi, optionally per body type", file, probe: false);
                    return;
                }

                // 渲染节点按 Graphic_Multi 加载贴图（PawnRenderNode.GraphicFor），除非节点类重写了它。
                if (field.DeclaringType == typeof(PawnRenderNodeProperties) && field.Name is "texPath" or "texPaths" or "texPathFemale" or "texPathsFemale")
                {
                    count++;
                    (TextureMatch match, string form) = TextureRules.Check(content.Textures, path, typeof(Graphic_Multi));
                    Report(recorder, $"{current}: {field.Name} '{path}'", match, form, file, probe: false);
                    return;
                }

                if (IsPathField(field))
                {
                    count++;
                    TextureMatch single = content.Textures.Find(path);
                    TextureMatch any = TextureRules.Check(content.Textures, path, null).Match;
                    if (single != TextureMatch.Found && any == TextureMatch.Found)
                    {
                        recorder.Info($"{current}: {field.Name} '{path}'", "Not a single texture but a multi/folder graphic exists at this path; verify the field's loader.", file);
                    }
                    else
                    {
                        Report(recorder, $"{current}: {field.Name} '{path}'", single, "file", file, probe: false);
                    }
                }
            },
        };

        foreach (XmlElement node in content.Corpus.Defs)
        {
            NodeSource? source = content.Corpus.SourceOf(node);
            if (!ContentModel.IsOurs(source?.Pack) || node.GetAttribute("Abstract").Equals("true", StringComparison.OrdinalIgnoreCase)
                || !DefIndex.MayRequireSatisfied(node, content.Catalog.Full))
            {
                continue;
            }

            // 解析继承后的节点带有继承来的字段（父 Def 的 graphicClass 决定加载形式）。
            XmlElement resolved = content.Inheritance.Resolve(node);
            current = $"{node.Name}[{node["defName"]?.InnerText.Trim()}]";
            file = XmlPaths.File(root, source!.File);
            walker.WalkDef(resolved, []);
        }

        return count;
    }

    private static void Report(CheckRecorder recorder, string subject, TextureMatch match, string form, string? location, bool probe)
    {
        switch (match)
        {
            case TextureMatch.Missing when probe:
                recorder.Info(subject, $"Not found ({form}); the call passes reportFailure: false and handles that.", location);
                break;
            case TextureMatch.Missing:
                recorder.Fail(subject, $"Texture not found ({form}) in this mod, its dependencies, the DLCs or the base game.", location);
                break;
            case TextureMatch.CaseMismatch:
                recorder.Warn(subject, "Found only by ignoring case (base resources resolve that; mod textures do not).", location);
                break;
            case TextureMatch.ContentCaseMismatch:
                recorder.Fail(subject, "Found only by ignoring case: RimWorld 1.7 looks up mod textures case-sensitively, so it renders as missing.", location);
                break;
            case TextureMatch.OptionalContentOnly:
                recorder.Warn(subject, "Found only in optional content (a DLC or mod the mod does not require).", location);
                break;
        }
    }

    // 本 Mod 类型中以 Path/Paths 结尾的字符串字段全部是贴图路径（southPath、sidePath 与各 …IconPath），一并按单张贴图核对。
    private static bool IsPathField(FieldInfo field)
        => PathFieldSuffixes.Any(suffix => field.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            || (field.DeclaringType?.Namespace is { } ns && (ns == "Mugirl" || ns.StartsWith("Mugirl.", StringComparison.Ordinal))
                && (field.Name.EndsWith("Path", StringComparison.Ordinal) || field.Name.EndsWith("Paths", StringComparison.Ordinal)));

    private static string? Text(XmlElement node, string child) => node[child]?.InnerText.Trim() is { Length: > 0 } text ? text : null;
}
