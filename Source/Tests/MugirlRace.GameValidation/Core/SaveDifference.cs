using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>
/// <see cref="SaveDiff"/> 找到的一处差异：<see cref="Kind"/> 为 "missing"（只在 A 中）、"added"（只在 B 中）、"changed"，
/// 或 "nullness"（IsNull 的真假不同；只有有类型证据的专门解释才能接受它）。
/// </summary>
/// <param name="Kind">差异种类。</param>
/// <param name="Item">比较项的键（<c>thing:&lt;id&gt;</c>、<c>thing:&lt;id&gt;/&lt;组件标签&gt;</c>、<c>class:…</c>、<c>def:…</c>）。</param>
/// <param name="Path">项内路径（标签与列表项），空串表示整项。</param>
/// <param name="Before">A 中的值（截断）。</param>
/// <param name="After">B 中的值（截断）。</param>
internal sealed record SaveDifference(string Kind, string Item, string Path, string? Before, string? After)
{
    /// <summary>A 中对应的元素（missing、changed、nullness）；解释差异时用它判断所属对象。</summary>
    public XElement? BeforeElement { get; init; }

    /// <summary>B 中对应的元素（added、changed、nullness）。</summary>
    public XElement? AfterElement { get; init; }

    /// <summary>路径的最后一段（标签名或列表项），没有时为空串。</summary>
    public string Leaf => Path.Length == 0 ? string.Empty : Path[(Path.LastIndexOf('/') + 1)..];

    public override string ToString() => Kind switch
    {
        "missing" => $"missing {Item} {Path} (was {Before})",
        "added" => $"added {Item} {Path} = {After}",
        "nullness" => $"nullness {Item} {Path}: {Before} -> {After}",
        _ => $"changed {Item} {Path}: {Before} -> {After}",
    };
}
