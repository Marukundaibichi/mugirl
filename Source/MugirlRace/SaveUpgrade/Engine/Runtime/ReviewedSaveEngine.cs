namespace Mugirl.SaveUpgrade;

/// <summary>仅认可逐项核验过的引擎读取协议；更新版本不能自动取得旧档升级权限。</summary>
internal static class ReviewedSaveEngine
{
    // StaticCacheLifecycle: 已核验的程序集身份，进程内不可变，不持有游戏对象。
    private static readonly Guid Build4979 = new("425a5939-54d7-432a-8f44-e3076b25fce9");
    private static readonly Guid Build4980 = new("8e962ecc-7ac7-42d2-8f3b-1b16cd24d2cd");

    internal static bool IsReviewed(Guid mvid) => mvid == Build4979 || mvid == Build4980;

    /// <summary>Job 空来源的保存省略规则、Clone 与 Clear 已在 4980 核验；未核验版本仍严格拒绝未知键。</summary>
    internal static bool CanPreserveUnresolvedThinkKeys(Guid mvid) => mvid == Build4980;

    /// <summary>把七个已核验的真实 Scribe 入口归到原契约编号；未知版本或入口返回 0。</summary>
    internal static int CanonicalReaderToken(Guid mvid, int token)
    {
        if (!IsReviewed(mvid)) { return 0; }
        // 4980 的这七个入口仅元数据编号后移一位；方法体、局部变量与异常区逐项相同。
        if (mvid == Build4980) { token--; }
        return token is 0x060026DD or 0x060026DE or 0x0600271C or 0x0600271D or 0x0600271F or 0x06002720 or 0x06002722
            ? token : 0;
    }
}
