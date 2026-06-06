using System;
using Verse;

namespace MooGirl
{
    // 配置字段可能来自 XML 实际文本，也可能来自 C# 默认翻译键；这里统一解析两种来源。
    public static class MooGirlText
    {
        public static string Resolve(string textOrKey)
        {
            if (string.IsNullOrEmpty(textOrKey))
            {
                return string.Empty;
            }

            return textOrKey.CanTranslate() ? textOrKey.Translate().ToString() : textOrKey;
        }

        public static string Resolve(string textOrKey, params NamedArgument[] args)
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
                object[] formatArgs = new object[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    formatArgs[i] = args[i].arg;
                }

                return string.Format(textOrKey, formatArgs);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("MooGirl.Text.FormatFailedLog".Translate(ex.ToString().Named("ERROR")).ToString(), Gen.HashCombineInt(textOrKey.GetHashCode(), 781233517));
                return textOrKey;
            }
        }
    }
}
