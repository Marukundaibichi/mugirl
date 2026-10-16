namespace Mugirl.Control;

/// <summary>
/// C.translation-keys 的键形字面量规则：只出现在字面量数组中的键也要存在（负对照）；本程序集带命名空间的类型名与以点结尾的
/// 键前缀不是翻译键（正对照）。
/// </summary>
public static class KeyLiteralControls
{
    public static readonly string[] Keys = ["Mugirl.Control.LiteralOnlyKey"];

    public static readonly string[] NotKeys = ["Mugirl.Control.KeyLiteralControls", "Mugirl.Control.Prefix."];
}
