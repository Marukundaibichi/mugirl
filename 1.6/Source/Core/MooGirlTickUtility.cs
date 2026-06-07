namespace MooGirl
{
    internal static class MooGirlTickUtility
    {
        internal static bool TryGetCurrentGameTick(out int tick)
        {
            if (Verse.Current.ProgramState == Verse.ProgramState.Playing && Verse.Find.TickManager != null)
            {
                tick = Verse.Find.TickManager.TicksGame;
                return true;
            }

            tick = 0;
            return false;
        }

        internal static int CurrentGameTickOrFallback(int fallback)
        {
            return TryGetCurrentGameTick(out int tick) ? tick : fallback;
        }

        internal static void Add(ref int counter, int delta)
        {
            counter += delta;
        }

        internal static bool ConsumeReady(ref int counter, int interval, out int elapsedTicks)
        {
            // 保持旧组件计数契约：调用方每 tick 累加，并在达到阈值时
            // 把完整累计值传给 interval 逻辑，随后再归零。
            if (counter < interval)
            {
                elapsedTicks = 0;
                return false;
            }

            elapsedTicks = counter;
            counter = 0;
            return true;
        }
    }
}
