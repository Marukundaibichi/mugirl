namespace Mugirl.Features.Social;

/// <summary>只对雪牛娘身体有效的记忆（XML thoughtClass，按类名存档）。</summary>
public class Thought_MugirlOnly : Thought_Memory
{
    // 身体不再是雪牛娘时丢弃。交给 MemoryThoughtHandler.RemoveExpiredMemories 逆序移除；1.6 在正序遍历的
    // ThoughtInterval 中移除自身，会让列表中的下一条记忆跳过本轮计时。使用本类的 ThoughtDef 都没有 nextThought。
    public override bool ShouldDiscard => !MugirlIdentity.HasMugirlBody(pawn) || base.ShouldDiscard;
}
