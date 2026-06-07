using Verse;
using RimWorld.QuestGen;
using RimWorld;
using RimWorld.Planet;

namespace MooGirl
{
    // 自定义事件：野生女性角色游荡进入地图
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

            // 配置Pawn生成请求
            PawnGenerationRequest request = new PawnGenerationRequest(
                MooGirl_DefOf.MooGirl_EscapeWildSlave, // 使用自定义Pawn种类
                faction,                               // 所属派系
                PawnGenerationContext.NonPlayer,       // 生成上下文
                forceGenerateNewPawn: true,            // 强制生成新角色
                allowDead: false,                      // 不允许死亡
                allowDowned: true,                     // 允许倒地状态
                canGeneratePawnRelations: false,       // 不生成关系
                mustBeCapableOfViolence: true,         // 必须具备暴力能力
                forceAddFreeWarmLayerIfNeeded: false,  // 不强制添加保暖衣物
                allowGay: true,                        // 允许同性恋
                allowPregnant: false,                  // 不允许怀孕
                forceRecruitable: true,                // 强制可招募
                fixedGender: Gender.Female,            // 固定性别为女性
                developmentalStages: DevelopmentalStage.Adult // 成年阶段
            );

            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null)
            {
                return false;
            }

            MooGirlApparelTagUtility.TryWearIdeoSuppressedKindApparel(pawn);

            // 直接将Pawn生成到地图指定位置
            GenSpawn.Spawn(pawn, loc, map, WipeMode.Vanish);

            // 生成事件通知信件（根据角色阶段调整内容）
            string value = pawn.DevelopmentalStage.Child() ? "MooGirl.FeralChild".Translate().ToString() : pawn.KindLabel;
            TaggedString value2 = pawn.DevelopmentalStage.Child() ? "MooGirl.Child".Translate() : "MooGirl.Person".Translate();
            TaggedString baseLetterLabel = def.letterLabel.Formatted(value, pawn.Named("PAWN")).CapitalizeFirst();
            TaggedString baseLetterText = def.letterText.Formatted(pawn.NameShortColored, value2, pawn.Named("PAWN")).AdjustedFor(pawn, "PAWN", true).CapitalizeFirst();
            // 添加与殖民者的关系信息
            PawnRelationUtility.TryAppendRelationsWithColonistsInfo(ref baseLetterText, ref baseLetterLabel, pawn);
            // 发送标准格式信件
            base.SendStandardLetter(baseLetterLabel, baseLetterText, def.letterDef, parms, pawn);

            return true;
        }

        // 尝试寻找合适的入口位置（地图边缘可达殖民地的位置）
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
