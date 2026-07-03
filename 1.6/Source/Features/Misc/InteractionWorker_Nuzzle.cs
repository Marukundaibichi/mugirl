using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    public class InteractionWorker_Nuzzle : InteractionWorker
    {
        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks, out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            this.AddNuzzledThought(initiator, recipient);
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = null;
        }

        private void AddNuzzledThought(Pawn initiator, Pawn recipient)
        {
            Thought_Memory thought_Memory = (Thought_Memory)ThoughtMaker.MakeThought(Mugirl_DefOf.Mugirl_Nuzzled);
            if (recipient.needs.mood != null)
            {
                recipient.needs.mood.thoughts.memories.TryGainMemory(thought_Memory, null);
            }
        }
    }
}
