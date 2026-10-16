using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>在独立 XML 副本上运行实际生产 DOM 流水线；不启动读档、不发布诊断、不写文件或运行 Phase A/B/C。</summary>
internal sealed record CanonicalSavePreview(XDocument Document, string ReportJson)
{
    private const BindingFlags Members = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string TraceType = "Mugirl.SaveUpgrade.ValidationReadTrace";

    public static CanonicalSavePreview Create(Assembly assembly, XDocument source)
    {
        var xml = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
        xml.LoadXml(source.ToString(SaveOptions.DisableFormatting));
        Type sessionType = RequiredType(assembly, "Mugirl.SaveUpgrade.SaveUpgradeSession");
        Type tablesType = RequiredType(assembly, "Mugirl.SaveUpgrade.UpgradeTables");
        Type catalogType = RequiredType(assembly, "Mugirl.SaveUpgrade.DefDatabaseCatalog");
        object session = sessionType.GetMethod("Start", Members)!.Invoke(null, [0, xml, null])!;
        object tables = tablesType.GetProperty("Default", Members)!.GetValue(null)!;
        object catalog = catalogType.GetProperty("Instance", Members)!.GetValue(null)!;
        sessionType.GetMethod("RunDom", Members)!.Invoke(session, [tables, catalog]);
        object report = sessionType.GetProperty("Report", Members)!.GetValue(session)!;
        Type reportType = report.GetType();
        if ((bool)reportType.GetProperty("Failed", Members)!.GetValue(report)!)
        {
            throw new InvalidOperationException("Canonical production DOM replay failed: " + reportType.GetProperty("Error", Members)!.GetValue(report));
        }

        string json = (string)reportType.GetMethod("ToJson", Members)!.Invoke(report, null)!;
        return new CanonicalSavePreview(XDocument.Parse(xml.OuterXml, LoadOptions.PreserveWhitespace), json);
    }

    public static bool EnableReadTrace(Assembly? assembly)
    {
        if (assembly?.GetType(TraceType) is not { } type)
        {
            return false;
        }

        type.GetMethod("EnableForValidation", Members)!.Invoke(null, null);
        return true;
    }

    public static string? ReadTrace(Assembly assembly)
        => assembly.GetType(TraceType)?.GetMethod("SnapshotJson", Members)?.Invoke(null, null) as string;

    private static Type RequiredType(Assembly assembly, string name)
        => assembly.GetType(name) ?? throw new InvalidOperationException("The production DOM preview contract is unavailable: " + name);
}
