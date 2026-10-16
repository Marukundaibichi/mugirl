using System.Reflection;

namespace Mugirl.GameValidation.Core;

/// <summary>验证观察器独立核验的引擎身份与入口编号；未知构建不能使用旧版投影规则。</summary>
internal static class ReviewedEngine
{
    // StaticCacheLifecycle: 已核验的程序集身份，进程内不可变，不持有游戏对象。
    private static readonly Guid Build4979 = new("425a5939-54d7-432a-8f44-e3076b25fce9");
    private static readonly Guid Build4980 = new("8e962ecc-7ac7-42d2-8f3b-1b16cd24d2cd");

    public static Guid Mvid
    {
        get
        {
            Guid current = typeof(Game).Module.ModuleVersionId;
            if (!IsReviewed(current)) { throw new InvalidOperationException("Unreviewed engine for save observations."); }
            return current;
        }
    }

    internal static bool IsReviewed(Guid mvid) => mvid == Build4979 || mvid == Build4980;

    // Type.FullName 会把泛型实参的程序集版本嵌进名字；只为两个已核验引擎统一这一段身份。
    // 字符串常量、其他程序集和真正的类型名不变，证据仍记录实际 MVID 与 token。
    public static string? CanonicalTypeName(Type? type, bool qualified = false)
    {
        _ = Mvid;
        string? name = qualified ? type?.AssemblyQualifiedName : type?.FullName;
        return name?.Replace(typeof(Game).Assembly.FullName!,
            "Assembly-CSharp, Version=1.7.9784.3272, Culture=neutral, PublicKeyToken=null", StringComparison.Ordinal);
    }

    public static int Token(int token4979) => Token(Mvid, token4979);

    internal static int Token(Guid mvid, int token4979)
    {
        // 每项均成对核验了方法签名、解析引用后的 IL、局部变量和异常区；不能按全程序集偏移猜编号。
        int token4980 = token4979 switch
        {
            0x06000595 => 0x06000595,
            0x06000DD2 => 0x06000DD2,
            0x06000DD3 => 0x06000DD3,
            0x06000DD9 => 0x06000DD9,
            0x06000DEF => 0x06000DEF,
            0x06001458 => 0x06001459,
            0x0600145B => 0x0600145C,
            0x06001460 => 0x06001461,
            0x060026DD => 0x060026DE,
            0x060026DE => 0x060026DF,
            0x060026E6 => 0x060026E7,
            0x06002705 => 0x06002706,
            0x0600271C => 0x0600271D,
            0x0600271D => 0x0600271E,
            0x0600271F => 0x06002720,
            0x06002720 => 0x06002721,
            0x06002722 => 0x06002723,
            0x06002742 => 0x06002743,
            0x06002769 => 0x0600276A,
            0x060027A2 => 0x060027A3,
            0x06002A98 => 0x06002A99,
            0x06002B79 => 0x06002B7A,
            0x06002B7C => 0x06002B7D,
            0x06002B7D => 0x06002B7E,
            0x06002B7F => 0x06002B80,
            0x060034A1 => 0x060034A2,
            0x060034B1 => 0x060034B2,
            0x060068A7 => 0x060068C3,
            0x060068C2 => 0x060068DE,
            0x060068D8 => 0x060068F4,
            0x06008731 => 0x0600874D,
            0x06008881 => 0x0600889E,
            0x06008889 => 0x060088A6,
            0x06009D8E => 0x06009DAB,
            0x06009D9F => 0x06009DBC,
            0x0600A4BF => 0x0600A4DC,
            0x0600B1C1 => 0x0600B1DE,
            0x0600E52D => 0x0600E550,
            _ => 0,
        };
        if (!IsReviewed(mvid) || token4980 == 0) { throw new InvalidOperationException("Unreviewed engine reader token."); }
        return mvid == Build4979 ? token4979 : token4980;
    }

    public static MethodBase ResolveMethod(int token4979)
        => typeof(Game).Module.ResolveMethod(Token(token4979))
            ?? throw new InvalidOperationException("The reviewed engine reader is missing.");
}
