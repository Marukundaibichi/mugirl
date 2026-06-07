using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MooGirl  // 定义命名空间MooGirl，用于组织相关类
{
    // 定义性能效果属性类，继承自HediffCompProperties，用于配置洗脑效果参数
    public class CompProperties_PerformanceEffect : HediffCompProperties
    {
        // 构造函数，指定关联的组件类
        public CompProperties_PerformanceEffect()
        {
            compClass = typeof(HediffComp_BrainWashingStar);  // 关联到洗脑效果组件
        }

        // 触发洗脑效果的时间阈值（tick）
        public int triggerTicks = 60;
        // 洗脑前的延迟时间（tick）
        public int stunDelayTicks = 10;
        // 洗脑持续时间（tick）
        public int stunDurationTicks = 120;

        // 声音效果列表
        public List<SoundWithParams> sounds;
        // 粒子效果列表
        public List<FleckWithParams> flecks;
        // 文本提示列表
        public List<TextWithParams> texts;

        // 头部显示的Mote（视觉效果）定义及显示时间范围
        public ThingDef moteToShowOnHead;  // 显示的Mote类型
        public int moteShowStartTick = 0;  // 开始显示时间
        public int moteShowEndTick = 999999;  // 结束时间

        // 声音参数内部类
        public class SoundWithParams
        {
            public SoundDef sound;  // 声音定义
            public int startTick = 0;  // 开始时间
            public int loopInterval = 60;  // 循环间隔
            public int endTick = 99999;  // 结束时间
        }

        // 粒子效果参数内部类
        public class FleckWithParams
        {
            public FleckDef fleck;  // 粒子定义
            public int startTick = 0;  // 开始时间
            public int loopInterval = 120;  // 循环间隔
            public int endTick = 99999;  // 结束时间
        }

        // 文本参数内部类
        public class TextWithParams
        {
            public string text;  // 显示文本
            public int startTick = 0;  // 开始时间
            public Color color = Color.white;  // 文本颜色
        }
    }
}
