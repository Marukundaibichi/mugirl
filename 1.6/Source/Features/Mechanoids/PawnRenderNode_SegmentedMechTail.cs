using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public sealed class PawnRenderNode_SegmentedMechTail : PawnRenderNode
    {
        internal readonly Comp_SegmentedMechTail TailComp;
        internal readonly SegmentedMechTailMeshPart MeshPart;

        internal PawnRenderNode_SegmentedMechTail(
            Pawn pawn,
            PawnRenderNodeProperties props,
            PawnRenderTree tree,
            Comp_SegmentedMechTail tailComp,
            SegmentedMechTailMeshPart meshPart)
            : base(pawn, props, tree)
        {
            TailComp = tailComp;
            MeshPart = meshPart;
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            return TailComp?.GraphicFor(MeshPart, false);
        }

        protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
        {
            Graphic idle = TailComp?.GraphicFor(MeshPart, false);
            if (idle != null)
            {
                yield return idle;
            }
            if (MeshPart != SegmentedMechTailMeshPart.Tip)
            {
                yield break;
            }

            Graphic active = TailComp?.GraphicFor(MeshPart, true);
            if (active != null && active != idle)
            {
                yield return active;
            }
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            return MeshPool.GetMeshSetForSize(1f, 1f);
        }

        public override Mesh GetMesh(PawnDrawParms parms)
        {
            return TailComp?.MeshFor(MeshPart);
        }
    }

    public sealed class PawnRenderNodeWorker_SegmentedMechTail : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_SegmentedMechTail tailNode =
                node as PawnRenderNode_SegmentedMechTail;
            return tailNode?.TailComp != null
                && !parms.Portrait
                && !parms.flags.FlagSet(PawnRenderFlags.NoBody)
                && base.CanDrawNow(node, parms);
        }

        protected override Graphic GetGraphic(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_SegmentedMechTail tailNode =
                (PawnRenderNode_SegmentedMechTail)node;
            bool active = tailNode.MeshPart == SegmentedMechTailMeshPart.Tip
                && tailNode.TailComp.ShouldUseActiveTip(parms);
            return tailNode.TailComp.GraphicFor(tailNode.MeshPart, active);
        }

        public override void PreDraw(
            PawnRenderNode node,
            Material mat,
            PawnDrawParms parms)
        {
            ((PawnRenderNode_SegmentedMechTail)node).TailComp.PrepareMeshes(parms);
            base.PreDraw(node, mat, parms);
        }

        public override Vector3 OffsetFor(
            PawnRenderNode node,
            PawnDrawParms parms,
            out Vector3 pivot)
        {
            pivot = Vector3.zero;
            return Vector3.zero;
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            return Quaternion.identity;
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            return Vector3.one;
        }

        public override float LayerFor(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_SegmentedMechTail tailNode =
                (PawnRenderNode_SegmentedMechTail)node;
            return tailNode.MeshPart == SegmentedMechTailMeshPart.Segments
                ? tailNode.TailComp.Props.segmentLayer
                : tailNode.TailComp.Props.tipLayer;
        }
    }
}
