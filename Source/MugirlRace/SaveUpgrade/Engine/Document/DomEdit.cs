using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>一条计划中的 DOM 编辑。</summary>
/// <param name="Name">属性名（SetAttribute）、新元素名（Rename、Append）；其他为 null。</param>
/// <param name="Value">属性值或文本（SetAttribute、SetText、Append）；其他为 null。</param>
internal readonly record struct DomEdit(DomEditKind Kind, XmlElement Target, string? Name, string? Value, XmlElement? Related = null);
