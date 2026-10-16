namespace Mugirl.Features.Milk;

// Pawn 在挤奶或喂奶动画中的角色，决定使用的 AnimationDef 与姿态计算。
public enum MilkingVisualRole
{
    SelfMilking,
    AssistedTarget,
    Helper,
    FeedingSource,
    FeedingRecipient,
}
