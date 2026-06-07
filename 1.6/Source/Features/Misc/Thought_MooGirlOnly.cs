using RimWorld;
using Verse;

namespace MooGirl
{
    public class Thought_MooGirlOnly : Thought_Memory
    {
        public override void ThoughtInterval()
        {
            Pawn currentPawn = pawn;
            if (!MooGirlIdentity.HasMooGirlBody(currentPawn))
            {
                currentPawn?.needs?.mood?.thoughts?.memories?.RemoveMemory(this);
                return;
            }

            base.ThoughtInterval();
        }
    }
}
