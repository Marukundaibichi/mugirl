using Verse.AI;

namespace Mugirl.Features.Lances;

/// <summary>
/// 骑枪冲锋：保存两种冲锋各自的冷却，生成装备指令，并负责目标校验与下达冲锋任务。
/// 瞄准会话由 <see cref="LanceChargeTargeter"/> 承担，组件本身不保存瞄准期间的临时状态。
/// </summary>
public sealed class CompLanceCharge : ThingComp
{
    private int pointCooldownUntilTick;
    private int lineCooldownUntilTick;

    public CompProperties_LanceCharge Props => (CompProperties_LanceCharge)props;

    private Pawn? Wearer => (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;

    private static int CurrentTick => TickUtility.CurrentGameTickOrFallback(0);

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref pointCooldownUntilTick, "mugirlLancePointCooldownUntilTick", 0);
        Scribe_Values.Look(ref lineCooldownUntilTick, "mugirlLanceLineCooldownUntilTick", 0);
    }

    // 武器装备后由装备栏补丁转发；原版装备栏不会调用武器的 CompGetGizmosExtra。
    public IEnumerable<Gizmo> GetEquippedGizmos()
    {
        Pawn? wearer = Wearer;
        if (wearer is null || !wearer.IsColonistPlayerControlled
            || !MugirlIdentity.IsMugirlPawn(wearer) || wearer.equipment?.Primary != parent)
        {
            yield break;
        }

        if (Props.hasPointCharge)
        {
            yield return CreateCommand(
                point: true,
                "Mugirl.Lance.PointCharge.Label".Translate(),
                "Mugirl.Lance.PointCharge.Desc".Translate());
        }

        if (Props.hasLineCharge)
        {
            yield return CreateCommand(
                point: false,
                "Mugirl.Lance.LineCharge.Label".Translate(),
                "Mugirl.Lance.LineCharge.Desc".Translate());
        }
    }

    internal bool IsValidTarget(Pawn? wearer, LocalTargetInfo target, bool point)
    {
        if (wearer?.Spawned != true || wearer.equipment?.Primary != parent || !target.IsValid
            || !target.Cell.IsValid || !target.Cell.InBounds(wearer.Map))
        {
            return false;
        }

        float distance = wearer.Position.DistanceTo(target.Cell);
        if (distance < Props.MinimumRangeFor(point) || distance > Props.maximumRange)
        {
            return false;
        }

        if (target.Thing is null)
        {
            return !point && LanceChargeDestinationUtility.TryFindLandingCell(wearer, target.Cell, false, out _);
        }

        bool validPawn = target.Thing is Pawn targetPawn
            && targetPawn.Spawned
            && !targetPawn.Dead
            && targetPawn != wearer
            && targetPawn.Map == wearer.Map
            && wearer.HostileTo(targetPawn);
        return validPawn && LanceChargeDestinationUtility.TryFindLandingCell(wearer, target.Thing.Position, point, out _);
    }

    internal void RejectInvalidTarget(Pawn? wearer, bool point)
    {
        string key = point ? "Mugirl.Lance.PointCharge.InvalidTarget" : "Mugirl.Lance.LineCharge.InvalidTarget";
        // 主线程文化由原版固定为 en-US（CultureInfoUtility.EnsureEnglish），小数点格式与 1.6 相同。
        Messages.Message(
            key.Translate(Props.MinimumRangeFor(point).ToString("0.#"), Props.maximumRange.ToString("0.#")),
            wearer,
            MessageTypeDefOf.RejectInput,
            false);
    }

    internal void StartChargeJob(Pawn wearer, LocalTargetInfo target, bool point)
    {
        if (!IsValidTarget(wearer, target, point))
        {
            RejectInvalidTarget(wearer, point);
            return;
        }

        JobDef jobDef = point ? LancesDefOf.Mugirl_LancePointCharge : LancesDefOf.Mugirl_LanceLineCharge;
        // 沿途冲锋锁定点击时的格子，不追逐会移动的 Pawn；定点冲锋仍锁定敌对 Pawn。
        Job job = point
            ? JobMaker.MakeJob(jobDef, target, new LocalTargetInfo(target.Cell))
            : JobMaker.MakeJob(jobDef, new LocalTargetInfo(target.Cell));
        job.playerForced = true;
        if (!wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc))
        {
            return;
        }

        if (point)
        {
            pointCooldownUntilTick = CurrentTick + Props.pointCooldownTicks;
        }
        else
        {
            lineCooldownUntilTick = CurrentTick + Props.lineCooldownTicks;
        }
    }

    private int CooldownUntilTick(bool point) => point ? pointCooldownUntilTick : lineCooldownUntilTick;

    private Command_ActionWithCooldown CreateCommand(bool point, string label, string description)
    {
        Pawn? wearer = Wearer;
        int ticksLeft = LanceChargeCooldown.TicksLeft(CooldownUntilTick(point), CurrentTick);
        Command_ActionWithCooldown command = new()
        {
            defaultLabel = label,
            defaultDesc = description,
            icon = Props.IconFor(point),
            action = () => BeginTargeting(point),
            cooldownPercentGetter = () => LanceChargeCooldown.Percent(CooldownUntilTick(point), Props.CooldownTicksFor(point), CurrentTick),
        };

        if (wearer?.Drafted != true)
        {
            command.Disable("Mugirl.Lance.MustBeDrafted".Translate());
        }
        else if (ticksLeft > 0)
        {
            command.Disable("Mugirl.Lance.Cooldown".Translate(ticksLeft.ToStringTicksToPeriod()));
        }

        return command;
    }

    private void BeginTargeting(bool point)
    {
        Pawn? wearer = Wearer;
        if (wearer is null || !wearer.Spawned || !wearer.Drafted)
        {
            return;
        }

        GameUtility.TryBeginTargeting(
            new LanceChargeTargeter(this, wearer, point),
            actionWhenFinished: null,
            allowNonSelectedTargetingSource: false,
            requiresAvailableVerb: false);
    }
}
