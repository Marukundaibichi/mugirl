using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    // 根据记忆 Thought 维持或移除自身 Hediff 的配置。
    internal class HediffCompProperties_WhileHavingThoughts : HediffCompProperties
    {
        public HediffCompProperties_WhileHavingThoughts()
        {
            this.compClass = typeof(HediffComp_WhileHavingThoughts);
        }

        public List<ThoughtDef> thoughtDefs = new List<ThoughtDef>();

        public List<ThoughtDef> removeThoughtDefs = new List<ThoughtDef>();

        public string hediffReduction = "";

        public float reductionAmount = 0f;

        public bool resurrectionEffect = false;
    }

    // 周期性检查 Pawn 记忆：指定 Thought 不存在时移除自身，并可削弱其他 Hediff。
    internal class HediffComp_WhileHavingThoughts : HediffComp
    {
        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look<bool>(ref this.flagAmIThinking, "flagAmIThinking", false, false);
            Scribe_Values.Look<int>(ref this.checkingCounter, "checkingCounter", 600, false);
        }

        public HediffCompProperties_WhileHavingThoughts Props
        {
            get
            {
                return this.props as HediffCompProperties_WhileHavingThoughts;
            }
        }

        // 创建时先处理一次可选的严重度削减，后续 tick 只负责 Thought 检查。
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

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            HediffCompProperties_WhileHavingThoughts thoughtProps = Props;
            if (thoughtProps == null)
            {
                RemoveSelf();
                return;
            }

            this.checkingCounter++;

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

                this.flagAmIThinking = false;
                bool flag2 = thoughtProps.thoughtDefs != null && thoughtProps.thoughtDefs.Count > 0;
                if (flag2)
                {
                    foreach (ThoughtDef def in thoughtProps.thoughtDefs)
                    {
                        bool flag3 = def != null && memories.GetFirstMemoryOfDef(def) != null;
                        if (flag3)
                        {
                            this.flagAmIThinking = true;
                            break;
                        }
                    }
                }

                bool flag4 = thoughtProps.removeThoughtDefs != null && thoughtProps.removeThoughtDefs.Count > 0;
                if (flag4)
                {
                    foreach (ThoughtDef def2 in thoughtProps.removeThoughtDefs)
                    {
                        Thought_Memory memory = def2 == null ? null : memories.GetFirstMemoryOfDef(def2);
                        if (memory != null)
                        {
                            memory.moodPowerFactor = 0f;
                        }
                    }
                }

                bool flag6 = !this.flagAmIThinking;
                if (flag6)
                {
                    RemoveSelf();
                }

                this.checkingCounter = 0;
            }
        }

        public bool flagAmIThinking = false;

        public int checkingInterval = 600;

        public int checkingCounter = 600;

        private void RemoveSelf()
        {
            Pawn?.health?.RemoveHediff(parent);
        }
    }
}
