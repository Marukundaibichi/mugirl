namespace Mugirl.Features.Mounting;

// 骑手武器绘制：近战武器交给 MountedMeleeSupport，远程武器在瞄准时朝向目标，否则按携带姿势绘制。
// 只在 DrawPhase.Draw（主线程）调用。
public static partial class MountedCombatController
{
    internal static void DrawWeapon(CompMount comp, UVector3 carrierDrawPos)
    {
        Pawn? rider = comp.MountedPawn;
        Pawn? carrier = comp.MooPawn;
        ThingWithComps? weapon = rider?.equipment?.Primary;
        if (rider is null || carrier is null || weapon is null)
        {
            return;
        }

        if (weapon.def.IsMeleeWeapon)
        {
            MountedMeleeSupport.DrawWeapon(comp, carrierDrawPos);
            return;
        }

        if (!weapon.def.IsRangedWeapon)
        {
            return;
        }

        UVector3 drawPos = comp.WeaponDrawPosAt(carrierDrawPos);
        float drawDistanceFactor = MountUtility.EquipmentDrawDistanceFactor(rider);
        LocalTargetInfo aimTarget = comp.Turret.aimTarget;
        Verb? mountedVerb = GetPrimaryRangedVerb(comp);
        if (!aimTarget.IsValid && mountedVerb?.state == VerbState.Bursting && mountedVerb.CurrentTarget.IsValid)
        {
            aimTarget = mountedVerb.CurrentTarget;
        }

        Thing? targetThing = aimTarget.Thing;
        if (aimTarget.IsValid && (targetThing is null || !targetThing.Destroyed))
        {
            UVector3 targetPos = aimTarget.HasThing ? targetThing!.DrawPos : aimTarget.Cell.ToVector3Shifted();
            // 与原版 PawnRenderUtility.DrawEquipmentAndApparelExtras 一致：目标几乎重合时角度为 0，verb 可覆盖瞄准角度。
            float aimAngle = MountDrawOffsets.AimAngle(targetPos.ToNumerics(), comp.RiderDrawPosAt(carrierDrawPos).ToNumerics());
            if (mountedVerb?.AimAngleOverride is { } overrideAngle)
            {
                aimAngle = overrideAngle;
            }

            Vector3 aimedPos = MountDrawOffsets.AimedWeaponPosition(drawPos.ToNumerics(), aimAngle, weapon.def.equippedDistanceOffset, drawDistanceFactor);
            PawnRenderUtility.DrawEquipmentAiming(weapon, aimedPos.ToUnity(), aimAngle);
            return;
        }

        PawnRenderUtility.DrawCarriedWeapon(weapon, drawPos, carrier.Rotation, drawDistanceFactor);
    }
}
