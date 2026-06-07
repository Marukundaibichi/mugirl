using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace MooGirl
{
    internal static class MooGirlPatchRegistry
    {
        private static readonly List<string> manualPatchNames = new List<string>();

        internal static IReadOnlyList<string> ManualPatchNames => manualPatchNames;

        internal static void RegisterManualPatches(Harmony harmony)
        {
            if (harmony == null)
            {
                MooGirlLog.WarningOnce("PatchRegistry.NullHarmony", "MooGirl.PatchRegistry.NullHarmony".Translate().ToString());
                return;
            }

            manualPatchNames.Clear();
            // 特性标注的 patch 由 MooGirlBootstrap 逐类处理；这里仅管理手动反射 patch，
            // 让可选兼容逻辑拥有一个可审计的边界。
            PatchPawnGeneratorGeneratePawn(harmony);
            PatchAlienRaceSwaddleGraphicFor(harmony);
        }

        private static void PatchPawnGeneratorGeneratePawn(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) });
            MethodInfo postfix = AccessTools.Method(typeof(PawnGenerator_GeneratePawn_Patch), nameof(PawnGenerator_GeneratePawn_Patch.Postfix));
            TryPatch(harmony, "MooGirl.PatchRegistry.PawnGeneratorGeneratePawn", target, postfix: postfix);
        }

        private static void PatchAlienRaceSwaddleGraphicFor(Harmony harmony)
        {
            // HAR 是运行时可选依赖。这里只通过反射访问，保证 AlienRace
            // 缺失或变更时本 mod 仍能干净加载。
            Type swaddleType = AccessTools.TypeByName("AlienRace.AlienPawnRenderNode_Swaddle");
            if (swaddleType == null)
            {
                return;
            }

            MethodInfo target = AccessTools.Method(swaddleType, "GraphicFor", new[] { typeof(Pawn) });
            MethodInfo prefix = AccessTools.Method(typeof(Patch_AlienPawnRenderNode_Swaddle_GraphicFor), nameof(Patch_AlienPawnRenderNode_Swaddle_GraphicFor.Prefix));
            TryPatch(harmony, "MooGirl.PatchRegistry.AlienRaceSwaddleGraphicFor", target, prefix: prefix);
        }

        private static void TryPatch(Harmony harmony, string nameKey, MethodBase target, MethodInfo prefix = null, MethodInfo postfix = null)
        {
            if (target == null)
            {
                MooGirlLog.WarningOnce(
                    "PatchRegistry.MissingTarget." + nameKey,
                    "MooGirl.PatchRegistry.MissingTarget".Translate(nameKey.Translate()).ToString());
                return;
            }

            if (prefix == null && postfix == null)
            {
                MooGirlLog.WarningOnce(
                    "PatchRegistry.MissingPatchMethod." + nameKey,
                    "MooGirl.PatchRegistry.MissingPatchMethod".Translate(nameKey.Translate()).ToString());
                return;
            }

            try
            {
                harmony.Patch(
                    target,
                    prefix: prefix == null ? null : new HarmonyMethod(prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(postfix));
                manualPatchNames.Add(nameKey);
            }
            catch (Exception ex)
            {
                string detail = ex.GetType().Name + ": " + ex.Message;
                MooGirlLog.WarningOnce(
                    "PatchRegistry.PatchFailed." + nameKey,
                    "MooGirl.PatchRegistry.PatchFailed".Translate(nameKey.Translate(), detail).ToString());
            }
        }
    }
}
