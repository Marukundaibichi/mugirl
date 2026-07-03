using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Mugirl
{
    // 电击项圈配置：控制自动电击、手动按钮和命中的 Hediff 列表。
    public class CompProperties_ShockCollar : CompProperties
    {
        public CompProperties_ShockCollar()
        {
            compClass = typeof(Comp_ShockCollar);
        }

        public List<HediffDef> hediffDefs = new List<HediffDef>();
        public List<HediffDef> powerhediffDefs = new List<HediffDef>();
        public List<HediffDef> nonMugirlPowerHediffDefs = new List<HediffDef>();

        // 自动电击参数：间隔、总触发概率和强力电击概率。
        public int ticks = 60;
        public int rand = 50;
        public int powerShockChance = 30;

        // 手动按钮参数来自 XML，可直接写翻译键或图标路径。
        public int useCooldownTicks = 480;
        public string powerLabel = "Mugirl.Restraints.ShockCollar.PowerLabel";
        public string powerDesc = "Mugirl.Restraints.ShockCollar.PowerDesc";
        public string powerIconPath = "UI/Commands/DesirePower";
        public string commonLabel = "Mugirl.Restraints.ShockCollar.CommonLabel";
        public string commonDesc = "Mugirl.Restraints.ShockCollar.CommonDesc";
        public string commonIconPath = "UI/Commands/DesirePower";

        // 是否只对玩家控制的单位生效（殖民者、囚犯、奴隶），避免访客模组冲突
        public bool playerColonistOnly = true;
    }

    // 电击项圈运行逻辑：未破解时自动随机电击，破解后显示手动控制按钮。
    public class Comp_ShockCollar : Comp_AdvancedSlaveApparel
    {
        private int ticks = 0;
        private int lastManualUseTick = -99999;

        public CompProperties_ShockCollar Props => props as CompProperties_ShockCollar;

        private Pawn GetWearer()
        {
            if (parent?.ParentHolder is Pawn_ApparelTracker tracker)
            {
                return tracker.pawn;
            }
            return null;
        }

        // 默认只影响玩家可控单位，避免访客、盟友或敌对单位被装备 mod 意外波及。
        private bool CanBeShocked(Pawn pawn, CompProperties_ShockCollar shockProps)
        {
            if (pawn == null || shockProps == null)
                return false;

            if (!shockProps.playerColonistOnly)
                return true;

            if (pawn.IsColonistPlayerControlled)
                return true;

            if (pawn.IsPrisonerOfColony)
                return true;

            return false;
        }

        public override void CompTick()
        {
            base.CompTick();

            // 热路径只递减计时器，完整检查延迟到间隔触发时执行。
            ticks--;

            if (ticks <= 0)
            {
                CompProperties_ShockCollar shockProps = Props;
                if (shockProps == null)
                {
                    return;
                }

                ticks = Mathf.Max(1, shockProps.ticks);

                Pawn wearer = GetWearer();

                if (wearer == null || wearer.Dead || !wearer.Spawned)
                    return;

                if (!CanBeShocked(wearer, shockProps))
                    return;

                // ParentIsCracked() 的历史语义是 true = 未破解；只有未破解状态会自动随机电击。
                if (!ParentIsCracked())
                    return;

                if (Rand.Chance(Mathf.Clamp01(shockProps.rand / 100f)))
                {
                    if (Rand.Chance(Mathf.Clamp01(shockProps.powerShockChance / 100f)))
                    {
                        ApplyHediffs(wearer, PowerHediffsFor(wearer, shockProps));
                    }
                    else
                    {
                        ApplyHediffs(wearer, shockProps.hediffDefs);
                    }
                }
            }
        }

        private void ApplyHediffs(Pawn pawn, List<HediffDef> hediffs)
        {
            if (pawn?.health?.hediffSet == null || hediffs == null)
                return;

            var hediffSet = pawn.health.hediffSet;

            foreach (HediffDef hediffDef in hediffs)
            {
                if (hediffDef == null)
                    continue;

                if (!hediffSet.HasHediff(hediffDef))
                {
                    pawn.health.AddHediff(hediffDef);
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticks, "ticks", 0);
            Scribe_Values.Look(ref lastManualUseTick, "lastManualUseTick", -99999);
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (var gizmo in base.CompGetWornGizmosExtra())
                yield return gizmo;

            Pawn wearer = GetWearer();
            CompProperties_ShockCollar shockProps = Props;

            if (wearer == null || shockProps == null)
                yield break;

            if (!CanBeShocked(wearer, shockProps))
                yield break;

            // 手动控制只在已破解状态显示，自动电击与手动控制互斥。
            if (ParentIsCracked())
                yield break;

            int cooldownTicks = Mathf.Max(1, shockProps.useCooldownTicks);
            int currentTick = CurrentGameTickOrFallback(lastManualUseTick);
            int cdLeft = Mathf.Max(0, (lastManualUseTick + cooldownTicks) - currentTick);
            bool canUse = cdLeft <= 0;
            float cooldownPercent = canUse ? 1f : Mathf.Clamp01(1f - (float)cdLeft / cooldownTicks);

            // 描述文本在生成 Gizmo 时计算一次；点击时仍会重新校验冷却和佩戴者。
            string powerDesc = canUse
                ? MugirlText.Resolve(shockProps.powerDesc)
                : MugirlText.Resolve("Mugirl.Restraints.ShockCollar.PowerCooldownTicksLeft", cdLeft);

            string commonDesc = canUse
                ? MugirlText.Resolve(shockProps.commonDesc)
                : MugirlText.Resolve("Mugirl.Restraints.ShockCollar.CommonCooldownTicksLeft", cdLeft);

            yield return CreateShockCommand(
                MugirlText.Resolve(shockProps.powerLabel),
                powerDesc,
                shockProps.powerIconPath,
                canUse,
                cooldownPercent,
                (currentWearer, currentProps) => ApplyHediffs(currentWearer, PowerHediffsFor(currentWearer, currentProps))
            );

            yield return CreateShockCommand(
                MugirlText.Resolve(shockProps.commonLabel),
                commonDesc,
                shockProps.commonIconPath,
                canUse,
                cooldownPercent,
                (currentWearer, currentProps) => ApplyHediffs(currentWearer, currentProps.hediffDefs)
            );
        }

        private Command_ActionWithCooldown CreateShockCommand(
            string label,
            string desc,
            string iconPath,
            bool enabled,
            float cooldownPercent,
            System.Action<Pawn, CompProperties_ShockCollar> applyAction)
        {
            return new Command_ActionWithCooldown
            {
                defaultLabel = label,
                defaultDesc = desc,
                icon = GetCommandIcon(iconPath),
                action = () =>
                {
                    // 点击执行时重新检查佩戴者、破解状态和冷却，避免旧 Gizmo 操作过期对象。
                    Pawn currentWearer = GetWearer();
                    CompProperties_ShockCollar shockProps = Props;
                    if (currentWearer != null && CanBeShocked(currentWearer, shockProps) && !ParentIsCracked() && ManualUseReady(shockProps, out int currentTick))
                    {
                        applyAction(currentWearer, shockProps);
                        lastManualUseTick = currentTick;
                    }
                },
                Disabled = !enabled,
                cooldownPercentGetter = () => ManualCooldownPercent(Props, cooldownPercent)
            };
        }

        private List<HediffDef> PowerHediffsFor(Pawn pawn, CompProperties_ShockCollar shockProps)
        {
            if (pawn != null && !MountedPawnUtility.IsMugirl(pawn) && shockProps?.nonMugirlPowerHediffDefs != null && shockProps.nonMugirlPowerHediffDefs.Count > 0)
            {
                return shockProps.nonMugirlPowerHediffDefs;
            }

            return shockProps?.powerhediffDefs;
        }

        private static int CurrentGameTickOrFallback(int fallback)
        {
            return MugirlTickUtility.CurrentGameTickOrFallback(fallback);
        }

        private static Texture2D GetCommandIcon(string iconPath)
        {
            return string.IsNullOrEmpty(iconPath) ? TexCommand.DesirePower : ContentFinder<Texture2D>.Get(iconPath, false) ?? TexCommand.DesirePower;
        }

        private bool ManualUseReady(CompProperties_ShockCollar shockProps, out int currentTick)
        {
            currentTick = CurrentGameTickOrFallback(lastManualUseTick);
            if (shockProps == null)
            {
                return false;
            }

            int cooldownTicks = Mathf.Max(1, shockProps.useCooldownTicks);
            return currentTick >= lastManualUseTick + cooldownTicks;
        }

        private float ManualCooldownPercent(CompProperties_ShockCollar shockProps, float fallback)
        {
            if (shockProps == null)
            {
                return Mathf.Clamp01(fallback);
            }

            int cooldownTicks = Mathf.Max(1, shockProps.useCooldownTicks);
            int currentTick = CurrentGameTickOrFallback(lastManualUseTick);
            int cdLeft = Mathf.Max(0, (lastManualUseTick + cooldownTicks) - currentTick);
            return cdLeft <= 0 ? 1f : Mathf.Clamp01(1f - (float)cdLeft / cooldownTicks);
        }
    }
}
