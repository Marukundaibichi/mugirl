using Verse;
using RimWorld.QuestGen;
using RimWorld;
using RimWorld.Planet;

namespace MooGirl
{
    // 野生奴隶游荡进入地图事件，生成无派系雪牛娘供玩家接触收编。
    public class IncidentWorker_MooGirl_WildManWandersIn : IncidentWorker_WildManWandersIn
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map map))
            {
                return false;
            }

            if (!TryFindEntryCell(map, out var loc))
                return false;

            // 逃亡奴隶保持无派系；敌对巨企派系只用于袭击，避免事件生成即敌对。
            Faction faction = null;

            // 事件生成成年女性、可招募且不生成亲属关系的逃亡野生奴隶。
            PawnGenerationRequest request = new PawnGenerationRequest(
                MooGirl_DefOf.MooGirl_EscapeWildSlave,
                faction,
                PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: true,
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: true,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                forceRecruitable: true,
                fixedGender: Gender.Female,
                developmentalStages: DevelopmentalStage.Adult
            );

            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null)
            {
                return false;
            }

            MooGirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);

            GenSpawn.Spawn(pawn, loc, map, WipeMode.Vanish);

            string value = pawn.DevelopmentalStage.Child() ? "MooGirl.FeralChild".Translate().ToString() : pawn.KindLabel;
            TaggedString value2 = pawn.DevelopmentalStage.Child() ? "MooGirl.Child".Translate() : "MooGirl.Person".Translate();
            TaggedString baseLetterLabel = def.letterLabel.Formatted(value, pawn.Named("PAWN")).CapitalizeFirst();
            TaggedString baseLetterText = def.letterText.Formatted(pawn.NameShortColored, value2, pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true).CapitalizeFirst();
            PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref baseLetterText, ref baseLetterLabel, pawn);
            base.SendStandardLetter(baseLetterLabel, baseLetterText, def.letterDef, parms, pawn);

            return true;
        }

        // 入口必须从地图边缘可达殖民地，避免事件 pawn 卡在不可达区域。
        private bool TryFindEntryCell(Map map, out IntVec3 cell)
        {
            if (map?.reachability == null)
            {
                cell = IntVec3.Invalid;
                return false;
            }

            return CellFinder.TryFindRandomEdgeCellWith((IntVec3 c) => map.reachability.CanReachColony(c), map, CellFinder.EdgeRoadChance_Ignore, out cell);
        }
    }
}
