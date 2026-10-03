using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
using Verse;

namespace Mugirl.Features.WeaponWheel
{
    // 普通背负武器的摆放参数，一个条目对应“武器序号 × 朝向”组合。
    internal struct WeaponWheelBackLayoutEntry
    {
        internal float OffsetX; // 相对 Pawn 绘制中心的横向偏移，东为正
        internal float OffsetZ; // 相对 Pawn 绘制中心的纵深偏移，北为正
        internal float Angle;   // 绕竖轴的旋转角（度）
        internal float Layer;   // PawnRenderNode 图层级，越大越靠前
        internal bool FlipHorizontal; // 旋转前贴图的左右镜像
        internal bool FlipVertical;   // 旋转前贴图的上下镜像
    }

    // 两把普通背负武器的可调布局：内置默认值 + Config 目录下的 XML 覆盖文件。
    // 渲染 Worker 每帧读取 Entries，调试窗口修改后立即生效，不需要重建渲染树。
    // 覆盖文件只服务开发调试（由调试窗口导出），正式数值固化在 Defaults 中；
    // 把导出值烤进 Defaults 后应删除覆盖文件，否则旧文件会继续压过新默认值。
    internal static class WeaponWheelBackLayout
    {
        internal const int WeaponCount = 2;
        internal const int EntryCount = WeaponCount * 4;

        private const float OffsetRange = 1.5f;
        private const float AngleRange = 180f;
        private const float LayerMin = -50f;
        private const float LayerMax = 150f;

        // 条目顺序：weaponIndex * 4 + facing.AsInt（Rot4：北 0、东 1、南 2、西 3）。
        // 2026-09-24：采用用户从调试窗口导出的八条预设，保留东西向不对称角度。
        internal static readonly WeaponWheelBackLayoutEntry[] Defaults =
        {
            new WeaponWheelBackLayoutEntry { OffsetX = 0f,    OffsetZ = -0.08f, Angle = 60f,  Layer = 22f }, // 武器1·北
            new WeaponWheelBackLayoutEntry { OffsetX = -0.3f, OffsetZ = -0.08f, Angle = 90f,  Layer = 18f }, // 武器1·东
            new WeaponWheelBackLayoutEntry { OffsetX = 0f,    OffsetZ = -0.08f, Angle = 60f,  Layer = -2f }, // 武器1·南
            new WeaponWheelBackLayoutEntry { OffsetX = 0.3f,  OffsetZ = -0.08f, Angle = -60f, Layer = 18f }, // 武器1·西
            new WeaponWheelBackLayoutEntry { OffsetX = 0f,    OffsetZ = -0.08f, Angle = -60f, Layer = 22f }, // 武器2·北
            new WeaponWheelBackLayoutEntry { OffsetX = -0.5f, OffsetZ = -0.08f, Angle = -60f, Layer = -1f }, // 武器2·东
            new WeaponWheelBackLayoutEntry { OffsetX = 0f,    OffsetZ = -0.08f, Angle = -60f, Layer = -2f }, // 武器2·南
            new WeaponWheelBackLayoutEntry { OffsetX = 0.5f,  OffsetZ = -0.08f, Angle = 90f,  Layer = -1f }  // 武器2·西
        };

        internal const float DefaultSouthLayerWithAutoloading = -8f;

        // StaticCacheLifecycle: 进程级布局数据；首次访问时读取一次 Config 下的覆盖文件，
        // 之后不再自动重读，存档、读档和切图都不清理。调试窗口的“恢复默认”只改内存。
        private static WeaponWheelBackLayoutEntry[] entries;
        private static float southLayerWithAutoloading = DefaultSouthLayerWithAutoloading;
        private static bool loaded;

