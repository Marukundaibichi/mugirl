using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Abilities;

internal static class DunkUtility
{
    // 扣篮是开发者模式专用能力：按钮显示、施放与目标校验都以此为准。
    internal static bool FeatureEnabled => Prefs.DevMode;

    internal static bool CanDunk([NotNullWhen(true)] Pawn? caster, [NotNullWhen(true)] Pawn? victim, [NotNullWhen(true)] out BodyPartRecord? head, out string? reason)
    {
        head = null;
        reason = null;
        if (!FeatureEnabled)
        {
            reason = "Mugirl.Dunk.DevOnly".Translate().ToString();
            return false;
        }

        if (caster is null || victim is null || caster == victim || victim.Destroyed || victim.Dead
            || !victim.Spawned || victim.Map != caster.Map || !victim.RaceProps.Humanlike
            || victim.story?.headType is null)
        {
            reason = "Mugirl.Dunk.InvalidTarget".Translate().ToString();
            return false;
        }

        if (ThrowUtility.FindHeldController(caster) is not null
            || ThrowUtility.FindDunkController(caster) is not null)
        {
            reason = "Mugirl.Throw.AlreadyHolding".Translate().ToString();
            return false;
        }

        List<BodyPartRecord> parts = victim.RaceProps.body.AllParts;
        for (int i = 0; i < parts.Count; i++)
        {
            BodyPartRecord part = parts[i];
            if (part.def == BodyPartDefOf.Head && !victim.health.hediffSet.PartIsMissing(part))
            {
                head = part;
                return true;
            }
        }

        reason = "Mugirl.Dunk.NoHead".Translate(victim.LabelCap).ToString();
        return false;
    }
}
