using System.IO;
using System.Reflection;
using System.Xml;

namespace Mugirl.GameValidation.Core;

/// <summary>只读生产读档会话的实际DOM引用；同形状/同ID的侧文件不能成为主读档证据。</summary>
internal static class MainSaveDocument
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static bool Matches(Assembly production, XmlDocument? xml, string? expectedPath = null)
    {
        Type runtime = production.GetType("Mugirl.SaveUpgrade.SaveUpgradeRuntime", throwOnError: true)!;
        object? session = runtime.GetField("activeSession", Fields)!.GetValue(null);
        if (session is null) { return false; }
        object? document = session.GetType().GetField("<Document>k__BackingField", Fields)!.GetValue(session);
        if (document is null || !ReferenceEquals(document.GetType().GetField("<Xml>k__BackingField", Fields)!.GetValue(document), xml)) { return false; }
        if (expectedPath is null) { return true; }
        string? actual = session.GetType().GetField("<FilePath>k__BackingField", Fields)!.GetValue(session) as string;
        return actual is not null && string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase);
    }
}
