using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mugirl.Features.WeaponWheel;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    internal static class YayoAnimationCompatibility
    {
        private delegate Vector3 GetBodyPositionDelegate(
            PawnRenderer renderer,
            Vector3 drawLoc,
            PawnPosture posture,
            out bool showBody);

        private const string PackageId = "com.yayo.yayoAni.continued";

        internal static bool Active => ModLister.GetActiveModWithIdentifier(PackageId) != null;

        internal static Vector3 DrawPositionFor(Pawn pawn)
        {
            if (pawn?.Drawer?.renderer == null || ReflectionCache.GetBodyPosition == null
                || ReflectionCache.InvocationFailed)
            {
                return pawn?.DrawPos ?? Vector3.zero;
            }

            try
            {
                return ReflectionCache.GetBodyPosition(pawn.Drawer.renderer, pawn.DrawPos, pawn.GetPosture(), out _);
            }
            catch
            {
                ReflectionCache.InvocationFailed = true;
                return pawn.DrawPos;
            }
        }

        private static class ReflectionCache
        {
            // StaticCacheLifecycle: 仅在 Yayo Animation 已启用且轮盘发生自定义绘制时初始化。
            internal static readonly GetBodyPositionDelegate GetBodyPosition = CreateBodyPositionDelegate();
            internal static bool InvocationFailed;

            private static GetBodyPositionDelegate CreateBodyPositionDelegate()
            {
                try
                {
                    MethodInfo method = AccessTools.Method(
                        typeof(PawnRenderer),
                        "GetBodyPos",
                        new[] { typeof(Vector3), typeof(PawnPosture), typeof(bool).MakeByRefType() });
                    return method == null
                        ? null
                        : (GetBodyPositionDelegate)method.CreateDelegate(typeof(GetBodyPositionDelegate));
                }
                catch
                {
                    return null;
                }
            }
        }
    }

    [HarmonyPatch(typeof(WeaponWheelAnimationRenderer), nameof(WeaponWheelAnimationRenderer.Draw))]
    internal static class Harmony_YayoAnimation_WeaponWheelDrawPosition
    {
        public static bool Prepare()
        {
            return YayoAnimationCompatibility.Active;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo pawnDrawPosGetter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.DrawPos));
            MethodInfo replacement = AccessTools.Method(
                typeof(YayoAnimationCompatibility),
                nameof(YayoAnimationCompatibility.DrawPositionFor));

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(pawnDrawPosGetter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }
                yield return instruction;
            }
        }
    }
}
