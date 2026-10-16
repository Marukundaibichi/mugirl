using HarmonyLib;

namespace Mugirl.Features.Appearance;

[HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.Draw))]
[MugirlPatch(PatchModule.Appearance, PatchRisk.Medium, "Optional Mugirl body/apparel outlines are absent; original drawing is preserved.")]
public static class Harmony_PawnRenderTree_Draw_ExtraOutline
{
    // 使用原版与 HAR 已准备好的最终绘制请求与矩阵：不额外遍历渲染树、不重算动画、不修改请求。
    // parms 按引用传入，避免每次人物绘制复制整个 PawnDrawParms；本补丁不修改它。
    public static void Prefix(PawnRenderTree __instance, ref PawnDrawParms parms, List<PawnGraphicDrawRequest> ___drawRequests)
    {
        ExtraOutlineRenderer.DrawBody(__instance.pawn, ___drawRequests, in parms);
    }
}
