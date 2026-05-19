using RimWorld;
using System.Linq;
using Verse;

namespace MooGirl
{
    // 自定义思想类，继承自Thought_Memory，用于处理与健康状态(Hediff)相关的思想效果
    internal class Thought_Hediff : Thought_Memory
    {
        // 标记是否已添加过健康状态，避免重复处理
        public bool added = false;

        // 数据序列化方法，用于保存/加载added字段
        public override void ExposeData()
        {
            Scribe_Values.Look<bool>(ref this.added, "added", false, false);
        }

        // 计算心情偏移量，并在首次计算时添加相关的健康状态效果
        public override float MoodOffset()
        {
            // 如果尚未添加健康状态效果
            bool flag = !this.added;
            if (flag)
            {
                // 检查思想效果是否被无效化
                bool flag2 = !ThoughtUtility.ThoughtNullified(this.pawn, this.def);
                if (flag2)
                {
                    // 如果定义中指定了基础健康状态，则添加到角色
                    bool flag3 = this.def.hediff != null;
                    if (flag3)
                    {
                        AddOrRefreshDefHediff();
                    }

                    // 检查是否有扩展定义
                    bool flag4 = this.def.HasModExtension<Thought_Hediff_Extension>();
                    if (flag4)
                    {
                        // 获取扩展定义
                        Thought_Hediff_Extension modExtension = this.def.GetModExtension<Thought_Hediff_Extension>();

                        // 处理第一个要影响的健康状态
                        bool flag5 = modExtension.hediffToAffect != null;
                        if (flag5)
                        {
                            // 获取指定身体部位并添加健康状态
                            BodyPartRecord part = this.pawn.RaceProps.body.GetPartsWithDef(modExtension.partToAffect).FirstOrDefault<BodyPartRecord>();
                            this.pawn.health.AddHediff(modExtension.hediffToAffect, part, null, null);
                            // 增加健康状态的严重程度
                            this.pawn.health.hediffSet.GetFirstHediffOfDef(modExtension.hediffToAffect, false).Severity += modExtension.percentage;
                        }

                        // 处理第二个要影响的健康状态（可选）
                        bool flag6 = modExtension.secondHediffToAffect != null;
                        if (flag6)
                        {
                            // 获取指定身体部位并添加健康状态
                            BodyPartRecord part2 = this.pawn.RaceProps.body.GetPartsWithDef(modExtension.secondPartToAffect).FirstOrDefault<BodyPartRecord>();
                            this.pawn.health.AddHediff(modExtension.secondHediffToAffect, part2, null, null);
                            // 增加健康状态的严重程度
                            this.pawn.health.hediffSet.GetFirstHediffOfDef(modExtension.secondHediffToAffect, false).Severity += modExtension.secondPercentage;
                        }

                        // 如果需要增加愉悦度
                        bool increaseJoy = modExtension.increaseJoy;
                        if (increaseJoy)
                        {
                            this.pawn.needs.joy.GainJoy(modExtension.extraJoy, JoyKindDefOf.Gluttonous);
                        }
                    }
                }
                // 标记已处理，避免重复执行
                this.added = true;
            }
            // 返回基础心情偏移量
            return base.MoodOffset();
        }

        public override bool TryMergeWithExistingMemory(out bool showBubble)
        {
            bool merged = base.TryMergeWithExistingMemory(out showBubble);
            if (merged && this.def.hediff != null && !ThoughtUtility.ThoughtNullified(this.pawn, this.def))
            {
                AddOrRefreshDefHediff();
            }
            return merged;
        }

        private void AddOrRefreshDefHediff()
        {
            Hediff hediff = this.pawn.health.hediffSet.GetFirstHediffOfDef(this.def.hediff, false);
            if (hediff == null)
            {
                hediff = this.pawn.health.AddHediff(this.def.hediff, null, null, null);
            }

            HediffComp_Disappears disappears = hediff.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ResetElapsedTicks();
            }

            HediffComp_CureFoodEffects cure = hediff.TryGetComp<HediffComp_CureFoodEffects>();
            if (cure != null)
            {
                cure.ReapplyCure();
            }
        }
    }

    // 思想效果的扩展定义，用于配置额外的健康状态影响
    public class Thought_Hediff_Extension : DefModExtension
    {
        // 第一个要影响的健康状态定义
        public HediffDef hediffToAffect = null;
        // 第一个要影响的身体部位
        public BodyPartDef partToAffect = null;
        // 第一个健康状态的严重程度增加比例
        public float percentage = 1f;

        // 第二个要影响的健康状态定义（可选）
        public HediffDef secondHediffToAffect = null;
        // 第二个要影响的身体部位（可选）
        public BodyPartDef secondPartToAffect = null;
        // 第二个健康状态的严重程度增加比例（可选）
        public float secondPercentage = 1f;

        // 是否增加愉悦度
        public bool increaseJoy = false;
        // 增加的愉悦度数值
        public float extraJoy = 0f;
    }
}
