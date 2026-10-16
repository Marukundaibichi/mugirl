namespace Mugirl.Features.Milk;

// Mugirl 挤奶/喂奶 AnimationDef 的 workerType（XML 引用，类名不可改）。只会被正在播放这些动画的 Pawn 调用，
// 避免在所有 Pawn、所有节点上安装全局 Harmony Postfix。1.7 中在并行预绘制线程上调用，只读动画状态。
public sealed class AnimationWorker_Milking : BaseAnimationWorker
{
    public override bool Enabled(AnimationDef def, PawnRenderNode node, AnimationPart part, PawnDrawParms parms)
    {
        return !parms.flags.FlagSet(PawnRenderFlags.Portrait) && MilkingAnimation.HasActiveAnimation(parms.pawn);
    }

    public override void PostDraw(AnimationDef def, PawnRenderNode node, AnimationPart part, PawnDrawParms parms, UMatrix4x4 matrix)
    {
    }

    public override UVector3 OffsetAtTick(int tick, AnimationDef def, PawnRenderNode node, AnimationPart part, PawnDrawParms parms)
    {
        MilkingAnimation.TryGetAnimationTransform(node?.Props?.tagDef, parms, out UVector3 offset, out _, out _);
        return offset;
    }

    public override float AngleAtTick(int tick, AnimationDef def, PawnRenderNode node, AnimationPart part, PawnDrawParms parms)
    {
        MilkingAnimation.TryGetAnimationTransform(node?.Props?.tagDef, parms, out _, out float angle, out _);
        return angle;
    }

    public override UVector3 ScaleAtTick(int tick, AnimationDef def, PawnRenderNode node, AnimationPart part, PawnDrawParms parms)
    {
        MilkingAnimation.TryGetAnimationTransform(node?.Props?.tagDef, parms, out _, out _, out UVector3 scale);
        return scale;
    }

    public override GraphicStateDef? GraphicStateAtTick(int tick, AnimationDef def, PawnRenderNode node, AnimationPart part, PawnDrawParms parms)
    {
        return null;
    }
}
