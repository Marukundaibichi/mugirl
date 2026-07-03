using RimWorld;
using Verse;

namespace Mugirl
{
    public class Thought_MugirlOnly : Thought_Memory
    {
        public override void ThoughtInterval()
        {
            Pawn currentPawn = pawn;
            if (!MugirlIdentity.HasMugirlBody(currentPawn))
            {
                currentPawn?.needs?.mood?.thoughts?.memories?.RemoveMemory(this);
                return;
            }

            base.ThoughtInterval();
        }
    }
}
