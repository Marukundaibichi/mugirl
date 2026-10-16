using System.Globalization;

namespace Mugirl.ShaderCheck;

/// <summary>
/// 与 Verse.ParseHelper.ParseRenderQueue 相同的规则：整数、UnityEngine.Rendering.RenderQueue 名称，或 名称+偏移 / 名称-偏移。
/// </summary>
internal static class RenderQueueParser
{
    private static readonly Dictionary<string, int> Names = new(StringComparer.Ordinal)
    {
        ["Background"] = 1000,
        ["Geometry"] = 2000,
        ["AlphaTest"] = 2450,
        ["GeometryLast"] = 2500,
        ["Transparent"] = 3000,
        ["Overlay"] = 4000,
    };

    /// <summary>解析 <paramref name="text"/>；不是渲染队列时抛出 <see cref="FormatException"/>。</summary>
    public static int Parse(string text)
    {
        text = text.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }

        int separator = text.IndexOfAny(['+', '-']);
        string name = (separator < 0 ? text : text[..separator]).Trim();
        if (!Names.TryGetValue(name, out int queue))
        {
            throw new FormatException($"'{text}' is not a render queue.");
        }

        if (separator >= 0)
        {
            int offset = int.Parse(text.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture);
            queue += text[separator] == '-' ? -offset : offset;
        }

        return queue;
    }
}
