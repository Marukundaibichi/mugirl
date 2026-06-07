using HarmonyLib;
using System;
using System.Reflection;
using Verse;

namespace MooGirl
{
    internal static class AlienRaceCompatibility
    {
        private const string SwaddleRenderNodeTypeName = "AlienRace.AlienPawnRenderNode_Swaddle";

        internal static bool TryGetSwaddleGraphicForTarget(out MethodInfo target)
        {
            target = null;
            Type swaddleType = AccessTools.TypeByName(SwaddleRenderNodeTypeName);
            if (swaddleType == null)
            {
                return false;
            }

            target = AccessTools.Method(swaddleType, "GraphicFor", new[] { typeof(Pawn) });
            return target != null;
        }
    }
}
