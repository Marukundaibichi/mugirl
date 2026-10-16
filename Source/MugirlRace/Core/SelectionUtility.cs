using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Core;

// Find.Selector 经 UIRoot_Play 取得，只在游戏进行中可用；这里统一先检查 ProgramState。
internal static class SelectionUtility
{
    internal static bool IsSelectedInPlaying([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn is not null
            && Current.ProgramState == ProgramState.Playing
            && Find.Selector is { } selector
            && selector.IsSelected(pawn);
    }

    internal static void SelectInPlaying(Pawn? pawn, bool playSound = true, bool forceDesignatorDeselect = true)
    {
        if (pawn is null || Current.ProgramState != ProgramState.Playing || Find.Selector is not { } selector)
        {
            return;
        }

        selector.ClearSelection();
        selector.Select(pawn, playSound, forceDesignatorDeselect);
    }

    internal static void ReselectIfSelectedInPlaying(Pawn? pawn, bool playSound = false, bool forceDesignatorDeselect = false)
    {
        if (IsSelectedInPlaying(pawn))
        {
            Find.Selector.Select(pawn, playSound, forceDesignatorDeselect);
        }
    }
}
