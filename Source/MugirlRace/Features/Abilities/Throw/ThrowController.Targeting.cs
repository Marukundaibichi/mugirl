namespace Mugirl.Features.Abilities;

// 瞄准接口：举起后由 Targeter 选择落点格。射程与读数按短 TTL 缓存，Targeter 与 GUI 每帧多次读取。
public partial class ThrowController : ITargetingSource
{
    // 瞄准读数与射程校验共用；携带上限在瞄准期内基本不变，按 30 tick 失效。
    private const int MaxThrowDistanceCacheTicks = 30;

    private float cachedMaxThrowDistance = -1f;
    private int cachedMaxThrowDistanceTick = int.MinValue;
    private TargetingParameters? cachedTargetParams;

    public bool CasterIsPawn => true;
    public bool IsMeleeAttack => false;
    public bool Targetable => true;
    public bool MultiSelect => false;
    public bool HidePawnTooltips => true;
    public Thing? Caster => carrier;
    public Pawn? CasterPawn => carrier;
    public Verb? GetVerb => null;
    public Texture2D UIIcon => sourceAbility?.def?.uiIcon ?? BaseContent.BadTex;
    public ITargetingSource? DestinationSelector => null;

    // Targeter 每 GUI 事件读取；参数在瞄准会话内不变，构造一次复用。
    public TargetingParameters targetParams => cachedTargetParams ??= new TargetingParameters
    {
        canTargetLocations = true,
        canTargetPawns = false,
        canTargetBuildings = false,
        canTargetItems = false,
        canTargetFires = false
    };

    // GetStatValue 走完整 Stat 管线，这里按短 TTL 缓存，携带上限变化最迟 30 tick 后反映。
    private float MaxThrowDistance
    {
        get
        {
            int tick = TickUtility.CurrentGameTickOrFallback(0);
            if (cachedMaxThrowDistanceTick >= 0 && tick < cachedMaxThrowDistanceTick + MaxThrowDistanceCacheTicks)
            {
                return cachedMaxThrowDistance;
            }

            cachedMaxThrowDistance = ThrowUtility.MaxThrowDistance(carrier, mass);
            cachedMaxThrowDistanceTick = tick;
            return cachedMaxThrowDistance;
        }
    }

    public bool CanHitTarget(LocalTargetInfo target)
    {
        return target.IsValid
            && target.Cell.InBounds(Map)
            && carrier is not null
            && carrier.Spawned
            && !carrier.WorkTagIsDisabled(WorkTags.Violent)
            && carrier.Position.DistanceTo(target.Cell) <= MaxThrowDistance;
    }

    public bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
    {
        if (CanHitTarget(target))
        {
            return true;
        }

        if (showMessages)
        {
            Messages.Message("Mugirl.Throw.OutOfRange".Translate(MaxThrowDistance.ToString("0.#")), carrier, MessageTypeDefOf.RejectInput, historical: false);
        }

        return false;
    }

    public void DrawHighlight(LocalTargetInfo target)
    {
        if (carrier is null)
        {
            return;
        }

        if (carrier.Spawned)
        {
            GenDraw.DrawRadiusRing(carrier.Position, MaxThrowDistance);
        }

        if (target.IsValid)
        {
            int damage = ThrowBalance.ImpactDamage(mass, carrier.Position.DistanceTo(target.Cell));
            GenDraw.DrawTargetHighlight(target);
            GenDraw.DrawRadiusRing(target.Cell, ThrowBalance.ExplosionRadius(damage), new Color(1f, 0.35f, 0.15f));
        }
    }

    public void OrderForceTarget(LocalTargetInfo target)
    {
        if (ValidateTarget(target))
        {
            Launch(target.Cell);
        }
    }

    public void OnGUI(LocalTargetInfo target)
    {
        GenUI.DrawMouseAttachment(CanHitTarget(target) ? UIIcon : TexCommand.CannotShoot);
        if (target.IsValid && carrier is not null)
        {
            float distance = carrier.Position.DistanceTo(target.Cell);
            int damage = ThrowBalance.ImpactDamage(mass, distance);
            float radius = ThrowBalance.ExplosionRadius(damage);
            Widgets.MouseAttachedLabel("Mugirl.Throw.AimReadout".Translate(damage, radius.ToString("0.#"), MaxThrowDistance.ToString("0.#")));
        }
    }
}
