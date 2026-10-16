namespace Mugirl.Core;

/// <summary>B.architecture mugirl-identity：与正式 Mod 同名的 DefOf 字段在 MugirlIdentity 之外被读取。</summary>
[DefOf]
public static class MugirlDefOf
{
    public static BodyDef MugirlBody;

    static MugirlDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(MugirlDefOf));
}
