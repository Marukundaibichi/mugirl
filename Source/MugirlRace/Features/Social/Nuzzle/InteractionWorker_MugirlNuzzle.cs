namespace Mugirl.Features.Social;

/// <summary>蹭蹭互动：接受方获得 Mugirl_Nuzzled 记忆，不发信件。</summary>
public class InteractionWorker_MugirlNuzzle : InteractionWorker
{
    public override void Interacted(
        Pawn initiator,
        Pawn recipient,
        List<RulePackDef> extraSentencePacks,
        out string? letterText,
        out string? letterLabel,
        out LetterDef? letterDef,
        out LookTargets? lookTargets)
    {
        AddNuzzledThought(recipient);
        letterText = null;
        letterLabel = null;
        letterDef = null;
        lookTargets = null;
    }

    private static void AddNuzzledThought(Pawn recipient)
    {
        Thought_Memory thought = (Thought_Memory)ThoughtMaker.MakeThought(SocialDefOf.Mugirl_Nuzzled);
        if (recipient.needs.mood is { } mood)
        {
            mood.thoughts.memories.TryGainMemory(thought, null);
        }
    }
}
