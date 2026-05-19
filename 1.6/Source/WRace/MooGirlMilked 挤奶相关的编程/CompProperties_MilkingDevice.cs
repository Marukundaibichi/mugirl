using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MooGirl
{
    public class CompProperties_MilkingDevice : CompProperties
    {
        public CompProperties_MilkingDevice()
        {
            compClass = typeof(Comp_MilkingDevice);
        }

        public int maxCharges = 3;
        public string releaseLabel = "释放储乳";
        public string releaseDesc = "将设备中储存的雪牛奶一次性释放到地面。";
        public string releaseIconPath = "UI/MooGirl_Charge";
        public string inspectLabel = "储乳";
        public string noChargeText = "当前没有可释放的储乳。";
        public string storedMessageKey = "MooGirl.MilkingDevice.StoredMessage";
        public string releaseMessageKey = "MooGirl.MilkingDevice.ReleaseMessage";
        public string storedMoteText = "储乳 +1";
        public string autoReleaseEnabledLabel = "自动榨乳: 开";
        public string autoReleaseDisabledLabel = "自动榨乳: 关";
        public string autoReleaseToggleDesc = "开启后，妞妞产奶一满就会由榨乳器立刻自动释放。关闭后，只有在储乳已满且再次接收到满乳时，才会按溢出方式自动释放。";
        public string powerReleaseEnabledLabel = "强力释放: 开";
        public string powerReleaseDisabledLabel = "强力释放: 关";
		public string powerReleaseToggleDesc = "";
        public string releaseThingDefName = "MooGirl_Milk";
        public bool spawnFilthOnRelease = false;
        public string filthDefName = "MooGirlMilkFilth";
        public IntRange filthCountRange = new IntRange(1, 2);
        public bool spawnFilthInFacingDirection = false;
        public string powerFilthDefName = "MooGirlWaterFilth";
        public IntRange powerFilthCountRange = new IntRange(1, 1);
        public List<SoundDef> releaseSounds;
        public List<SoundDef> powerReleaseSounds;
        public int powerReleaseStunTicks = 180;
    }

    public class Comp_MilkingDevice : ThingComp
    {
        private int storedCharges;
        private int storedMilkAmount;
        private bool autoReleaseEnabled;
        private bool powerReleaseEnabled;

        public CompProperties_MilkingDevice Props => (CompProperties_MilkingDevice)props;

        public Pawn Wearer => (parent as Apparel)?.Wearer;

        private ThingDef ReleaseThingDef => DefDatabase<ThingDef>.GetNamedSilentFail(Props.releaseThingDefName);

        private ThingDef FilthThingDef => DefDatabase<ThingDef>.GetNamedSilentFail(Props.filthDefName);

        private ThingDef PowerFilthThingDef => DefDatabase<ThingDef>.GetNamedSilentFail(Props.powerFilthDefName);

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
            return Props.inspectLabel + ": " + storedCharges + "/" + Props.maxCharges + " (" + storedMilkAmount + "x " + MooGirl_DefOf.MooGirl_Milk.label + ")";
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
                defaultLabel = Props.releaseLabel,
                defaultDesc = canRelease ? Props.releaseDesc : Props.noChargeText,
                icon = ContentFinder<Texture2D>.Get(Props.releaseIconPath),
                action = () => DoMilkingRelease(),
                Disabled = !canRelease,
                cooldownPercentGetter = () => canRelease ? 1f : 0f
            };

            yield return new Command_Toggle
            {
                defaultLabel = autoReleaseEnabled ? Props.autoReleaseEnabledLabel : Props.autoReleaseDisabledLabel,
                defaultDesc = Props.autoReleaseToggleDesc,
                icon = ContentFinder<Texture2D>.Get(Props.releaseIconPath),
                isActive = () => autoReleaseEnabled,
                toggleAction = () => autoReleaseEnabled = !autoReleaseEnabled
            };

            if (AdultContentUtility.AdultContentEnabled)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = powerReleaseEnabled ? Props.powerReleaseEnabledLabel : Props.powerReleaseDisabledLabel,
                    defaultDesc = Props.powerReleaseToggleDesc,
                    icon = ContentFinder<Texture2D>.Get(Props.releaseIconPath),
                    isActive = () => powerReleaseEnabled,
                    toggleAction = () => powerReleaseEnabled = !powerReleaseEnabled
                };
            }

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dev: 挤奶进度攒满",
                    defaultDesc = "开发者模式下立刻将当前穿戴者的产奶进度充满，并触发榨乳器逻辑。",
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
                MoteMaker.ThrowText(wearer.DrawPos, wearer.Map, Props.storedMoteText, Color.cyan, 4f);
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
                Log.Warning("MooGirl milking device could not resolve release thing def.");
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
                Messages.Message($"{wearer.LabelShortCap} 当前无法开发者充满产奶进度。", wearer, MessageTypeDefOf.RejectInput);
                return;
            }

            Messages.Message($"已将 {wearer.LabelShortCap} 的产奶进度开发者充满。", wearer, MessageTypeDefOf.PositiveEvent);
        }

        private void SpawnReleasedMilk(Pawn wearer, ThingDef thingDef, int amount)
        {
            while (amount > 0)
            {
                int stack = Mathf.Clamp(amount, 1, thingDef.stackLimit);
                amount -= stack;

                Thing thing = ThingMaker.MakeThing(thingDef);
                thing.stackCount = stack;
                GenPlace.TryPlaceThing(thing, wearer.Position, wearer.Map, ThingPlaceMode.Near);
            }
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
