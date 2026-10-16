using Mugirl.Features.Incidents;

namespace Mugirl.Features.Abilities;

// 触地结算（爆炸、碎裂与血迹）、中途放下载荷，以及落脚格搜索。
public partial class DunkController
{
    // 飞行者朝南绘制，落在篮筐北侧两格，头才会砸在她前方和画面下方；正北优先，其次西北、东北。
    private static readonly IntVec3[] LandingOffsets = [new(0, 0, 2), new(-1, 0, 2), new(1, 0, 2)];

    // 放置失败后的重试退避（与 ThrowController 相同节奏），不存档；
    // 避免 pendingDrop 状态每 tick 重复全径向落点扫描。
    private DropRetryBackoff dropRetry;

    internal static bool TryFindLandingCell(Pawn? carrier, IntVec3 center, out IntVec3 landingCell)
    {
        landingCell = IntVec3.Invalid;
        Map? map = carrier?.Map;
        if (carrier is null || map is null || !center.IsValid || !center.InBounds(map))
        {
            return false;
        }

        for (int i = 0; i < LandingOffsets.Length; i++)
        {
            IntVec3 cell = center + LandingOffsets[i];
            if (cell.InBounds(map) && JumpUtility.ValidJumpTarget(carrier, map, cell))
            {
                landingCell = cell;
                return true;
            }
        }

        return false;
    }

    private void FinishImpact(bool explode)
    {
        Map? map = Map;
        if (resolving || map is null)
        {
            return;
        }

        resolving = true;
        int damage = ThrowBalance.DunkImpactDamage(mass);
        Thing? payload = Payload;
        if (payload is DunkHead)
        {
            // 触地当刻毁掉容器内的头，不会在地面多绘制一帧完整的头。
            payload.Destroy(DestroyMode.Vanish);
        }
        else
        {
            Thing? placed = PayloadPlacement.TryPlace(innerContainer, impactCell, originalPosition, map, originalRotation, "Dunk.PlaceFailed.");
            if (placed is null && HasPayload)
            {
                resolving = false;
                TryDropSafely();
                return;
            }

            ShatterPayload(placed, damage);
        }

        Vector3 smashPos = impactCell.ToVector3Shifted().ToNumerics();
        ThrowBloodBurst(smashPos, 36, 0.5f, 1.35f, 1.1f, 3.5f);
        PunisherDeathEffectUtility.Play(smashPos.ToUnity(), map, 1.35f);

        if (explode)
        {
            Explode(map, damage);
        }

        LeaveBlood(impactCell, 5);
        motion.phase = DunkPhase.Finished;
        motion.impactAnimating = false;
        motion.impactTracksCarrier = false;
        motion.phaseTicks = 0;
        presentation.Reset();
        resolving = false;
    }

    private void Explode(Map map, int damage)
    {
        float radius = UnityMathf.Clamp(ThrowBalance.ExplosionRadius(damage) * 1.12f, 2.8f, 7.2f);
        DamageDef damageDef = AbilitiesDefOf.Mugirl_ThrownImpact ?? DamageDefOf.Bomb;
        List<Thing> ignored = [this];
        if (carrier is not null)
        {
            ignored.Add(carrier);
        }

        if (carrier?.ParentHolder is PawnFlyer activeFlyer && !activeFlyer.Destroyed)
        {
            ignored.Add(activeFlyer);
        }

        if (victim?.Corpse is { Destroyed: false } corpse)
        {
            ignored.Add(corpse);
        }

        GenExplosion.DoExplosion(
            center: impactCell,
            map: map,
            radius: radius,
            damType: damageDef,
            instigator: carrier,
            damAmount: damage,
            armorPenetration: damage * 0.015f,
            damageFalloff: true,
            ignoredThings: ignored,
            screenShakeFactor: UnityMathf.Clamp(radius / 2.2f, 1.3f, 3f));
    }

    private void ThrowBloodBurst(Vector3 center, int count, float minScale, float maxScale, float minSpeed, float maxSpeed)
    {
        Map? map = Map;
        FleckDef? splashDef = AbilitiesDefOf.Mugirl_DunkBloodSplash;
        if (!bleeds || bloodDef is null || map is null || splashDef is null)
        {
            return;
        }

        Color color = bloodDef.graphicData?.color ?? UnityColor.Red;
        color.a = 0.9f;
        for (int i = 0; i < count; i++)
        {
            Vector3 sprayPoint = center + Gen.RandomHorizontalVector(0.18f).ToNumerics();
            FleckCreationData fleck = FleckMaker.GetDataStatic(sprayPoint.ToUnity(), map, splashDef, Rand.Range(minScale, maxScale));
            fleck.velocityAngle = Rand.Range(0f, 360f);
            fleck.velocitySpeed = Rand.Range(minSpeed, maxSpeed);
            fleck.rotationRate = Rand.Range(-180f, 180f);
            fleck.instanceColor = color;
            map.flecks.CreateFleck(fleck);
        }
    }

    private void LeaveBlood(IntVec3 cell, int count)
    {
        Map? map = Map;
        if (bleeds && bloodDef is not null && map is not null && cell.InBounds(map))
        {
            FilthMaker.TryMakeFilth(cell, map, bloodDef, victim?.LabelShort, count);
        }
    }

    // 非头部载荷按其 Thing 类型结算致命撞击与销毁，保留载荷的实际实例。
    private void ShatterPayload(Thing? payload, int damage)
    {
        if (payload is null || payload.Destroyed)
        {
            return;
        }

        if (payload is Pawn payloadPawn)
        {
            if (!payloadPawn.Dead)
            {
                DamageInfo fatalImpact = new(DamageDefOf.Crush, UnityMathf.Max(damage * 6f, 9999f), 1f, -1f, carrier);
                payloadPawn.TakeDamage(fatalImpact);
                if (!payloadPawn.Dead)
                {
                    payloadPawn.Kill(fatalImpact);
                }
            }

            Corpse? corpse = payloadPawn.Corpse;
            if (corpse is not null && !corpse.Destroyed)
            {
                corpse.TakeDamage(new DamageInfo(DamageDefOf.Bomb, UnityMathf.Max(damage * 4f, corpse.MaxHitPoints + 20f), 1f, -1f, carrier));
                if (!corpse.Destroyed)
                {
                    corpse.Destroy(DestroyMode.KillFinalize);
                }
            }

            return;
        }

        payload.TakeDamage(new DamageInfo(DamageDefOf.Crush, UnityMathf.Max(damage * 6f, payload.MaxHitPoints + 20f), 1f, -1f, carrier));
        if (!payload.Destroyed)
        {
            payload.Destroy(DestroyMode.KillFinalize);
        }
    }

    public void TryDropSafely()
    {
        if (Destroyed || resolving)
        {
            return;
        }

        // 上次放置失败后的退避窗口内不再重试。
        int now = TickUtility.CurrentGameTickOrFallback(0);
        if (pendingDrop && dropRetry.IsWaiting(now))
        {
            return;
        }

        pendingDrop = true;
        resolving = true;
        Map? map = Map;
        IntVec3 preferred = carrier is not null && carrier.Spawned && carrier.Map == map ? carrier.Position : originalPosition;
        Thing? dropped = PayloadPlacement.TryPlace(innerContainer, preferred, originalPosition, map, originalRotation, "Dunk.PlaceFailed.");
        if (dropped is not null)
        {
            sourceAbility?.ResetCooldown();
            Destroy();
        }
        else
        {
            resolving = false;
            dropRetry.Schedule(now);
        }
    }
}
