using System.Reflection;

namespace Mugirl.GameValidation.Core;

/// <summary>依赖 XML 必须真正实例化冻结薄别名；规范实现或其它派生类不能冒充公开入口。</summary>
internal static class FrozenXmlAliasEvidence
{
    public static bool SlaveRange(Assembly production, IEnumerable<StockGenerator> generators, out string detail)
    {
        Type? alias = production.GetType("Mugirl.StockGenerator_Mugirl_Slaves");
        Type? implementation = production.GetType("Mugirl.Features.Trade.StockGenerator_MugirlSlaves");
        if (alias is null || implementation is null || alias.BaseType != implementation
            || !implementation.IsAssignableFrom(alias) || !typeof(StockGenerator).IsAssignableFrom(implementation))
        {
            detail = "The frozen alias does not directly bind to the canonical StockGenerator implementation in the loaded target assembly.";
            return false;
        }
        StockGenerator[] matches = [.. generators.Where(g => g is not null && g.GetType() == alias)];
        if (matches.Length != 1)
        {
            detail = "Expected one actual frozen alias instance, found " + matches.Length;
            return false;
        }
        FieldInfo? field = alias.GetField("slaveCountRange", BindingFlags.Instance | BindingFlags.Public);
        if (field?.DeclaringType != implementation || field.FieldType != typeof(IntRange))
        {
            detail = "slaveCountRange is not the unchanged inherited public IntRange field of the canonical implementation.";
            return false;
        }
        var range = (IntRange)field.GetValue(matches[0])!;
        detail = alias.FullName + " -> " + implementation.FullName + "; inherited slaveCountRange=" + range.min + "~" + range.max;
        return range.min == 1 && range.max == 2;
    }
}
