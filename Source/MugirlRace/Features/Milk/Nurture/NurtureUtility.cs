namespace Mugirl.Features.Milk;

// 雪牛娘哺育：累积哺育进度 Hediff，满进度时发放特质、热情提升与哺育余韵。
public static class NurtureUtility
{
    public const float ChildInteractionProgressMin = 0.07f;
    public const float ChildInteractionProgressMax = 0.10f;
    public const float BabyFullFeedProgress = 0.04f;

    private const float InitialNurtureSeverity = 0.001f;

    public static bool CanStartNurture(Pawn? pawn)
    {
        HediffSet? hediffSet = pawn?.health?.hediffSet;
        return hediffSet != null && !hediffSet.HasHediff(MilkDefOf.Mugirl_NurtureAfterglow);
    }

    public static void AddChildNurtureProgress(Pawn child, Pawn? feeder)
    {
        AddNurtureProgress(child, Rand.Range(ChildInteractionProgressMin, ChildInteractionProgressMax), feeder);
    }

    public static void AddBabyNurtureProgress(Pawn baby, Pawn? feeder, int delta)
    {
        float fullFeedTicks = UnityMathf.Max(1f, ChildcareUtility.FullFeedSessionTicks);
        AddNurtureProgress(baby, BabyFullFeedProgress * Math.Max(1, delta) / fullFeedTicks, feeder);
    }

    public static void AddNurtureProgress(Pawn? pawn, float amount, Pawn? feeder = null)
    {
        if (pawn?.health == null || amount <= 0f || !CanStartNurture(pawn))
        {
            return;
        }

        HediffDef def = MilkDefOf.Mugirl_MotherlyNurture;
        Hediff? hediff = pawn.health.hediffSet.GetFirstHediffOfDef(def);
        if (hediff == null)
        {
            hediff = HediffMaker.MakeHediff(def, pawn);
            hediff.Severity = InitialNurtureSeverity;
            pawn.health.AddHediff(hediff);
        }

        hediff.Severity = UnityMathf.Min(hediff.Severity + amount, def.maxSeverity);
        hediff.TryGetComp<HediffComp_NurtureProgress>()?.TryComplete(feeder);
    }

    public static void CompleteNurture(Pawn? pawn, Hediff? hediff, Pawn? feeder = null)
    {
        if (pawn?.health == null)
        {
            return;
        }

        HediffDef afterglow = MilkDefOf.Mugirl_NurtureAfterglow;
        if (pawn.health.hediffSet.HasHediff(afterglow))
        {
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }

            return;
        }

        TraitDef traitDef = MilkDefOf.Mugirl_NurturedByMugirl;
        if (pawn.story?.traits != null && !pawn.story.traits.HasTrait(traitDef))
        {
            pawn.story.traits.GainTrait(new Trait(traitDef));
        }

        ImproveTopSkillPassions(pawn);
        pawn.health.AddHediff(HediffMaker.MakeHediff(afterglow, pawn));

        if (hediff != null && pawn.health.hediffSet.hediffs.Contains(hediff))
        {
            pawn.health.RemoveHediff(hediff);
        }

        Messages.Message("Mugirl.Nurture.Completed".Translate(pawn.Named("PAWN")), pawn, MessageTypeDefOf.PositiveEvent);
    }

    // 提升等级最高（同级比总经验）的两项技能的热情：无 → 次要，次要 → 主要。
    private static void ImproveTopSkillPassions(Pawn pawn)
    {
        List<SkillRecord>? skills = pawn.skills?.skills;
        if (skills == null)
        {
            return;
        }

        SkillRecord? first = null;
        SkillRecord? second = null;
        for (int i = 0; i < skills.Count; i++)
        {
            SkillRecord skill = skills[i];
            if (!CanImprovePassion(skill))
            {
                continue;
            }

            if (IsBetterSkillCandidate(skill, first))
            {
                second = first;
                first = skill;
            }
            else if (IsBetterSkillCandidate(skill, second))
            {
                second = skill;
            }
        }

        ImprovePassion(first);
        ImprovePassion(second);
    }

    private static bool CanImprovePassion(SkillRecord? skill)
    {
        return skill != null && !skill.TotallyDisabled && skill.passion < Passion.Major;
    }

    private static bool IsBetterSkillCandidate(SkillRecord candidate, SkillRecord? current)
    {
        if (current == null)
        {
            return true;
        }

        if (candidate.Level != current.Level)
        {
            return candidate.Level > current.Level;
        }

        return candidate.XpTotalEarned > current.XpTotalEarned;
    }

    private static void ImprovePassion(SkillRecord? skill)
    {
        if (skill != null)
        {
            skill.passion = skill.passion == Passion.None ? Passion.Minor : Passion.Major;
        }
    }
}
