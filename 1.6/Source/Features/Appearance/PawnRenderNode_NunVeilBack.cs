using UnityEngine;
using Verse;

namespace Mugirl.Features.Appearance
{
    // 修女头纱的南向白色垂纱位于头部后方；前方帽边仍由标准服装节点绘制。
    public sealed class PawnRenderNode_NunVeilBack : PawnRenderNode
    {
        public PawnRenderNode_NunVeilBack(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            if (Props.texPath.NullOrEmpty())
            {
                return null;
            }

            return GraphicDatabase.Get<Graphic_Single>(
                Props.texPath,
                ShaderFor(pawn),
                Vector2.one,
                ColorFor(pawn));
        }

        public override GraphicMeshSet MeshSetFor(Pawn pawn)
        {
            return HumanlikeMeshPoolUtility.GetHumanlikeHeadSetForPawn(pawn);
        }
    }
}
