using UnityEngine;
using Verse;

namespace MooGirl
{
    public static class Patch_AlienPawnRenderNode_Swaddle_GraphicFor
    {
        internal static bool Prefix(Pawn pawn, ref Graphic __result)
        {
            if (pawn?.def == MooGirl_DefOf.MooGirl)
            {
                const string path = "MooGirl/Bodies/SwaddledBaby/Swaddled_Child";
                __result = GraphicDatabase.Get<Graphic_Multi>(path, ShaderDatabase.Cutout, Vector2.one, Color.white);
                return false;
            }

            return true;
        }
    }
}