        internal static string OverrideDirectory => Path.Combine(GenFilePaths.ConfigFolderPath, "Mugirl");
        internal static string OverrideFilePath => Path.Combine(OverrideDirectory, "WeaponWheelBackLayout.xml");
        internal static bool HasOverrideFile
        {
            get
            {
                try
                {
                    return File.Exists(OverrideFilePath);
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static WeaponWheelBackLayoutEntry[] Entries
        {
            get
            {
                EnsureLoaded();
                return entries;
            }
        }

        internal static float SouthLayerWithAutoloading
        {
            get
            {
                EnsureLoaded();
                return southLayerWithAutoloading;
            }
            set
            {
                EnsureLoaded();
                southLayerWithAutoloading = Mathf.Clamp(value, LayerMin, LayerMax);
            }
        }

        // 渲染 Worker 使用的安全读取入口：索引越界（含无效朝向）时返回零偏移。
        internal static WeaponWheelBackLayoutEntry GetEntry(int displayIndex, Rot4 facing)
        {
            if ((uint)displayIndex >= WeaponCount || !facing.IsValid)
            {
                return new WeaponWheelBackLayoutEntry { Layer = -2f };
            }
            WeaponWheelBackLayoutEntry[] source = Entries;
            int index = displayIndex * 4 + facing.AsInt;
            if ((uint)index >= source.Length)
            {
                return Defaults[0];
            }
            return source[index];
        }

        internal static void ResetToDefaults()
        {
            EnsureLoaded();
            entries = (WeaponWheelBackLayoutEntry[])Defaults.Clone();
            southLayerWithAutoloading = DefaultSouthLayerWithAutoloading;
        }

        // 与覆盖文件同构的 XML 文本，供剪贴板导出与文件导出共用。
        internal static string ExportXml()
        {
            EnsureLoaded();
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.AppendLine("<!-- Mugirl 武器轮盘背负武器布局（开发者调试窗口导出）。");
            builder.AppendLine("     放到 RimWorld 的 Config/Mugirl/WeaponWheelBackLayout.xml 后，启动游戏时覆盖内置默认值。 -->");
            builder.AppendLine("<WeaponWheelBackLayout>");
            builder.AppendLine("  <southLayerWithAutoloading>" + Number(southLayerWithAutoloading) + "</southLayerWithAutoloading>");
            builder.AppendLine("  <entries>");
            for (int weaponIndex = 0; weaponIndex < WeaponCount; weaponIndex++)
            {
                for (int facingIndex = 0; facingIndex < 4; facingIndex++)
                {
                    WeaponWheelBackLayoutEntry entry = entries[weaponIndex * 4 + facingIndex];
                    builder.AppendLine("    <li>");
                    builder.AppendLine("      <weaponIndex>" + weaponIndex.ToString(CultureInfo.InvariantCulture) + "</weaponIndex>");
                    builder.AppendLine("      <facing>" + FacingName(facingIndex) + "</facing>");
                    builder.AppendLine("      <offsetX>" + Number(entry.OffsetX) + "</offsetX>");
                    builder.AppendLine("      <offsetZ>" + Number(entry.OffsetZ) + "</offsetZ>");
                    builder.AppendLine("      <angle>" + Number(entry.Angle) + "</angle>");
                    builder.AppendLine("      <layer>" + Number(entry.Layer) + "</layer>");
                    builder.AppendLine("      <flipHorizontal>" + BooleanLiteral(entry.FlipHorizontal) + "</flipHorizontal>");
                    builder.AppendLine("      <flipVertical>" + BooleanLiteral(entry.FlipVertical) + "</flipVertical>");
                    builder.AppendLine("    </li>");
                }
            }
            builder.AppendLine("  </entries>");
            builder.AppendLine("</WeaponWheelBackLayout>");
            return builder.ToString();
        }

        // 导出可直接粘贴回本文件 Defaults 的 C# 代码，便于把调好的数值固化为新默认值。
        internal static string ExportCSharp()
        {
            EnsureLoaded();
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("        internal static readonly WeaponWheelBackLayoutEntry[] Defaults =");
            builder.AppendLine("        {");
            for (int weaponIndex = 0; weaponIndex < WeaponCount; weaponIndex++)
            {
                for (int facingIndex = 0; facingIndex < 4; facingIndex++)
                {
                    WeaponWheelBackLayoutEntry entry = entries[weaponIndex * 4 + facingIndex];
                    builder.AppendLine(
                        "            new WeaponWheelBackLayoutEntry { OffsetX = " + NumberLiteral(entry.OffsetX)
                        + ", OffsetZ = " + NumberLiteral(entry.OffsetZ)
                        + ", Angle = " + NumberLiteral(entry.Angle)
                        + ", Layer = " + NumberLiteral(entry.Layer)
                        + ", FlipHorizontal = " + BooleanLiteral(entry.FlipHorizontal)
                        + ", FlipVertical = " + BooleanLiteral(entry.FlipVertical)
                        + " }, // 武器" + (weaponIndex + 1).ToString(CultureInfo.InvariantCulture) + "·" + FacingName(facingIndex));
                }
            }
            builder.AppendLine("        };");
            builder.AppendLine("        internal const float DefaultSouthLayerWithAutoloading = " + NumberLiteral(southLayerWithAutoloading) + ";");
            return builder.ToString();
        }

        internal static string WriteOverrideFile()
        {
            string path = OverrideFilePath;
            Directory.CreateDirectory(OverrideDirectory);
            File.WriteAllText(path, ExportXml(), new UTF8Encoding(false));
            MugirlLog.DevMessage("Weapon wheel back layout exported to " + path);
            return path;
        }

        internal static bool DeleteOverrideFile()
        {
            if (!HasOverrideFile)
            {
                return false;
            }
            File.Delete(OverrideFilePath);
            MugirlLog.DevMessage("Weapon wheel back layout override deleted: " + OverrideFilePath);
            return true;
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }
            loaded = true;
            entries = (WeaponWheelBackLayoutEntry[])Defaults.Clone();
            try
            {
                if (!File.Exists(OverrideFilePath))
                {
                    return;
                }
                if (TryParseXml(File.ReadAllText(OverrideFilePath)))
                {
                    MugirlLog.DevMessage("Weapon wheel back layout override loaded from " + OverrideFilePath);
                }
                else
                {
                    // TryParseXml 可能已写入部分条目才失败，失败时整体回退默认表。
                    entries = (WeaponWheelBackLayoutEntry[])Defaults.Clone();
                    MugirlLog.WarningOnce(
                        "WeaponWheelBackLayout.InvalidOverride",
                        "Invalid weapon wheel back layout file, falling back to built-in defaults: " + OverrideFilePath);
                }
            }
            catch (Exception ex)
            {
                MugirlLog.WarningOnce(
                    "WeaponWheelBackLayout.LoadFailed",
                    "Failed to load weapon wheel back layout override: " + ex.Message);
            }
        }

        private static bool TryParseXml(string text)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(text);
            }
            catch
            {
                return false;
            }
            XElement root = document.Root;
            if (root == null || root.Name != "WeaponWheelBackLayout")
            {
                return false;
            }

            XElement entriesElement = root.Element("entries");
            if (entriesElement != null)
            {
                foreach (XElement item in entriesElement.Elements("li"))
                {
                    int weaponIndex = ParseInt(item.Element("weaponIndex")?.Value, -1);
                    int facingIndex = ParseFacingIndex(item.Element("facing")?.Value);
                    if (weaponIndex < 0 || weaponIndex >= WeaponCount || facingIndex < 0 || facingIndex >= 4)
                    {
                        return false;
                    }
                    WeaponWheelBackLayoutEntry entry = new WeaponWheelBackLayoutEntry
                    {
                        OffsetX = ParseFloat(item.Element("offsetX")?.Value, 0f),
                        OffsetZ = ParseFloat(item.Element("offsetZ")?.Value, 0f),
                        Angle = ParseFloat(item.Element("angle")?.Value, 0f),
                        Layer = ParseFloat(item.Element("layer")?.Value, 0f),
                        // 旧覆盖文件未提供镜像字段时保持原显示。
                        FlipHorizontal = bool.TryParse(item.Element("flipHorizontal")?.Value, out bool horizontal) && horizontal,
                        FlipVertical = bool.TryParse(item.Element("flipVertical")?.Value, out bool vertical) && vertical
                    };
                    entry.OffsetX = Mathf.Clamp(entry.OffsetX, -OffsetRange, OffsetRange);
                    entry.OffsetZ = Mathf.Clamp(entry.OffsetZ, -OffsetRange, OffsetRange);
                    entry.Angle = Mathf.Clamp(entry.Angle, -AngleRange, AngleRange);
                    entry.Layer = Mathf.Clamp(entry.Layer, LayerMin, LayerMax);
                    entries[weaponIndex * 4 + facingIndex] = entry;
                }
            }

            southLayerWithAutoloading = Mathf.Clamp(
                ParseFloat(root.Element("southLayerWithAutoloading")?.Value, DefaultSouthLayerWithAutoloading),
                LayerMin, LayerMax);
            return true;
        }

        private static int ParseFacingIndex(string value)
        {
            switch (value?.Trim())
            {
                case "North": return 0;
                case "East": return 1;
                case "South": return 2;
                case "West": return 3;
                default: return ParseInt(value, -1);
            }
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                ? result
                : fallback;
        }

        private static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
                && !float.IsNaN(result) && !float.IsInfinity(result)
                ? result
                : fallback;
        }

        private static string FacingName(int facingIndex)
        {
            switch (facingIndex)
            {
                case 0: return "North";
                case 1: return "East";
                case 2: return "South";
                default: return "West";
            }
        }

        private static string Number(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string NumberLiteral(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture) + "f";
        }

        private static string BooleanLiteral(bool value) => value ? "true" : "false";
    }
}
