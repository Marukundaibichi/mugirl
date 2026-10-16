namespace Mugirl.Features.Abilities;

// 扣篮能力：校验受害者与落点，实际流程由 JobDriver_Dunk 与 DunkController 执行。仅开发者模式可见可用。
public class CompAbilityEffect_Dunk : CompAbilityEffect_WithDest
{
    // Targeter 每 GUI 事件读取；参数固定，构造一次复用。
    private TargetingParameters? cachedTargetParams;

    public override bool ShouldHideGizmo => !DunkUtility.FeatureEnabled;
    public override bool CanCast => DunkUtility.FeatureEnabled && base.CanCast;

    public override TargetingParameters targetParams => cachedTargetParams ??= new TargetingParameters
    {
        canTargetLocations = true,
        canTargetPawns = false,
        canTargetBuildings = false,
        canTargetItems = false,
        canTargetFires = false
    };

    public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
    {
        Pawn? caster = parent?.pawn;
        Pawn? victim = target.Thing as Pawn;
        if (!base.Valid(target, throwMessages))
        {
            return false;
        }

        if (DunkUtility.CanDunk(caster, victim, out _, out string? reason))
        {
            return true;
        }

        if (throwMessages && !reason.NullOrEmpty())
        {
            Messages.Message(reason, (Thing?)victim ?? caster, MessageTypeDefOf.RejectInput, historical: false);
        }

        return false;
    }

    public override bool CanHitTarget(LocalTargetInfo target)
    {
        Pawn? caster = parent?.pawn;
        return target.IsValid
            && caster?.Map is { } map
            && target.Cell.InBounds(map)
            && selectedTarget.IsValid
            && selectedTarget.Cell.DistanceTo(target.Cell) <= Props.range
            && DunkController.TryFindLandingCell(caster, target.Cell, out _);
    }

    public override bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
    {
        if (CanHitTarget(target))
        {
            return true;
        }

        if (showMessages)
        {
            Messages.Message("Mugirl.Dunk.InvalidDestination".Translate(Props.range.ToString("0.#")), parent?.pawn, MessageTypeDefOf.RejectInput, historical: false);
        }

        return false;
    }

    public override string? ExtraLabelMouseAttachment(LocalTargetInfo target)
    {
        if (target.Thing is not Pawn victim)
        {
            return null;
        }

        return "Mugirl.Dunk.PickupReadout".Translate(victim.LabelShortCap).ToString();
    }
}
