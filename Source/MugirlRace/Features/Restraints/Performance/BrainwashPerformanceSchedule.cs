namespace Mugirl.Features.Restraints;

// 洗脑表演时间表的纯计算部分（不访问游戏状态，可离线测试）。
internal static class BrainwashPerformanceSchedule
{
    // 配置为负的开始 tick 视为 0。
    internal static int FirstTick(int startTick)
    {
        return Math.Max(0, startTick);
    }

    // 循环效果的下一次触发 tick；间隔至少 1 tick。
    internal static int NextLoopTick(int age, int loopInterval)
    {
        return age + Math.Max(1, loopInterval);
    }

    // 开始 tick 之后的有效结束 tick（结束早于开始时取开始 tick）。
    internal static int EndTick(int startTick, int endTick)
    {
        return Math.Max(FirstTick(startTick), endTick);
    }

    // 眩晕开始的 tick：触发延迟与眩晕延迟各自不小于 0。
    internal static int StunStartTick(HediffCompProperties_BrainwashPerformance props)
    {
        return Math.Max(0, props.triggerTicks) + Math.Max(0, props.stunDelayTicks);
    }

    // 表演结束的 tick：眩晕结束、头顶 Mote 结束、各声音/Fleck 的结束 tick 与最后一条文字的开始 tick 中的最大值，至少为 1。
    internal static int CompletionTick(HediffCompProperties_BrainwashPerformance props)
    {
        int completionTick = Math.Max(1, props.triggerTicks + props.stunDelayTicks + props.stunDurationTicks);

        if (props.moteToShowOnHead != null)
        {
            completionTick = Math.Max(completionTick, props.moteShowEndTick);
        }

        if (props.sounds != null)
        {
            for (int i = 0; i < props.sounds.Count; i++)
            {
                if (props.sounds[i] is { } sound)
                {
                    completionTick = Math.Max(completionTick, sound.endTick);
                }
            }
        }

        if (props.flecks != null)
        {
            for (int i = 0; i < props.flecks.Count; i++)
            {
                if (props.flecks[i] is { } fleck)
                {
                    completionTick = Math.Max(completionTick, fleck.endTick);
                }
            }
        }

        if (props.texts != null)
        {
            for (int i = 0; i < props.texts.Count; i++)
            {
                if (props.texts[i] is { } text)
                {
                    completionTick = Math.Max(completionTick, text.startTick);
                }
            }
        }

        return completionTick;
    }
}
