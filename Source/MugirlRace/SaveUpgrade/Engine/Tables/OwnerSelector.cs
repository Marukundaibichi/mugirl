using System.Linq;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 选择 Scribe 标签所在的“所属元素”：标签总是所属元素的直接子元素。表格行用它把同名标签限定在正确的对象上。
/// </summary>
/// <remarks>
/// <para>
/// 组件的标签直接写在父物品/健康状态元素里，与原版和其他组件的标签共用一个平面命名空间，所以组件标签只能按
/// “该元素的 def 带有这个组件”来选（<see cref="ThingComp(string)"/>、<see cref="HediffComp(string)"/>）。
/// 判断用的是旧版 def→组件关系（模块表格登记），只有表格里没有的 def（第三方种族）才查当前 Def 目录。
/// </para>
/// <para>
/// 类型名一律用存档写法（见 <see cref="SaveTypeNames"/>）并按类型表映射后的名称比较，所以选择器写新类型名即可同时匹配旧档。
/// <see cref="Text"/> 是写入升级清单的稳定文本。
/// </para>
/// </remarks>
internal abstract class OwnerSelector
{
    private protected OwnerSelector()
    {
    }

    /// <summary>任意元素。只用于原版结构上下文（例如任何 ThingFilter 的 <c>allowedDefs</c>）。</summary>
    internal static OwnerSelector Any { get; } = new AnySelector();

    /// <summary>写入升级清单的选择器文本。</summary>
    internal abstract string Text { get; }

    /// <summary>为了枚举候选元素，存档索引需要按名收集的元素名。</summary>
    internal virtual IEnumerable<string> IndexNames => [];

    /// <summary>元素（映射后的）<c>Class</c> 是给定类型之一。</summary>
    internal static OwnerSelector Class(params Type[] types) => new ClassSelector(Names(types), ComponentList.None);

    /// <summary>元素（映射后的）<c>Class</c> 是给定存档类名之一。</summary>
    internal static OwnerSelector Class(params string[] saveNames) => new ClassSelector(Normalize(saveNames), ComponentList.None);

    /// <summary><c>game/components</c> 中的指定 GameComponent。</summary>
    internal static OwnerSelector GameComponent(Type type) => new ClassSelector(Names([type]), ComponentList.Game);

    /// <summary><c>game/components</c> 中的指定 GameComponent（存档类名）。</summary>
    internal static OwnerSelector GameComponent(string saveName) => new ClassSelector(Normalize([saveName]), ComponentList.Game);

    /// <summary><c>maps/li/components</c> 中的指定 MapComponent。</summary>
    internal static OwnerSelector MapComponent(Type type) => new ClassSelector(Names([type]), ComponentList.Map);

    /// <summary><c>maps/li/components</c> 中的指定 MapComponent（存档类名）。</summary>
    internal static OwnerSelector MapComponent(string saveName) => new ClassSelector(Normalize([saveName]), ComponentList.Map);

    /// <summary><c>world/components</c> 中的指定 WorldComponent。</summary>
    internal static OwnerSelector WorldComponent(Type type) => new ClassSelector(Names([type]), ComponentList.World);

    /// <summary><c>world/components</c> 中的指定 WorldComponent（存档类名）。</summary>
    internal static OwnerSelector WorldComponent(string saveName) => new ClassSelector(Normalize([saveName]), ComponentList.World);

    /// <summary>物品元素，其 def 带有组件类 <paramref name="compClass"/>。</summary>
    internal static OwnerSelector ThingComp(Type compClass) => new CompSelector(SaveTypeNames.Of(compClass), hediff: false);

    /// <summary>物品元素，其 def 带有组件类（存档类名）。</summary>
    internal static OwnerSelector ThingComp(string compClass) => new CompSelector(SaveTypeNames.Normalize(compClass), hediff: false);

    /// <summary><c>hediffSet/hediffs</c> 中的健康状态，其 def 带有组件类 <paramref name="compClass"/>。</summary>
    internal static OwnerSelector HediffComp(Type compClass) => new CompSelector(SaveTypeNames.Of(compClass), hediff: true);

    /// <summary><c>hediffSet/hediffs</c> 中的健康状态，其 def 带有组件类（存档类名）。</summary>
    internal static OwnerSelector HediffComp(string compClass) => new CompSelector(SaveTypeNames.Normalize(compClass), hediff: true);

    /// <summary>
    /// 从 <paramref name="root"/> 所选元素出发、经相对路径（例如 <c>slots/li</c>）到达的元素；用于没有 <c>Class</c> 的嵌套对象。
    /// 路径段 <c>*</c> 匹配任意元素名。
    /// </summary>
    internal static OwnerSelector Path(OwnerSelector root, string relativePath) => new PathSelector(root, Segments(relativePath), document: false);

    /// <summary>从 <c>game</c> 元素出发的绝对路径（例如 <c>maps/li/storyState</c>）；路径段 <c>*</c> 匹配任意元素名。</summary>
    internal static OwnerSelector Document(string path) => new PathSelector(null, Segments(path), document: true);

    /// <summary><paramref name="owner"/> 是否被本选择器选中。</summary>
    internal abstract bool Matches(XmlElement owner, UpgradeContext context);

