namespace Mugirl.Core;

// 配置字段可能来自 XML 实际文本，也可能来自 C# 默认翻译键；这里统一解析两种来源。
public static class MugirlText
{
    public static string Resolve(string? textOrKey)
    {
        if (string.IsNullOrEmpty(textOrKey))
        {
            return string.Empty;
        }

        return textOrKey.CanTranslate() ? textOrKey.Translate().ToString() : textOrKey;
    }

    public static string Resolve(string? textOrKey, params NamedArgument[] args)
    {
        if (string.IsNullOrEmpty(textOrKey))
        {
            return string.Empty;
        }

        if (textOrKey.CanTranslate())
        {
            return textOrKey.Translate(args).ToString();
        }

        try
        {
            object?[] formatArgs = new object?[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                formatArgs[i] = args[i].arg;
            }

            return string.Format(textOrKey, formatArgs);
        }
        catch (Exception ex)
        {
            // 参数的 ToString 也可能抛出任意异常；文本解析失败时退回原文，不中断调用方。
            string detail = ex.GetType().Name + ": " + ex.Message;
            // 限次键用稳定哈希：string.GetHashCode 在游戏运行时与标准 .NET 上结果不同。
            MugirlLog.WarningOnce(
                "Text.FormatFailed." + MugirlLog.StableHash(textOrKey),
                () => "Mugirl.Text.FormatFailedLog".Translate(detail.Named("ERROR")).ToString());
            return textOrKey;
        }
    }
}
