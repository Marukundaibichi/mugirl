using HarmonyLib;

namespace Mugirl.Features.LifeStages;

// 新生成的雪牛娘按当前生命阶段校正身形。GeneratePawn 有两个重载，按参数类型指定 PawnGenerationRequest 版本。
[HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), [typeof(PawnGenerationRequest)])]
[MugirlPatch(PatchModule.LifeStages, PatchRisk.High, "Generated Mugirl newborns keep the base body visual state.")]
public static class Harmony_PawnGenerator_GeneratePawn_BodyType
{
    [HarmonyPriority(Priority.Normal - 1)]
    public static void Postfix(Pawn? __result)
    {
        if (__result is not null
            && MugirlIdentity.HasMugirlBody(__result)
            && __result.ageTracker?.CurLifeStage is not null
            && __result.story is not null)
        {
            LifeStageBodyTypeUtility.NormalizeBodyType(__result, queueRenderRefresh: true);
        }
    }
}
