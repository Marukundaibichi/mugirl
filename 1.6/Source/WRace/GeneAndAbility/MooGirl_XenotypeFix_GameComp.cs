using RimWorld;
using System.Linq;
using Verse;

namespace MooGirl
{
    // 游戏组件：用于统一修正雪牛体型 Pawn 的 Xenotype（仅在 Biotech 启用时）
    public class MooGirl_XenotypeFix_GameComp : GameComponent
    {
        // 是否已经执行过修正
        public bool xenotypeFixed = false;

        // 检查计时器
        public int checkTimer = 300;

        // 空构造函数（反序列化用）
        public MooGirl_XenotypeFix_GameComp() { }

        // 主构造函数
        public MooGirl_XenotypeFix_GameComp(Game game) { }

        // Tick 更新
        public override void GameComponentTick()
        {
            // Biotech DLC 未启用，直接跳过
            if (!ModsConfig.BiotechActive)
            {
                return;
            }

            // 已处理过则不再执行
            if (xenotypeFixed)
            {
                return;
            }

            // 计时器递减
            if (checkTimer > 0)
            {
                checkTimer--;
                return;
            }

            // 到点后执行修正
            TryFixMooGirlXenotype();

            // 标记已完成
            xenotypeFixed = true;
        }

        // 实际执行逻辑
        private void TryFixMooGirlXenotype()
        {
            XenotypeDef targetXenotype = MooGirl_DefOf.MooGirl_Xenotype;
            if (targetXenotype == null) return;

            foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
            {
                if (pawn == null) continue;
                if (pawn.RaceProps == null) continue;

                // 只处理 ThingDef 是 MooGirl 的 Pawn
                if (pawn.def != MooGirl_DefOf.MooGirl) continue;

                // 必须有基因系统
                if (pawn.genes == null) continue;

                // 玩家植入的异种胚芽或人物编辑器创建的自定义异种会使用 xenogene/UniqueXenotype。
                // 这类情况不能用 SetXenotype 重置，否则会清空玩家已有的异种基因。
                if (pawn.genes.UniqueXenotype || pawn.genes.Xenogenes.Any()) continue;

                // Xenotype 已正确则只补缺失的基础基因
                if (pawn.genes.Xenotype != targetXenotype)
                {
                    pawn.genes.SetXenotypeDirect(targetXenotype);
                }

                foreach (GeneDef geneDef in targetXenotype.genes)
                {
                    if (!pawn.genes.HasEndogene(geneDef) && !pawn.genes.HasXenogene(geneDef))
                    {
                        pawn.genes.AddGene(geneDef, xenogene: false);
                    }
                }
            }
        }


        // 存档
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref xenotypeFixed, "xenotypeFixed", false);
            Scribe_Values.Look(ref checkTimer, "checkTimer", 300);
        }
    }
}
