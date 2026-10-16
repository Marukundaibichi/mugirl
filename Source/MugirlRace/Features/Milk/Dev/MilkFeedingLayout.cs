using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Mugirl.Features.Milk;

// 喂奶姿态的开发者微调值：首次使用时读取 Config/Mugirl/MilkFeedingLayout.xml 覆盖默认值（默认全为 0）。
// 调试窗口只修改内存值，显式保存后才写盘。
internal static class MilkFeedingLayout
{
    internal static readonly MilkFeedingLayoutEntry Default = default;

    // StaticCacheLifecycle: 进程级开发者布局；首次读取 Config 覆盖文件后常驻，切图、读档不清理。
    private static bool loaded;
    private static MilkFeedingLayoutEntry current;

    internal static string OverrideDirectory => Path.Combine(GenFilePaths.ConfigFolderPath, "Mugirl");

    internal static string OverrideFilePath => Path.Combine(OverrideDirectory, "MilkFeedingLayout.xml");

    internal static MilkFeedingLayoutEntry Current
    {
        get
        {
            EnsureLoaded();
            return current;
        }

        set
        {
            EnsureLoaded();
            current = MilkFeedingLayoutEntry.Clamp(value);
        }
    }

    internal static void ResetToDefaults()
    {
        EnsureLoaded();
        current = Default;
    }

    internal static string ExportXml() => MilkFeedingLayoutXml.Write(Current);

    internal static string WriteOverrideFile()
    {
        Directory.CreateDirectory(OverrideDirectory);
        File.WriteAllText(OverrideFilePath, ExportXml(), new UTF8Encoding(false));
        MugirlLog.DevMessage("Milk feeding layout exported to " + OverrideFilePath);
        return OverrideFilePath;
    }

    private static void EnsureLoaded()
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        current = Default;
        try
        {
            if (!File.Exists(OverrideFilePath))
            {
                return;
            }

            current = MilkFeedingLayoutXml.Read(XDocument.Load(OverrideFilePath).Root);
            MugirlLog.DevMessage("Milk feeding layout loaded from " + OverrideFilePath);
        }
        catch (Exception ex)
        {
            // 覆盖文件只影响开发者微调，任何读取或解析失败都退回默认值，不能影响喂奶动画本身。
            current = Default;
            MugirlLog.WarningOnce("MilkFeedingLayout.InvalidOverride", "Invalid milk feeding layout; using defaults: " + ex.Message);
        }
    }
}
