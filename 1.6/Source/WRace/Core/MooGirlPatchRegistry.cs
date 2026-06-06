using HarmonyLib;
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
                MooGirlLog.WarningOnce("PatchRegistry.NullHarmony", "Manual patches skipped because the Harmony instance is missing.");
                return;
            }

            manualPatchNames.Clear();
            // 特性标注的 patch 仍由 PatchAll 处理；这里仅管理手动反射 patch，
            // 让可选兼容逻辑拥有一个可审计的边界。
            PatchPawnGeneratorGeneratePawn(harmony);
            PatchAlienRaceSwaddleGraphicFor(harmony);
        }

        private static void PatchPawnGeneratorGeneratePawn(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) });
            MethodInfo postfix = AccessTools.Method(typeof(PawnGenerator_GeneratePawn_Patch), nameof(PawnGenerator_GeneratePawn_Patch.Postfix));
            TryPatch(harmony, "PawnGenerator.GeneratePawn slave apparel lock postfix", target, postfix: postfix);
        }

        private static void PatchAlienRaceSwaddleGraphicFor(Harmony harmony)
        {
            // HAR 是运行时可选依赖。这里只通过反射访问，保证 AlienRace
            // 缺失或变更时本 mod 仍能干净加载。
            System.Type swaddleType = AccessTools.TypeByName("AlienRace.AlienPawnRenderNode_Swaddle");
            if (swaddleType == null)
            {
                return;
            }

            MethodInfo target = AccessTools.Method(swaddleType, "GraphicFor", new[] { typeof(Pawn) });
            MethodInfo prefix = AccessTools.Method(typeof(Patch_AlienPawnRenderNode_Swaddle_GraphicFor), nameof(Patch_AlienPawnRenderNode_Swaddle_GraphicFor.Prefix));
            TryPatch(harmony, "AlienRace swaddle GraphicFor prefix", target, prefix: prefix);
        }

        private static void TryPatch(Harmony harmony, string name, MethodBase target, MethodInfo prefix = null, MethodInfo postfix = null)
        {
            if (target == null)
            {
                MooGirlLog.WarningOnce("PatchRegistry.MissingTarget." + name, "Manual patch target missing: " + name);
                return;
            }

            if (prefix == null && postfix == null)
            {
                MooGirlLog.WarningOnce("PatchRegistry.MissingPatchMethod." + name, "Manual patch method missing: " + name);
                return;
            }

            harmony.Patch(
                target,
                prefix: prefix == null ? null : new HarmonyMethod(prefix),
                postfix: postfix == null ? null : new HarmonyMethod(postfix));
            manualPatchNames.Add(name);
        }
    }
}
