using System.IO;
using System.Text;

namespace Mugirl.Features.WeaponWheel;

// 两把普通背负武器的运行期布局：内置默认值，加上 Config/Mugirl/WeaponWheelBackLayout.xml 覆盖文件（由调试窗口导出）。
// 渲染 Worker 每帧在 ParallelPreDraw 工作线程上读取；只有开发者调试窗口在主线程写入。
// 正式数值固化在 WeaponWheelBackLayoutData 的默认表中；把导出值烤进默认表后应删除覆盖文件，否则旧文件会继续压过新默认值。
[StaticConstructorOnStartup]
internal static class WeaponWheelBackLayout
{
    // StaticCacheLifecycle: 进程级布局数据，启动时在主线程的静态构造中读取一次覆盖文件，之后不再自动重读；
    // 存档、读档和切图都不清理。调试窗口的修改与“恢复默认”只改内存。
    // 静态构造由 CLR 保证只执行一次且对所有线程可见，取代 1.6 的惰性加载（工作线程首次访问时可能读到半初始化的表）。
    private static readonly WeaponWheelBackLayoutEntry[] entries;
    private static float southLayerWithAutoloading;

    static WeaponWheelBackLayout()
    {
        entries = WeaponWheelBackLayoutData.CopyDefaults();
        southLayerWithAutoloading = WeaponWheelBackLayoutData.DefaultSouthLayerWithAutoloading;
        LoadOverrideFile();
    }

    internal static string OverrideDirectory => Path.Combine(GenFilePaths.ConfigFolderPath, "Mugirl");

    internal static string OverrideFilePath => Path.Combine(OverrideDirectory, "WeaponWheelBackLayout.xml");

    internal static float SouthLayerWithAutoloading
    {
        get => southLayerWithAutoloading;
        set => southLayerWithAutoloading = UnityMathf.Clamp(value, WeaponWheelBackLayoutData.LayerMin, WeaponWheelBackLayoutData.LayerMax);
    }

    // 渲染 Worker 使用的安全读取入口：武器序号越界或朝向无效时返回零偏移。
    internal static WeaponWheelBackLayoutEntry GetEntry(int displayIndex, Rot4 facing)
    {
        if ((uint)displayIndex >= WeaponWheelBackLayoutData.WeaponCount || !facing.IsValid)
        {
            return new WeaponWheelBackLayoutEntry { Layer = -2f };
        }
        return entries[WeaponWheelBackLayoutData.IndexOf(displayIndex, facing.AsInt)];
    }

    internal static WeaponWheelBackLayoutEntry GetEntry(int index) => entries[index];

    // 调试窗口写入（主线程）。工作线程可能读到写入中的条目，只影响开发者模式下一帧的显示。
    internal static void SetEntry(int index, WeaponWheelBackLayoutEntry entry)
    {
        entries[index] = entry;
    }

    internal static void ResetToDefaults()
    {
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = WeaponWheelBackLayoutData.DefaultAt(i);
        }
        southLayerWithAutoloading = WeaponWheelBackLayoutData.DefaultSouthLayerWithAutoloading;
    }

    internal static string ExportXml() => WeaponWheelBackLayoutData.ToXml(entries, southLayerWithAutoloading);

    internal static string ExportCSharp() => WeaponWheelBackLayoutData.ToCSharp(entries, southLayerWithAutoloading);

    internal static string WriteOverrideFile()
    {
        string path = OverrideFilePath;
        Directory.CreateDirectory(OverrideDirectory);
        File.WriteAllText(path, ExportXml(), new UTF8Encoding(false));
        MugirlLog.DevMessage("Weapon wheel back layout exported to " + path);
        return path;
    }

    private static void LoadOverrideFile()
    {
        // 在静态构造中运行：任何异常都必须就地吞掉并回退默认表，否则类型初始化失败会让每帧的渲染读取都抛异常。
        try
        {
            string path = OverrideFilePath;
            if (!File.Exists(path))
            {
                return;
            }

            if (WeaponWheelBackLayoutData.TryParseXml(File.ReadAllText(path), out WeaponWheelBackLayoutEntry[] parsed, out float southLayer))
            {
                parsed.CopyTo(entries, 0);
                southLayerWithAutoloading = southLayer;
                MugirlLog.DevMessage("Weapon wheel back layout override loaded from " + path);
            }
            else
            {
                MugirlLog.WarningOnce(
                    "WeaponWheelBackLayout.InvalidOverride",
                    "Invalid weapon wheel back layout file, falling back to built-in defaults: " + path);
            }
        }
        catch (Exception ex)
        {
            MugirlLog.WarningOnce(
                "WeaponWheelBackLayout.LoadFailed",
                "Failed to load weapon wheel back layout override: " + ex.Message);
        }
    }
}
