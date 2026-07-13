using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Mugirl.Features.WeaponWheel
{
    public sealed class PawnRenderNode_BackWeapon : PawnRenderNode
    {
        private readonly Comp_WeaponWheel wheelComp;
        private readonly int displayIndex;

        internal ThingWithComps Weapon => wheelComp?.GetBackWeapon(displayIndex);
        internal int DisplayIndex => displayIndex;

        public PawnRenderNode_BackWeapon(
            Pawn pawn,
            PawnRenderNodeProperties props,
            PawnRenderTree tree,
            Comp_WeaponWheel wheelComp,
            int displayIndex)
            : base(pawn, props, tree)
        {
            this.wheelComp = wheelComp;
            this.displayIndex = displayIndex;
            wheelComp?.RegisterBackWeaponNode(this);
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            return Weapon?.Graphic;
        }

        protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
        {
            Graphic graphic = GraphicFor(pawn);
            if (graphic != null)
            {
                yield return graphic;
            }
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            return MeshPool.GetMeshSetForSize(1f, 1f);
        }
    }

    public sealed class PawnRenderNodeWorker_BackWeapon : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_BackWeapon backNode = node as PawnRenderNode_BackWeapon;
            return backNode?.Weapon != null
                && !parms.flags.FlagSet(PawnRenderFlags.Portrait)
                && !parms.pawn.Dead
                && base.CanDrawNow(node, parms);
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 offset = base.OffsetFor(node, parms, out pivot);
            PawnRenderNode_BackWeapon backNode = (PawnRenderNode_BackWeapon)node;
            float side = backNode.DisplayIndex == 0 ? -0.11f : 0.11f;

            if (parms.facing == Rot4.East)
            {
                offset.x += side * 0.55f;
                offset.z += 0.03f;
            }
            else if (parms.facing == Rot4.West)
            {
                offset.x -= side * 0.55f;
                offset.z += 0.03f;
            }
            else
            {
                if (parms.facing == Rot4.North)
                {
                    side = -side;
                }
                offset.x += side;
                offset.z -= 0.04f;
            }
            return offset;
        }

        public override float LayerFor(PawnRenderNode node, PawnDrawParms parms)
        {
            if (parms.facing == Rot4.South)
            {
                return -2f;
            }
            if (parms.facing == Rot4.North)
            {
                return 22f;
            }
            return ((PawnRenderNode_BackWeapon)node).DisplayIndex == 0 ? 18f : -1f;
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_BackWeapon backNode = (PawnRenderNode_BackWeapon)node;
            float angle = backNode.DisplayIndex == 0 ? 43f : -43f;
            if (parms.facing == Rot4.East)
            {
                angle += 8f;
            }
            else if (parms.facing == Rot4.West)
            {
                angle -= 8f;
            }
            return Quaternion.AngleAxis(angle, Vector3.up);
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            ThingWithComps weapon = ((PawnRenderNode_BackWeapon)node).Weapon;
            Vector2 drawSize = weapon?.Graphic?.drawSize ?? Vector2.one;
            return new Vector3(drawSize.x, 1f, drawSize.y);
        }
    }
}
