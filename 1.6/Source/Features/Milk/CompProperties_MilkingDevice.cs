using System.Collections.Generic;
using Verse;
using Verse.Sound;

namespace MooGirl
{
    // 榨乳器组件配置，仅保存 XML 可调参数；运行状态放在 Comp_MilkingDevice 中。
    public class CompProperties_MilkingDevice : CompProperties
    {
        public CompProperties_MilkingDevice()
        {
            compClass = typeof(Comp_MilkingDevice);
        }

        public int maxCharges = 3;
        public string releaseLabel = "MooGirl.MilkingDevice.ReleaseLabel";
        public string releaseDesc = "MooGirl.MilkingDevice.ReleaseDesc";
        public string releaseIconPath = "UI/MooGirl_Charge";
        public string inspectLabel = "MooGirl.MilkingDevice.InspectLabel";
        public string noChargeText = "MooGirl.MilkingDevice.NoChargeText";
        public string storedMessageKey = "MooGirl.MilkingDevice.StoredMessage";
        public string releaseMessageKey = "MooGirl.MilkingDevice.ReleaseMessage";
        public string storedMoteText = "MooGirl.MilkingDevice.StoredMoteText";
        public string autoReleaseEnabledLabel = "MooGirl.MilkingDevice.AutoReleaseEnabled";
        public string autoReleaseDisabledLabel = "MooGirl.MilkingDevice.AutoReleaseDisabled";
        public string autoReleaseToggleDesc = "MooGirl.MilkingDevice.AutoReleaseDesc";
        public string powerReleaseEnabledLabel = "MooGirl.MilkingDevice.PowerReleaseEnabled";
        public string powerReleaseDisabledLabel = "MooGirl.MilkingDevice.PowerReleaseDisabled";
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
}
