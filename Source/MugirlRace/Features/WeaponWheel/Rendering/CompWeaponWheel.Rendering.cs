using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.WeaponWheel;

// 渲染接口：登记背负/凹槽渲染节点，并向 WeaponWheelAnimationRenderer 提供抬枪、换枪与瞄准过渡的只读快照。
public sealed partial class CompWeaponWheel
{
    public override List<PawnRenderNode>? CompRenderNodes()
    {
        return renderCache.CompRenderNodes();
    }

    // 主线程绘制阶段调用：处于抬枪/换枪动画时给出进出武器、进度与卡入时刻。
    internal bool TryGetAnimationSnapshot(out WeaponWheelAnimationSnapshot snapshot)
    {
        return combat.TryGetAnimationSnapshot(out snapshot);
    }

    // 动画结束后的一 tick 内由轮盘继续绘制手持武器，衔接原版瞄准绘制。
    internal bool TryGetAimHandoff([NotNullWhen(true)] out ThingWithComps? weapon, out float aimAngle)
    {
        return combat.TryGetAimHandoff(out weapon, out aimAngle);
    }
}