    /// <summary>本选择器可能选中的全部元素（由存档索引提供，未过滤已移除的元素）。</summary>
    internal abstract IEnumerable<XmlElement> Candidates(UpgradeContext context);

    public override string ToString() => Text;

    private static string[] Names(Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        if (types.Length == 0)
        {
            throw new ArgumentException("At least one type is required.", nameof(types));
        }

        return [.. types.Select(SaveTypeNames.Of)];
    }

    private static string[] Normalize(string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Length == 0 || names.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Type names must be non-empty.", nameof(names));
        }

        return [.. names.Select(SaveTypeNames.Normalize)];
    }

    private static string[] Segments(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string[] segments = path.Split('/');
        if (segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Path segments must be non-empty: " + path, nameof(path));
        }

        return segments;
    }

    private enum ComponentList
    {
        None,
        Game,
        Map,
        World,
    }

    private sealed class AnySelector : OwnerSelector
    {
        internal override string Text => "Any";

        internal override bool Matches(XmlElement owner, UpgradeContext context) => true;

        internal override IEnumerable<XmlElement> Candidates(UpgradeContext context) =>
            throw new InvalidOperationException("OwnerSelector.Any has no candidate set; use it only for value contexts.");
    }

    private sealed class ClassSelector(string[] names, ComponentList list) : OwnerSelector
    {
        internal override string Text => list switch
        {
            ComponentList.Game => "GameComponent(" + names[0] + ")",
            ComponentList.Map => "MapComponent(" + names[0] + ")",
            ComponentList.World => "WorldComponent(" + names[0] + ")",
            _ => "Class(" + string.Join("|", names) + ")",
        };

        internal override bool Matches(XmlElement owner, UpgradeContext context)
        {
            string? mapped = context.MappedClassOf(owner);
            if (mapped is null || !names.Contains(mapped, SaveTypeNames.Comparer))
            {
                return false;
            }

            return list switch
            {
                ComponentList.Game => IsComponentOf(owner, context, "game"),
                ComponentList.Map => IsMapComponent(owner, context),
                ComponentList.World => IsComponentOf(owner, context, "world") && owner.ParentNode?.ParentNode?.ParentNode == context.Document.Game,
                _ => true,
            };
        }

        internal override IEnumerable<XmlElement> Candidates(UpgradeContext context) =>
            names.SelectMany(context.Index.ElementsWithMappedClass).Where(element => Matches(element, context));

        private static bool IsComponentOf(XmlElement owner, UpgradeContext context, string container)
        {
            if (owner.ParentNode is not XmlElement list || list.Name != "components" || list.ParentNode is not XmlElement parent || parent.Name != container)
            {
                return false;
            }

            return container != "game" || parent == context.Document.Game;
        }

        private static bool IsMapComponent(XmlElement owner, UpgradeContext context) =>
            owner.ParentNode is XmlElement { Name: "components" } list
            && list.ParentNode is XmlElement { Name: "li" } map
            && map.ParentNode is XmlElement { Name: "maps" } maps
            && maps.ParentNode == context.Document.Game;
    }

    private sealed class CompSelector(string compClass, bool hediff) : OwnerSelector
    {
        internal override string Text => (hediff ? "HediffComp(" : "ThingComp(") + compClass + ")";

        internal override bool Matches(XmlElement owner, UpgradeContext context)
        {
            if (hediff)
            {
                return context.Index.TryGetHediffDef(owner, out string? def) && context.DefHasComp("HediffDef", def, compClass);
            }

            return context.Index.TryGetThing(owner, out IndexedThing thing) && context.DefHasComp("ThingDef", thing.Def, compClass);
        }

        internal override IEnumerable<XmlElement> Candidates(UpgradeContext context)
        {
            IEnumerable<XmlElement> all = hediff ? context.Index.Hediffs.Select(h => h.Element) : context.Index.Things.Select(t => t.Element);
            return all.Where(element => Matches(element, context));
        }
    }

    private sealed class PathSelector(OwnerSelector? root, string[] segments, bool document) : OwnerSelector
    {
        internal override string Text => document
            ? "Document(" + string.Join("/", segments) + ")"
            : "Path(" + root!.Text + ", " + string.Join("/", segments) + ")";

        internal override IEnumerable<string> IndexNames
        {
            get
            {
                string last = segments[^1];
                IEnumerable<string> own = last == "*" ? [] : [last];
                return root is null ? own : own.Concat(root.IndexNames);
            }
        }

        internal override bool Matches(XmlElement owner, UpgradeContext context)
        {
            XmlNode? node = owner;
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                if (node is not XmlElement element || (segments[i] != "*" && element.Name != segments[i]))
                {
                    return false;
                }

                node = element.ParentNode;
            }

            if (document)
            {
                return node == context.Document.Game;
            }

            return node is XmlElement rootElement && root!.Matches(rootElement, context);
        }

        internal override IEnumerable<XmlElement> Candidates(UpgradeContext context)
        {
            string last = segments[^1];
            if (last == "*")
            {
                throw new InvalidOperationException("A path selector whose last segment is '*' has no candidate set: " + Text);
            }

            return context.Index.ElementsNamed(last).Where(element => Matches(element, context));
        }
    }
}
