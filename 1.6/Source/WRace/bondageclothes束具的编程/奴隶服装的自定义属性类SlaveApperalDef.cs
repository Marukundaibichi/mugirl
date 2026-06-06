using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // 定义奴隶服装的自定义属性类，继承自ThingDef
    public class SlaveApparelDef : ThingDef
    {
        // 灵魂类型（用于特殊功能）
        public Type soul_type;
        // 装备时添加的Hediff（健康影响）
        public HediffDef equipped_hediff = null;
        // 是否触发束缚心情效果
        public bool gives_bound_moodlet = false;
        // 是否触发口塞心情效果
        public bool gives_gagged_moodlet = false;
        // 以下布尔值表示是否阻塞对应身体部位的功能
        public bool blocks_hands = false;
        public bool blocks_oral = false;
        public bool blocks_penis = false;
        public bool blocks_vagina = false;
        public bool blocks_anus = false;
        public bool blocks_breasts = false;

        // 解锁所需时间（游戏刻）
        public int unlockTick = 100;
        // 解锁所需钥匙数量
        public int needkeynumber = 2;
        // 钥匙类型定义
        public ThingDef keytype = null;

        // 下一阶段可升级的奴隶服装列表
        public List<ThingDef> NextSlaveApperalDefs;
        // Hediff作用的目标身体部位列表
        public List<BodyPartDef> HediffTargetBodyPartDefs;
        // 被束缚的身体部位组列表
        public List<BodyPartGroupDef> BoundBodyPartGroupDefs;
    }

    // 奴隶服装基类，继承自Apparel
    public class SlaveApparel : Apparel
    {
        // 当前锁的数量
        public int lockCount;
        // 是否被锁定
        public bool isLocked = true;
        // 快捷访问自定义属性定义
        public SlaveApparelDef SlaveDef => this.def as SlaveApparelDef;

        // 破解虚拟方法（待实现）
        public virtual void Crack() { }

        // 物品制作完成后的初始化
        public override void PostMake()
        {
            base.PostMake();
            // 初始化锁的数量
            if (this.def is SlaveApparelDef def)
            {
                this.lockCount = def.needkeynumber;
            }
        }

        // 检查是否允许使用特定Verb（动作）
        public override bool AllowVerbCast(Verb verb)
        {
            var slaveDef = this.def as SlaveApparelDef;
            // 如果动作关联的身体部位组被束缚则禁止该动作
            if (slaveDef?.BoundBodyPartGroupDefs != null &&
                verb.tool != null &&
                slaveDef.BoundBodyPartGroupDefs.Contains(verb.tool.linkedBodyPartsGroup))
            {
                return false;
            }
            return true;
        }

        // 数据保存/加载
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref this.lockCount, "lockCount");
            Scribe_Values.Look(ref isLocked, "isLocked", false);
        }

        // 装备时的处理逻辑
        public override void Notify_Equipped(Pawn pawn)
        {
            var def = this.def as SlaveApparelDef;
            if (def == null || pawn == null) return;

            // 添加装备特定的Hediff效果
            if (def.equipped_hediff != null && def.HediffTargetBodyPartDefs != null)
            {
                foreach (BodyPartDef partDef in def.HediffTargetBodyPartDefs)
                {
                    List<BodyPartRecord> bodyParts = pawn.RaceProps.body.AllParts.FindAll(part => part.def == partDef);
                    foreach (BodyPartRecord bodyPart in bodyParts)
                    {
                        pawn.health.AddHediff(def.equipped_hediff, bodyPart);
                    }
                }
            }
            // 如果服装被锁定则锁定装备
            if (this.isLocked == true)
            {
                pawn.apparel.Lock(this);
            }
        }

        // 卸下装备时的处理逻辑
        public override void Notify_Unequipped(Pawn pawn)
        {
            var def = this.def as SlaveApparelDef;
            if (def == null)
                return;

            // 移除装备特定的Hediff效果
            if (def.equipped_hediff != null && def.HediffTargetBodyPartDefs != null)
            {
                var hediffsToRemove = pawn.health.hediffSet.hediffs
                    .Where(h => h.def == def.equipped_hediff && def.HediffTargetBodyPartDefs.Contains(h.Part?.def))
                    .ToList();

                foreach (var hediff in hediffsToRemove)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }

            // 自动装备下一阶段服装（如果存在）
            if (def.NextSlaveApperalDefs != null)
            {
                foreach (var nextDef in def.NextSlaveApperalDefs)
                {
                    ThingDef stuff = this.Stuff ?? GenStuff.DefaultStuffFor(nextDef);
                    Apparel nextApparel = (Apparel)ThingMaker.MakeThing(nextDef, stuff);
                    pawn.apparel.Wear(nextApparel);
                    pawn.apparel.Lock(nextApparel);
                    break; // 只处理第一件可装备的
                }
            }
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
            Messages.Message("MooGirl.Restraints.DevUnlock.Message".Translate(Wearer.LabelShortCap, LabelCap), Wearer, MessageTypeDefOf.PositiveEvent);
        }
    }

    // 高级奴隶服装类，继承自SlaveApparel
    public class AdvancedSlaveApparel : SlaveApparel
    {
        // 是否已被破解
        private bool isCracked = false;
        // 查询破解状态
        public bool IsCracked() => isCracked;

        // 破解服装的实现
        public override void Crack()
        {
            this.isCracked = true;
            DevUnlockFromWearer();

            // 显示破解消息并选中角色
            if (this.Wearer != null)
            {
                Messages.Message($"{this.Wearer.LabelShortCap} {"MooGirl.SlaveApparelCracked".Translate()}", this.Wearer, MessageTypeDefOf.PositiveEvent);
                Find.Selector.Select(this.Wearer);
            }
        }

        // 数据保存/加载（包含破解状态）
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref isCracked, "isCracked", false);
        }

        // 获取带状态标签的名称
        public override string LabelNoCount
        {
            get
            {
                string label = base.LabelNoCount;
                label += IsCracked() ? $"（{"MooGirl.Cracked".Translate()}）" : $"（{"MooGirl.Uncracked".Translate()}）";
                return label;
            }
        }

        // 添加特殊属性显示（如破解状态）
        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            foreach (var baseEntry in base.SpecialDisplayStats()) yield return baseEntry;

            yield return new StatDrawEntry(
                StatCategoryDefOf.Apparel,
                "MooGirl.CrackStatus".Translate(),
                IsCracked() ? "MooGirl.Cracked".Translate() : "MooGirl.Uncracked".Translate(),
                "MooGirl.CrackStatusDesc".Translate(),
                999);
        }

        // 装备时的处理逻辑（覆盖基类实现）
        public override void Notify_Equipped(Pawn pawn)
        {
            var def = this.def as SlaveApparelDef;
            if (def == null || pawn == null) return;

            // 添加装备特定的Hediff效果
            if (def.equipped_hediff != null && def.HediffTargetBodyPartDefs != null)
            {
                foreach (BodyPartDef partDef in def.HediffTargetBodyPartDefs)
                {
                    List<BodyPartRecord> bodyParts = pawn.RaceProps.body.AllParts.FindAll(part => part.def == partDef);
                    foreach (BodyPartRecord bodyPart in bodyParts)
                    {
                        pawn.health.AddHediff(def.equipped_hediff, bodyPart);
                    }
                }
            }

            // 根据破解状态处理装备锁定
            if (this.isLocked == true)
            {
                pawn.apparel.Lock(this);
            }
            // 只有未破解的高级奴隶服装才会锁定
            foreach (var wornApparel in pawn.apparel.WornApparel)
            {
                if (wornApparel is AdvancedSlaveApparel advancedSlave)
                {
                    if (!advancedSlave.IsCracked() && isLocked == true)
                    {
                        pawn.apparel.Lock(wornApparel);
                    }
                    else
                    {
                        pawn.apparel.Unlock(wornApparel); // 防止已破解装备被锁定
                    }
                }
                else if (wornApparel is SlaveApparel)
                {
                    // 普通奴隶服装保持原有锁定逻辑
                    pawn.apparel.Lock(wornApparel);
                }
            }
        }
    }

    // 洗脑奴隶服装类，继承自AdvancedSlaveApparel（当前为空实现）
    public class BrainWashSlaveApparel : AdvancedSlaveApparel { }
}
