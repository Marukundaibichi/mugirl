using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl.Features.Appearance
{
    /// <summary>
    /// 发型后发层条目：目标发型与各朝向后发贴图路径，可在 XML 中为每个带后发的发型配置一条。
    /// 未配置 hairDef/southPath 时回退到 Mugirl_LongCascade 的默认后发（XNN_Hairs8F）。
    /// </summary>
    public sealed class CompProperties_LongCascadeBackHair : CompProperties
    {
        public CompProperties_LongCascadeBackHair()
        {
            compClass = typeof(Comp_LongCascadeBackHair);
        }

        public HairDef hairDef;

        public string southPath;

        public string sidePath;

        public override void ResolveReferences(ThingDef parentDef)
        {
            base.ResolveReferences(parentDef);
            bool isDefaultEntry = southPath.NullOrEmpty();
            if (hairDef == null)
            {
                hairDef = Mugirl_DefOf.Mugirl_LongCascade;
            }
            if (southPath.NullOrEmpty())
            {
                southPath = "Mugirl/Hair/Mugirl_LongCascadeBack_south";
            }
            if (sidePath.NullOrEmpty())
            {
                // 默认条目沿用 LongCascade 的东西向后发；显式留空 sidePath 表示该发型没有侧向后发（仅南向绘制）。
                sidePath = isDefaultEntry ? "Mugirl/Hair/Mugirl_LongCascadeBack_east" : null;
            }
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef))
            {
                yield return error;
            }
            if (hairDef == null)
            {
                yield return $"{parentDef.defName}: CompProperties_LongCascadeBackHair 条目的 hairDef 未解析（DefOf 缺失或被移除），后发层不会绘制。";
            }
        }
    }

    /// <summary>
    /// 携带后发路径与目标发型的渲染节点属性；由 <see cref="Comp_LongCascadeBackHair"/> 在构建节点时填充。
    /// </summary>
    public sealed class PawnRenderNodeProperties_LongCascadeBackHair : PawnRenderNodeProperties
    {
        public HairDef hairDef;

        public string southPath;

        public string sidePath;
    }

    /// <summary>
    /// 发型后发层：跟随 Head 变换，但绘制在头部之后（baseLayer -1），使用头发颜色。
    /// 例如 XNN_Hairs8F（LongCascade）与 XNN_Hairs10F（Hairs10，仅南向）。
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

            PawnRenderNodeProperties nodeProps = CreateNodeProperties((CompProperties_LongCascadeBackHair)props);
            return new List<PawnRenderNode>(1)
            {
                new PawnRenderNode_LongCascadeBackHair(pawn, nodeProps, tree)
            };
        }

        internal static PawnRenderNodeProperties CreateNodeProperties(CompProperties_LongCascadeBackHair compProps)
        {
            return new PawnRenderNodeProperties_LongCascadeBackHair
            {
                debugLabel = "Mugirl back hair (" + (compProps?.hairDef?.defName ?? "LongCascade") + ")",
                nodeClass = typeof(PawnRenderNode_LongCascadeBackHair),
                workerClass = typeof(PawnRenderNodeWorker_LongCascadeBackHair),
                pawnType = PawnRenderNodeProperties.RenderNodePawnType.HumanlikeOnly,
                parentTagDef = PawnRenderNodeTagDefOf.Head,
                useGraphic = true,
                baseLayer = -1f,
                drawSize = Vector2.one,
                colorType = PawnRenderNodeProperties.AttachmentColorType.Hair,
                skipFlag = RenderSkipFlagDefOf.Hair,
                visibleFacing = compProps?.sidePath != null
                    ? new List<Rot4> { Rot4.South, Rot4.East, Rot4.West }
                    : new List<Rot4> { Rot4.South },
                hairDef = compProps?.hairDef,
                southPath = compProps?.southPath,
                sidePath = compProps?.sidePath
            };
        }
    }

    public sealed class PawnRenderNode_LongCascadeBackHair : PawnRenderNode
    {
        private PawnRenderNodeProperties_LongCascadeBackHair BackHairProps => (PawnRenderNodeProperties_LongCascadeBackHair)props;

        internal HairDef BackHairDef => BackHairProps.hairDef;

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
            if (BackHairProps.sidePath != null)
            {
                yield return GetOrCreateSideGraphic(pawn);
            }
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            // 头发网格的西向已经镜像；额外 FlipGraphic 会再次翻转，让前后发朝向相反。
            return HumanlikeMeshPoolUtility.GetHumanlikeHairSetForPawn(pawn);
        }

        internal Graphic GraphicForFacing(Pawn pawn, Rot4 facing)
        {
            return facing == Rot4.South
                ? GetOrCreateSouthGraphic(pawn)
                : GetOrCreateSideGraphic(pawn);
        }

        private Graphic GetOrCreateSouthGraphic(Pawn pawn)
        {
            return southGraphic ?? (southGraphic = CreateGraphic(pawn, BackHairProps.southPath));
        }

        private Graphic GetOrCreateSideGraphic(Pawn pawn)
        {
            return sideGraphic ?? (sideGraphic = CreateGraphic(pawn, BackHairProps.sidePath));
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
                && parms.pawn?.story?.hairDef == ((PawnRenderNode_LongCascadeBackHair)node)?.BackHairDef
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
