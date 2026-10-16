namespace MugirlRace.Validation.Legacy;

/// <summary>一个 Scribe 标签在某个 owner 中的写法（见 <see cref="ScribeSchema"/>）。</summary>
/// <param name="Owner">写这个标签的类型（comp 的标签写在宿主节点内，owner 仍是 comp 类型）。</param>
/// <param name="Label">节点名。</param>
/// <param name="Kind">Values、Defs、References、Deep、Collections、TargetInfo、BodyParts、EnterNode 等（<c>Scribe_</c> 之后的部分）。</param>
/// <param name="ValueType">被保存的值的类型（集合含元素类型）；EnterNode 为 <c>-</c>。</param>
/// <param name="LookMode">Collections 的各 LookMode（逗号分隔），其他种类的隐含值。</param>
/// <param name="Default">Values 的默认值文本、<c>def-dependent</c> 或 <c>dynamic</c>；其他种类为 <c>-</c>。</param>
/// <param name="ForceSave">Values 的 forceSave（<c>True</c>/<c>False</c>/<c>dynamic</c>）；其他种类为 <c>-</c>。</param>
/// <param name="Field">被保存的字段 <c>Owner::name</c>（能确定时）。</param>
/// <param name="Location">调用点 <c>Type::Method+IL_xxxx</c>（只用于消息，不进语料）。</param>
internal sealed record LabelFact(string Owner, string Label, string Kind, string ValueType, string LookMode, string Default, string ForceSave, string? Field, string Location);
