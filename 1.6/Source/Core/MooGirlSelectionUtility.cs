using Verse;

namespace MooGirl
{
    internal static class MooGirlSelectionUtility
    {
        internal static bool IsSelectedInPlaying(Pawn pawn)
        {
            return pawn != null &&
                Current.ProgramState == ProgramState.Playing &&
                Find.Selector != null &&
                Find.Selector.IsSelected(pawn);
        }

        internal static void SelectInPlaying(Pawn pawn, bool playSound = true, bool forceDesignatorDeselect = true)
        {
            if (pawn == null || Current.ProgramState != ProgramState.Playing || Find.Selector == null)
            {
                return;
            }

            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn, playSound, forceDesignatorDeselect);
        }

        internal static void ReselectIfSelectedInPlaying(Pawn pawn, bool playSound = false, bool forceDesignatorDeselect = false)
        {
            if (IsSelectedInPlaying(pawn))
            {
                Find.Selector.Select(pawn, playSound, forceDesignatorDeselect);
            }
        }
    }
}
