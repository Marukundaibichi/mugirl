using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace Mugirl
{
    internal static class MugirlPatchRegistry
    {
        // StaticCacheLifecycle: 进程级手动 patch 审计数据；只在启动注册时刷新。
        private static readonly List<string> manualPatchNames = new List<string>();

        internal static IReadOnlyList<string> ManualPatchNames => manualPatchNames;

        internal static void RegisterManualPatches(Harmony harmony)
        {
            if (harmony == null)
            {
                MugirlLog.WarningOnce("PatchRegistry.NullHarmony", "Mugirl.PatchRegistry.NullHarmony".Translate().ToString());
                return;
            }

            manualPatchNames.Clear();
            // 特性标注的 patch 由 MugirlBootstrap 逐类处理；这里仅管理手动反射 patch，
            // 让可选兼容逻辑拥有一个可审计的边界。
            PatchPawnGeneratorGeneratePawn(harmony);
        }

        private static void PatchPawnGeneratorGeneratePawn(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) });
            MethodInfo postfix = AccessTools.Method(typeof(PawnGenerator_GeneratePawn_Patch), nameof(PawnGenerator_GeneratePawn_Patch.Postfix));
            TryPatch(harmony, "Mugirl.PatchRegistry.PawnGeneratorGeneratePawn", target, postfix: postfix);
        }

        private static void TryPatch(Harmony harmony, string nameKey, MethodBase target, MethodInfo prefix = null, MethodInfo postfix = null)
        {
            if (target == null)
            {
                MugirlLog.WarningOnce(
                    "PatchRegistry.MissingTarget." + nameKey,
                    "Mugirl.PatchRegistry.MissingTarget".Translate(nameKey.Translate()).ToString());
                return;
            }

            if (prefix == null && postfix == null)
            {
                MugirlLog.WarningOnce(
                    "PatchRegistry.MissingPatchMethod." + nameKey,
                    "Mugirl.PatchRegistry.MissingPatchMethod".Translate(nameKey.Translate()).ToString());
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
                MugirlLog.WarningOnce(
                    "PatchRegistry.PatchFailed." + nameKey,
                    "Mugirl.PatchRegistry.PatchFailed".Translate(nameKey.Translate(), detail).ToString());
            }
        }
    }
}
