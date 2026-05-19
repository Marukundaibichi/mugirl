using RimWorld;
using Verse;

namespace MooGirl
{
    public class Thought_MooGirlOnly : Thought_Memory
    {
        public override void ThoughtInterval()
        {
            // 如果不是雪牛娘种族，直接移除自身
            if (this.pawn?.RaceProps?.body.defName != "MooGirlBody")
            {
                this.pawn.needs.mood.thoughts.memories.RemoveMemory(this);
                return;
            }

            // 原有逻辑
            base.ThoughtInterval();
        }

    }

}
