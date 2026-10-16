using System.Xml;
using System.Xml.Linq;

namespace Mugirl.ShaderCheck;

/// <summary>
/// 游戏（Core 与 DLC）和本模组 1.7 加载目录的 Defs XML，按游戏加载顺序：Core、按官方顺序的 DLC、最后是本模组。
/// </summary>
internal sealed class DefXmlIndex
{
    /// <summary>本模组 Def 的包名。</summary>
    public const string OwnPackage = "MugirlRace";

    // Core 之后各 DLC 的官方加载顺序。
    private static readonly string[] DlcOrder = ["Royalty", "Ideology", "Biotech", "Anomaly", "Odyssey"];

    private readonly List<XmlDef> defs = [];
    private readonly List<(XDocument Document, string File, string? ActiveWithPackage)> ownDocuments = [];

    private DefXmlIndex()
    {
    }

    /// <summary>游戏的内容包，按加载顺序（Data/ 下的目录名）。</summary>
    public IReadOnlyList<string> GamePackages { get; private set; } = [];

    /// <summary>本模组 1.7 加载目录中的全部 XML 文档（Defs 与 Patches），供引用扫描使用。</summary>
    public IReadOnlyList<(XDocument Document, string File, string? ActiveWithPackage)> OwnDocuments => ownDocuments;

    /// <summary>加载索引。本模组文件中的 XML 错误记为失败。</summary>
    public static DefXmlIndex Load(CheckPaths paths, CheckReport report)
    {
        var index = new DefXmlIndex();
        string dataDir = Path.Combine(paths.GameDir, "Data");
        List<string> packages = [.. Directory.EnumerateDirectories(dataDir).Select(Path.GetFileName).OfType<string>()
            .Where(name => Directory.Exists(Path.Combine(dataDir, name, "Defs")))
            .OrderBy(LoadRank).ThenBy(name => name, StringComparer.Ordinal)];
        index.GamePackages = packages;
        foreach (string package in packages)
        {
            index.LoadFolder(Path.Combine(dataDir, package, "Defs"), package, isOwn: false, activeWith: null, report);
        }

        // 本模组 LoadFolders.xml v1.7 的全部目录；条件目录也检查（它们在对应模组启用时加载）。
        foreach (LoadFolder folder in paths.LoadFolders())
        {
            index.LoadFolder(Path.Combine(folder.Path, "Defs"), OwnPackage, isOwn: true, folder.IfModActive, report);
            index.LoadOwnDocuments(Path.Combine(folder.Path, "Patches"), folder.IfModActive, report);
        }

        return index;
    }

    /// <summary>类型为 <paramref name="defType"/>（元素名）的全部非抽象 Def，按加载顺序。</summary>
    public IEnumerable<XmlDef> OfType(string defType) => defs.Where(def => def.Element.Name.LocalName == defType);

    private static int LoadRank(string package)
    {
        if (package == "Core")
        {
            return 0;
        }

        int dlc = Array.IndexOf(DlcOrder, package);
        return dlc >= 0 ? 1 + dlc : 1 + DlcOrder.Length;
    }

    private void LoadFolder(string defsDir, string package, bool isOwn, string? activeWith, CheckReport report)
    {
        if (!Directory.Exists(defsDir))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(defsDir, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            XDocument? document = TryLoad(file, isOwn, report);
            if (document?.Root is null)
            {
                continue;
            }

            if (isOwn)
            {
                ownDocuments.Add((document, file, activeWith));
            }

            foreach (XElement element in document.Root.Elements())
            {
                string? isAbstract = element.Attribute("Abstract")?.Value;
                if (!string.Equals(isAbstract, "true", StringComparison.OrdinalIgnoreCase))
                {
                    defs.Add(new XmlDef(element, file, package, isOwn, activeWith));
                }
            }
        }
    }

    private void LoadOwnDocuments(string folder, string? activeWith, CheckReport report)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            XDocument? document = TryLoad(file, isOwn: true, report);
            if (document is not null)
            {
                ownDocuments.Add((document, file, activeWith));
            }
        }
    }

    private static XDocument? TryLoad(string file, bool isOwn, CheckReport report)
    {
        try
        {
            return XDocument.Load(file, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            if (isOwn)
            {
                report.Fail($"{file}: invalid XML: {exception.Message}");
            }

            return null;
        }
    }
}
