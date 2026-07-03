using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    // 奴隶服装 ThingDef 扩展，保存锁定、Hediff 和身体部位限制等 XML 配置。
    public class SlaveApparelDef : ThingDef
    {
        public Type soul_type;
        public HediffDef equipped_hediff = null;
        public bool gives_bound_moodlet = false;
        public bool gives_gagged_moodlet = false;

        // 旧 XML 仍使用这些身体功能开关，运行逻辑会按需读取。
        public bool blocks_hands = false;
        public bool blocks_oral = false;
        public bool blocks_penis = false;
        public bool blocks_vagina = false;
        public bool blocks_anus = false;
        public bool blocks_breasts = false;

        public int unlockTick = 100;
        public int needkeynumber = 2;
        public ThingDef keytype = null;

        public List<ThingDef> NextSlaveApparelDefs;
        public List<BodyPartDef> HediffTargetBodyPartDefs;
        public List<BodyPartGroupDef> BoundBodyPartGroupDefs;
    }

    // 奴隶服装基类，负责锁数、穿戴 Hediff、装备锁定和下一阶段自动替换。
    public class SlaveApparel : Apparel
    {
        public int lockCount;
        public bool isLocked = true;
        public SlaveApparelDef SlaveDef => this.def as SlaveApparelDef;

        public virtual void Crack() { }

        private int InitialLockCount => SlaveDef?.needkeynumber ?? 0;

        public override void PostMake()
        {
            base.PostMake();
            if (this.def is SlaveApparelDef def)
            {
                this.lockCount = def.needkeynumber;
            }
        }

        public override bool AllowVerbCast(Verb verb)
        {
            var slaveDef = this.def as SlaveApparelDef;
            // Verb 关联的身体部位组被束缚时，禁止该动作。
            if (slaveDef?.BoundBodyPartGroupDefs != null &&
                verb.tool != null &&
                slaveDef.BoundBodyPartGroupDefs.Contains(verb.tool.linkedBodyPartsGroup))
            {
                return false;
            }
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref this.lockCount, "lockCount", InitialLockCount);
            Scribe_Values.Look(ref isLocked, "isLocked", true);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && isLocked && lockCount <= 0)
            {
                lockCount = InitialLockCount;
            }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);

            var def = this.def as SlaveApparelDef;
            if (def == null || pawn?.apparel == null) return;

            // 穿戴时按配置给指定身体部位添加 Hediff，避免重复添加同部位效果。
            if (def.equipped_hediff != null && def.HediffTargetBodyPartDefs != null && pawn.health?.hediffSet != null && pawn.RaceProps?.body?.AllParts != null)
            {
                foreach (BodyPartDef partDef in def.HediffTargetBodyPartDefs)
                {
                    if (partDef == null)
                    {
                        continue;
                    }

                    List<BodyPartRecord> bodyParts = pawn.RaceProps.body.AllParts;
                    for (int i = 0; i < bodyParts.Count; i++)
                    {
                        BodyPartRecord bodyPart = bodyParts[i];
                        if (bodyPart.def == partDef && !pawn.health.hediffSet.HasHediff(def.equipped_hediff, bodyPart))
                        {
                            pawn.health.AddHediff(def.equipped_hediff, bodyPart);
                        }
                    }
                }
            }
            if (this.isLocked == true)
            {
                pawn.apparel.Lock(this);
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);

            var def = this.def as SlaveApparelDef;
            if (def == null || pawn?.apparel == null)
                return;

            // 卸下时只移除本服装配置过的 Hediff 和目标部位。
            if (def.equipped_hediff != null && def.HediffTargetBodyPartDefs != null && pawn.health?.hediffSet != null)
            {
                List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
                for (int i = hediffs.Count - 1; i >= 0; i--)
                {
                    Hediff hediff = hediffs[i];
                    if (hediff.def == def.equipped_hediff && def.HediffTargetBodyPartDefs.Contains(hediff.Part?.def))
                    {
                        pawn.health.RemoveHediff(hediff);
                    }
                }
            }

            // 存在下一阶段服装时，卸下当前件后自动换上第一件可穿目标。
            if (def.NextSlaveApparelDefs != null)
            {
                foreach (var nextDef in def.NextSlaveApparelDefs)
                {
                    if (!CanAutoWearNextStage(pawn, nextDef))
                    {
                        continue;
                    }

                    ThingDef stuff = this.Stuff ?? GenStuff.DefaultStuffFor(nextDef);
                    if (ThingMaker.MakeThing(nextDef, stuff) is Apparel nextApparel)
                    {
                        pawn.apparel.Wear(nextApparel, locked: true);
                        break;
                    }
                }
            }
        }

        private static bool CanAutoWearNextStage(Pawn pawn, ThingDef nextDef)
        {
            return pawn?.apparel != null
                && nextDef != null
                && nextDef.IsApparel
                && pawn.RaceProps?.body != null
                && ApparelUtility.HasPartsToWear(pawn, nextDef);
        }

        protected virtual void DevUnlockFromWearer()
        {
            if (Wearer == null)
            {
                return;
            }

            isLocked = false;
            lockCount = 0;
            Wearer.apparel?.Unlock(this);
            Messages.Message("Mugirl.Restraints.DevUnlock.Message".Translate(Wearer.LabelShortCap, LabelCap), Wearer, MessageTypeDefOf.PositiveEvent);
        }
    }

    // 高级奴隶服装额外保存破解状态，破解后不再保持锁定。
    public class AdvancedSlaveApparel : SlaveApparel
    {
        private bool isCracked = false;
        public bool IsCracked() => isCracked;

        public override void Crack()
        {
            this.isCracked = true;
            DevUnlockFromWearer();

            Pawn wearer = Wearer;
            if (wearer != null)
            {
                Messages.Message("Mugirl.SlaveApparelCracked".Translate(wearer.LabelShortCap), wearer, MessageTypeDefOf.PositiveEvent);
                MugirlSelectionUtility.SelectInPlaying(wearer);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref isCracked, "isCracked", false);
        }

        public override string LabelNoCount
        {
            get
            {
                string label = base.LabelNoCount;
                string status = IsCracked() ? "Mugirl.Cracked".Translate().ToString() : "Mugirl.Uncracked".Translate().ToString();
                return "Mugirl.SlaveApparel.StatusLabel".Translate(label, status).ToString();
            }
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            foreach (var baseEntry in base.SpecialDisplayStats()) yield return baseEntry;

            yield return new StatDrawEntry(
                StatCategoryDefOf.Apparel,
                "Mugirl.CrackStatus".Translate(),
                IsCracked() ? "Mugirl.Cracked".Translate() : "Mugirl.Uncracked".Translate(),
                "Mugirl.CrackStatusDesc".Translate(),
                999);
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            if (pawn == null) return;

            // 已破解的高级装备需要主动解锁，普通奴隶服装仍按自身 isLocked 状态处理。
            foreach (var wornApparel in pawn.apparel.WornApparel)
            {
                if (wornApparel is AdvancedSlaveApparel advancedSlave)
                {
                    if (!advancedSlave.IsCracked() && advancedSlave.isLocked)
                    {
                        pawn.apparel.Lock(wornApparel);
                    }
                    else
                    {
                        pawn.apparel.Unlock(wornApparel);
                    }
                }
                else if (wornApparel is SlaveApparel slaveApparel)
                {
                    if (slaveApparel.isLocked)
                    {
                        pawn.apparel.Lock(wornApparel);
                    }
                    else
                    {
                        pawn.apparel.Unlock(wornApparel);
                    }
                }
            }
        }
    }

    // XML 标记类型，行为继承自 AdvancedSlaveApparel。
    public class BrainWashSlaveApparel : AdvancedSlaveApparel { }
}
