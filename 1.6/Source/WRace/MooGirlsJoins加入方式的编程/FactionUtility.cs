using RimWorld;
using System.Linq;
using Verse;

namespace MooGirl
{
    // 静态构造函数：在程序启动时自动执行，用于初始化静态成员
    [StaticConstructorOnStartup]
    public static class MooGirl_FactionUtility
    {
        // 获取一个符合条件的非敌对类人型派系（可能返回null）
        // 条件：非玩家、类人型、非隐藏、未击败且不与玩家敌对
        public static Faction GetNonHostileHumanlikeFaction()
        {
            return Find.FactionManager.AllFactionsListForReading
                .Where(f =>
                    !f.IsPlayer &&          // 排除玩家派系
                    f.def.humanlikeFaction && // 必须是类人型派系
                    !f.HostileTo(Faction.OfPlayer) && // 不与玩家敌对
                    !f.def.hidden &&        // 排除隐藏派系
                    !f.defeated)           // 排除已击败派系
                .RandomElementWithFallback(null); // 随机返回一个符合条件的派系，若无则返回null
        }

        // 获取最佳可用的非敌对派系（优先返回友好派系，若无则返回第一个可见的非玩家派系）
        // 条件：非玩家、类人型、非隐藏、未击败，并按玩家好感度排序，最后尝试获取非敌对派系
        public static Faction GetBestAvailableNonHostileFaction()
        {
            return Find.FactionManager.AllFactionsListForReading
                .Where(f =>
                    !f.IsPlayer &&          // 排除玩家派系
                    f.def.humanlikeFaction && // 必须是类人型派系
                    !f.def.hidden &&        // 排除隐藏派系
                    !f.defeated)           // 排除已击败派系
                .OrderByDescending(f => f.PlayerGoodwill) // 按玩家好感度降序排序
                .FirstOrDefault(f => !f.HostileTo(Faction.OfPlayer)) // 返回第一个不与玩家敌对的派系
                ?? Find.FactionManager.AllFactionsVisible.FirstOrDefault(f => !f.IsPlayer); // 若无则返回第一个可见的非玩家派系
        }
    }
}