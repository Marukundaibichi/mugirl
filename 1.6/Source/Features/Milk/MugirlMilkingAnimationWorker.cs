using UnityEngine;
using Verse;

namespace Mugirl
{
    // 只会被正在播放 Mugirl 挤奶/喂奶 AnimationDef 的 Pawn 调用；避免在所有 Pawn、所有节点上安装全局 Harmony Postfix。
    public class MugirlMilkingAnimationWorker : BaseAnimationWorker
    {
        public override bool Enabled(AnimationDef def, PawnRenderNode node, AnimationPart part, PawnDrawParms parms)
        {
            return !parms.flags.FlagSet(PawnRenderFlags.Portrait) && MugirlMilkingAnimation.HasActiveAnimation(parms.pawn);
        }

        public override void PostDraw(AnimationDef def, PawnRenderNode node, AnimationPart part, PawnDrawParms parms, Matrix4x4 matrix)
        {
        }

        public override Vector3 OffsetAtTick(int tick, AnimationDef def, PawnRenderNode node, AnimationPart part, PawnDrawParms parms)
        {
            return MugirlMilkingAnimation.TryGetAnimationTransform(node?.Props?.tagDef, parms, out Vector3 offset, out _, out _)
                ? offset
                : Vector3.zero;
        }

        public override float AngleAtTick(int tick, AnimationDef def, PawnRenderNode node, AnimationPart part, PawnDrawParms parms)
        {
            return MugirlMilkingAnimation.TryGetAnimationTransform(node?.Props?.tagDef, parms, out _, out float angle, out _)
                ? angle
                : 0f;
        }

        public override Vector3 ScaleAtTick(int tick, AnimationDef def, PawnRenderNode node, AnimationPart part, PawnDrawParms parms)
        {
            return MugirlMilkingAnimation.TryGetAnimationTransform(node?.Props?.tagDef, parms, out _, out _, out Vector3 scale)
                ? scale
                : Vector3.one;
        }

        public override GraphicStateDef GraphicStateAtTick(int tick, AnimationDef def, PawnRenderNode node, AnimationPart part, PawnDrawParms parms)
        {
            return null;
        }
    }
}
