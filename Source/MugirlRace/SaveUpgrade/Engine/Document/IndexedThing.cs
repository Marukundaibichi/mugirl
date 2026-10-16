using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 按结构识别的物品元素：首个子元素是 <c>def</c>（<c>Thing.ExposeData</c> 最先写它），其后紧跟 <c>id</c>，
/// 且 id = defName + 数字（或已改写为改名后的 defName + 数字）。<see cref="Def"/> 与 <see cref="Id"/> 是建立索引时存档中的原文。
/// </summary>
internal readonly record struct IndexedThing(XmlElement Element, string Def, string Id);
