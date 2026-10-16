using System.Xml;

namespace MugirlRace.Validation.Legacy;

/// <summary>
/// 游戏（Core 与 DLC）和依赖 Mod 定义的 defName。旧内容树可能覆盖其中的 Def（例如原版思维树 <c>HumanlikeConstant</c>），存档里也到处
/// 是这些名字：它们不是本 Mod 的名字，存档观测扫描不把它们记成 Mugirl 的 Def。
/// </summary>
internal static class ForeignDefNames
{
    /// <summary><paramref name="modRoots"/> 下每个 <c>Defs</c> 目录（任意层级）中 XML 的 <c>defName</c> 文本。</summary>
    public static HashSet<string> Collect(IEnumerable<string> modRoots)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreWhitespace = true };
        foreach (string root in modRoots.Where(Directory.Exists))
        {
            foreach (string defs in Directory.EnumerateDirectories(root, "Defs", SearchOption.AllDirectories))
            {
                foreach (string file in Directory.EnumerateFiles(defs, "*.xml", SearchOption.AllDirectories))
                {
                    try
                    {
                        using XmlReader reader = XmlReader.Create(file, settings);
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element && reader.Name == "defName" && !reader.IsEmptyElement)
                            {
                                string value = reader.ReadElementContentAsString().Trim();
                                if (value.Length > 0)
                                {
                                    names.Add(value);
                                }
                            }
                        }
                    }
                    catch (XmlException)
                    {
                        // 游戏同样跳过无法解析的文件。
                    }
                }
            }
        }

        return names;
    }
}
