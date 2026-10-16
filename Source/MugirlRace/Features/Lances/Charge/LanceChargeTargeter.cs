namespace Mugirl.Features.Lances;

/// <summary>
/// 一次骑枪冲锋的瞄准会话：每次点击指令时新建，保存模式、持枪者与图标，结束瞄准后随 Targeter 一起释放。
/// 冷却、目标校验与下达任务仍由 <see cref="CompLanceCharge"/> 负责。
/// </summary>
internal sealed class LanceChargeTargeter : ITargetingSource
{
    private const float MaximumMinimumRingRadius = 90f;
    private const float MinimumVisibleRingRadius = 0.1f;

    private readonly CompLanceCharge comp;
    private readonly Pawn wearer;
    private readonly bool pointCharge;

    internal LanceChargeTargeter(CompLanceCharge comp, Pawn wearer, bool pointCharge)
    {
        this.comp = comp;
        this.wearer = wearer;
        this.pointCharge = pointCharge;
        UIIcon = comp.Props.IconFor(pointCharge);
        // 瞄准期间 Targeter 每帧多次读取；整个会话共用一份参数，不再每次分配参数与校验闭包。
        targetParams = new TargetingParameters
        {
            canTargetLocations = !pointCharge,
            canTargetPawns = true,
            canTargetHumans = true,
            canTargetAnimals = true,
            canTargetMechs = true,
            canTargetSubhumans = true,
            canTargetEntities = true,
            canTargetBloodfeeders = true,
            canTargetBuildings = false,
            mapObjectTargetsMustBeAutoAttackable = false,
            validator = target => CanHitTarget(target.HasThing
                ? new LocalTargetInfo(target.Thing)
                : new LocalTargetInfo(target.Cell)),
        };
    }

    public bool CasterIsPawn => true;

    public bool IsMeleeAttack => false;

    public bool Targetable => true;

    public bool MultiSelect => false;

    public bool HidePawnTooltips => false;

    public Thing Caster => wearer;

    public Pawn CasterPawn => wearer;

    public Verb? GetVerb => null;

    public Texture2D UIIcon { get; }

    public TargetingParameters targetParams { get; }

    public ITargetingSource? DestinationSelector => null;

    public bool CanHitTarget(LocalTargetInfo target) => comp.IsValidTarget(wearer, target, pointCharge);

    public bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
    {
        if (CanHitTarget(target))
        {
            return true;
        }

        if (showMessages)
        {
            comp.RejectInvalidTarget(wearer, pointCharge);
        }

        return false;
    }

    public void DrawHighlight(LocalTargetInfo target)
    {
        if (wearer.Spawned)
        {
            GenDraw.DrawRadiusRing(wearer.Position, comp.Props.maximumRange);
            float minimumRange = comp.Props.MinimumRangeFor(pointCharge);
            if (minimumRange > MinimumVisibleRingRadius && minimumRange < MaximumMinimumRingRadius)
            {
                GenDraw.DrawRadiusRing(wearer.Position, minimumRange);
            }
        }

        if (CanHitTarget(target))
        {
            GenDraw.DrawTargetHighlight(target);
        }
    }

    public void OrderForceTarget(LocalTargetInfo target)
    {
        if (ValidateTarget(target))
        {
            comp.StartChargeJob(wearer, target, pointCharge);
        }
    }

    public void OnGUI(LocalTargetInfo target)
    {
        GenUI.DrawMouseAttachment(CanHitTarget(target) ? UIIcon : TexCommand.CannotShoot);
    }
}
