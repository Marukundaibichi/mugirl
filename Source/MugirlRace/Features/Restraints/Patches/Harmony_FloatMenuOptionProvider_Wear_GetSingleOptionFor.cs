using HarmonyLib;

namespace Mugirl.Features.Restraints;

// 会替换锁定束具的服装，在右键“穿上”菜单中改为禁用项。目标是受保护的 override，写明参数类型以区分基类的 Pawn 重载。
[HarmonyPatch(typeof(FloatMenuOptionProvider_Wear), "GetSingleOptionFor", new[] { typeof(Thing), typeof(FloatMenuContext) })]
[MugirlPatch(PatchModule.Restraints, PatchRisk.Medium, "The wear float menu may offer apparel that would replace locked slave apparel.")]
public static class Harmony_FloatMenuOptionProvider_Wear_GetSingleOptionFor
{
    public static void Postfix(Thing clickedThing, FloatMenuContext context, ref FloatMenuOption? __result)
    {
        Pawn? pawn = context?.FirstSelectedPawn;
        if (!RestraintWearGuard.WouldReplaceLockedSlaveApparel(pawn, clickedThing as Apparel))
        {
            return;
        }

        TaggedString label = "Mugirl.FloatMenu.OptionWithReason".Translate(
            "ForceWear".Translate(clickedThing.LabelShort),
            "Mugirl.SlaveApparelCannotBeRemoved".Translate());
        __result = new FloatMenuOption(label, null, MenuOptionPriority.DisabledOption);
    }
}
