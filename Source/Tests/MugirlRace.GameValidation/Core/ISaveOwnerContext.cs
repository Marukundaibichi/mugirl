using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>求值 <see cref="SaveOwnerSelector"/> 时需要的存档外知识：类型映射与 Def 带有哪些组件。</summary>
internal interface ISaveOwnerContext
{
    /// <summary>元素的 <c>Class</c> 经升级清单的类型映射后的名字；没有 <c>Class</c> 时为 null。</summary>
    string? MappedClassOf(XElement element);

    /// <summary>
    /// 根类型 <paramref name="root"/>（<c>ThingDef</c> 或 <c>HediffDef</c>）中名为 <paramref name="defName"/> 的 Def 是否带有组件类
    /// <paramref name="compClass"/>（新名）。旧 defName 按旧构建的 Def→组件关系判断，新 defName 按当前构建判断。
    /// </summary>
    bool DefHasComp(string root, string defName, string compClass);
}
