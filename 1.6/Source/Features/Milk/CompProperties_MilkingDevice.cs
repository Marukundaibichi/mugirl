using System.Collections.Generic;
using Verse;
using Verse.Sound;

namespace Mugirl
{
    // 榨乳器组件配置，仅保存 XML 可调参数；运行状态放在 Comp_MilkingDevice 中。
    public class CompProperties_MilkingDevice : CompProperties
    {
        public CompProperties_MilkingDevice()
        {
            compClass = typeof(Comp_MilkingDevice);
        }

        public int maxCharges = 3;
        public string releaseLabel = "Mugirl.MilkingDevice.ReleaseLabel";
        public string releaseDesc = "Mugirl.MilkingDevice.ReleaseDesc";
        public string releaseIconPath = "UI/Mugirl_Charge";
        public string inspectLabel = "Mugirl.MilkingDevice.InspectLabel";
        public string noChargeText = "Mugirl.MilkingDevice.NoChargeText";
        public string storedMessageKey = "Mugirl.MilkingDevice.StoredMessage";
        public string releaseMessageKey = "Mugirl.MilkingDevice.ReleaseMessage";
        public string storedMoteText = "Mugirl.MilkingDevice.StoredMoteText";
        public string autoReleaseEnabledLabel = "Mugirl.MilkingDevice.AutoReleaseEnabled";
        public string autoReleaseDisabledLabel = "Mugirl.MilkingDevice.AutoReleaseDisabled";
        public string autoReleaseToggleDesc = "Mugirl.MilkingDevice.AutoReleaseDesc";
        public string powerReleaseEnabledLabel = "Mugirl.MilkingDevice.PowerReleaseEnabled";
        public string powerReleaseDisabledLabel = "Mugirl.MilkingDevice.PowerReleaseDisabled";
        public string powerReleaseToggleDesc = "";
        public string releaseThingDefName = "Mugirl_Milk";
        public bool spawnFilthOnRelease = false;
        public string filthDefName = "MugirlMilkStain";
        public IntRange filthCountRange = new IntRange(1, 2);
        public bool spawnFilthInFacingDirection = false;
        public string powerFilthDefName = "MugirlWaterFilth";
        public IntRange powerFilthCountRange = new IntRange(1, 1);
        public List<SoundDef> releaseSounds;
        public List<SoundDef> powerReleaseSounds;
        public int powerReleaseStunTicks = 180;
    }
}
