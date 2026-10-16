using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

[HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming))]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.High, "The base aiming weapon may overlap the custom equip animation.")]
public static class Harmony_PawnRenderUtility_DrawEquipmentAiming_WeaponWheel
{
    // 轮盘动画或长枪冲锋期间跳过原版手持武器绘制，由轮盘/冲锋自己绘制。
    public static bool Prefix(Thing eq)
    {
        ThingWithComps? weapon = eq as ThingWithComps;
        Pawn? pawn = (weapon?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
        if (pawn?.ParentHolder is ICustomEquippedWeaponDrawOwner)
        {
            return false;
        }

        return WeaponWheelLookup.CompFor(pawn)?.ShouldSuppressVanillaWeaponDraw(weapon) != true;
    }
}
