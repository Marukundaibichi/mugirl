using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MooGirl
{
    // 榨乳器运行组件：负责储乳状态、穿戴者 Gizmo、释放产物和释放反馈。
    public class Comp_MilkingDevice : ThingComp
    {
        private int storedCharges;
        private int storedMilkAmount;
        private bool autoReleaseEnabled;
        private bool powerReleaseEnabled;
        private string cachedReleaseThingDefName;
        private ThingDef cachedReleaseThingDef;
        private string cachedFilthThingDefName;
        private ThingDef cachedFilthThingDef;

        public CompProperties_MilkingDevice Props => props as CompProperties_MilkingDevice;

        public Pawn Wearer => (parent as Apparel)?.Wearer;

        public bool CanManageMilkSource(CompMooMilkable milkComp)
        {
            Pawn wearer = Wearer;
            return milkComp != null && wearer != null && milkComp.parent == wearer;
        }

        public bool TryAcceptFullMilk(CompMooMilkable milkComp)
        {
            CompProperties_MilkingDevice deviceProps = Props;
            if (deviceProps == null || !CanManageMilkSource(milkComp) || !milkComp.IsFullNow)
            {
                return false;
            }

            bool shouldAutoRelease = autoReleaseEnabled;
            if (shouldAutoRelease && storedMilkAmount > 0)
            {
                DoMilkingRelease();
            }

            int maxCharges = Mathf.Max(1, deviceProps.maxCharges);
            if (storedCharges >= maxCharges)
            {
                DoMilkingRelease();

                if (storedCharges >= maxCharges)
                {
                    return false;
                }
            }

            int milkAmount = Mathf.Max(1, milkComp.GetResourceAmountForCurrentFullness());
            if (!milkComp.TryConsumeFullness())
            {
                return false;
            }

            storedCharges++;
            storedMilkAmount += milkAmount;

            if (shouldAutoRelease)
            {
                DoMilkingRelease();
            }
            else
            {
                NotifyStoredCharge();
            }

            return true;
        }

        public override string CompInspectStringExtra()
        {
            CompProperties_MilkingDevice deviceProps = Props;
            if (deviceProps == null)
            {
                return null;
            }

            return "MooGirl.MilkingDevice.Inspect".Translate(
                TranslateProp(deviceProps.inspectLabel),
                storedCharges,
                Mathf.Max(1, deviceProps.maxCharges),
                storedMilkAmount,
                MooGirl_DefOf.MooGirl_Milk.label);
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn wearer = Wearer;
            CompProperties_MilkingDevice deviceProps = Props;
            if (deviceProps == null || !ShouldShowReleaseGizmo(wearer))
            {
                yield break;
            }

            bool canRelease = storedCharges > 0 && storedMilkAmount > 0;
            Texture2D releaseIcon = GetCommandIcon(deviceProps.releaseIconPath);
            yield return new Command_ActionWithCooldown
            {
                defaultLabel = TranslateProp(deviceProps.releaseLabel),
                defaultDesc = canRelease ? TranslateProp(deviceProps.releaseDesc) : TranslateProp(deviceProps.noChargeText),
                icon = releaseIcon,
                action = DoMilkingRelease,
                Disabled = !canRelease,
                cooldownPercentGetter = () => canRelease ? 1f : 0f
            };

            yield return new Command_Toggle
            {
                defaultLabel = TranslateProp(autoReleaseEnabled ? deviceProps.autoReleaseEnabledLabel : deviceProps.autoReleaseDisabledLabel),
                defaultDesc = TranslateProp(deviceProps.autoReleaseToggleDesc),
                icon = releaseIcon,
                isActive = () => autoReleaseEnabled,
                toggleAction = () => autoReleaseEnabled = !autoReleaseEnabled
            };

            yield return new Command_Toggle
            {
                defaultLabel = TranslateProp(powerReleaseEnabled ? deviceProps.powerReleaseEnabledLabel : deviceProps.powerReleaseDisabledLabel),
                defaultDesc = TranslateProp(deviceProps.powerReleaseToggleDesc),
                icon = releaseIcon,
                isActive = () => powerReleaseEnabled,
                toggleAction = () => powerReleaseEnabled = !powerReleaseEnabled
            };

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "MooGirl.MilkingDevice.DevFill.Label".Translate(),
                    defaultDesc = "MooGirl.MilkingDevice.DevFill.Desc".Translate(),
                    icon = TexCommand.DesirePower,
                    action = DevFillMilkResource
                };
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref storedCharges, "storedCharges", 0);
            Scribe_Values.Look(ref storedMilkAmount, "storedMilkAmount", 0);
            Scribe_Values.Look(ref autoReleaseEnabled, "autoReleaseEnabled", false);
            Scribe_Values.Look(ref powerReleaseEnabled, "powerReleaseEnabled", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                storedCharges = Mathf.Max(0, storedCharges);
                storedMilkAmount = Mathf.Max(0, storedMilkAmount);
            }
        }

        private bool ShouldShowReleaseGizmo(Pawn wearer)
        {
            if (wearer == null)
            {
                return false;
            }

            return wearer.IsColonistPlayerControlled || wearer.IsSlave;
        }

        private void NotifyStoredCharge()
        {
            Pawn wearer = Wearer;
            CompProperties_MilkingDevice deviceProps = Props;
            if (deviceProps == null)
            {
                return;
            }

            if (wearer?.Map != null && !string.IsNullOrEmpty(deviceProps.storedMoteText))
            {
                MoteMaker.ThrowText(wearer.DrawPos, wearer.Map, TranslateProp(deviceProps.storedMoteText), Color.cyan, 4f);
            }

            if (wearer != null && !string.IsNullOrEmpty(deviceProps.storedMessageKey))
            {
                Messages.Message(deviceProps.storedMessageKey.Translate(wearer.LabelShortCap, storedCharges, Mathf.Max(1, deviceProps.maxCharges)), wearer, MessageTypeDefOf.PositiveEvent);
            }
        }

        private void DoMilkingRelease()
        {
            CompProperties_MilkingDevice deviceProps = Props;
            Pawn wearer = Wearer;
            if (deviceProps == null || wearer == null || wearer.Map == null || storedCharges <= 0 || storedMilkAmount <= 0)
            {
                return;
            }

            ThingDef thingDef = GetReleaseThingDef(deviceProps);
            if (thingDef == null)
            {
                MooGirlLog.WarningOnce("MilkingDevice.ReleaseThingMissing", "MooGirl.MilkingDevice.ReleaseThingMissing".Translate().ToString());
                return;
            }

            int releasedMilkAmount = storedMilkAmount;
            SpawnReleasedMilk(wearer, thingDef, releasedMilkAmount);
            SpawnReleaseFilth(wearer, deviceProps);
            PlayReleaseSounds(wearer, powerReleaseEnabled, deviceProps);
            parent.TryGetComp<CompMilkingDeviceReleaseEffect>()?.Trigger();

            if (powerReleaseEnabled)
            {
                ApplyPowerReleasePause(wearer, deviceProps);
            }

            if (!string.IsNullOrEmpty(deviceProps.releaseMessageKey))
            {
                Messages.Message(deviceProps.releaseMessageKey.Translate(wearer.LabelShortCap, releasedMilkAmount), wearer, MessageTypeDefOf.PositiveEvent);
            }

            storedCharges = 0;
            storedMilkAmount = 0;
        }

        private void DevFillMilkResource()
        {
            Pawn wearer = Wearer;
            if (wearer == null)
            {
                return;
            }

            CompMooMilkable milkComp = wearer.TryGetComp<CompMooMilkable>();
            if (milkComp == null || !milkComp.DevFillToFull(triggerNotify: true))
            {
                Messages.Message("MooGirl.MilkingDevice.DevFill.Failed".Translate(wearer.LabelShortCap), wearer, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Messages.Message("MooGirl.MilkingDevice.DevFill.Success".Translate(wearer.LabelShortCap), wearer, MessageTypeDefOf.PositiveEvent);
        }

        private static string TranslateProp(string textOrKey)
        {
            return MooGirlText.Resolve(textOrKey);
        }

        private static ThingDef GetCachedThingDef(string defName, ref string cachedDefName, ref ThingDef cachedDef)
        {
            if (cachedDefName != defName)
            {
                cachedDefName = defName;
                cachedDef = string.IsNullOrEmpty(defName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            }

            return cachedDef;
        }

        private static Texture2D GetCommandIcon(string iconPath)
        {
            return string.IsNullOrEmpty(iconPath) ? TexCommand.DesirePower : ContentFinder<Texture2D>.Get(iconPath, false) ?? TexCommand.DesirePower;
        }

        private void SpawnReleasedMilk(Pawn wearer, ThingDef thingDef, int amount)
        {
            MooGirlMilkOutputUtility.SpawnStacksNear(thingDef, amount, wearer.Position, wearer.Map);
        }

        private void SpawnReleaseFilth(Pawn wearer, CompProperties_MilkingDevice deviceProps)
        {
            if (wearer?.Map == null || deviceProps == null || !deviceProps.spawnFilthOnRelease)
            {
                return;
            }

            ThingDef filthThingDef = GetFilthThingDef(deviceProps);
            if (filthThingDef == null)
            {
                return;
            }

            int filthCount = Mathf.Max(0, deviceProps.filthCountRange.RandomInRange);
            for (int i = 0; i < filthCount; i++)
            {
                IntVec3 pos = deviceProps.spawnFilthInFacingDirection && wearer.Rotation != Rot4.Invalid
                    ? wearer.Position + wearer.Rotation.FacingCell
                    : wearer.Position;

                if (!pos.InBounds(wearer.Map))
                {
                    pos = wearer.Position;
                }

                Thing filth = ThingMaker.MakeThing(filthThingDef);
                GenPlace.TryPlaceThing(filth, pos, wearer.Map, ThingPlaceMode.Near);
            }
        }

        private void PlayReleaseSounds(Pawn wearer, bool powerMode, CompProperties_MilkingDevice deviceProps)
        {
            List<SoundDef> sounds = powerMode ? deviceProps?.powerReleaseSounds : deviceProps?.releaseSounds;
            if (wearer?.Map == null || sounds == null)
            {
                return;
            }

            TargetInfo target = new TargetInfo(wearer.Position, wearer.Map, false);
            for (int i = 0; i < sounds.Count; i++)
            {
                sounds[i]?.PlayOneShot(target);
            }
        }

        private void ApplyPowerReleasePause(Pawn wearer, CompProperties_MilkingDevice deviceProps)
        {
            int stunTicks = Mathf.Max(1, deviceProps.powerReleaseStunTicks);
            wearer.stances?.stunner?.StunFor(stunTicks, wearer, false, true, true);
        }

        private ThingDef GetReleaseThingDef(CompProperties_MilkingDevice deviceProps)
        {
            return GetCachedThingDef(deviceProps?.releaseThingDefName, ref cachedReleaseThingDefName, ref cachedReleaseThingDef);
        }

        private ThingDef GetFilthThingDef(CompProperties_MilkingDevice deviceProps)
        {
            return GetCachedThingDef(deviceProps?.filthDefName, ref cachedFilthThingDefName, ref cachedFilthThingDef);
        }
    }
}
