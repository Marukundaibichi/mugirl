using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 一个步骤计划的 DOM 编辑与附带效果。计划阶段只记录，不修改 DOM；<see cref="Apply"/> 依次执行安全原语。
/// </summary>
/// <remarks>
/// 原语遵守存档 DOM 的约束：不插入文本或注释节点到元素之间（<c>Scribe_Collections</c> 把每个子节点交给
/// <c>SaveableFromNode</c>，非元素节点会让外层对象整个丢失），只改写叶元素文本，改名用新建元素并移入原有属性与子节点。
/// </remarks>
internal sealed class EditPlan(UpgradeContext context)
{
    private readonly List<DomEdit> edits = [];

    internal int Count => edits.Count;

    internal IReadOnlyList<DomEdit> Edits => edits;

    /// <summary>
    /// 失效的加载 ID：应用时自动加入被删除或置空子树中已索引的物品、账单与 Lord，步骤也可以显式加入；
    /// 最后的加载 ID 步骤把指向它们的引用置空或从容器中删除。
    /// </summary>
    internal List<string> RemovedLoadIds { get; } = [];

    /// <summary>本步骤删除的配方；加载 ID 步骤置空 <c>Bill_&lt;recipe&gt;_&lt;n&gt;</c> 引用。</summary>
    internal List<string> RemovedRecipes { get; } = [];

    /// <summary>本步骤改写过 <c>Class</c> 的元素及其原值（旧版证据）。</summary>
    internal Dictionary<XmlElement, string> LegacyClasses { get; } = [];

    /// <summary>本步骤把状态图重置到起始 LordToil 的 Lord 的 <c>loadID</c>；阶段 B 为它们重新进入起始 LordToil。</summary>
    internal List<int> ResetLordIds { get; } = [];

    /// <summary>应用后写入会话数据的标志（传给读档后步骤）。</summary>
    internal List<string> SessionFlags { get; } = [];

    internal void SetAttribute(XmlElement element, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        XmlConvert.VerifyName(name);
        ArgumentNullException.ThrowIfNull(value);
        edits.Add(new DomEdit(DomEditKind.SetAttribute, element, name, value));
    }

    /// <summary>改写叶元素文本；元素有子元素时抛出（计划失败）。</summary>
    internal void SetText(XmlElement element, string text)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(text);
        if (DomEditor.HasElementChild(element))
        {
            throw new InvalidOperationException("Cannot set text on non-leaf element <" + element.Name + ">.");
        }

        edits.Add(new DomEdit(DomEditKind.SetText, element, null, text));
    }

    internal void Rename(XmlElement element, string newName)
    {
        ArgumentNullException.ThrowIfNull(element);
        XmlConvert.VerifyName(newName);
        edits.Add(new DomEdit(DomEditKind.Rename, element, newName, null));
    }

    /// <summary>直接删除元素。只用于所属元素中的标签，或调用方已确认容器允许删除；列表项用 <see cref="RemoveItem"/>。</summary>
    internal void Remove(XmlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        edits.Add(new DomEdit(DomEditKind.Remove, element, null, null));
    }

    /// <summary>
    /// 置空元素：深层对象与 <see cref="NullForm.Value"/> 叶元素换成 <c>IsNull="True"</c> 元素，
    /// <see cref="NullForm.Reference"/> 叶元素的文本改成 <c>null</c>（见 <see cref="NullForm"/>）。
    /// </summary>
    internal void NullOut(XmlElement element, NullForm leafForm)
    {
        ArgumentNullException.ThrowIfNull(element);
        DomEditKind kind = leafForm == NullForm.Reference && !DomEditor.IsDeep(element) ? DomEditKind.NullReference : DomEditKind.NullOut;
        edits.Add(new DomEdit(kind, element, null, null));
    }

    internal void Append(XmlElement parent, string name, string text)
    {
        ArgumentNullException.ThrowIfNull(parent);
        XmlConvert.VerifyName(name);
        ArgumentNullException.ThrowIfNull(text);
        edits.Add(new DomEdit(DomEditKind.Append, parent, name, text));
    }

    /// <summary>计划创建容器；返回尚未挂入文档的元素，继续使用计划原语设置它的内容。</summary>
    internal XmlElement AppendElement(XmlElement parent, string name)
    {
        ArgumentNullException.ThrowIfNull(parent);
        XmlConvert.VerifyName(name);
        XmlElement created = parent.OwnerDocument.CreateElement(name);
        edits.Add(new DomEdit(DomEditKind.AppendElement, parent, null, null, created));
        return created;
    }

    /// <summary>把元素移入另一容器，可同时改标签名；不克隆保存对象、不登记失效 ID。</summary>
    internal void Move(XmlElement element, XmlElement parent, string? newName = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(parent);
        if (newName is not null)
        {
            XmlConvert.VerifyName(newName);
        }

        if (!ReferenceEquals(element.OwnerDocument, parent.OwnerDocument))
        {
            throw new ArgumentException("Cannot move an element between save documents.", nameof(parent));
        }

        edits.Add(new DomEdit(DomEditKind.Move, element, newName, null, parent));
    }

    /// <summary>按所在容器删除一个列表项或深层对象（规则见 <see cref="ContainerRules"/>）。</summary>
    /// <param name="leafForm">只能置空时，叶元素按读取方的写法置空（见 <see cref="NullForm"/>）。</param>
    internal ItemRemoval RemoveItem(XmlElement item, NullForm leafForm)
    {
        ArgumentNullException.ThrowIfNull(item);
        return context.Containers.PlanRemoval(item, this, context, leafForm);
    }

    /// <summary>应用编辑（见 <see cref="DomEditor"/>），返回实际生效的编辑数；被删除子树中对象的加载 ID 记入 <see cref="RemovedLoadIds"/>。</summary>
    internal int Apply(SaveIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        List<XmlElement> removed = [];
        int applied = DomEditor.Apply(edits, index, removed);
        foreach (XmlElement root in removed)
        {
            index.CollectLoadIds(root, RemovedLoadIds);
        }

        return applied;
    }
}
