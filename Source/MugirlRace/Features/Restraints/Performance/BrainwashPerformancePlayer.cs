using Verse.Sound;

namespace Mugirl.Features.Restraints;

// 洗脑表演的时间轴播放器：按配置在指定 tick 显示文字、循环播放声音与 Fleck、眩晕并在头顶挂 Mote。
// 作为 IExposable 存在 HediffComp_BrainwashPerformance 与 GameComponent_BrainwashPerformance 中；存档标签与 1.6 相同。
public class BrainwashPerformancePlayer : IExposable
{
    // 头顶 Mote 相对 Pawn 的偏移（世界坐标，向北 0.5 格）；不可变常量，不是缓存。
    private static readonly UVector3 HeadMoteOffset = new(0f, 0f, 0.5f);

    private int age;
    private int stunTickTarget = -1;
    private bool stunned;
    private List<int> nextTextShowTicks = new List<int>();
    private List<int> nextSoundTicks = new List<int>();
    private List<int> nextFleckTicks = new List<int>();
    private int stunEndTick = -1;
    private bool stunMessageSent;
    private Mote? attachedMote;
    private bool active;

    // Def 配置在单次表演期间稳定；此实例缓存不进存档，开始/读档/配置引用变化时重建。
    private HediffCompProperties_BrainwashPerformance? cachedTimingProps;
    private int completionTick;

    public bool Active => active;

    public void Start(HediffCompProperties_BrainwashPerformance? props)
    {
        Stop();

        if (props == null)
        {
            return;
        }

        active = true;
        age = 0;
        stunTickTarget = BrainwashPerformanceSchedule.StunStartTick(props);
        stunned = false;
        stunEndTick = -1;
        stunMessageSent = false;

        nextTextShowTicks = new List<int>();
        if (props.texts != null)
        {
            for (int i = 0; i < props.texts.Count; i++)
            {
                HediffCompProperties_BrainwashPerformance.TextWithParams? textParams = props.texts[i];
                nextTextShowTicks.Add(textParams == null ? -1 : BrainwashPerformanceSchedule.FirstTick(textParams.startTick));
            }
        }

        nextSoundTicks = new List<int>();
        if (props.sounds != null)
        {
            for (int i = 0; i < props.sounds.Count; i++)
            {
                HediffCompProperties_BrainwashPerformance.SoundWithParams? soundParams = props.sounds[i];
                nextSoundTicks.Add(soundParams == null ? -1 : BrainwashPerformanceSchedule.FirstTick(soundParams.startTick));
            }
        }

        nextFleckTicks = new List<int>();
        if (props.flecks != null)
        {
            for (int i = 0; i < props.flecks.Count; i++)
            {
                HediffCompProperties_BrainwashPerformance.FleckWithParams? fleckParams = props.flecks[i];
                nextFleckTicks.Add(fleckParams == null ? -1 : BrainwashPerformanceSchedule.FirstTick(fleckParams.startTick));
            }
        }

        CacheTimingProps(props);
    }

    // 返回 true 表示表演已结束（或无法继续）。
    public bool Tick(Pawn? pawn, HediffCompProperties_BrainwashPerformance? props, bool sendMessages = true)
    {
        if (!active)
        {
            return false;
        }

        if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null || props == null)
        {
            Stop();
            return true;
        }

        if (cachedTimingProps != props)
        {
            EnsureTimingLists(props);
            CacheTimingProps(props);
        }

        age++;

        TickTexts(pawn, props);
        TickSounds(pawn, props);
        TickFlecks(pawn, props);
        TickStun(pawn, props, sendMessages);
        TickAttachedMote(pawn, props);

        if (age >= completionTick)
        {
            Stop();
            return true;
        }

