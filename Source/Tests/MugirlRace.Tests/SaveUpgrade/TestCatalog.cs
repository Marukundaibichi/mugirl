using System.Collections.Generic;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>内存中的 Def 目录：(根, defName) → 组件类名。</summary>
internal sealed class TestCatalog : IDefCatalog
{
    private readonly Dictionary<(string, string), string[]> comps = [];
    private readonly Dictionary<(string, string, string), int> integers = [];

    internal TestCatalog WithInteger(string root, string defName, string field, int value)
    {
        integers[(root, defName, field)] = value;
        comps.TryAdd((root, defName), []);
        return this;
    }

    public int? IntegerField(string root, string defName, string field) => integers.TryGetValue((root, defName, field), out int value) ? value : null;

    internal TestCatalog With(string root, string defName, params string[] compClasses)
    {
        comps[(root, defName)] = compClasses;
        return this;
    }

    public bool Contains(string root, string defName) => comps.ContainsKey((root, defName));

    public IReadOnlyCollection<string>? CompClasses(string root, string defName) =>
        comps.TryGetValue((root, defName), out string[]? classes) ? classes : null;
}
