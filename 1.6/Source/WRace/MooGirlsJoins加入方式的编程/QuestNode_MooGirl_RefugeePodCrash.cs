using Verse;
using RimWorld.QuestGen;
using RimWorld;
using RimWorld.Planet;

namespace MooGirl
{
    public class QuestNode_Root_MooGirl_RefugeePodCrash : QuestNode_Root_RefugeePodCrash
    {
        public override Pawn GeneratePawn()
        {
            // Escaped slaves are factionless; the hostile corporation faction is reserved for raids.
            Faction faction = null;

            // 创建一个PawnGenerationRequest对象，详细定义了生成的pawn的属性和条件。
            PawnGenerationRequest request = new PawnGenerationRequest(
                // 指定要生成的Pawn的种类
                MooGirl_DefOf.MooGirl_EscapeSpaceSlave,
                // 指定该Pawn所属的派系
                faction,
                // 指定生成上下文为非玩家角色
                PawnGenerationContext.NonPlayer,
                // 指定的种子值，这里使用-1表示不使用特定的种子，即随机生成
                -1,
                // 强制生成一个新的Pawn对象，而不是从现有的池中获取
                forceGenerateNewPawn: true,
                // 不允许生成的Pawn是死亡的
                allowDead: false,
                // 允许生成的Pawn是倒下的
                allowDowned: true,
                // 不允许为该Pawn生成关系（如亲友关系等）
                canGeneratePawnRelations: false,
                // 生成的Pawn必须有能力进行暴力行为
                mustBeCapableOfViolence: true,
                // 如果需要，不强制添加免费的保暖层（可能是针对某些特定环境或生物的设定）
                forceAddFreeWarmLayerIfNeeded: false,
                // 允许生成的Pawn是同性恋的
                allowGay: true,
                // 不允许生成的Pawn是怀孕的
                allowPregnant: false,
                // 强制生成的Pawn可被招募
                forceRecruitable: true,
                // 指定生成的Pawn的性别为女性
                fixedGender: Gender.Female,
                // 不允许生成儿童
                developmentalStages: DevelopmentalStage.Adult); // 仅允许成人

            // 最多尝试 10 次，直到生成一个能被打倒的 pawn
            int num = 0;
            Pawn pawn = null;
            while (num < 10 && (pawn == null || !pawn.Downed))
            {
                num++;
                if (pawn != null)
                {
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
                }

                pawn = PawnGenerator.GeneratePawn(request);
                HealthUtility.DamageUntilDowned(pawn, true);
            }

            MooGirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);

            // 如果生成的pawn不是世界pawn，则将其传递到世界pawn管理中。
            if (!pawn.IsWorldPawn())
            {
                Find.WorldPawns.PassToWorld(pawn);
            }

            // 返回生成的pawn。
            return pawn;
        }
        public override void SendLetter_NewTemp(Quest quest, Pawn pawn, Map map)
        {
            TaggedString label = "MooGirl.LetterLabelRefugeePodCrash".Translate();
            TaggedString taggedString = "MooGirl.RefugeePodCrash".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            taggedString += "\n\n";
            if (pawn.Faction == null)
            {
                taggedString += "MooGirl.RefugeePodCrash_Factionless".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            }
            else if (pawn.Faction.HostileTo(Faction.OfPlayer))
            {
                taggedString += "MooGirl.RefugeePodCrash_Hostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            }
            else
            {
                taggedString += "MooGirl.RefugeePodCrash_NonHostile".Translate(pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true);
            }
            if (pawn.DevelopmentalStage.Juvenile())
            {
                string arg = (pawn.ageTracker.AgeBiologicalYears * 3600000).ToStringTicksToPeriod(true, false, true, true, false);
                taggedString += "\n\n" + "MooGirl.RefugeePodCrash_Child".Translate(pawn.Named("PAWN"), arg.Named("AGE"));
            }
            QuestNode_Root_WandererJoin_WalkIn.AppendCharityInfoToLetter("JoinerCharityInfo".Translate(pawn), ref taggedString);
            PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref taggedString, ref label, pawn);
            Find.LetterStack.ReceiveLetter(label, taggedString, LetterDefOf.NeutralEvent, new TargetInfo(pawn), null, null, null, null, 0, true);
        }
    }
}

