namespace Mugirl.Features.Incidents;

// 事件功能（快递、逃跑牧场、惩戒者等）定义的 Def，以及本模块用到、原版 DefOf 没有收录的原版 Core Def。
[DefOf]
internal static class IncidentsDefOf
{
    // 逃跑牧场的围栏与牧草。
    public static ThingDef Fence = null!;
    public static ThingDef FenceGate = null!;
    public static ThingDef Plant_Haygrass = null!;

    // 救援对象迫降时的轻伤。
    public static HediffDef Scratch = null!;
    public static HediffDef Bruise = null!;
    public static HediffDef Crack = null!;

    // 快递事件特殊物品引用。
    public static ThingDef Mugirl_CourierDiary = null!;

    // 快递事件任务引用。
    public static QuestScriptDef Mugirl_CourierContact = null!;

    public static SitePartDef Mugirl_RunawayFarm = null!;

    public static HediffDef Mugirl_RunawayPanic = null!;

    public static ThingDef Mugirl_PunisherRace = null!;

    public static PawnKindDef Mugirl_Punisher = null!;

    public static HediffDef Mugirl_PunisherKnockout = null!;

    public static FleckDef ShockwaveFast = null!;

    // 快递事件交互 Job 引用。
    public static JobDef Mugirl_ReadCourierDiary = null!;

    public static JobDef Mugirl_TalkCourier = null!;

    public static JobDef Mugirl_TalkFusionInvestor = null!;

    public static HediffDef Mugirl_Abasia = null!;

    public static PawnKindDef Mugirl_Beginning_Slave = null!;

    public static PawnKindDef Mugirl_EscapeSpaceSlave = null!;

    public static PawnKindDef Mugirl_EscapeWanderSlave = null!;

    public static PawnKindDef Mugirl_EscapeWildSlave = null!;

    public static PawnKindDef Mugirl_PreEscapeWildSlave = null!;

    public static PawnKindDef Mugirl_WildMugirl = null!;

    public static QuestScriptDef Mugirl_SlaveOpeningPodCrash = null!;

    static IncidentsDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(IncidentsDefOf));
    }
}
