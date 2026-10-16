namespace Mugirl.SaveUpgrade;

/// <summary>
/// 允许直接删除子项的列表容器，按元素名路径后缀匹配（例如 <c>hediffSet/hediffs</c>）。只有读取方能容忍缺项的列表才能登记：
/// 过滤器与 HashSet、ThingOwner 的 <c>innerList</c>、地图物品列表、组件列表等。其他列表的项只能置空。
/// </summary>
internal sealed record RemovableContainer(IReadOnlyList<string> Path, string Module, string Reason)
{
    internal string Text => string.Join("/", Path);
}
