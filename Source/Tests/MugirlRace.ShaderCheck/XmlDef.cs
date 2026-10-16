using System.Xml;
using System.Xml.Linq;

namespace Mugirl.ShaderCheck;

/// <summary>从 Defs XML 读到的一个 Def 元素。</summary>
/// <param name="Element">Def 元素（例如 <c>&lt;ShaderDef&gt;</c>）。</param>
/// <param name="File">XML 文件。</param>
/// <param name="Package">"Core"、DLC 目录名，或本模组的 <see cref="DefXmlIndex.OwnPackage"/>。</param>
/// <param name="IsOwn">本模组的 Def 为 true。</param>
/// <param name="ActiveWithPackage">本模组按条件加载的目录（LoadFolders.xml 的 IfModActive）所依赖的 packageId，无条件时为 null。</param>
internal sealed record XmlDef(XElement Element, string File, string Package, bool IsOwn, string? ActiveWithPackage)
{
    /// <summary>defName，缺失时为空字符串。</summary>
    public string DefName => Element.Element("defName")?.Value.Trim() ?? string.Empty;

    /// <summary>文件与行号，用于消息。</summary>
    public string Location => Describe(Element, File);

    /// <summary>格式化 <paramref name="node"/> 在 <paramref name="file"/> 中的位置。</summary>
    public static string Describe(XObject node, string file)
        => node is IXmlLineInfo info && info.HasLineInfo() ? $"{file}:{info.LineNumber}" : file;
}
