using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
{
    public partial class Comp_MagneticShackles
    {
        // 手动按钮
        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (var g in base.CompGetWornGizmosExtra())
                yield return g;

            CompProperties_MagneticShackles shackleProps = Props;
            if (shackleProps == null)
            {
                yield break;
            }

            if (parent is AdvancedSlaveApparel slave && slave.IsCracked() && Wearer != null)
            {
                int cdLeft = ManualCooldownTicksLeft(shackleProps);
                bool canUse = cdLeft <= 0;
                float cooldownPercent = ManualCooldownPercent(shackleProps);

                if (!isActive)
                {
                    yield return new Command_ActionWithCooldown
                    {
                        defaultLabel = MooGirlText.Resolve(shackleProps.activateLabel),
                        defaultDesc = canUse ? MooGirlText.Resolve(shackleProps.activateDesc) : MooGirlText.Resolve("MooGirl.Restraints.MagneticShackles.CooldownTicksLeft", cdLeft),
                        icon = GetCommandIcon(shackleProps.activateIconPath),
                        action = () =>
                        {
                            if (!ManualUseReady(Props, out int currentTick)) return;
                            ActivateShackles();
                            lastManualUseTick = currentTick;
                        },
                        Disabled = !canUse,
                        cooldownPercentGetter = () => ManualCooldownPercent(Props, cooldownPercent)
                    };
                }
                else
                {
                    yield return new Command_ActionWithCooldown
                    {
                        defaultLabel = MooGirlText.Resolve(shackleProps.deactivateLabel),
                        defaultDesc = MooGirlText.Resolve(shackleProps.deactivateDesc),
                        icon = GetCommandIcon(shackleProps.deactivateIconPath),
                        action = () =>
                        {
                            if (!ManualUseReady(Props, out int currentTick)) return;
                            DeactivateShackles();
                            lastManualUseTick = currentTick;
                        },
                        Disabled = !canUse,
                        cooldownPercentGetter = () => ManualCooldownPercent(Props, cooldownPercent)
                    };
                }
            }
        }

        private static Texture2D GetCommandIcon(string iconPath)
        {
            return string.IsNullOrEmpty(iconPath) ? TexCommand.DesirePower : ContentFinder<Texture2D>.Get(iconPath, false) ?? TexCommand.DesirePower;
        }

        private bool ManualUseReady(CompProperties_MagneticShackles shackleProps, out int currentTick)
        {
            currentTick = CurrentGameTickOrFallback(lastManualUseTick);
            if (shackleProps == null || !(parent is AdvancedSlaveApparel slave) || !slave.IsCracked() || Wearer == null)
            {
                return false;
            }

            return ManualCooldownTicksLeft(shackleProps, currentTick) <= 0;
        }

        private int ManualCooldownTicksLeft(CompProperties_MagneticShackles shackleProps)
        {
            return ManualCooldownTicksLeft(shackleProps, CurrentGameTickOrFallback(lastManualUseTick));
        }

        private int ManualCooldownTicksLeft(CompProperties_MagneticShackles shackleProps, int currentTick)
        {
            if (shackleProps == null)
            {
                return int.MaxValue;
            }

            int cooldownTicks = Mathf.Max(1, shackleProps.useCooldownTicks);
            return Mathf.Max(0, (lastManualUseTick + cooldownTicks) - currentTick);
        }

        private float ManualCooldownPercent(CompProperties_MagneticShackles shackleProps, float fallback = 0f)
        {
            if (shackleProps == null)
            {
                return Mathf.Clamp01(fallback);
            }

            int cooldownTicks = Mathf.Max(1, shackleProps.useCooldownTicks);
            int cdLeft = ManualCooldownTicksLeft(shackleProps);
            return cdLeft <= 0 ? 1f : Mathf.Clamp01(1f - (float)cdLeft / cooldownTicks);
        }
    }
}
