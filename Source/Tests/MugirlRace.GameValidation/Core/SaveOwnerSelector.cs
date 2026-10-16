using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>
/// 升级清单（K1）中标签行的 owner 选择器文本的解析与求值，只读原版 XML 形状。文本由升级器写出（<c>OwnerSelector.Text</c>）：
/// <c>Any</c>、<c>Class(A|B)</c>、<c>GameComponent(T)</c>、<c>MapComponent(T)</c>、<c>WorldComponent(T)</c>、<c>ThingComp(C)</c>、
/// <c>HediffComp(C)</c>、<c>Document(a/b)</c>、<c>Path(&lt;选择器&gt;, a/b)</c>（路径段 <c>*</c> 匹配任意元素名）。
/// </summary>
/// <remarks>
/// 选择器中的类型名都是新名；求值时元素的 <c>Class</c> 先经类型映射（<see cref="ISaveOwnerContext.MappedClassOf"/>），
/// 所以同一个选择器既能选中旧档中的元素，也能选中重存档中的元素。无法解析的文本得到一个不选中任何元素的选择器（<see cref="IsValid"/> 为 false）。
/// </remarks>
internal sealed class SaveOwnerSelector
{
    private readonly Kind kind;
    private readonly string[] names;
    private readonly string[] segments;
    private readonly SaveOwnerSelector? root;

    private SaveOwnerSelector(string text, Kind kind, string[] names, string[] segments, SaveOwnerSelector? root)
    {
        Text = text;
        this.kind = kind;
        this.names = names;
        this.segments = segments;
        this.root = root;
    }

    private enum Kind
    {
        Invalid,
        Any,
        Class,
        GameComponent,
        MapComponent,
        WorldComponent,
        ThingComp,
        HediffComp,
        Document,
        Path,
    }

    /// <summary>选择器文本（与升级清单中相同）。</summary>
    public string Text { get; }

    public bool IsValid => kind != Kind.Invalid;

    /// <summary>是否选择组件写在物品或健康状态节点里的标签（<c>ThingComp</c>/<c>HediffComp</c>）。</summary>
    public bool IsComp => kind is Kind.ThingComp or Kind.HediffComp;

    /// <summary><c>ThingComp</c> 选择器的组件类（新名）；其他选择器为 null。</summary>
    public string? ThingCompClass => kind == Kind.ThingComp ? names[0] : null;

    /// <summary><c>HediffComp</c> 选择器的组件类（新名）；其他选择器为 null。</summary>
    public string? HediffCompClass => kind == Kind.HediffComp ? names[0] : null;

    /// <summary>直接按类型选择元素的选择器（<c>Class</c>、各组件列表）的类型名（新名）；其他选择器为空。</summary>
    public IReadOnlyList<string> ClassNames => kind is Kind.Class or Kind.GameComponent or Kind.MapComponent or Kind.WorldComponent ? names : [];

    public static SaveOwnerSelector Parse(string? text)
    {
        string value = (text ?? string.Empty).Trim();
        return TryParse(value) ?? new SaveOwnerSelector(value, Kind.Invalid, [], [], null);
    }

    /// <summary><paramref name="owner"/>（标签的直接父元素）是否被本选择器选中。</summary>
    public bool Matches(XElement owner, ISaveOwnerContext context)
    {
        switch (kind)
        {
            case Kind.Any:
                return true;
            case Kind.Class:
                return context.MappedClassOf(owner) is { } mapped && names.Contains(mapped, StringComparer.Ordinal);
            case Kind.GameComponent:
                return IsComponent(owner, context) && owner.Parent?.Parent is { } game && SaveXml.IsGameElement(game);
            case Kind.MapComponent:
                return IsComponent(owner, context) && owner.Parent?.Parent is { Name.LocalName: "li" } map
                    && map.Parent is { Name.LocalName: "maps" } maps && maps.Parent is { } mapsOwner && SaveXml.IsGameElement(mapsOwner);
            case Kind.WorldComponent:
                return IsComponent(owner, context) && owner.Parent?.Parent is { Name.LocalName: "world" } world
                    && world.Parent is { } worldOwner && SaveXml.IsGameElement(worldOwner);
            case Kind.ThingComp:
                return SaveXml.IsThing(owner) && SaveXml.Def(owner) is { } thingDef && context.DefHasComp("ThingDef", thingDef, names[0]);
            case Kind.HediffComp:
                return owner.Name.LocalName == "li" && owner.Parent?.Name.LocalName == "hediffs" && SaveXml.Def(owner) is { } hediffDef
                    && context.DefHasComp("HediffDef", hediffDef, names[0]);
            case Kind.Document:
            case Kind.Path:
                return MatchesPath(owner, context);
            case Kind.Invalid:
            default:
                return false;
        }
    }

    public override string ToString() => Text;

    private bool IsComponent(XElement owner, ISaveOwnerContext context)
        => owner.Parent?.Name.LocalName == "components" && context.MappedClassOf(owner) is { } mapped && names.Contains(mapped, StringComparer.Ordinal);

    private bool MatchesPath(XElement owner, ISaveOwnerContext context)
    {
        XElement? node = owner;
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            if (node is null || (segments[i] != "*" && node.Name.LocalName != segments[i]))
            {
                return false;
            }

            node = node.Parent;
        }

        return kind == Kind.Document ? node is not null && SaveXml.IsGameElement(node) : node is not null && root!.Matches(node, context);
    }

    private static SaveOwnerSelector? TryParse(string text)
    {
        if (text == "Any")
        {
            return new SaveOwnerSelector(text, Kind.Any, [], [], null);
        }

        int open = text.IndexOf('(', StringComparison.Ordinal);
        if (open <= 0 || !text.EndsWith(')'))
        {
            return null;
        }

        string head = text[..open];
        string body = text[(open + 1)..^1].Trim();
        if (body.Length == 0)
        {
            return null;
        }

        switch (head)
        {
            case "Class":
                return new SaveOwnerSelector(text, Kind.Class, [.. body.Split('|').Select(n => n.Trim())], [], null);
            case "GameComponent":
                return new SaveOwnerSelector(text, Kind.GameComponent, [body], [], null);
            case "MapComponent":
                return new SaveOwnerSelector(text, Kind.MapComponent, [body], [], null);
            case "WorldComponent":
                return new SaveOwnerSelector(text, Kind.WorldComponent, [body], [], null);
            case "ThingComp":
                return new SaveOwnerSelector(text, Kind.ThingComp, [body], [], null);
            case "HediffComp":
                return new SaveOwnerSelector(text, Kind.HediffComp, [body], [], null);
            case "Document":
                return new SaveOwnerSelector(text, Kind.Document, [], body.Split('/'), null);
            case "Path":
                // 根选择器本身可能含括号与逗号：取深度 0 的最后一个逗号。
                int comma = LastTopLevelComma(body);
                if (comma <= 0 || TryParse(body[..comma].Trim()) is not { } inner)
                {
                    return null;
                }

                string path = body[(comma + 1)..].Trim();
                return path.Length == 0 ? null : new SaveOwnerSelector(text, Kind.Path, [], path.Split('/'), inner);
            default:
                return null;
        }
    }

    private static int LastTopLevelComma(string text)
    {
        int depth = 0;
        int last = -1;
        for (int i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    last = i;
                    break;
                default:
                    break;
            }
        }

        return last;
    }
}
