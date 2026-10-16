using System.Globalization;

namespace Mugirl.GameValidation.Core;

/// <summary>与 1.7.4979 ScribeMetaHeaderUtility.ModListsMatch 相同的列表比较：顺序保留，逐项按当前区域性转小写。</summary>
internal static class SaveEnvironment
{
    public static bool ModListsMatch(IEnumerable<string>? saved, IEnumerable<string>? running)
        => saved is not null && running is not null
            && saved.Select(s => s.ToLower(CultureInfo.CurrentCulture)).SequenceEqual(running.Select(s => s.ToLower(CultureInfo.CurrentCulture)), StringComparer.Ordinal);
}
