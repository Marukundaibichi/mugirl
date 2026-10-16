namespace Mugirl.Features.Corporate;

/// <summary>
/// 窗口级的文本测量与截断缓存。终端每个 GUI 事件要为每张卡片测量与截断多段文字，
/// 1.6 每次都重新调用 <c>Text.CalcHeight</c>/<c>CalcSize</c> 并逐字缩短；这里按（文本、尺寸、字体、换行）记住结果。
/// </summary>
/// <remarks>
/// 截断算法与 1.6 逐步相同，所以结果逐字一致：<see cref="FitHeight"/> 是终端卡片的“…”截断，<see cref="FitWidth"/> 是
/// Verse <c>GenText.Truncate</c> 的“...”截断。测量结果取决于字体与界面缩放，窗口是模态的，打开期间二者不会变化；
/// 窗口关闭时调用 <see cref="Clear"/>。
/// </remarks>
internal sealed class CorporateTextCache
{
    private const int Limit = 4096;
    private const string Ellipsis = "…";
    private const string VerseEllipsis = "...";

    private readonly Dictionary<Key, string> fitted = [];
    private readonly Dictionary<Key, float> measured = [];

    private enum Mode
    {
        FitHeight,
        FitWidth,
        Height,
        Width,
    }

    private readonly record struct Key(string Text, float Width, float Height, GameFont Font, bool WordWrap, Mode Mode);

    /// <summary>
    /// 以 <paramref name="font"/> 与当前换行设置，把 <paramref name="text"/> 截断到 <paramref name="width"/> × <paramref name="height"/> 之内：
    /// 放得下时原样返回，否则返回最长的“前缀 + …”。
    /// </summary>
    internal string FitHeight(string text, float width, float height, GameFont font)
    {
        using (new TextBlock(font))
        {
            Key key = new(text, width, height, font, Text.WordWrap, Mode.FitHeight);
            if (fitted.TryGetValue(key, out string? result))
            {
                return result;
            }

            result = text;
            if (Text.CalcHeight(text, width) > height)
            {
                int length = text.Length;
                while (length > 0 && Text.CalcHeight(string.Concat(text.AsSpan(0, length), Ellipsis), width) > height)
                {
                    length--;
                }

                result = string.Concat(text.AsSpan(0, length), Ellipsis);
            }

            Store(fitted, key, result);
            return result;
        }
    }

    /// <summary>以 <paramref name="font"/> 单行截断到 <paramref name="width"/>（Verse <c>GenText.Truncate</c> 的算法与“...”后缀）。</summary>
    internal string FitWidth(string text, float width, GameFont font)
    {
        using (new TextBlock(font))
        {
            Key key = new(text, width, 0f, font, Text.WordWrap, Mode.FitWidth);
            if (fitted.TryGetValue(key, out string? result))
            {
                return result;
            }

            result = text;
            if (Text.CalcSize(text).x > width)
            {
                do
                {
                    result = result[..^1];
                }
                while (result.Length > 0 && Text.CalcSize(result + VerseEllipsis).x > width);

                result += VerseEllipsis;
            }

            Store(fitted, key, result);
            return result;
        }
    }

    /// <summary>以 <paramref name="font"/> 与当前换行设置测量 <paramref name="text"/> 在 <paramref name="width"/> 宽度下的高度。</summary>
    internal float Height(string text, float width, GameFont font)
    {
        using (new TextBlock(font))
        {
            Key key = new(text, width, 0f, font, Text.WordWrap, Mode.Height);
            if (!measured.TryGetValue(key, out float height))
            {
                height = Text.CalcHeight(text, width);
                Store(measured, key, height);
            }

            return height;
        }
    }

    /// <summary>以 <paramref name="font"/> 测量 <paramref name="text"/> 的单行宽度。</summary>
    internal float Width(string text, GameFont font)
    {
        using (new TextBlock(font))
        {
            Key key = new(text, 0f, 0f, font, Text.WordWrap, Mode.Width);
            if (!measured.TryGetValue(key, out float width))
            {
                width = Text.CalcSize(text).x;
                Store(measured, key, width);
            }

            return width;
        }
    }

    internal void Clear()
    {
        fitted.Clear();
        measured.Clear();
    }

    private static void Store<T>(Dictionary<Key, T> cache, Key key, T value)
    {
        if (cache.Count >= Limit)
        {
            cache.Clear();
        }

        cache[key] = value;
    }
}
