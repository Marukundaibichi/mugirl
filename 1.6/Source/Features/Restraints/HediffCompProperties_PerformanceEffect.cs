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
        public int moteShowEndTick = 999999;  // 结束显示时间

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

    public class BrainwashPerformancePlayer : IExposable
    {
        private int age = 0;
        private int stunTickTarget = -1;
        private bool stunned = false;
        private List<int> nextTextShowTicks = new List<int>();
        private List<int> nextSoundTicks = new List<int>();
        private List<int> nextFleckTicks = new List<int>();
        private int stunEndTick = -1;
        private bool stunMessageSent = false;
        private Mote attachedMote;
        private bool active = false;

        public bool Active => active;

        public void Start(CompProperties_PerformanceEffect props)
        {
            Stop();

            if (props == null)
                return;

            active = true;
            age = 0;
            stunTickTarget = Mathf.Max(0, props.triggerTicks) + Mathf.Max(0, props.stunDelayTicks);
            stunned = false;
            stunEndTick = -1;
            stunMessageSent = false;

            nextTextShowTicks = new List<int>();
            if (props.texts != null)
            {
                for (int i = 0; i < props.texts.Count; i++)
                {
                    CompProperties_PerformanceEffect.TextWithParams textParams = props.texts[i];
                    nextTextShowTicks.Add(textParams == null ? -1 : Mathf.Max(0, textParams.startTick));
                }
            }

            nextSoundTicks = new List<int>();
            if (props.sounds != null)
            {
                for (int i = 0; i < props.sounds.Count; i++)
                {
                    CompProperties_PerformanceEffect.SoundWithParams soundParams = props.sounds[i];
                    nextSoundTicks.Add(soundParams == null ? -1 : Mathf.Max(0, soundParams.startTick));
                }
            }

            nextFleckTicks = new List<int>();
            if (props.flecks != null)
            {
                for (int i = 0; i < props.flecks.Count; i++)
                {
                    CompProperties_PerformanceEffect.FleckWithParams fleckParams = props.flecks[i];
                    nextFleckTicks.Add(fleckParams == null ? -1 : Mathf.Max(0, fleckParams.startTick));
                }
            }
        }

        public bool Tick(Pawn pawn, CompProperties_PerformanceEffect props, bool sendMessages = true)
        {
            if (!active)
                return false;

            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null || props == null)
            {
                Stop();
                return true;
            }

            EnsureTimingLists(props);

            age++;

            TickTexts(pawn, props);
            TickSounds(pawn, props);
            TickFlecks(pawn, props);
            TickStun(pawn, props, sendMessages);
            TickAttachedMote(pawn, props);

            if (age >= GetCompletionTick(props))
            {
                Stop();
                return true;
            }

            return false;
        }

        public void Stop()
        {
            active = false;
            DestroyAttachedMote();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref age, "age", 0);
            Scribe_Values.Look(ref stunTickTarget, "stunTickTarget", -1);
            Scribe_Values.Look(ref stunned, "stunned", false);
            Scribe_Values.Look(ref stunEndTick, "stunEndTick", -1);
            Scribe_Values.Look(ref stunMessageSent, "stunMessageSent", false);
            Scribe_Collections.Look(ref nextTextShowTicks, "nextTextShowTicks", LookMode.Value);
            Scribe_Collections.Look(ref nextSoundTicks, "nextSoundTicks", LookMode.Value);
            Scribe_Collections.Look(ref nextFleckTicks, "nextFleckTicks", LookMode.Value);
            Scribe_Values.Look(ref active, "active", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (nextTextShowTicks == null) nextTextShowTicks = new List<int>();
                if (nextSoundTicks == null) nextSoundTicks = new List<int>();
                if (nextFleckTicks == null) nextFleckTicks = new List<int>();
            }
        }

        private void EnsureTimingLists(CompProperties_PerformanceEffect props)
        {
            if (nextTextShowTicks == null) nextTextShowTicks = new List<int>();
            if (nextSoundTicks == null) nextSoundTicks = new List<int>();
            if (nextFleckTicks == null) nextFleckTicks = new List<int>();

            if (props.texts != null)
            {
                while (nextTextShowTicks.Count < props.texts.Count)
                {
                    int index = nextTextShowTicks.Count;
                    CompProperties_PerformanceEffect.TextWithParams textParams = props.texts[index];
                    int startTick = textParams == null ? -1 : Mathf.Max(0, textParams.startTick);
                    nextTextShowTicks.Add(startTick >= age ? startTick : -1);
                }
            }

            if (props.sounds != null)
            {
                while (nextSoundTicks.Count < props.sounds.Count)
                {
                    int index = nextSoundTicks.Count;
                    CompProperties_PerformanceEffect.SoundWithParams soundParams = props.sounds[index];
                    nextSoundTicks.Add(soundParams == null ? -1 : Mathf.Max(age, soundParams.startTick));
                }
            }

            if (props.flecks != null)
            {
                while (nextFleckTicks.Count < props.flecks.Count)
                {
                    int index = nextFleckTicks.Count;
                    CompProperties_PerformanceEffect.FleckWithParams fleckParams = props.flecks[index];
                    nextFleckTicks.Add(fleckParams == null ? -1 : Mathf.Max(age, fleckParams.startTick));
                }
            }
        }

        private void TickTexts(Pawn pawn, CompProperties_PerformanceEffect props)
        {
            if (props.texts == null)
                return;

            for (int i = 0; i < props.texts.Count; i++)
            {
                if (i < nextTextShowTicks.Count && nextTextShowTicks[i] != -1 && age >= nextTextShowTicks[i])
                {
                    var textParams = props.texts[i];
                    if (textParams != null && !string.IsNullOrEmpty(textParams.text))
                    {
                        MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, textParams.text, textParams.color);
                    }
                    nextTextShowTicks[i] = -1;
                }
            }
        }

        private void TickSounds(Pawn pawn, CompProperties_PerformanceEffect props)
        {
            if (props.sounds == null)
                return;

            for (int i = 0; i < props.sounds.Count; i++)
            {
                if (i >= nextSoundTicks.Count)
                    continue;

                var s = props.sounds[i];
                if (s == null || nextSoundTicks[i] == -1)
                    continue;

                int startTick = Mathf.Max(0, s.startTick);
                int endTick = Mathf.Max(startTick, s.endTick);
                if (age >= startTick && age <= endTick && age >= nextSoundTicks[i])
                {
                    SoundInfo info = SoundInfo.InMap(new TargetInfo(pawn.Position, pawn.Map));
                    s.sound?.PlayOneShot(info);
                    nextSoundTicks[i] = age + Mathf.Max(1, s.loopInterval);
                }
            }
        }

        private void TickFlecks(Pawn pawn, CompProperties_PerformanceEffect props)
        {
            if (props.flecks == null)
                return;

            for (int i = 0; i < props.flecks.Count; i++)
            {
                if (i >= nextFleckTicks.Count)
                    continue;

                var f = props.flecks[i];
                if (f == null || f.fleck == null || nextFleckTicks[i] == -1)
                    continue;

                int startTick = Mathf.Max(0, f.startTick);
                int endTick = Mathf.Max(startTick, f.endTick);
                if (age >= startTick && age <= endTick && age >= nextFleckTicks[i])
                {
                    IntVec3 offset = new IntVec3(Rand.RangeInclusive(-1, 1), 0, Rand.RangeInclusive(0, 1));
                    IntVec3 pos = pawn.Position + offset;
                    if (pos.InBounds(pawn.Map))
                    {
                        var data = FleckMaker.GetDataStatic(pos.ToVector3Shifted(), pawn.Map, f.fleck, 0.42f);
                        data.velocityAngle = 0f;
                        data.velocitySpeed = Rand.Range(0.2f, 0.5f);
                        pawn.Map.flecks.CreateFleck(data);
                    }
                    nextFleckTicks[i] = age + Mathf.Max(1, f.loopInterval);
                }
            }
        }

        private void TickStun(Pawn pawn, CompProperties_PerformanceEffect props, bool sendMessages)
        {
            if (!stunned && age >= stunTickTarget)
            {
                stunned = true;
                int stunDurationTicks = Mathf.Max(0, props.stunDurationTicks);
                stunEndTick = age + stunDurationTicks;
                if (sendMessages)
                {
                    Messages.Message("MooGirl.BrainwashStart".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.NegativeEvent);
                }
                if (stunDurationTicks > 0)
                {
                    pawn.stances?.stunner?.StunFor(stunDurationTicks, null, false, false, true);
                }
            }

            if (stunned && !stunMessageSent && age >= stunEndTick)
            {
                stunMessageSent = true;
                if (sendMessages)
                {
                    Messages.Message("MooGirl.BrainwashEnd".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.PositiveEvent);
                }
            }
        }

        private void TickAttachedMote(Pawn pawn, CompProperties_PerformanceEffect props)
        {
            bool shouldShowMote = props.moteToShowOnHead != null
                && age >= props.moteShowStartTick
                && age <= props.moteShowEndTick;

            if (!shouldShowMote)
            {
                DestroyAttachedMote();
                return;
            }

            if (attachedMote == null || attachedMote.Destroyed)
            {
                Mote mote = ThingMaker.MakeThing(props.moteToShowOnHead, null) as Mote;
                if (mote == null)
                    return;

                mote.Attach(pawn, new Vector3(0f, 0f, 0.5f), false);
                GenSpawn.Spawn(mote, pawn.Position, pawn.Map, Rot4.North, WipeMode.Vanish, false, false);
                attachedMote = mote;
            }
            else
            {
                attachedMote.Maintain();
            }
        }

        private int GetCompletionTick(CompProperties_PerformanceEffect props)
        {
            int completionTick = Mathf.Max(1, props.triggerTicks + props.stunDelayTicks + props.stunDurationTicks);

            if (props.moteToShowOnHead != null)
            {
                completionTick = Mathf.Max(completionTick, props.moteShowEndTick);
            }

            if (props.sounds != null)
            {
                for (int i = 0; i < props.sounds.Count; i++)
                {
                    CompProperties_PerformanceEffect.SoundWithParams soundParams = props.sounds[i];
                    if (soundParams != null)
                    {
                        completionTick = Mathf.Max(completionTick, soundParams.endTick);
                    }
                }
            }

            if (props.flecks != null)
            {
                for (int i = 0; i < props.flecks.Count; i++)
                {
                    CompProperties_PerformanceEffect.FleckWithParams fleckParams = props.flecks[i];
                    if (fleckParams != null)
                    {
                        completionTick = Mathf.Max(completionTick, fleckParams.endTick);
                    }
                }
            }

            if (props.texts != null)
            {
                for (int i = 0; i < props.texts.Count; i++)
                {
                    CompProperties_PerformanceEffect.TextWithParams textParams = props.texts[i];
                    if (textParams != null)
                    {
                        completionTick = Mathf.Max(completionTick, textParams.startTick);
                    }
                }
            }

            return completionTick;
        }

        private void DestroyAttachedMote()
        {
            if (attachedMote != null && !attachedMote.Destroyed)
            {
                attachedMote.Destroy(DestroyMode.Vanish);
            }
            attachedMote = null;
        }
    }

    public class GameComponent_BrainwashPerformance : GameComponent
    {
        private List<ActiveBrainwashPerformance> activePerformances = new List<ActiveBrainwashPerformance>();

        public GameComponent_BrainwashPerformance()
        {
        }

        public GameComponent_BrainwashPerformance(Game game)
        {
        }

        public static void StartFor(Pawn pawn, HediffDef sourceHediffDef)
        {
            if (pawn == null || sourceHediffDef == null)
                return;

            if (!MooGirlGameUtility.TryGetGameComponent(out GameComponent_BrainwashPerformance comp))
            {
                MooGirlLog.WarningOnce(
                    "BrainwashPerformance.GameComponentMissing",
                    "Tried to start a MooGirl brainwash performance while its GameComponent was unavailable.");
                return;
            }

            comp.StartPerformance(pawn, sourceHediffDef);
        }

        public override void GameComponentTick()
        {
            if (activePerformances == null || activePerformances.Count == 0)
                return;

            for (int i = activePerformances.Count - 1; i >= 0; i--)
            {
                ActiveBrainwashPerformance performance = activePerformances[i];
                if (performance == null || performance.Tick())
                {
                    activePerformances.RemoveAt(i);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref activePerformances, "activeBrainwashPerformances", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && activePerformances == null)
            {
                activePerformances = new List<ActiveBrainwashPerformance>();
            }
        }

        private void StartPerformance(Pawn pawn, HediffDef sourceHediffDef)
        {
            CompProperties_PerformanceEffect props = GetPerformanceProps(sourceHediffDef);
            if (props == null)
                return;

            if (activePerformances == null)
            {
                activePerformances = new List<ActiveBrainwashPerformance>();
            }

            for (int i = activePerformances.Count - 1; i >= 0; i--)
            {
                if (activePerformances[i]?.Pawn == pawn)
                {
                    activePerformances[i].Stop();
                    activePerformances.RemoveAt(i);
                }
            }

            ActiveBrainwashPerformance performance = new ActiveBrainwashPerformance();
            performance.Start(pawn, sourceHediffDef, props);
            activePerformances.Add(performance);
        }

        private static CompProperties_PerformanceEffect GetPerformanceProps(HediffDef hediffDef)
        {
            if (hediffDef?.comps == null)
                return null;

            foreach (HediffCompProperties compProps in hediffDef.comps)
            {
                if (compProps is CompProperties_PerformanceEffect performanceProps)
                {
                    return performanceProps;
                }
            }

            return null;
        }

        public class ActiveBrainwashPerformance : IExposable
        {
            private Pawn pawn;
            private HediffDef sourceHediffDef;
            private BrainwashPerformancePlayer player = new BrainwashPerformancePlayer();

            public Pawn Pawn => pawn;

            public void Start(Pawn pawn, HediffDef sourceHediffDef, CompProperties_PerformanceEffect props)
            {
                this.pawn = pawn;
                this.sourceHediffDef = sourceHediffDef;
                if (player == null)
                {
                    player = new BrainwashPerformancePlayer();
                }
                player.Start(props);
            }

            public bool Tick()
            {
                CompProperties_PerformanceEffect props = GetPerformanceProps(sourceHediffDef);
                if (player == null)
                {
                    player = new BrainwashPerformancePlayer();
                    player.Start(props);
                }

                return player.Tick(pawn, props);
            }

            public void Stop()
            {
                player?.Stop();
            }

            public void ExposeData()
            {
                Scribe_References.Look(ref pawn, "pawn");
                Scribe_Defs.Look(ref sourceHediffDef, "sourceHediffDef");
                Scribe_Deep.Look(ref player, "player");

                if (Scribe.mode == LoadSaveMode.PostLoadInit && player == null)
                {
                    player = new BrainwashPerformancePlayer();
                }
            }
        }
    }

    // 定义洗脑效果组件，继承自HediffComp，实现具体的洗脑逻辑
    public class HediffComp_BrainWashingStar : HediffComp
    {
        private BrainwashPerformancePlayer performancePlayer = new BrainwashPerformancePlayer();

        // 快捷属性，获取配置参数
        public CompProperties_PerformanceEffect Props => props as CompProperties_PerformanceEffect;

        // 组件初始化后调用
        public override void CompPostMake()
        {
            base.CompPostMake();
            performancePlayer.Start(Props);
        }

        // 每帧调用，处理效果逻辑
        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (performancePlayer == null)
            {
                performancePlayer = new BrainwashPerformancePlayer();
                performancePlayer.Start(Props);
            }

            performancePlayer.Tick(Pawn, Props);
        }

        // 数据保存/加载
        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Deep.Look(ref performancePlayer, "performancePlayer");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (performancePlayer == null)
                    performancePlayer = new BrainwashPerformancePlayer();
            }
        }
    }
}
