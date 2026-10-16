namespace MugirlRace.Validation.Legacy;

/// <summary>某个构建的 XML 为某个类的字段写过的值（例如 CompProperties 的 <c>saveKey</c>），供存档标签求值。</summary>
internal interface IFieldValueSource
{
    /// <summary><paramref name="typeName"/>（完整名）实例的 <paramref name="field"/> 在 XML 中出现过的文本值。</summary>
    IEnumerable<string> ValuesOf(string typeName, string field);
}
