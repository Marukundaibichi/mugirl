using Verse;
using RimWorld.QuestGen;
using RimWorld;
using RimWorld.Planet;
using System.Linq;
using System.Collections.Generic;

namespace MooGirl
{
    // 自定义事件：野生女性角色游荡进入地图
    public class IncidentWorker_MooGirl_WildManWandersIn : IncidentWorker_WildManWandersIn
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            // 获取目标地图
            Map map = (Map)parms.target;
            // 尝试寻找入口位置
            if (!TryFindEntryCell(map, out var loc))
                return false;

            // Escaped slaves are factionless; the hostile corporation faction is reserved for raids.
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

            // 生成Pawn实例
            Pawn pawn = PawnGenerator.GeneratePawn(request);

            // 检查是否允许服装要求（根据信仰系统）
            bool allowApparelRequirements = false;
            if (pawn.Ideo != null)
            {
                // 遍历信仰的所有meme，检查是否阻止服装要求
                foreach (var meme in pawn.Ideo.memes)
                {
                    if (meme.preventApparelRequirements)
                    {
                        allowApparelRequirements = true;
                        break;
                    }
                }
            }

            // 如果允许服装要求且Pawn种类有服装标签，则生成对应服装
            if (allowApparelRequirements && pawn.kindDef.apparelTags != null && pawn.kindDef.apparelTags.Count > 0)
            {
                List<string> apparelTags = pawn.kindDef.apparelTags.ToList();

                foreach (var tag in apparelTags)
                {
                    // 查找符合标签的服装定义
                    var candidates = DefDatabase<ThingDef>.AllDefsListForReading
                        .Where(td => td.IsApparel
                                     && td.apparel != null
                                     && td.apparel.tags != null
                                     && td.apparel.tags.Contains(tag)
                                     && AdultContentUtility.IsAllowed(td))
                        .ToList();

                    if (candidates.Count > 0)
                    {
                        // 随机选择一个服装定义
                        ThingDef chosenDef = candidates.RandomElement();

                        Apparel newApparel;
                        // 根据是否需要材料生成服装
                        if (chosenDef.MadeFromStuff)
                        {
                            ThingDef stuff = GenStuff.RandomStuffFor(chosenDef);
                            newApparel = (Apparel)ThingMaker.MakeThing(chosenDef, stuff);
                        }
                        else
                        {
                            newApparel = (Apparel)ThingMaker.MakeThing(chosenDef, null);
                        }

                        // 检查是否需要锁定服装（特殊类型）
                        bool shouldLock = newApparel is AdvancedSlaveApparel || newApparel is BrainWashSlaveApparel;

                        // 装备服装（根据类型决定是否锁定）
                        pawn.apparel.Wear(newApparel, dropReplacedApparel: true, locked: shouldLock);
                    }
                }
            }

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
            return CellFinder.TryFindRandomEdgeCellWith((IntVec3 c) => map.reachability.CanReachColony(c), map, CellFinder.EdgeRoadChance_Ignore, out cell);
        }
    }
}
