using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    // 定义一个Hediff组件属性类，用于配置"当拥有特定想法时"的效果
    internal class HediffCompProperties_WhileHavingThoughts : HediffCompProperties
    {
        public HediffCompProperties_WhileHavingThoughts()
        {
            // 设置对应的组件类
            this.compClass = typeof(HediffComp_WhileHavingThoughts);
        }

        // 需要检测的想法定义列表
        public List<ThoughtDef> thoughtDefs = new List<ThoughtDef>();

        // 需要移除的想法定义列表
        public List<ThoughtDef> removeThoughtDefs = new List<ThoughtDef>();

        // 需要减少严重度的Hediff定义名称
        public string hediffReduction = "";

        // 严重度减少的数值
        public float reductionAmount = 0f;

        // 是否具有复活效果
        public bool resurrectionEffect = false;
    }

    // 实现"当拥有特定想法时"效果的Hediff组件
    internal class HediffComp_WhileHavingThoughts : HediffComp
    {
        // 数据暴露方法，用于存档/读档
        public override void CompExposeData()
        {
            base.CompExposeData();
            // 读写flagAmIThinking标志位
            Scribe_Values.Look<bool>(ref this.flagAmIThinking, "flagAmIThinking", false, false);
            Scribe_Values.Look<int>(ref this.checkingCounter, "checkingCounter", 600, false);
        }

        // 获取组件属性的快捷方式
        public HediffCompProperties_WhileHavingThoughts Props
        {
            get
            {
                return this.props as HediffCompProperties_WhileHavingThoughts;
            }
        }

        // 组件创建后初始化
        public override void CompPostMake()
        {
            base.CompPostMake();

            HediffCompProperties_WhileHavingThoughts thoughtProps = Props;
            if (thoughtProps == null || string.IsNullOrEmpty(thoughtProps.hediffReduction) || Pawn?.health?.hediffSet == null)
            {
                return;
            }

            HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(thoughtProps.hediffReduction);
            Hediff firstHediffOfDef = hediffDef == null ? null : Pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef, false);
            if (firstHediffOfDef != null)
            {
                firstHediffOfDef.Severity -= thoughtProps.reductionAmount;
            }
        }

        // 每帧调用的方法
        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            HediffCompProperties_WhileHavingThoughts thoughtProps = Props;
            if (thoughtProps == null)
            {
                RemoveSelf();
                return;
            }

            // 计数器递增
            this.checkingCounter++;

            // 当计数器达到检查间隔时执行检查
            int interval = this.checkingInterval < 1 ? 1 : this.checkingInterval;
            bool flag = this.checkingCounter > interval;
            if (flag)
            {
                var memories = Pawn?.needs?.mood?.thoughts?.memories;
                if (memories == null)
                {
                    RemoveSelf();
                    return;
                }

                // 检查需要检测的想法列表
                this.flagAmIThinking = false;
                bool flag2 = thoughtProps.thoughtDefs != null && thoughtProps.thoughtDefs.Count > 0;
                if (flag2)
                {
                    // 遍历所有需要检测的想法
                    foreach (ThoughtDef def in thoughtProps.thoughtDefs)
                    {
                        // 检查角色是否拥有该想法
                        bool flag3 = def != null && memories.GetFirstMemoryOfDef(def) != null;
                        if (flag3)
                        {
                            this.flagAmIThinking = true;
                            break;
                        }
                    }
                }

                // 处理需要移除效果的想法列表
                bool flag4 = thoughtProps.removeThoughtDefs != null && thoughtProps.removeThoughtDefs.Count > 0;
                if (flag4)
                {
                    // 遍历所有需要移除效果的想法
                    foreach (ThoughtDef def2 in thoughtProps.removeThoughtDefs)
                    {
                        // 检查角色是否拥有该想法
                        Thought_Memory memory = def2 == null ? null : memories.GetFirstMemoryOfDef(def2);
                        if (memory != null)
                        {
                            // 将该想法的情绪影响因子设为0（移除效果）
                            memory.moodPowerFactor = 0f;
                        }
                    }
                }

                // 如果角色没有任何需要检测的想法
                bool flag6 = !this.flagAmIThinking;
                if (flag6)
                {
                    // 移除当前Hediff效果
                    RemoveSelf();
                }

                // 重置计数器
                this.checkingCounter = 0;
            }
        }

        // 标志位：角色是否正在思考配置中的想法
        public bool flagAmIThinking = false;

        // 检查间隔（tick数）
        public int checkingInterval = 600;

        // 当前计数器值
        public int checkingCounter = 600;

        private void RemoveSelf()
        {
            Pawn?.health?.RemoveHediff(parent);
        }
    }
}
