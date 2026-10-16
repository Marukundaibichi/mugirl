using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Mugirl.Features.Milk;

// 喂奶姿态覆盖文件 Config/Mugirl/MilkFeedingLayout.xml 的读写格式。元素名是已导出文件的格式约定，不可改名。
internal static class MilkFeedingLayoutXml
{
    internal const string RootName = "MilkFeedingLayout";

    internal static string Write(MilkFeedingLayoutEntry value)
    {
        var text = new StringBuilder();
        text.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        text.AppendLine("<!-- 喂奶姿态微调；放在 RimWorld Config/Mugirl/MilkFeedingLayout.xml。 -->");
        text.AppendLine("<" + RootName + ">");
        AppendElement(text, "sourceX", value.SourceX);
        AppendElement(text, "sourceZ", value.SourceZ);
        AppendElement(text, "recipientX", value.RecipientX);
        AppendElement(text, "recipientZ", value.RecipientZ);
        AppendElement(text, "sourceAngle", value.SourceAngle);
        AppendElement(text, "recipientAngle", value.RecipientAngle);
        text.AppendLine("</" + RootName + ">");
        return text.ToString();
    }

    // 解析并钳制；根元素不对、缺少元素或数值非有限时抛出 InvalidDataException。
    internal static MilkFeedingLayoutEntry Read(XElement? root)
    {
        if (root == null || root.Name != RootName)
        {
            throw new InvalidDataException("Unexpected XML root");
        }

        return MilkFeedingLayoutEntry.Clamp(new MilkFeedingLayoutEntry
        {
            SourceX = ReadNumber(root, "sourceX"),
            SourceZ = ReadNumber(root, "sourceZ"),
            RecipientX = ReadNumber(root, "recipientX"),
            RecipientZ = ReadNumber(root, "recipientZ"),
            SourceAngle = ReadNumber(root, "sourceAngle"),
            RecipientAngle = ReadNumber(root, "recipientAngle"),
        });
    }

    private static void AppendElement(StringBuilder text, string name, float value)
    {
        text.Append("  <").Append(name).Append('>')
            .Append(value.ToString("0.####", CultureInfo.InvariantCulture))
            .Append("</").Append(name).AppendLine(">");
    }

    private static float ReadNumber(XElement root, string name)
    {
        string? value = root.Element(name)?.Value;
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) || !float.IsFinite(result))
        {
            throw new InvalidDataException("Invalid " + name);
        }

        return result;
    }
}
