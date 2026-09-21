using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl.Features.Appearance
{
    public sealed class CompProperties_LongCascadeBackHair : CompProperties
    {
        public CompProperties_LongCascadeBackHair()
        {
            compClass = typeof(Comp_LongCascadeBackHair);
        }
    }

    /// <summary>
    /// XNN_Hairs8F 是 LongCascade 的后发层。它跟随 Head 变换，但绘制在头部之后；
    /// 北向原图已经包含完整后发，因此这里只补南、东、西三个朝向。
    /// </summary>
    public sealed class Comp_LongCascadeBackHair : ThingComp
    {
        public override List<PawnRenderNode> CompRenderNodes()
        {
            Pawn pawn = parent as Pawn;
            PawnRenderTree tree = pawn?.Drawer?.renderer?.renderTree;
            if (tree == null)
            {
                return null;
            }

            PawnRenderNodeProperties nodeProps = CreateNodeProperties();
            return new List<PawnRenderNode>(1)
            {
                new PawnRenderNode_LongCascadeBackHair(pawn, nodeProps, tree)
            };
        }

        internal static PawnRenderNodeProperties CreateNodeProperties()
        {
            return new PawnRenderNodeProperties
            {
                debugLabel = "Mugirl long cascade back hair",
                nodeClass = typeof(PawnRenderNode_LongCascadeBackHair),
                workerClass = typeof(PawnRenderNodeWorker_LongCascadeBackHair),
                pawnType = PawnRenderNodeProperties.RenderNodePawnType.HumanlikeOnly,
                parentTagDef = PawnRenderNodeTagDefOf.Head,
                useGraphic = true,
                baseLayer = -1f,
                drawSize = Vector2.one,
                colorType = PawnRenderNodeProperties.AttachmentColorType.Hair,
                skipFlag = RenderSkipFlagDefOf.Hair,
                visibleFacing = new List<Rot4> { Rot4.South, Rot4.East, Rot4.West }
            };
        }
    }

    public sealed class PawnRenderNode_LongCascadeBackHair : PawnRenderNode
    {
        private const string SouthPath = "Mugirl/Hair/Mugirl_LongCascadeBack_south";
        private const string SidePath = "Mugirl/Hair/Mugirl_LongCascadeBack_east";

        private Graphic southGraphic;
        private Graphic sideGraphic;

        public PawnRenderNode_LongCascadeBackHair(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            return GetOrCreateSouthGraphic(pawn);
        }

        protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
        {
            yield return GetOrCreateSouthGraphic(pawn);
            yield return GetOrCreateSideGraphic(pawn);
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            return HumanlikeMeshPoolUtility.GetHumanlikeHairSetForPawn(pawn);
        }

        public override bool FlipGraphic(PawnDrawParms parms)
        {
            return base.FlipGraphic(parms) ^ (parms.facing == Rot4.West);
        }

        internal Graphic GraphicForFacing(Pawn pawn, Rot4 facing)
        {
            return facing == Rot4.South
                ? GetOrCreateSouthGraphic(pawn)
                : GetOrCreateSideGraphic(pawn);
        }

        private Graphic GetOrCreateSouthGraphic(Pawn pawn)
        {
            return southGraphic ?? (southGraphic = CreateGraphic(pawn, SouthPath));
        }

        private Graphic GetOrCreateSideGraphic(Pawn pawn)
        {
            return sideGraphic ?? (sideGraphic = CreateGraphic(pawn, SidePath));
        }

        private Graphic CreateGraphic(Pawn pawn, string path)
        {
            return GraphicDatabase.Get<Graphic_Single>(
                path,
                ShaderFor(pawn),
                Vector2.one,
                ColorFor(pawn));
        }
    }

    public sealed class PawnRenderNodeWorker_LongCascadeBackHair : PawnRenderNodeWorker_FlipWhenCrawling
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            return base.CanDrawNow(node, parms)
                && parms.pawn?.story?.hairDef == Mugirl_DefOf.Mugirl_LongCascade
                && !parms.pawn.DevelopmentalStage.Baby()
                && !parms.pawn.DevelopmentalStage.Newborn();
        }

        protected override Graphic GetGraphic(PawnRenderNode node, PawnDrawParms parms)
        {
            PawnRenderNode_LongCascadeBackHair backHair = node as PawnRenderNode_LongCascadeBackHair;
            return backHair?.GraphicForFacing(parms.pawn, parms.facing);
        }
    }
}
