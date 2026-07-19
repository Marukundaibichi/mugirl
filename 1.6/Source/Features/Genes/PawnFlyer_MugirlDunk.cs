using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    internal static class MugirlPawnSpinRenderer
    {
        internal static void Draw(Pawn pawn, DrawPhase phase, Vector3 drawPos, float clockwiseAngle)
        {
            if (pawn == null)
            {
                return;
            }

            PawnRenderer renderer = pawn.Drawer.renderer;
            if (phase == DrawPhase.EnsureInitialized)
            {
                renderer.EnsureGraphicsInitialized();
                return;
            }

            PawnDrawParms parms = PawnDrawParms.DefaultFor(pawn);
            parms.facing = Rot4.South;
            parms.posture = PawnPosture.Standing;
            parms.flags |= PawnRenderFlags.NeverAimWeapon;
            parms.rotDrawMode = renderer.CurRotDrawMode;
            parms.tint = renderer.flasher.CurColor.ToTransparent(InvisibilityUtility.GetAlpha(pawn));
            parms.carriedThing = pawn.carryTracker?.CarriedThing;
            parms.matrix = Matrix4x4.TRS(
                drawPos + pawn.ageTracker.CurLifeStage.bodyDrawOffset,
                Quaternion.AngleAxis(clockwiseAngle, Vector3.up),
                Vector3.one);

            if (phase == DrawPhase.ParallelPreDraw)
            {
                renderer.renderTree.ParallelPreDraw(parms);
            }
            else if (phase == DrawPhase.Draw)
            {
                renderer.renderTree.Draw(parms);
            }
        }
    }

    public sealed class PawnFlyerWorker_MugirlDunk : PawnFlyerWorker
    {
        private const float WindupFraction = 0.36f;

        public PawnFlyerWorker_MugirlDunk(PawnFlyerProperties properties) : base(properties)
        {
        }

        public override float AdjustedProgress(float t)
        {
            return t <= WindupFraction ? 0f : Mathf.InverseLerp(WindupFraction, 1f, t);
        }
    }

}
