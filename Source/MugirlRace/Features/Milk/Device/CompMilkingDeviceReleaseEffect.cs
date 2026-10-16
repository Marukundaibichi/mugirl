using Verse.Sound;

namespace Mugirl.Features.Milk;

// 榨乳器释放特效的运行组件：Trigger 后在 durationTicks 内按排程播放音效、Fleck 与文字；排程随存档保存。
public sealed class CompMilkingDeviceReleaseEffect : ThingComp
{
    private int mugirlReleaseEffectTicksRemaining;
    private int mugirlReleaseAge;
    private List<int> mugirlReleaseNextSoundTicks = [];
    private List<int> mugirlReleaseNextFleckTicks = [];
    private List<int> mugirlReleaseNextTextTicks = [];

    public CompProperties_MilkingDeviceReleaseEffect? Props => props as CompProperties_MilkingDeviceReleaseEffect;

    private Pawn? Wearer => (parent as Apparel)?.Wearer;

    public void Trigger()
    {
        CompProperties_MilkingDeviceReleaseEffect? releaseProps = Props;
        Pawn? wearer = Wearer;
        Map? map = wearer?.Map;
        if (releaseProps == null || wearer == null || map == null)
        {
            return;
        }

        mugirlReleaseEffectTicksRemaining = Math.Max(1, releaseProps.durationTicks);
        mugirlReleaseAge = 0;
        mugirlReleaseNextSoundTicks = MilkReleaseSchedule.Build(releaseProps.sounds, static s => s.startTick);
        mugirlReleaseNextFleckTicks = MilkReleaseSchedule.Build(releaseProps.flecks, static f => f.startTick);
        mugirlReleaseNextTextTicks = MilkReleaseSchedule.Build(releaseProps.texts, static t => t.startTick);
        RunEffectFrame(wearer, map, releaseProps);
    }

    public override void CompTick()
    {
        base.CompTick();
        if (mugirlReleaseEffectTicksRemaining <= 0)
        {
            return;
        }

        Pawn? wearer = Wearer;
        Map? map = wearer?.Map;
        CompProperties_MilkingDeviceReleaseEffect? releaseProps = Props;
        if (releaseProps == null || wearer == null || map == null)
        {
            mugirlReleaseEffectTicksRemaining = 0;
            return;
        }

        mugirlReleaseAge++;
        mugirlReleaseEffectTicksRemaining--;
        RunEffectFrame(wearer, map, releaseProps);
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref mugirlReleaseEffectTicksRemaining, "mugirlReleaseEffectTicksRemaining", 0);
        Scribe_Values.Look(ref mugirlReleaseAge, "mugirlReleaseAge", 0);
        Scribe_Collections.Look(ref mugirlReleaseNextSoundTicks, "mugirlReleaseNextSoundTicks", LookMode.Value);
        Scribe_Collections.Look(ref mugirlReleaseNextFleckTicks, "mugirlReleaseNextFleckTicks", LookMode.Value);
        Scribe_Collections.Look(ref mugirlReleaseNextTextTicks, "mugirlReleaseNextTextTicks", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            // 旧存档或空列表读入为 null。
            mugirlReleaseNextSoundTicks ??= [];
            mugirlReleaseNextFleckTicks ??= [];
            mugirlReleaseNextTextTicks ??= [];
        }
    }

    private void RunEffectFrame(Pawn wearer, Map map, CompProperties_MilkingDeviceReleaseEffect releaseProps)
    {
        RunTexts(wearer, map, releaseProps.texts);
        RunSounds(wearer, map, releaseProps.sounds);
        RunFlecks(wearer, map, releaseProps.flecks);
    }

    private void RunTexts(Pawn wearer, Map map, List<CompProperties_MilkingDeviceReleaseEffect.TextWithParams?>? texts)
    {
        if (texts == null)
        {
            return;
        }

        for (int i = 0; i < texts.Count && i < mugirlReleaseNextTextTicks.Count; i++)
        {
            int nextTick = mugirlReleaseNextTextTicks[i];
            if (nextTick == MilkReleaseSchedule.Finished || mugirlReleaseAge != nextTick)
            {
                continue;
            }

            CompProperties_MilkingDeviceReleaseEffect.TextWithParams? textParams = texts[i];
            if (textParams != null && !textParams.text.NullOrEmpty())
            {
                // 与榨乳器其他文字一致：XML 可写翻译键或直接文本。
                MoteMaker.ThrowText(wearer.DrawPos, map, MugirlText.Resolve(textParams.text), textParams.color, 4f);
            }

            mugirlReleaseNextTextTicks[i] = MilkReleaseSchedule.Finished;
        }
    }

    private void RunSounds(Pawn wearer, Map map, List<CompProperties_MilkingDeviceReleaseEffect.SoundWithParams?>? sounds)
    {
        if (sounds == null)
        {
            return;
        }

        for (int i = 0; i < sounds.Count && i < mugirlReleaseNextSoundTicks.Count; i++)
        {
            CompProperties_MilkingDeviceReleaseEffect.SoundWithParams? soundParams = sounds[i];
            if (soundParams == null || !MilkReleaseSchedule.ShouldFireLooping(mugirlReleaseAge, mugirlReleaseNextSoundTicks[i], soundParams.startTick, soundParams.endTick))
            {
                continue;
            }

            soundParams.sound?.PlayOneShot(SoundInfo.InMap(new TargetInfo(wearer.Position, map)));
            mugirlReleaseNextSoundTicks[i] = MilkReleaseSchedule.NextLoopTick(mugirlReleaseAge, soundParams.loopInterval);
        }
    }

    private void RunFlecks(Pawn wearer, Map map, List<CompProperties_MilkingDeviceReleaseEffect.FleckWithParams?>? flecks)
    {
        if (flecks == null)
        {
            return;
        }

        for (int i = 0; i < flecks.Count && i < mugirlReleaseNextFleckTicks.Count; i++)
        {
            CompProperties_MilkingDeviceReleaseEffect.FleckWithParams? fleckParams = flecks[i];
            if (fleckParams == null || !MilkReleaseSchedule.ShouldFireLooping(mugirlReleaseAge, mugirlReleaseNextFleckTicks[i], fleckParams.startTick, fleckParams.endTick))
            {
                continue;
            }

            IntVec3 offset = new(Rand.RangeInclusive(-1, 1), 0, Rand.RangeInclusive(0, 1));
            IntVec3 pos = wearer.Position + offset;
            if (pos.InBounds(map) && fleckParams.fleck != null)
            {
                float minSpeed = UnityMathf.Min(fleckParams.speedMin, fleckParams.speedMax);
                float maxSpeed = UnityMathf.Max(fleckParams.speedMin, fleckParams.speedMax);
                FleckCreationData data = FleckMaker.GetDataStatic(pos.ToVector3Shifted(), map, fleckParams.fleck, UnityMathf.Max(0.01f, fleckParams.scale));
                data.velocityAngle = 0f;
                data.velocitySpeed = Rand.Range(minSpeed, maxSpeed);
                map.flecks.CreateFleck(data);
            }

            mugirlReleaseNextFleckTicks[i] = MilkReleaseSchedule.NextLoopTick(mugirlReleaseAge, fleckParams.loopInterval);
        }
    }
}
