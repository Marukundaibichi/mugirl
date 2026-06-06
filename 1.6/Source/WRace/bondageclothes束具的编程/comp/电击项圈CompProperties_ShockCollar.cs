using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MooGirl
{
    // 定义电击项圈的属性类，继承自CompProperties
    public class CompProperties_ShockCollar : CompProperties
    {
        public CompProperties_ShockCollar()
        {
            compClass = typeof(Comp_ShockCollar);
        }

        // 普通电击效果列表
        public List<HediffDef> hediffDefs = new List<HediffDef>();
        // 强力电击效果列表
        public List<HediffDef> powerhediffDefs = new List<HediffDef>();
        // 非雪牛娘强力电击效果列表
        public List<HediffDef> nonMooGirlPowerHediffDefs = new List<HediffDef>();
        // 自动触发间隔时间（tick）
        public int ticks = 60;
        // 自动触发的概率百分比（0-100）
        public int rand = 50;
        // 强力电击的概率百分比（0-100）
        public int powerShockChance = 30;

        // 主动使用模式的冷却时间（tick）
        public int useCooldownTicks = 480;

        // 强力电击按钮显示文本
        public string powerLabel = "MooGirl.Restraints.ShockCollar.PowerLabel";
        public string powerDesc = "MooGirl.Restraints.ShockCollar.PowerDesc";
        public string powerIconPath = "UI/Commands/DesirePower";

        // 普通电击按钮显示文本
        public string commonLabel = "MooGirl.Restraints.ShockCollar.CommonLabel";
        public string commonDesc = "MooGirl.Restraints.ShockCollar.CommonDesc";
        public string commonIconPath = "UI/Commands/DesirePower";

        // 是否只对玩家控制的单位生效（殖民者、囚犯、奴隶），避免访客模组冲突
        public bool playerColonistOnly = true;
    }

    // 电击项圈组件实现
    public class Comp_ShockCollar : Comp_AdvancedSlaveApparel
    {
        // 内部计时器
        private int ticks = 0;
        // 记录上次手动使用的时间点
        private int lastManualUseTick = -99999;

        // 获取组件属性
        public CompProperties_ShockCollar Props => (CompProperties_ShockCollar)props;

        // 获取佩戴者（每次实时检查）
        private Pawn GetWearer()
        {
            if (parent?.ParentHolder is Pawn_ApparelTracker tracker)
            {
                return tracker.pawn;
            }
            return null;
        }

        // 检查是否可以被电击（玩家殖民者、囚犯、奴隶可以，访客不行）
        private bool CanBeShocked(Pawn pawn)
        {
            if (pawn == null)
                return false;

            // 如果配置允许所有单位，直接返回true
            if (!Props.playerColonistOnly)
                return true;

            // 检查是否是玩家的殖民者
            if (pawn.IsColonistPlayerControlled)
                return true;

            // 检查是否是玩家的囚犯（包括奴隶）
            if (pawn.IsPrisonerOfColony)
                return true;

            // 其他情况（访客、敌对单位、友军等）都不行
            return false;
        }

        // 每帧调用，处理自动电击逻辑
        public override void CompTick()
        {
            base.CompTick();

            // 每tick只做计时器递减，非常轻量
            ticks--;

            // 只有计时器触发时才做完整检查
            if (ticks <= 0)
            {
                // 重置计时器
                ticks = Props.ticks;

                // 实时获取佩戴者
                Pawn wearer = GetWearer();

                // 完整检查：佩戴者是否有效
                if (wearer == null || wearer.Dead || !wearer.Spawned)
                    return;

                // 检查：只对玩家控制的单位生效（避免访客模组冲突）
                if (!CanBeShocked(wearer))
                    return;

                // 检查：ParentIsCracked() 返回 true = 未破解，返回 false = 已破解
                // 只有未破解状态才会自动随机电击
                if (!ParentIsCracked())
                    return;

                // 使用RimWorld内置的随机数系统
                // 概率触发电击
                if (Rand.Chance(Props.rand / 100f))
                {
                    // 随机选择电击类型
                    if (Rand.Chance(Props.powerShockChance / 100f))
                    {
                        ApplyHediffs(wearer, PowerHediffsFor(wearer));
                    }
                    else
                    {
                        ApplyHediffs(wearer, Props.hediffDefs);
                    }
                }
            }
        }

        // 提取公共方法 - 应用健康效果
        private void ApplyHediffs(Pawn pawn, List<HediffDef> hediffs)
        {
            if (pawn == null || hediffs == null)
                return;

            // 批量处理，减少多次访问health.hediffSet
            var hediffSet = pawn.health.hediffSet;

            foreach (HediffDef hediffDef in hediffs)
            {
                // 提前检查null
                if (hediffDef == null)
                    continue;

                // 避免重复添加相同效果
                if (!hediffSet.HasHediff(hediffDef))
                {
                    pawn.health.AddHediff(hediffDef);
                }
            }
        }

        // 保存/加载游戏时调用
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticks, "ticks", 0);
            Scribe_Values.Look(ref lastManualUseTick, "lastManualUseTick", -99999);
        }

        // 获取装备时显示的按钮
        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (var gizmo in base.CompGetWornGizmosExtra())
                yield return gizmo;

            // 实时获取佩戴者
            Pawn wearer = GetWearer();

            // 基础检查：存在佩戴者
            if (wearer == null)
                yield break;

            // 只对玩家控制的单位显示按钮（殖民者、囚犯、奴隶）
            if (!CanBeShocked(wearer))
                yield break;

            // ParentIsCracked() 返回 true = 未破解，返回 false = 已破解
            // 只有已破解才显示手动控制按钮
            if (ParentIsCracked())
                yield break;

            // 计算冷却信息（只计算一次）
            int currentTick = Find.TickManager.TicksGame;
            int cdLeft = Mathf.Max(0, (lastManualUseTick + Props.useCooldownTicks) - currentTick);
            bool canUse = cdLeft <= 0;
            float cooldownPercent = canUse ? 1f : Mathf.Clamp01(1f - (float)cdLeft / Props.useCooldownTicks);

            // 提前计算描述文本，避免类型混淆
            string powerDesc = canUse
                ? MooGirlText.Resolve(Props.powerDesc)
                : MooGirlText.Resolve("MooGirl.Restraints.ShockCollar.PowerCooldownTicksLeft", cdLeft);

            string commonDesc = canUse
                ? MooGirlText.Resolve(Props.commonDesc)
                : MooGirlText.Resolve("MooGirl.Restraints.ShockCollar.CommonCooldownTicksLeft", cdLeft);

            // 强力电击按钮
            yield return CreateShockCommand(
                wearer,
                MooGirlText.Resolve(Props.powerLabel),
                powerDesc,
                Props.powerIconPath,
                canUse,
                cooldownPercent,
                () => ApplyHediffs(wearer, PowerHediffsFor(wearer))
            );

            // 普通电击按钮
            yield return CreateShockCommand(
                wearer,
                MooGirlText.Resolve(Props.commonLabel),
                commonDesc,
                Props.commonIconPath,
                canUse,
                cooldownPercent,
                () => ApplyHediffs(wearer, Props.hediffDefs)
            );
        }

        // 提取公共方法 - 创建电击命令按钮
        private Command_ActionWithCooldown CreateShockCommand(
            Pawn wearer,
            string label,
            string desc,
            string iconPath,
            bool enabled,
            float cooldownPercent,
            System.Action applyAction)
        {
            return new Command_ActionWithCooldown
            {
                defaultLabel = label,
                defaultDesc = desc,
                icon = ContentFinder<Texture2D>.Get(iconPath),
                action = () =>
                {
                    if (!enabled) return;

                    // 双重检查，确保执行时佩戴者仍然有效且已破解
                    Pawn currentWearer = GetWearer();
                    if (currentWearer != null && CanBeShocked(currentWearer) && !ParentIsCracked())
                    {
                        applyAction();
                        lastManualUseTick = Find.TickManager.TicksGame;
                    }
                },
                Disabled = !enabled,
                cooldownPercentGetter = () => cooldownPercent
            };
        }

        private List<HediffDef> PowerHediffsFor(Pawn pawn)
        {
            if (pawn != null && !MountedPawnUtility.IsMooGirl(pawn) && Props.nonMooGirlPowerHediffDefs != null && Props.nonMooGirlPowerHediffDefs.Count > 0)
            {
                return Props.nonMooGirlPowerHediffDefs;
            }

            return Props.powerhediffDefs;
        }
    }
}
