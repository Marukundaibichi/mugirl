using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>存档 XML 的只读工具：只认原版 Scribe 的形状（<c>def</c>/<c>id</c>、<c>Class</c> 属性、<c>li</c> 列表），不引用任何 Mod 类型。</summary>
/// <remarks><see cref="SaveFacts"/>、<see cref="SaveDiff"/> 与夹具清单都用它，所以 -Plumbing 构建与旧构建运行也能使用。</remarks>
internal static class SaveXml
{
    /// <summary>雪牛娘各时代的类型命名空间前缀（MugirlRace 时代与 MooGirl 时代）。</summary>
    public static readonly IReadOnlyList<string> ModClassPrefixes = ["Mugirl.", "MooGirl.", "MooGirlSkinFix."];

    /// <summary>读入存档；扩展名为 .gz 时先解压。</summary>
    public static XDocument Load(string path)
    {
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            using FileStream file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            return XDocument.Load(gzip, LoadOptions.PreserveWhitespace);
        }

        return XDocument.Load(path, LoadOptions.PreserveWhitespace);
    }

    /// <summary>存档根下的 <c>game</c> 元素。</summary>
    public static XElement? Game(XDocument document) => document.Root?.Element("game");

    /// <summary>存档的 meta/modIds（小写 packageId，按存档中的顺序）。</summary>
    public static List<string> ModIds(XDocument document)
        => [.. document.Root?.Element("meta")?.Element("modIds")?.Elements("li").Select(e => e.Value.Trim()) ?? []];

    /// <summary>元素的 Class 属性；没有时为 null。</summary>
    public static string? ClassOf(XElement element) => element.Attribute("Class")?.Value;

    /// <summary>Class 属性是否属于雪牛娘的命名空间。</summary>
    public static bool IsModClass(string? className)
        => className is not null && ModClassPrefixes.Any(prefix => className.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Scribe 写出的 Thing 节点：直接子元素中有 <c>def</c> 与 <c>id</c>。</summary>
    public static bool IsThing(XElement element) => element.Element("id") is not null && element.Element("def") is not null;

    /// <summary>Pawn 节点：Thing 节点且有 <c>kindDef</c>。</summary>
    public static bool IsPawn(XElement element) => IsThing(element) && element.Element("kindDef") is not null;

    public static string? Def(XElement element) => element.Element("def")?.Value.Trim();

    public static string? Id(XElement element) => element.Element("id")?.Value.Trim();

    /// <summary>没有子元素的叶子。</summary>
    public static bool IsLeaf(XElement element) => !element.HasElements;

    /// <summary>Scribe_Values/Deep/Collections 只把值为 true（不区分大小写）的 IsNull 属性解释为空。</summary>
    public static bool HasNullAttribute(XElement element)
        => string.Equals(element.Attribute("IsNull")?.Value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>只用于已知 Scribe_References 字段；普通保存字符串中的 "null" 不是空值。</summary>
    public static bool IsNullReference(XElement element)
        => !element.HasElements && element.Value == "null";

    /// <summary>保留序列化文本的全部精度与玩家文字；不做数字转换、舍入或去空白。</summary>
    public static string Normalize(string value) => value;

    /// <summary>无类型上下文的 Scribe 叶子只能逐字符比较；颜色的特定读档变化由调用方按已验证的字段解释。</summary>
    public static bool SameValue(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
    /// <summary>元素是否是存档的 <c>game</c> 元素（<c>savegame</c> 的子元素）。</summary>
    public static bool IsGameElement(XElement element) => element.Name.LocalName == "game" && element.Parent?.Name.LocalName == "savegame";
}
