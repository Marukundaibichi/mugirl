using Mugirl.SaveUpgrade;

namespace Mugirl.Core;

// 类名与 Scribe 标签写入设置文件；改动时同时在存档升级层登记设置升级行（RenameSettingsClass / RenameSetting）。
public class MugirlSettings : ModSettings
{
    internal const float ExtraOutlineDefaultWidth = 1f;
    internal const float ExtraOutlineMinWidth = 0.25f;
    internal const float ExtraOutlineMaxWidth = 3f;

    private int settingsSchema = SaveSchema.SettingsCurrent;

    public bool enableOpeningStoryline = true;

    public bool enableMigrationEvent = true;

    public bool enableFusionInvestmentEvent = true;

    public bool enableRunawayFarmQuest = true;

    public bool enableFastMilking;

    public bool enableVanillaMilkGauge;

    public bool enableExtraOutline;

    public float extraOutlineWidth = ExtraOutlineDefaultWidth;

    // 开发者日志：输出雪牛娘武器轮盘射击诊断和 Harmony 补丁归属报告，用于排查 mod 冲突。
    public bool enableWeaponWheelDevLog;

    // 隐藏武器轮盘普通背负武器（背上的两把）；自驱装弹系统凹槽武器不受影响。
    public bool hideWeaponWheelBackWeapons;

    public override void ExposeData()
    {
        base.ExposeData();

        Scribe_Values.Look(ref settingsSchema, SaveSchema.SettingsStampLabel, 0, forceSave: true);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            settingsSchema = Math.Max(settingsSchema, SaveSchema.SettingsCurrent);
        }

        Scribe_Values.Look(ref enableOpeningStoryline, "enableOpeningStoryline", true);
        Scribe_Values.Look(ref enableMigrationEvent, "enableMigrationEvent", true);
        Scribe_Values.Look(ref enableFusionInvestmentEvent, "enableFusionInvestmentEvent", true);
        Scribe_Values.Look(ref enableRunawayFarmQuest, "enableRunawayFarmQuest", true);
        Scribe_Values.Look(ref enableFastMilking, "enableFastMilking", false);
        Scribe_Values.Look(ref enableVanillaMilkGauge, "enableVanillaMilkGauge", false);
        Scribe_Values.Look(ref enableExtraOutline, "enableExtraOutline", false);
        Scribe_Values.Look(ref extraOutlineWidth, "extraOutlineWidth", ExtraOutlineDefaultWidth);
        if (float.IsNaN(extraOutlineWidth) || float.IsInfinity(extraOutlineWidth))
        {
            extraOutlineWidth = ExtraOutlineDefaultWidth;
        }

        extraOutlineWidth = UnityMathf.Clamp(extraOutlineWidth, ExtraOutlineMinWidth, ExtraOutlineMaxWidth);
        Scribe_Values.Look(ref enableWeaponWheelDevLog, "enableWeaponWheelDevLog", false);
        Scribe_Values.Look(ref hideWeaponWheelBackWeapons, "hideWeaponWheelBackWeapons", false);
    }
}
