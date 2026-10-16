using System.Xml;

namespace MugirlRace.Validation.Content;

/// <summary>统一 Def 文档中节点的稳定可读位置（用作结果的主题）。</summary>
internal static class XmlPaths
{
    /// <summary>例如 <c>ThingDef[Mugirl_A]/comps/li[2]/compClass</c>。</summary>
    public static string Of(XmlNode node)
    {
        var segments = new List<string>();
        for (XmlNode? current = node; current is XmlElement element && element.ParentNode is XmlElement parent; current = parent)
        {
            if (parent.ParentNode is XmlDocument)
            {
                string id = element["defName"]?.InnerText.Trim() ?? element.GetAttribute("Name");
                segments.Add(id.Length == 0 ? element.Name : $"{element.Name}[{id}]");
                break;
            }

            if (element.Name == "li")
            {
                int index = 1;
                for (XmlNode? sibling = element.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
                {
                    if (sibling is XmlElement { Name: "li" })
                    {
                        index++;
                    }
                }

                segments.Add($"li[{index}]");
            }
            else
            {
                segments.Add(element.Name);
            }
        }

        segments.Reverse();
        return string.Join("/", segments);
    }

    /// <summary>相对 Mod 根目录、使用正斜杠的文件路径。</summary>
    public static string File(string modRoot, string path) => Path.GetRelativePath(modRoot, path).Replace('\\', '/');
}
