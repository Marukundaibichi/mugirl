namespace Mugirl.Features.Restraints;

// 高级束具（电击项圈、磁力镣铐、洗脑头盔）comp 的公共基类：未破解时自动触发效果，破解后改为带冷却的手动按钮。
// 公共状态与冷却命令由具体组件复用；配置类决定实际组件类型。
public abstract class CompAdvancedRestraint : ThingComp
{
    public Pawn? Wearer => parent.ParentHolder is Pawn_ApparelTracker tracker ? tracker.pawn : null;

    protected bool IsParentCracked => parent is AdvancedRestraintApparel advanced && advanced.IsCracked();

    // 不在游戏中（如预览）时返回 fallback，冷却按“刚使用过”处理。
    protected static int CurrentGameTickOrFallback(int fallback)
    {
        return TickUtility.CurrentGameTickOrFallback(fallback);
    }

    // 带冷却进度的手动按钮；进度在绘制时由 cooldownPercent 按当前 tick 重新计算。
    protected static Command_ActionWithCooldown MakeCooldownCommand(
        string label,
        string desc,
        Texture2D icon,
        bool disabled,
        Func<float> cooldownPercent,
        Action action)
    {
        return new Command_ActionWithCooldown
        {
            defaultLabel = label,
            defaultDesc = desc,
            icon = icon,
            action = action,
            Disabled = disabled,
            cooldownPercentGetter = cooldownPercent,
        };
    }
}
