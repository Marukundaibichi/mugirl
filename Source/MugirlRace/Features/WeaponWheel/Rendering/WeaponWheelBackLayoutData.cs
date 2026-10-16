using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Mugirl.Features.WeaponWheel;

// 背负武器布局的纯数据部分：内置默认表、取值范围，以及覆盖文件（XML）与 C# 默认表文本的读写。
// 不访问文件系统与游戏状态，供 WeaponWheelBackLayout、调试窗口与单元测试共用。
internal static class WeaponWheelBackLayoutData
{
    internal const int WeaponCount = 2;
    internal const int FacingCount = 4;
    internal const int EntryCount = WeaponCount * FacingCount;

    internal const float OffsetRange = 1.5f;
    internal const float AngleRange = 180f;
    internal const float LayerMin = -50f;
    internal const float LayerMax = 150f;

    internal const float DefaultSouthLayerWithAutoloading = -8f;

    internal const string RootElementName = "WeaponWheelBackLayout";

    // 条目顺序：weaponIndex * 4 + facing.AsInt（Rot4：北 0、东 1、南 2、西 3）。
    // 2026-09-24：采用用户从调试窗口导出的八条预设，保留东西向不对称角度。只读：使用方先复制。
    private static readonly WeaponWheelBackLayoutEntry[] defaults =
    [
        new WeaponWheelBackLayoutEntry { OffsetX = 0f,    OffsetZ = -0.08f, Angle = 60f,  Layer = 22f }, // 武器1·北
        new WeaponWheelBackLayoutEntry { OffsetX = -0.3f, OffsetZ = -0.08f, Angle = 90f,  Layer = 18f }, // 武器1·东
        new WeaponWheelBackLayoutEntry { OffsetX = 0f,    OffsetZ = -0.08f, Angle = 60f,  Layer = -2f }, // 武器1·南
        new WeaponWheelBackLayoutEntry { OffsetX = 0.3f,  OffsetZ = -0.08f, Angle = -60f, Layer = 18f }, // 武器1·西
        new WeaponWheelBackLayoutEntry { OffsetX = 0f,    OffsetZ = -0.08f, Angle = -60f, Layer = 22f }, // 武器2·北
        new WeaponWheelBackLayoutEntry { OffsetX = -0.5f, OffsetZ = -0.08f, Angle = -60f, Layer = -1f }, // 武器2·东
        new WeaponWheelBackLayoutEntry { OffsetX = 0f,    OffsetZ = -0.08f, Angle = -60f, Layer = -2f }, // 武器2·南
        new WeaponWheelBackLayoutEntry { OffsetX = 0.5f,  OffsetZ = -0.08f, Angle = 90f,  Layer = -1f }  // 武器2·西
    ];

    internal static WeaponWheelBackLayoutEntry DefaultAt(int index) => defaults[index];

    internal static WeaponWheelBackLayoutEntry[] CopyDefaults() => (WeaponWheelBackLayoutEntry[])defaults.Clone();

    internal static int IndexOf(int weaponIndex, int facingIndex) => weaponIndex * FacingCount + facingIndex;

    // 解析覆盖文件。任何条目的武器序号或朝向无效、或文本不是本格式时整体失败并返回默认表；
    // 文件未列出的条目保持默认；数值越界时钳制，非有限数按缺省处理。
    internal static bool TryParseXml(string text, out WeaponWheelBackLayoutEntry[] entries, out float southLayerWithAutoloading)
    {
        entries = CopyDefaults();
        southLayerWithAutoloading = DefaultSouthLayerWithAutoloading;

        XDocument document;
        try
        {
            document = XDocument.Parse(text);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }

        XElement? root = document.Root;
        if (root == null || root.Name != RootElementName)
        {
            return false;
        }

        WeaponWheelBackLayoutEntry[] parsed = CopyDefaults();
        XElement? entriesElement = root.Element("entries");
        if (entriesElement != null)
        {
            foreach (XElement item in entriesElement.Elements("li"))
            {
                int weaponIndex = ParseInt(item.Element("weaponIndex")?.Value, -1);
                int facingIndex = ParseFacingIndex(item.Element("facing")?.Value);
                if (weaponIndex < 0 || weaponIndex >= WeaponCount || facingIndex < 0 || facingIndex >= FacingCount)
                {
                    return false;
                }

                parsed[IndexOf(weaponIndex, facingIndex)] = new WeaponWheelBackLayoutEntry
                {
                    OffsetX = UnityMathf.Clamp(ParseFloat(item.Element("offsetX")?.Value, 0f), -OffsetRange, OffsetRange),
                    OffsetZ = UnityMathf.Clamp(ParseFloat(item.Element("offsetZ")?.Value, 0f), -OffsetRange, OffsetRange),
                    Angle = UnityMathf.Clamp(ParseFloat(item.Element("angle")?.Value, 0f), -AngleRange, AngleRange),
                    Layer = UnityMathf.Clamp(ParseFloat(item.Element("layer")?.Value, 0f), LayerMin, LayerMax),
                    // 旧覆盖文件未提供镜像字段时保持原显示。
                    FlipHorizontal = bool.TryParse(item.Element("flipHorizontal")?.Value, out bool horizontal) && horizontal,
                    FlipVertical = bool.TryParse(item.Element("flipVertical")?.Value, out bool vertical) && vertical
                };
            }
        }

        entries = parsed;
        southLayerWithAutoloading = UnityMathf.Clamp(
            ParseFloat(root.Element("southLayerWithAutoloading")?.Value, DefaultSouthLayerWithAutoloading),
            LayerMin,
            LayerMax);
        return true;
    }

