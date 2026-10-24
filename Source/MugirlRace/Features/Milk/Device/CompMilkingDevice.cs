using Verse.Sound;

namespace Mugirl.Features.Milk;

// 榨乳器运行组件：负责储乳状态、穿戴者 Gizmo、释放产物和释放反馈。
public sealed class CompMilkingDevice : ThingComp
{
    private int mugirlDeviceStoredCharges;
    private int mugirlDeviceStoredMilkAmount;
    private bool mugirlDeviceAutoReleaseEnabled;
    private bool mugirlDevicePowerReleaseEnabled;

    public CompProperties_MilkingDevice? Props => props as CompProperties_MilkingDevice;

    public Pawn? Wearer => (parent as Apparel)?.Wearer;

    public bool CanManageMilkSource(CompMilkProduction? milkComp)
    {
        Pawn? wearer = Wearer;
        return milkComp != null && wearer != null && milkComp.parent == wearer;
    }

    public bool TryAcceptFullMilk(CompMilkProduction milkComp)
    {
        CompProperties_MilkingDevice? deviceProps = Props;
        if (deviceProps == null || !CanManageMilkSource(milkComp) || !milkComp.IsFullNow)
        {
            return false;
        }

        bool shouldAutoRelease = mugirlDeviceAutoReleaseEnabled;
        if (shouldAutoRelease && mugirlDeviceStoredMilkAmount > 0)
        {
            DoMilkingRelease();
        }

        int maxCharges = MaxCharges(deviceProps);
        if (mugirlDeviceStoredCharges >= maxCharges)
        {
            DoMilkingRelease();

            if (mugirlDeviceStoredCharges >= maxCharges)
            {
                return false;
            }
        }

        int milkAmount = Math.Max(1, milkComp.GetResourceAmountForCurrentFullness());
        if (!milkComp.TryConsumeFullness())
        {
            return false;
        }

        mugirlDeviceStoredCharges++;
        mugirlDeviceStoredMilkAmount += milkAmount;

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

    public override string? CompInspectStringExtra()
    {
        CompProperties_MilkingDevice? deviceProps = Props;
        if (deviceProps == null)
        {
            return null;
        }

        return "Mugirl.MilkingDevice.Inspect".Translate(
            MugirlText.Resolve(deviceProps.inspectLabel),
            mugirlDeviceStoredCharges,
            MaxCharges(deviceProps),
            mugirlDeviceStoredMilkAmount,
            MilkDefOf.Mugirl_Milk.label);
    }

    public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
    {
        foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
        {
            yield return gizmo;
        }

        CompProperties_MilkingDevice? deviceProps = Props;
        if (deviceProps == null || !ShouldShowReleaseGizmo(Wearer))
        {
            yield break;
        }

        bool canRelease = mugirlDeviceStoredCharges > 0 && mugirlDeviceStoredMilkAmount > 0;
        Texture2D releaseIcon = deviceProps.ReleaseIcon;
        yield return new Command_ActionWithCooldown
        {
            defaultLabel = MugirlText.Resolve(deviceProps.releaseLabel),
            defaultDesc = MugirlText.Resolve(canRelease ? deviceProps.releaseDesc : deviceProps.noChargeText),
            icon = releaseIcon,
            action = DoMilkingRelease,
            Disabled = !canRelease,
            cooldownPercentGetter = () => canRelease ? 1f : 0f,
        };

        yield return new Command_Toggle
        {
            defaultLabel = MugirlText.Resolve(mugirlDeviceAutoReleaseEnabled ? deviceProps.autoReleaseEnabledLabel : deviceProps.autoReleaseDisabledLabel),
            defaultDesc = MugirlText.Resolve(deviceProps.autoReleaseToggleDesc),
            icon = releaseIcon,
            isActive = () => mugirlDeviceAutoReleaseEnabled,
            toggleAction = () => mugirlDeviceAutoReleaseEnabled = !mugirlDeviceAutoReleaseEnabled,
        };

        yield return new Command_Toggle
        {
            defaultLabel = MugirlText.Resolve(mugirlDevicePowerReleaseEnabled ? deviceProps.powerReleaseEnabledLabel : deviceProps.powerReleaseDisabledLabel),
            defaultDesc = MugirlText.Resolve(deviceProps.powerReleaseToggleDesc),
            icon = releaseIcon,
            isActive = () => mugirlDevicePowerReleaseEnabled,
            toggleAction = () => mugirlDevicePowerReleaseEnabled = !mugirlDevicePowerReleaseEnabled,
        };

        if (Prefs.DevMode)
        {
            yield return new Command_Action
            {
                defaultLabel = "Mugirl.MilkingDevice.DevFill.Label".Translate(),
                defaultDesc = "Mugirl.MilkingDevice.DevFill.Desc".Translate(),
                icon = TexCommand.DesirePower,
                action = DevFillMilkResource,
            };
        }
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref mugirlDeviceStoredCharges, "mugirlDeviceStoredCharges", 0);
        Scribe_Values.Look(ref mugirlDeviceStoredMilkAmount, "mugirlDeviceStoredMilkAmount", 0);
        Scribe_Values.Look(ref mugirlDeviceAutoReleaseEnabled, "mugirlDeviceAutoReleaseEnabled", false);
        Scribe_Values.Look(ref mugirlDevicePowerReleaseEnabled, "mugirlDevicePowerReleaseEnabled", false);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            mugirlDeviceStoredCharges = Math.Max(0, mugirlDeviceStoredCharges);
            mugirlDeviceStoredMilkAmount = Math.Max(0, mugirlDeviceStoredMilkAmount);
        }
    }

    private static int MaxCharges(CompProperties_MilkingDevice deviceProps) => Math.Max(1, deviceProps.maxCharges);

    private static bool ShouldShowReleaseGizmo(Pawn? wearer)
    {
        return wearer != null && (wearer.IsColonistPlayerControlled || wearer.IsSlave);
    }

    private void NotifyStoredCharge()
    {
        Pawn? wearer = Wearer;
        CompProperties_MilkingDevice? deviceProps = Props;
        if (deviceProps == null || wearer == null)
        {
            return;
        }

        if (wearer.Map != null && !deviceProps.storedMoteText.NullOrEmpty())
        {
            MoteMaker.ThrowText(wearer.DrawPos, wearer.Map, MugirlText.Resolve(deviceProps.storedMoteText), UnityColor.Cyan, 4f);
        }

        if (!deviceProps.storedMessageKey.NullOrEmpty())
        {
            Messages.Message(
                deviceProps.storedMessageKey.Translate(wearer.LabelShortCap, mugirlDeviceStoredCharges, MaxCharges(deviceProps)),
                wearer,
                MessageTypeDefOf.PositiveEvent);
        }
    }

    private void DoMilkingRelease()
    {
        CompProperties_MilkingDevice? deviceProps = Props;
        Pawn? wearer = Wearer;
        Map? map = wearer?.Map;
        if (deviceProps == null || wearer == null || map == null || mugirlDeviceStoredCharges <= 0 || mugirlDeviceStoredMilkAmount <= 0)
        {
            return;
        }

        ThingDef? thingDef = deviceProps.ReleaseThingDef;
        if (thingDef == null)
        {
            MugirlLog.WarningOnce("MilkingDevice.ReleaseThingMissing", "Mugirl.MilkingDevice.ReleaseThingMissing".Translate().ToString());
            return;
        }

        int releasedMilkAmount = mugirlDeviceStoredMilkAmount;
        MilkOutputUtility.SpawnStacksNear(thingDef, releasedMilkAmount, wearer.Position, map);
        SpawnReleaseFilth(wearer, map, deviceProps);
        PlayReleaseSounds(wearer, map, mugirlDevicePowerReleaseEnabled ? deviceProps.powerReleaseSounds : deviceProps.releaseSounds);

        if (mugirlDevicePowerReleaseEnabled)
        {
            wearer.stances?.stunner?.StunFor(Math.Max(1, deviceProps.powerReleaseStunTicks), wearer, false, true, true);
        }

        if (!deviceProps.releaseMessageKey.NullOrEmpty())
        {
            Messages.Message(deviceProps.releaseMessageKey.Translate(wearer.LabelShortCap, releasedMilkAmount), wearer, MessageTypeDefOf.PositiveEvent);
        }

        mugirlDeviceStoredCharges = 0;
        mugirlDeviceStoredMilkAmount = 0;
    }

    private void DevFillMilkResource()
    {
        Pawn? wearer = Wearer;
        if (wearer == null)
        {
            return;
        }

        CompMilkProduction? milkComp = wearer.TryGetComp<CompMilkProduction>();
        if (milkComp == null || !milkComp.DevFillToFull(triggerNotify: true))
        {
            Messages.Message("Mugirl.MilkingDevice.DevFill.Failed".Translate(wearer.LabelShortCap), wearer, MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        Messages.Message("Mugirl.MilkingDevice.DevFill.Success".Translate(wearer.LabelShortCap), wearer, MessageTypeDefOf.PositiveEvent);
    }

    private static void SpawnReleaseFilth(Pawn wearer, Map map, CompProperties_MilkingDevice deviceProps)
    {
        if (!deviceProps.spawnFilthOnRelease || deviceProps.FilthThingDef is not { } filthThingDef)
        {
            return;
        }

        int filthCount = Math.Max(0, deviceProps.filthCountRange.RandomInRange);
        for (int i = 0; i < filthCount; i++)
        {
            IntVec3 pos = deviceProps.spawnFilthInFacingDirection && wearer.Rotation != Rot4.Invalid
                ? wearer.Position + wearer.Rotation.FacingCell
                : wearer.Position;

            if (!pos.InBounds(map))
            {
                pos = wearer.Position;
            }

            GenPlace.TryPlaceThing(ThingMaker.MakeThing(filthThingDef), pos, map, ThingPlaceMode.Near);
        }
    }

    private static void PlayReleaseSounds(Pawn wearer, Map map, List<SoundDef>? sounds)
    {
        if (sounds == null)
        {
            return;
        }

        TargetInfo target = new(wearer.Position, map, false);
        for (int i = 0; i < sounds.Count; i++)
        {
            sounds[i]?.PlayOneShot(target);
        }
    }
}
