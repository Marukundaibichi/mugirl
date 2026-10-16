using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Core;

// 每帧每个绘制中的 Pawn 都会调用，保持为几次字段比较，不做缓存或查找。
internal static class MugirlIdentity
{
    internal static bool IsMugirlDef([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn is not null && pawn.def == MugirlDefOf.Mugirl;
    }

    internal static bool HasMugirlBody([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn?.RaceProps?.body is { } body && body == MugirlDefOf.MugirlBody;
    }

    internal static bool IsMugirlPawn([NotNullWhen(true)] Pawn? pawn)
    {
        // 种族本身与共用身体的衍生种族都属于本功能的目标。
        return IsMugirlDef(pawn) || HasMugirlBody(pawn);
    }

}
