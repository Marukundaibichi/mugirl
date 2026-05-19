using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MooGirl
{
    [StaticConstructorOnStartup]
    public class MooGirlMod : Mod
    {
        public MooGirlMod(ModContentPack modContentPack) : base(modContentPack)
        {
            harmony = new Harmony("MooGirlMod.Mod");
            harmony.PatchAll();
            settings = GetSettings<MechanoidWorkControlSettings>();
        }

        public static List<SlaveApparelDef> allArmorDefs = new List<SlaveApparelDef>();

        public static Harmony harmony;

        public static MechanoidWorkControlSettings settings;

        // 构造函数
        //public MooGirlMod(ModContentPack content) : base(content)
        //{
        //    settings = GetSettings<MechanoidWorkControlSettings>();

        //}

        public override string SettingsCategory()
        {
            return "牛牛模组控制";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            settings.DoWindowContents(inRect);
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);

            listingStandard.Gap(12f);

            // 添加事件开关选项
            listingStandard.CheckboxLabeled("启用牛牛坠机事件", ref settings.enableStructuralCrashEvent,
                "控制是否启用牛牛坠机事件。取消勾选将禁用该事件的触发。");

            listingStandard.Gap(12f);
            bool oldAdultContent = settings.enableAdultContent;
            bool enableAdultContent = settings.enableAdultContent;
            listingStandard.CheckboxLabeled("启用18禁内容", ref enableAdultContent,
                "关闭后，18禁服装与道具不会继续生成，地图、商队、商人库存以及角色身上的相关物品会被直接清除。");
            settings.enableAdultContent = enableAdultContent;
            if (oldAdultContent && !enableAdultContent)
            {
                AdultContentUtility.CleanupAllAdultContent(removeWornApparel: true);
            }
            listingStandard.End();
            // 确保设置保存
            base.DoSettingsWindowContents(inRect);

        }

    }
    // 设置类
    public class MechanoidWorkControlSettings : ModSettings
    {

        public override void ExposeData()
        {
            base.ExposeData();

            // 保存和加载牛牛坠机事件是否启用 的值
            Scribe_Values.Look(ref enableStructuralCrashEvent, "enableStructuralCrashEvent", true);
            Scribe_Values.Look(ref enableAdultContent, "enableAdultContent", false);
        }

        public void DoWindowContents(Rect rect)
        {
            // 全局设置
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(rect);
            listingStandard.End();
        }
        // 控制牛牛坠机事件事件是否启用的开关
        public bool enableStructuralCrashEvent = true;

        public bool enableAdultContent = false;
    }
}
