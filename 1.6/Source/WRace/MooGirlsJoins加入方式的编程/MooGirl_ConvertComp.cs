using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    // 游戏组件：用于在游戏初始化时将特定类型的殖民者进行转换
    public class MooGirl_ConvertComp : GameComponent
    {
        // 标记转换是否已完成，避免重复执行
        private bool conversionDone = false;

        // 构造函数，初始化游戏组件
        public MooGirl_ConvertComp(Game game)
        {
        }

        // 游戏初始化最终阶段执行的方法
        public override void FinalizeInit()
        {
            base.FinalizeInit();

            ConvertEscapeWildSlaves();  // 执行转换逻辑
            conversionDone = true;     // 保留旧存档字段，转换本身每次读档都可修复漏网个体
        }

        // 静态方法：将所有EscapeWildSlave类型的殖民者转换为PreEscapeWildSlave类型
        public static int ConvertEscapeWildSlaves()
        {
            // 获取当前游戏里所有属于玩家阵营的存活pawn，兼容殖民者、婴儿和临时/世界pawn
            List<Pawn> playerPawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;

            // 记录成功转换的殖民者数量
            int convertedCount = 0;

            // 遍历所有玩家殖民者
            foreach (var pawn in playerPawns)
            {
                if (pawn.Faction != Faction.OfPlayer)
                {
                    continue;
                }

                if (MooGirlWildSlaveUtility.NormalizePlayerPawnIfNeeded(pawn))
                {
                    convertedCount++;
                }
            }

            // 根据转换结果输出汇总日志
            if (convertedCount > 0)
            {
                Log.Message($"[MooGirl] 本次共转换 {convertedCount} 个 MooGirl_EscapeWildSlave。");
            }
            else
            {
                Log.Message("[MooGirl] 未发现需要转换的 MooGirl_EscapeWildSlave。");
            }

            return convertedCount;
        }

        // 数据序列化方法，用于保存/加载conversionDone状态
        public override void ExposeData()
        {
            base.ExposeData();
            // 序列化conversionDone字段，默认值为false
            Scribe_Values.Look(ref conversionDone, "conversionDone", false);
        }
    }
}
