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

        public CompProperties_MilkingDevice Props => (CompProperties_MilkingDevice)props;

        public Pawn Wearer => (parent as Apparel)?.Wearer;

        private ThingDef ReleaseThingDef => GetCachedThingDef(Props.releaseThingDefName, ref cachedReleaseThingDefName, ref cachedReleaseThingDef);

        private ThingDef FilthThingDef => GetCachedThingDef(Props.filthDefName, ref cachedFilthThingDefName, ref cachedFilthThingDef);

        public bool CanManageMilkSource(CompMooMilkable milkComp)
        {
            Pawn wearer = Wearer;
            return milkComp != null && wearer != null && milkComp.parent == wearer;
        }

        public bool TryAcceptFullMilk(CompMooMilkable milkComp)
        {
            if (!CanManageMilkSource(milkComp) || !milkComp.IsFullNow)
            {
                return false;
            }

            bool shouldAutoRelease = autoReleaseEnabled;
            if (shouldAutoRelease && storedMilkAmount > 0)
            {
                DoMilkingRelease();
            }

            if (storedCharges >= Props.maxCharges)
            {
                DoMilkingRelease();

                if (storedCharges >= Props.maxCharges)
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
            return "MooGirl.MilkingDevice.Inspect".Translate(
                TranslateProp(Props.inspectLabel),
                storedCharges,
                Props.maxCharges,
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
            if (!ShouldShowReleaseGizmo(wearer))
            {
                yield break;
            }

            bool canRelease = storedCharges > 0 && storedMilkAmount > 0;
            yield return new Command_ActionWithCooldown
            {
                defaultLabel = TranslateProp(Props.releaseLabel),
                defaultDesc = canRelease ? TranslateProp(Props.releaseDesc) : TranslateProp(Props.noChargeText),
                icon = ContentFinder<Texture2D>.Get(Props.releaseIconPath),
                action = DoMilkingRelease,
                Disabled = !canRelease,
                cooldownPercentGetter = () => canRelease ? 1f : 0f
            };

            yield return new Command_Toggle
            {
                defaultLabel = TranslateProp(autoReleaseEnabled ? Props.autoReleaseEnabledLabel : Props.autoReleaseDisabledLabel),
                defaultDesc = TranslateProp(Props.autoReleaseToggleDesc),
                icon = ContentFinder<Texture2D>.Get(Props.releaseIconPath),
                isActive = () => autoReleaseEnabled,
                toggleAction = () => autoReleaseEnabled = !autoReleaseEnabled
            };

            yield return new Command_Toggle
            {
                defaultLabel = TranslateProp(powerReleaseEnabled ? Props.powerReleaseEnabledLabel : Props.powerReleaseDisabledLabel),
                defaultDesc = TranslateProp(Props.powerReleaseToggleDesc),
                icon = ContentFinder<Texture2D>.Get(Props.releaseIconPath),
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
            if (wearer?.Map != null && !string.IsNullOrEmpty(Props.storedMoteText))
            {
                MoteMaker.ThrowText(wearer.DrawPos, wearer.Map, TranslateProp(Props.storedMoteText), Color.cyan, 4f);
            }

            if (wearer != null && !string.IsNullOrEmpty(Props.storedMessageKey))
            {
                Messages.Message(Props.storedMessageKey.Translate(wearer.LabelShortCap, storedCharges, Props.maxCharges), wearer, MessageTypeDefOf.PositiveEvent);
            }
        }

        private void DoMilkingRelease()
        {
            Pawn wearer = Wearer;
            if (wearer == null || wearer.Map == null || storedCharges <= 0 || storedMilkAmount <= 0)
            {
                return;
            }

            ThingDef thingDef = ReleaseThingDef;
            if (thingDef == null)
            {
                Log.Warning("MooGirl.MilkingDevice.ReleaseThingMissing".Translate().ToString());
                return;
            }

            int releasedMilkAmount = storedMilkAmount;
            SpawnReleasedMilk(wearer, thingDef, releasedMilkAmount);
            SpawnReleaseFilth(wearer);
            PlayReleaseSounds(wearer, powerReleaseEnabled);
            parent.TryGetComp<CompMilkingDeviceReleaseEffect>()?.Trigger();

            if (powerReleaseEnabled)
            {
                ApplyPowerReleasePause(wearer);
            }

            if (!string.IsNullOrEmpty(Props.releaseMessageKey))
            {
                Messages.Message(Props.releaseMessageKey.Translate(wearer.LabelShortCap, releasedMilkAmount), wearer, MessageTypeDefOf.PositiveEvent);
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
                Messages.Message("MooGirl.MilkingDevice.DevFill.Failed".Translate(wearer.LabelShortCap), wearer, MessageTypeDefOf.RejectInput);
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

        private void SpawnReleasedMilk(Pawn wearer, ThingDef thingDef, int amount)
        {
            MooGirlMilkOutputUtility.SpawnStacksNear(thingDef, amount, wearer.Position, wearer.Map);
        }

        private void SpawnReleaseFilth(Pawn wearer)
        {
            if (!Props.spawnFilthOnRelease)
            {
                return;
            }

            ThingDef filthThingDef = FilthThingDef;
            if (filthThingDef == null)
            {
                return;
            }

            int filthCount = Props.filthCountRange.RandomInRange;
            for (int i = 0; i < filthCount; i++)
            {
                IntVec3 pos = Props.spawnFilthInFacingDirection && wearer.Rotation != Rot4.Invalid
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

        private void PlayReleaseSounds(Pawn wearer, bool powerMode)
        {
            List<SoundDef> sounds = powerMode ? Props.powerReleaseSounds : Props.releaseSounds;
            if (sounds == null)
            {
                return;
            }

            TargetInfo target = new TargetInfo(wearer.Position, wearer.Map, false);
            for (int i = 0; i < sounds.Count; i++)
            {
                sounds[i]?.PlayOneShot(target);
            }
        }

        private void ApplyPowerReleasePause(Pawn wearer)
        {
            int stunTicks = Mathf.Max(1, Props.powerReleaseStunTicks);
            wearer.stances?.stunner?.StunFor(stunTicks, wearer, false, true, true);
        }
    }
}
