namespace Mugirl.Features.Abilities;

// 扣篮起跳用的飞行器（XML thingClass）：进入下坠阶段时，扣篮道具让头先脱手砸向地面。
public sealed class PawnFlyer_Dunk : PawnFlyer
{
    internal float AdjustedFlightProgress => ticksFlightTime > 0
        ? def.pawnFlyer.Worker.AdjustedProgress(UnityMathf.Clamp01((float)ticksFlying / ticksFlightTime))
        : 0f;

    internal bool IsDescending => AdjustedFlightProgress >= 0.5f;
}