        return false;
    }

    public void Stop()
    {
        active = false;
        cachedTimingProps = null;
        completionTick = 0;
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
            cachedTimingProps = null;
            completionTick = 0;
            nextTextShowTicks ??= new List<int>();
            nextSoundTicks ??= new List<int>();
            nextFleckTicks ??= new List<int>();
        }
    }

    // 读档或配置变化后补齐计时列表：新增的文字若开始 tick 已过则不再显示，新增的声音/Fleck 从当前 tick 起播放。
    private void EnsureTimingLists(HediffCompProperties_BrainwashPerformance props)
    {
        nextTextShowTicks ??= new List<int>();
        nextSoundTicks ??= new List<int>();
        nextFleckTicks ??= new List<int>();

        if (props.texts != null)
        {
            while (nextTextShowTicks.Count < props.texts.Count)
            {
                HediffCompProperties_BrainwashPerformance.TextWithParams? textParams = props.texts[nextTextShowTicks.Count];
                int startTick = textParams == null ? -1 : BrainwashPerformanceSchedule.FirstTick(textParams.startTick);
                nextTextShowTicks.Add(startTick >= age ? startTick : -1);
            }
        }

        if (props.sounds != null)
        {
            while (nextSoundTicks.Count < props.sounds.Count)
            {
                HediffCompProperties_BrainwashPerformance.SoundWithParams? soundParams = props.sounds[nextSoundTicks.Count];
                nextSoundTicks.Add(soundParams == null ? -1 : Math.Max(age, soundParams.startTick));
            }
        }

        if (props.flecks != null)
        {
            while (nextFleckTicks.Count < props.flecks.Count)
            {
                HediffCompProperties_BrainwashPerformance.FleckWithParams? fleckParams = props.flecks[nextFleckTicks.Count];
                nextFleckTicks.Add(fleckParams == null ? -1 : Math.Max(age, fleckParams.startTick));
            }
        }
    }

    private void TickTexts(Pawn pawn, HediffCompProperties_BrainwashPerformance props)
    {
        if (props.texts == null)
        {
            return;
        }

        for (int i = 0; i < props.texts.Count && i < nextTextShowTicks.Count; i++)
        {
            int showTick = nextTextShowTicks[i];
            if (showTick == -1 || age < showTick)
            {
                continue;
            }

            HediffCompProperties_BrainwashPerformance.TextWithParams? textParams = props.texts[i];
            if (textParams != null && !string.IsNullOrEmpty(textParams.text))
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, textParams.text, textParams.color);
            }

            nextTextShowTicks[i] = -1;
        }
    }

    private void TickSounds(Pawn pawn, HediffCompProperties_BrainwashPerformance props)
    {
        if (props.sounds == null)
        {
            return;
        }

        for (int i = 0; i < props.sounds.Count && i < nextSoundTicks.Count; i++)
        {
            HediffCompProperties_BrainwashPerformance.SoundWithParams? s = props.sounds[i];
            if (s == null || nextSoundTicks[i] == -1)
            {
                continue;
            }

            int startTick = BrainwashPerformanceSchedule.FirstTick(s.startTick);
            int endTick = BrainwashPerformanceSchedule.EndTick(s.startTick, s.endTick);
            if (age >= startTick && age <= endTick && age >= nextSoundTicks[i])
            {
                s.sound?.PlayOneShot(SoundInfo.InMap(new TargetInfo(pawn.Position, pawn.Map)));
                nextSoundTicks[i] = BrainwashPerformanceSchedule.NextLoopTick(age, s.loopInterval);
            }
        }
    }

    private void TickFlecks(Pawn pawn, HediffCompProperties_BrainwashPerformance props)
    {
        if (props.flecks == null)
        {
            return;
        }

        for (int i = 0; i < props.flecks.Count && i < nextFleckTicks.Count; i++)
        {
            HediffCompProperties_BrainwashPerformance.FleckWithParams? f = props.flecks[i];
            if (f?.fleck == null || nextFleckTicks[i] == -1)
            {
                continue;
            }

            int startTick = BrainwashPerformanceSchedule.FirstTick(f.startTick);
            int endTick = BrainwashPerformanceSchedule.EndTick(f.startTick, f.endTick);
            if (age >= startTick && age <= endTick && age >= nextFleckTicks[i])
            {
                IntVec3 offset = new IntVec3(Rand.RangeInclusive(-1, 1), 0, Rand.RangeInclusive(0, 1));
                IntVec3 pos = pawn.Position + offset;
                if (pos.InBounds(pawn.Map))
                {
                    FleckCreationData data = FleckMaker.GetDataStatic(pos.ToVector3Shifted(), pawn.Map, f.fleck, 0.42f);
                    data.velocityAngle = 0f;
                    data.velocitySpeed = Rand.Range(0.2f, 0.5f);
                    pawn.Map.flecks.CreateFleck(data);
                }

                nextFleckTicks[i] = BrainwashPerformanceSchedule.NextLoopTick(age, f.loopInterval);
            }
        }
    }

    private void TickStun(Pawn pawn, HediffCompProperties_BrainwashPerformance props, bool sendMessages)
    {
        if (!stunned && age >= stunTickTarget)
        {
            stunned = true;
            int stunDurationTicks = Math.Max(0, props.stunDurationTicks);
            stunEndTick = age + stunDurationTicks;
            if (sendMessages)
            {
                Messages.Message("Mugirl.BrainwashStart".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.NegativeEvent);
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
                Messages.Message("Mugirl.BrainwashEnd".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.PositiveEvent);
            }
        }
    }

    private void TickAttachedMote(Pawn pawn, HediffCompProperties_BrainwashPerformance props)
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
            if (ThingMaker.MakeThing(props.moteToShowOnHead, null) is not Mote mote)
            {
                return;
            }

            mote.Attach(pawn, HeadMoteOffset, false);
            GenSpawn.Spawn(mote, pawn.Position, pawn.Map, Rot4.North, WipeMode.Vanish, false, false);
            attachedMote = mote;
        }
        else
        {
            attachedMote.Maintain();
        }
    }

    private void CacheTimingProps(HediffCompProperties_BrainwashPerformance props)
    {
        cachedTimingProps = props;
        completionTick = BrainwashPerformanceSchedule.CompletionTick(props);
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
