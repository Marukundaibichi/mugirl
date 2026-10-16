namespace Mugirl.SaveUpgrade;

/// <summary>按已保存的语义证据匹配已发布布局；不把游戏版本当作 Mod 布局版本。</summary>
internal static class RestraintItemFlow
{
    internal enum Kind { Crack, UnlockSelf, UnlockOther, UseOnTarget }
    internal enum Destination { Unknown, Item, Target, Both }
    private enum Phase { Reserve, TakeInventory, GotoItem, PickUp, Label, GotoTarget, Wait, Apply, Terminal }

    internal readonly record struct Snapshot(int Current, int Next, int TicksLeft, bool WantNext, bool Moving, Destination PathTarget);
    internal readonly record struct Result(int Current, int Next);
    private sealed record Layout(string Name, Phase[] Phases, int[] Map);

    internal static Result Resolve(Kind kind, Snapshot state)
    {
        Layout[] layouts = Layouts(kind);
        int currentLength = layouts[^1].Phases.Length;
        if (state.Current == -1 && state.Next is -1 or 0)
        {
            return new Result(-1, state.Next);
        }

        Result? unique = null;
        foreach (Layout layout in layouts)
        {
            if (state.Current < 0 || state.Current >= layout.Phases.Length || !Matches(layout.Phases[state.Current], state))
            {
                continue;
            }

            int next = state.Next;
            if (next != -1)
            {
                if (next < 0 || next > layout.Phases.Length)
                {
                    continue;
                }

                if (next == layout.Phases.Length)
                {
                    next = currentLength;
                }
                else
                {
                    // 尚未执行的库存转手不能偷换为当前跳过取物的标签；没有安全证据则不提交转换。
                    if (layout.Phases[next] == Phase.TakeInventory)
                    {
                        throw new InvalidOperationException("The " + layout.Name + " item-job continuation still needs an unperformed inventory transfer; its semantics cannot be guessed.");
                    }

                    next = layout.Phases[next] == Phase.Terminal ? currentLength : layout.Map[next];
                }
            }

            var candidate = new Result(layout.Map[state.Current], next);
            if (unique is { } previous && previous != candidate)
            {
                throw new InvalidOperationException("Ambiguous restraint item-job layout: saved movement/wait state does not identify one continuation.");
            }

            unique = candidate;
        }

        return unique ?? throw new InvalidOperationException("Unproven restraint item-job layout: preserve the original save; no work is ended or restarted.");
    }

    private static bool Matches(Phase phase, Snapshot state)
    {
        if (state.TicksLeft > 0)
        {
            return phase == Phase.Wait;
        }

        // 正常存档在同步 Instant 链之外发生；忙碌姿态中的待切换瞬间不按编号猜测。
        if (state.WantNext || !state.Moving)
        {
            return false;
        }

        return phase switch
        {
            Phase.GotoItem => state.PathTarget is Destination.Item or Destination.Both,
            Phase.GotoTarget => state.PathTarget is Destination.Target or Destination.Both,
            _ => false,
        };
    }

    private static Layout[] Layouts(Kind kind) => kind switch
    {
        Kind.Crack =>
        [
            new("inventory", [Phase.Reserve, Phase.TakeInventory, Phase.GotoTarget, Phase.Wait, Phase.Apply, Phase.Terminal], [0, 5, 6, 7, 8, 8]),
            new("ground", [Phase.Reserve, Phase.Reserve, Phase.GotoItem, Phase.PickUp, Phase.GotoTarget, Phase.Wait, Phase.Apply, Phase.Terminal], [0, 2, 3, 4, 6, 7, 8, 8]),
            new("current", [Phase.Reserve, Phase.Label, Phase.Reserve, Phase.GotoItem, Phase.PickUp, Phase.Label, Phase.GotoTarget, Phase.Wait, Phase.Apply], [0, 1, 2, 3, 4, 5, 6, 7, 8]),
        ],
        Kind.UnlockSelf =>
        [
            new("inventory", [Phase.Reserve, Phase.TakeInventory, Phase.Wait, Phase.Apply], [0, 5, 6, 7]),
            new("ground", [Phase.Reserve, Phase.Reserve, Phase.GotoItem, Phase.PickUp, Phase.Wait, Phase.Apply], [0, 2, 3, 4, 6, 7]),
            new("current", [Phase.Reserve, Phase.Label, Phase.Reserve, Phase.GotoItem, Phase.PickUp, Phase.Label, Phase.Wait, Phase.Apply], [0, 1, 2, 3, 4, 5, 6, 7]),
        ],
        Kind.UnlockOther =>
        [
            new("inventory", [Phase.Reserve, Phase.Reserve, Phase.TakeInventory, Phase.GotoTarget, Phase.Wait, Phase.Apply], [0, 1, 6, 7, 8, 9]),
            new("ground", [Phase.Reserve, Phase.Reserve, Phase.Reserve, Phase.GotoItem, Phase.PickUp, Phase.GotoTarget, Phase.Wait, Phase.Apply], [0, 1, 3, 4, 5, 7, 8, 9]),
            new("current", [Phase.Reserve, Phase.Reserve, Phase.Label, Phase.Reserve, Phase.GotoItem, Phase.PickUp, Phase.Label, Phase.GotoTarget, Phase.Wait, Phase.Apply], [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]),
        ],
        Kind.UseOnTarget =>
        [
            new("inventory", [Phase.Reserve, Phase.TakeInventory, Phase.GotoTarget, Phase.Wait, Phase.Apply], [0, 5, 6, 7, 8]),
            new("ground", [Phase.Reserve, Phase.Reserve, Phase.GotoItem, Phase.PickUp, Phase.GotoTarget, Phase.Wait, Phase.Apply], [0, 2, 3, 4, 6, 7, 8]),
            new("current", [Phase.Reserve, Phase.Label, Phase.Reserve, Phase.GotoItem, Phase.PickUp, Phase.Label, Phase.GotoTarget, Phase.Wait, Phase.Apply], [0, 1, 2, 3, 4, 5, 6, 7, 8]),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
