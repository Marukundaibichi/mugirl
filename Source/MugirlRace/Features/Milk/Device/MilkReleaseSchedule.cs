namespace Mugirl.Features.Milk;

// 释放特效排程的纯计算：每个条目记录下一次触发的效果年龄（tick），-1 表示已结束或条目无效。
internal static class MilkReleaseSchedule
{
    internal const int Finished = -1;

    // 为每个条目生成首次触发 tick；null 条目直接标记为结束，保持与 XML 列表下标一一对应。
    internal static List<int> Build<T>(List<T?>? entries, Func<T, int> startTickOf)
        where T : class
    {
        var ticks = new List<int>();
        if (entries == null)
        {
            return ticks;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            T? entry = entries[i];
            ticks.Add(entry == null ? Finished : Math.Max(0, startTickOf(entry)));
        }

        return ticks;
    }

    // 循环条目在 [startTick, endTick] 内、且到达下一次触发 tick 时触发；endTick 小于 startTick 时按 startTick 处理。
    internal static bool ShouldFireLooping(int age, int nextTick, int startTick, int endTick)
    {
        if (nextTick == Finished)
        {
            return false;
        }

        int start = Math.Max(0, startTick);
        int end = Math.Max(start, endTick);
        return age >= start && age <= end && age >= nextTick;
    }

    internal static int NextLoopTick(int age, int loopInterval) => age + Math.Max(1, loopInterval);
}
