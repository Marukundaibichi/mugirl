using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary><c>hediffSet/hediffs/li</c> 中的健康状态元素与其 <c>def</c> 子元素的原文。</summary>
internal readonly record struct IndexedHediff(XmlElement Element, string Def);
