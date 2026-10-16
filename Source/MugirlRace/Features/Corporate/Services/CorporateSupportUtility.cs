using Verse.AI;

namespace Mugirl.Features.Corporate;

// 巨企支援小队的随队补给与战场医疗。
// 补给生成后直接进背包；进食交给原版 LordDuty 子树的 UrgentlyHungry 兜底
// （JobGiver_GetFood 优先吃背包食物），医疗由 Mugirl_CorporateSupportHunt
// duty 里的 JobGiver_CorporateSupportTend 在索敌之后执行。
internal static class CorporateSupportUtility
{
    public const int SurvivalPackCount = 2;
    public const int MedicineCount = 2;

    internal static bool CanAcceptFieldTending(Pawn medic, Pawn? patient)
    {
        if (patient == null || patient.Dead || !patient.Spawned || patient.Map != medic.Map
            || patient.InMentalState || patient.Drafted)
        {
            return false;
        }

        if (patient == medic || patient.Downed || patient.InBed())
        {
            return true;
        }

        // 尊重玩家正在执行的命令，也不让两名医护互相打断治疗或拦下离图人员。
        Job? current = patient.CurJob;
        return current == null || (!current.playerForced && !current.exitMapOnArrival
            && current.def != JobDefOf.TendPatient
            && current.def != CorporateDefOf.Mugirl_CorporateSupportTend
            && current.def != JobDefOf.AttackMelee && current.def != JobDefOf.AttackStatic
            && current.def != JobDefOf.Wait_Combat);
    }

    public static void EquipFieldSupplies(Pawn? pawn)
    {
        if (pawn?.inventory == null)
        {
            return;
        }

        AddToInventory(pawn, ThingDefOf.MealSurvivalPack, SurvivalPackCount, null);
        // 医药固定普通品质，与每周赠礼的医药一致。
        AddToInventory(pawn, ThingDefOf.MedicineIndustrial, MedicineCount, QualityCategory.Normal);
    }

    private static void AddToInventory(Pawn pawn, ThingDef def, int count, QualityCategory? quality)
    {
        Thing thing = ThingMaker.MakeThing(def);
        if (quality.HasValue)
        {
            thing.TryGetComp<CompQuality>()?.SetQuality(quality.Value, ArtGenerationContext.Outsider);
        }

        thing.stackCount = count;
        if (!pawn.inventory.innerContainer.TryAdd(thing))
        {
            thing.Destroy(DestroyMode.Vanish);
        }
    }
}
