namespace Mugirl.SaveUpgrade;

/// <summary>物品 ID 与加载 ID 的解析规则（与原版一致）。</summary>
/// <remarks>
/// <c>Thing.ThingID = def.defName + thingIDNumber</c>；读档时原版用正则 <c>\d+$</c> 从 ID 末尾取编号
/// （<c>Thing.IDNumberFromThingID</c>）。defName 本身以数字结尾时，编号包含 defName 的末尾数字；改名后的新 ID 必须保留
/// 这个“原版解析出的编号”，否则 <c>thingIDNumber</c> 会变，引用与按编号保存的整数（故事记录、观察记忆）就对不上。
/// 加载 ID 中嵌入的 ThingID 以开头、结尾或 <c>_</c> 为界（<c>Thing_X</c>、<c>CompEquippable_X_0</c>、
/// <c>Thing_X_tool_Maneuver</c>）。这条边界规则成立的前提是 <c>thingIDNumber</c> 全局唯一：两个不同物品的 ID 不可能
/// 一个是另一个以 <c>_</c> 分隔的后缀。微粒（Mote）没有编号，ID 就是 defName，不参与改写。
/// </remarks>
internal static class LoadIdTokens
{
    /// <summary>原版 <c>Thing.IDNumberFromThingID</c> 取到的编号文本（末尾连续数字）；没有数字时为空串。</summary>
    internal static ReadOnlySpan<char> TrailingNumber(ReadOnlySpan<char> thingId)
    {
        int start = thingId.Length;
        while (start > 0 && char.IsAsciiDigit(thingId[start - 1]))
        {
            start--;
        }

        return thingId[start..];
    }

    /// <summary>
    /// <paramref name="id"/> 是否是 def 为 <paramref name="defName"/> 的物品 ID：以 defName 开头，其后至少一位且全是数字。
    /// 用于在存档中按结构识别物品元素（<c>&lt;def&gt;</c> 加 <c>&lt;id&gt;</c>）。
    /// </summary>
    internal static bool IsThingIdOf(string defName, string id)
    {
        if (defName.Length == 0 || id.Length <= defName.Length || !id.StartsWith(defName, StringComparison.Ordinal))
        {
            return false;
        }

        for (int i = defName.Length; i < id.Length; i++)
        {
            if (!char.IsAsciiDigit(id[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>旧物品 ID 在 def 改名后的新 ID：新 defName 加上旧 ID 按原版规则解析出的编号。</summary>
    internal static string RenamedThingId(string oldId, string newDefName) => newDefName + TrailingNumber(oldId).ToString();

    /// <summary>
    /// 同一 DOM 再升级时，def 仍是旧名而 id 已规范化：重建原始身份，令尚未读取的引用仍可解析。
    /// 旧 defName 的数字后缀已经属于原版解析出的编号，不能再拼一次。
    /// </summary>
    internal static string? OriginalThingId(string oldDef, string newDef, string normalizedId)
    {
        if (!IsThingIdOf(newDef, normalizedId))
        {
            return null;
        }

        ReadOnlySpan<char> number = TrailingNumber(normalizedId);
        ReadOnlySpan<char> defDigits = TrailingNumber(oldDef);
        if (!number.StartsWith(defDigits, StringComparison.Ordinal) || number.Length <= defDigits.Length)
        {
            return null;
        }

        return oldDef[..(oldDef.Length - defDigits.Length)] + number.ToString();
    }

    /// <summary>
    /// 账单加载 ID <c>Bill_&lt;recipe&gt;_&lt;n&gt;</c>（<c>Bill.GetUniqueLoadID</c>）。成功时返回配方名与编号。
    /// </summary>
    internal static bool TryParseBillId(string text, out string recipe, out string number)
    {
        recipe = string.Empty;
        number = string.Empty;
        const string Prefix = "Bill_";
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        int underscore = text.LastIndexOf('_');
        if (underscore <= Prefix.Length || underscore == text.Length - 1)
        {
            return false;
        }

        for (int i = underscore + 1; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                return false;
            }
        }

        recipe = text[Prefix.Length..underscore];
        number = text[(underscore + 1)..];
        return true;
    }
}