    // 与覆盖文件同构的 XML 文本，供剪贴板导出与文件导出共用。
    internal static string ToXml(IReadOnlyList<WeaponWheelBackLayoutEntry> entries, float southLayerWithAutoloading)
    {
        StringBuilder builder = new();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<!-- Mugirl 武器轮盘背负武器布局（开发者调试窗口导出）。");
        builder.AppendLine("     放到 RimWorld 的 Config/Mugirl/WeaponWheelBackLayout.xml 后，启动游戏时覆盖内置默认值。 -->");
        builder.Append('<').Append(RootElementName).AppendLine(">");
        builder.Append("  <southLayerWithAutoloading>").Append(FormatNumber(southLayerWithAutoloading)).AppendLine("</southLayerWithAutoloading>");
        builder.AppendLine("  <entries>");
        for (int weaponIndex = 0; weaponIndex < WeaponCount; weaponIndex++)
        {
            for (int facingIndex = 0; facingIndex < FacingCount; facingIndex++)
            {
                WeaponWheelBackLayoutEntry entry = entries[IndexOf(weaponIndex, facingIndex)];
                builder.AppendLine("    <li>");
                builder.Append("      <weaponIndex>").Append(weaponIndex.ToString(CultureInfo.InvariantCulture)).AppendLine("</weaponIndex>");
                builder.Append("      <facing>").Append(FacingName(facingIndex)).AppendLine("</facing>");
                builder.Append("      <offsetX>").Append(FormatNumber(entry.OffsetX)).AppendLine("</offsetX>");
                builder.Append("      <offsetZ>").Append(FormatNumber(entry.OffsetZ)).AppendLine("</offsetZ>");
                builder.Append("      <angle>").Append(FormatNumber(entry.Angle)).AppendLine("</angle>");
                builder.Append("      <layer>").Append(FormatNumber(entry.Layer)).AppendLine("</layer>");
                builder.Append("      <flipHorizontal>").Append(BooleanLiteral(entry.FlipHorizontal)).AppendLine("</flipHorizontal>");
                builder.Append("      <flipVertical>").Append(BooleanLiteral(entry.FlipVertical)).AppendLine("</flipVertical>");
                builder.AppendLine("    </li>");
            }
        }
        builder.AppendLine("  </entries>");
        builder.Append("</").Append(RootElementName).AppendLine(">");
        return builder.ToString();
    }

    // 可直接粘贴回本文件默认表的 C# 代码，便于把调好的数值固化为新默认值。
    internal static string ToCSharp(IReadOnlyList<WeaponWheelBackLayoutEntry> entries, float southLayerWithAutoloading)
    {
        StringBuilder builder = new();
        builder.AppendLine("    private static readonly WeaponWheelBackLayoutEntry[] defaults =");
        builder.AppendLine("    [");
        for (int weaponIndex = 0; weaponIndex < WeaponCount; weaponIndex++)
        {
            for (int facingIndex = 0; facingIndex < FacingCount; facingIndex++)
            {
                WeaponWheelBackLayoutEntry entry = entries[IndexOf(weaponIndex, facingIndex)];
                bool last = weaponIndex == WeaponCount - 1 && facingIndex == FacingCount - 1;
                builder.Append("        new WeaponWheelBackLayoutEntry { OffsetX = ").Append(NumberLiteral(entry.OffsetX))
                    .Append(", OffsetZ = ").Append(NumberLiteral(entry.OffsetZ))
                    .Append(", Angle = ").Append(NumberLiteral(entry.Angle))
                    .Append(", Layer = ").Append(NumberLiteral(entry.Layer))
                    .Append(", FlipHorizontal = ").Append(BooleanLiteral(entry.FlipHorizontal))
                    .Append(", FlipVertical = ").Append(BooleanLiteral(entry.FlipVertical))
                    .Append(last ? " }" : " },")
                    .Append(" // 武器").Append((weaponIndex + 1).ToString(CultureInfo.InvariantCulture)).Append('·').AppendLine(FacingName(facingIndex));
            }
        }
        builder.AppendLine("    ];");
        builder.Append("    internal const float DefaultSouthLayerWithAutoloading = ").Append(NumberLiteral(southLayerWithAutoloading)).AppendLine(";");
        return builder.ToString();
    }

    // 调试窗口数值输入框：只接受范围内的有限数（不变区域性，小数点为“.”）。
    internal static bool TryReadNumber(string? text, float min, float max, out float value)
    {
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && float.IsFinite(value) && value >= min && value <= max;
    }

    internal static string FormatNumber(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    internal static string FacingName(int facingIndex) => facingIndex switch
    {
        0 => "North",
        1 => "East",
        2 => "South",
        _ => "West"
    };

    private static int ParseFacingIndex(string? value) => value?.Trim() switch
    {
        "North" => 0,
        "East" => 1,
        "South" => 2,
        "West" => 3,
        _ => ParseInt(value, -1)
    };

    private static int ParseInt(string? value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : fallback;
    }

    private static float ParseFloat(string? value, float fallback)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) && float.IsFinite(result)
            ? result
            : fallback;
    }

    private static string NumberLiteral(float value) => FormatNumber(value) + "f";

    private static string BooleanLiteral(bool value) => value ? "true" : "false";
}
