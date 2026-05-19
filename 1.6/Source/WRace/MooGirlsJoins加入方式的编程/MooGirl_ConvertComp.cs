using RimWorld;
using System.Linq;
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

            // 确保转换逻辑只执行一次
            if (!conversionDone)
            {
                ConvertEscapeWildSlaves();  // 执行转换逻辑
                conversionDone = true;     // 标记转换已完成
            }
        }

        // 静态方法：将所有EscapeWildSlave类型的殖民者转换为PreEscapeWildSlave类型
        public static void ConvertEscapeWildSlaves()
        {
            // 获取当前地图上所有属于玩家阵营的自由殖民者
            var playerPawns = PawnsFinder.AllMaps_FreeColonists.Where(p => p.Faction == Faction.OfPlayer);

            // 记录成功转换的殖民者数量
            int convertedCount = 0;

            // 遍历所有玩家殖民者
            foreach (var pawn in playerPawns)
            {
                // 检查是否为需要转换的目标类型
                if (pawn.kindDef == MooGirl_DefOf.MooGirl_EscapeWildSlave)
                {
                    // 执行类型转换
                    pawn.kindDef = MooGirl_DefOf.MooGirl_PreEscapeWildSlave;
                    convertedCount++;
                    // 记录转换日志（使用短名称或标签）
                    Log.Message($"[MooGirl] 已将 {pawn.Name?.ToStringShort ?? pawn.LabelShort} 的 kindDef 从 EscapeWildSlave 改为 PreEscapeWildSlave。");
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