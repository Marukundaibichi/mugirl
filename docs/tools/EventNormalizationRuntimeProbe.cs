// 独立运行期回归探针，不编入正式 DLL；仅接受显式开关和本模组 TMP 隔离配置。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Mugirl;
using RimWorld;
using RimWorld.QuestGen;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MugirlEventProbe
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private static string output;
        private static int phase, passed, failed, deadline, silverAfterGift;
        private static Game savedGame;
        private static float nextStatus;
        private static Map Map => Find.CurrentMap;
        private static CorporateIntroduction Intro => CorporateIntroduction.Current;
        private static MugirlStoryState Story => Current.Game.GetComponent<MugirlStoryState>();
        private static Pawn Actor => Map.mapPawns.FreeColonistsSpawned.First(p => !p.Downed && !p.Dead);

        static Bootstrap()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlEventProbe")) return;
            string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Mugirl_DefOf).Assembly.Location), "../..", "TMP"));
            output = Path.GetFullPath(GenFilePaths.SaveDataFolderPath);
            if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            var harmony = new Harmony("mugirl.events.runtime.probe");
            harmony.Patch(AccessTools.Method(typeof(Game), "UpdatePlay"),
                postfix: new HarmonyMethod(typeof(Bootstrap), "Update"));
            harmony.Patch(AccessTools.Method(typeof(Root), "Update"), postfix: new HarmonyMethod(typeof(Bootstrap), "Status"));
            File.WriteAllText(Path.Combine(output, "progress.txt"), "Waiting for isolated quicktest");
            Application.runInBackground = true;
        }

        private static object Field(object obj, string name) => AccessTools.Field(obj.GetType(), name).GetValue(obj);
        private static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
        private static object Call(object obj, string name, params object[] args) =>
            AccessTools.Method(obj is Type ? (Type)obj : obj.GetType(), name).Invoke(obj is Type ? null : obj, args);
        private static void Check(string label, bool ok)
        {
            if (ok) passed++; else failed++;
            File.AppendAllText(Path.Combine(output, "checks.txt"), (ok ? "PASS " : "FAIL ") + label + Environment.NewLine);
        }
        private static void Step(string text) => File.WriteAllText(Path.Combine(output, "progress.txt"), text);
        private static void Status()
        {
            if (Time.realtimeSinceStartup < nextStatus) return;
            nextStatus = Time.realtimeSinceStartup + 5f;
            File.WriteAllText(Path.Combine(output, "status.txt"), "Phase=" + phase + " State=" + Current.ProgramState
                + " LongEvent=" + LongEventHandler.AnyEventNowOrWaiting + " Map=" + Find.CurrentMap
                + " Colonists=" + Find.CurrentMap?.mapPawns.FreeColonistsSpawned.Count);
        }
        private static void SetOpening(bool enabled) => Set(AccessTools.Property(typeof(MugirlMod), "Settings").GetValue(null, null), "enableStructuralCrashEvent", enabled);
        private static void Service()
        {
            Set(Intro, "nextServiceTick", 0);
            Intro.GameComponentTick();
        }
        private static void Jump(int tick) => Find.TickManager.DebugSetTicksGame(tick);
        private static void Tick(int count)
        {
            for (int i = 0; i < count; i++) Find.TickManager.DoSingleTick();
        }
        private static void Update()
        {
            if (phase >= 4 || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting
                || Find.CurrentMap?.mapPawns.FreeColonistsSpawned.Count < 1) return;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                if (phase == 0) Begin();
                else if (phase == 1 && !ReferenceEquals(Current.Game, savedGame)) PendingReloaded();
                else if (phase == 2 && !ReferenceEquals(Current.Game, savedGame)) CompletedReloaded();
            }
            catch (Exception ex) { Check("runtime exception: " + ex, false); Finish(); }
        }
        private static void Roundtrip(string name, int nextPhase)
        {
            savedGame = Current.Game;
            phase = nextPhase;
            GameDataSaveLoader.SaveGame(name);
            GameDataSaveLoader.LoadGame(name);
        }
        private static void ResetIntro()
        {
            Pawn rep = Intro.Representative;
            RemoveVisitor(rep);
            Quest quest = Field(Intro, "introductionQuest") as Quest;
            if (quest?.State == QuestState.Ongoing) quest.End(QuestEndOutcome.Unknown, false, false);
            foreach (string key in new[] { "pending", "completed", "independentContact", "giftsPrepared", "courierDied", "courierKilledByPlayer", "migrated", "independentAggression" })
                Set(Intro, key, false);
            foreach (string key in new[] { "representative", "courier", "introductionQuest" }) Set(Intro, key, null);
            Set(Intro, "courierChoice", 0);
            Set(Intro, "contactTick", -1);
            Set(Intro, "representativeArrivalTick", -1);
            Intro.StartedNewGame();
            Story.openingCrashStarted = false;
            Story.courierRaidTriggered = false;
            Story.courierRaidQuestStarted = false;
            Story.openingCrashPawns.Clear();
        }
        private static void Begin()
        {
            phase = 3;
            Prefs.DevMode = true;
            Step("Disabled opening, setting changes and pending save/load");
            ResetIntro();
            SetOpening(true);
            Service();
            Check("Enabled opening: no representative before opening or courier", Intro.Representative == null && !(bool)Field(Intro, "pending"));
            Set(Intro, "initialized", false);
            Intro.LoadedGame();
            Service();
            Check("Legacy component migration also waits for courier when opening is enabled", !(bool)Field(Intro, "pending"));
            SetOpening(false);
            Service();
            deadline = (int)Field(Intro, "representativeArrivalTick");
            Check("Disabled opening schedules independent contact", (bool)Field(Intro, "pending") && (bool)Field(Intro, "independentContact"));
            Check("Independent visit delay remains 120000 +/- 9000 ticks", deadline - Find.TickManager.TicksGame >= 111000 && deadline - Find.TickManager.TicksGame <= 129000);
            Check("No premature independent representative", Intro.Representative == null);
            Quest quest = (Quest)Field(Intro, "introductionQuest");
            Check("Independent quest has its own text", quest.description == "Mugirl.CorporateIntro.Independent.QuestDescription".Translate().ToString());
            SetOpening(true);
            Service();
            Check("Re-enabling opening before arrival cancels independent schedule", !(bool)Field(Intro, "pending") && (int)Field(Intro, "representativeArrivalTick") == -1);
            Jump(deadline + 1);
            Service();
            Check("Cancelled visit does not appear at its old deadline", Intro.Representative == null);
            SetOpening(false);
            Service();
            deadline = (int)Field(Intro, "representativeArrivalTick");
            var retired = DefDatabase<IncidentDef>.GetNamed("Mugirl_WildManWandersIn");
            var parms = new IncidentParms { target = Map, points = 200f, forced = true };
            Check("Retired event excluded from random selection", retired.hidden && retired.baseChance == 0f);
            Check("Retired event rejects both selection and forced execution", !retired.Worker.CanFireNow(parms) && !retired.Worker.TryExecute(parms));
            Find.Storyteller.incidentQueue.Add(retired, Find.TickManager.TicksGame + 2000000, parms);
            Roundtrip("EventPendingRoundtrip", 1);
        }
        private static void PendingReloaded()
        {
            phase = 3;
            Step("Independent representative arrival and completion");
            Check("Pending route and exact arrival time survive real save/load", (bool)Field(Intro, "independentContact") && (int)Field(Intro, "representativeArrivalTick") == deadline);
            Jump(deadline - 1);
            Service();
            Check("Independent representative waits until deadline", Intro.Representative == null);
            Jump(deadline + 1);
            Service();
            Check("Independent representative actually spawns", Intro.Representative?.Spawned == true);
            if (Intro.Representative == null) throw new InvalidOperationException("Representative not spawned");
            DiaNode node = (DiaNode)Call(Intro, "BuildIntroductionDialog", Actor, Intro.Representative);
            string text = node.text.ToString() + string.Join("\n", node.options.Select(o => Field(o, "text") + "\n" + o.link?.text.ToString()));
            File.WriteAllText(Path.Combine(output, "independent-dialog.txt"), text);
            Check("Independent dialogue uses prospecting introduction", node.text.ToString().Contains("Mugirl.CorporateIntro.Independent.Opening".Translate().ToString()));
            Check("Independent dialogue has no courier or two-Mugirl claim", !text.Contains("运货员") && !text.Contains("那两位") && !text.ToLowerInvariant().Contains("courier"));
            Pawn colonyMugirl = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Mugirl_Colony"), Faction.OfPlayer,
                forceGenerateNewPawn: true, fixedGender: Gender.Female, fixedBiologicalAge: 24f, fixedChronologicalAge: 24f));
            SpawnNear(colonyMugirl);
            node = (DiaNode)Call(Intro, "BuildIntroductionDialog", Actor, Intro.Representative);
            Check("Independent dialogue handles existing Mugirl colonists", node.text.ToString().Contains("Mugirl.CorporateIntro.Independent.MugirlsPresent".Translate().ToString()));
            node.options.First(o => Field(o, "text").ToString() == "Mugirl.CorporateIntro.Accept".Translate().ToString()).action();
            Check("Independent contact unlocks corporate services", Intro.Completed && CorporateNetwork.Current.Unlocked);
            silverAfterGift = Count(ThingDefOf.Silver);
            Service();
            Check("Repeated service does not repeat welcome gift", Count(ThingDefOf.Silver) == silverAfterGift);
            Roundtrip("EventCompletedRoundtrip", 2);
        }
        private static void CompletedReloaded()
        {
            phase = 3;
            Step("Opening -> courier -> representative, then random events and milk jobs");
            Check("Completed independent contact survives save/load", Intro.Completed && (bool)Field(Intro, "independentContact") && CorporateNetwork.Current.Unlocked);
            Service();
            Check("Loading completed contact does not repeat gifts", Count(ThingDefOf.Silver) == silverAfterGift);
            TestStoryOrder();
            TestRandomEntrances();
            TestMilk();
            TestSharedGeneration();
            Finish();
        }
        private static void TestStoryOrder()
        {
            ResetIntro();
            SetOpening(true);
            Story.openingCrashCheckTimer = 0;
            Set(Story, "storyServiceNextTick", -1);
            Story.GameComponentTick();
            Check("Actual opening story delivers exactly two rescue pawns", Story.openingCrashStarted && Story.openingCrashPawns.Count == 2);
            Check("Opening rescue pawns remain adult, downed and factionless", Story.openingCrashPawns.Count == 2
                && Story.openingCrashPawns.All(p => p.Faction == null && p.Downed && !p.Dead && p.ageTracker.AgeBiologicalYears == 18));
            Service();
            Check("Opening crash alone cannot trigger representative", Intro.Representative == null && !(bool)Field(Intro, "pending"));
            Story.courierRaidTimer = 0;
            Set(Story, "storyServiceNextTick", -1);
            Story.GameComponentTick();
            Tick(5);
            Pawn courier = Field(Intro, "courier") as Pawn;
            Check("Courier quest spawns before representative", courier?.Spawned == true && Intro.Representative == null);
            Service();
            Check("Unresolved courier encounter does not trigger representative", !(bool)Field(Intro, "pending"));
            Call(typeof(CourierConversationUtility), "TryDropItemsAndLeave", courier);
            deadline = (int)Field(Intro, "representativeArrivalTick");
            Check("Actual release choice schedules representative after courier", (bool)Field(Intro, "pending") && deadline - Find.TickManager.TicksGame >= 111000);
            Jump(deadline - 1);
            Service();
            Check("Courier route respects full waiting time", Intro.Representative == null);
            Jump(deadline + 1);
            Service();
            Check("Representative spawns after resolved courier and delay", Intro.Representative?.Spawned == true && !(bool)Field(Intro, "independentContact"));
            DiaNode node = (DiaNode)Call(Intro, "BuildIntroductionDialog", Actor, Intro.Representative);
            Check("Courier route retains released-courier dialogue", node.text.ToString().Contains("Mugirl.CorporateIntro.Reaction.Released".Translate().ToString()));
            // 已放行者不能再选择战斗；生成另一个真实运货员测试互斥的战斗分支。
            RemoveVisitor(Intro.Representative);
            Set(Intro, "representative", null);
            Set(Intro, "pending", false);
            Set(Intro, "courierChoice", 0);
            Set(Intro, "contactTick", -1);
            var spawn = new QuestPart_SpawnCourier { map = Map, faction = CorporateNetwork.Current.CorporateFaction,
                spawnCell = CellFinder.RandomClosewalkCellNear(Actor.Position, Map, 5) };
            Call(spawn, "SpawnCourier");
            courier = spawn.courier;
            Call(typeof(CourierConversationUtility), "TryStartFight", courier);
            deadline = (int)Field(Intro, "representativeArrivalTick");
            Jump(deadline + 1);
            Service();
            Check("Active courier combat delays representative", courier.InAggroMentalState && Intro.Representative == null);
            courier.mindState.mentalStateHandler.Reset();
            Service();
            Check("Representative can arrive after courier combat ends", Intro.Representative?.Spawned == true);
            RemoveVisitor(Intro.Representative);
            Set(Intro, "representative", null);
            courier.Kill(null);
            deadline = (int)Field(Intro, "representativeArrivalTick");
            Check("Courier death preserves access and schedules visit", (bool)Field(Intro, "courierDied") && deadline > Find.TickManager.TicksGame);
            Jump(deadline + 1);
            Service();
            Check("Representative arrives after courier death and delay", Intro.Representative?.Spawned == true);
        }
        private static void RemoveVisitor(Pawn pawn)
        {
            if (pawn?.Spawned != true) return;
            pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily);
            pawn.DeSpawn();
            Find.WorldPawns.PassToWorld(pawn);
        }
        private static void TestRandomEntrances()
        {
            foreach (string name in new[] { "Mugirl_SlaveRefugeePodCrash", "Mugirl_SlaveWandererJoins" })
            {
                var def = DefDatabase<IncidentDef>.GetNamed(name);
                var quests = new HashSet<Quest>(Find.QuestManager.QuestsListForReading);
                var parms = new IncidentParms { target = Map, points = 200f, forced = true };
                Check(name + " resolves positive random weight and bound quest", def.baseChance > 0f && def.questScriptDef?.defName == name);
                Check(name + " can fire on player home map", def.Worker.CanFireNow(parms));
                Check(name + " executes through actual incident worker", def.Worker.TryExecute(parms));
                var quest = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => !quests.Contains(q) && q.root == def.questScriptDef);
                var added = new List<Pawn>();
                if (quest != null)
                    foreach (var part in quest.PartsListForReading)
                        foreach (var field in part.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                        {
                            object value = field.GetValue(part);
                            if (value is Pawn) added.Add((Pawn)value);
                            else if (value is System.Collections.IEnumerable) added.AddRange(((System.Collections.IEnumerable)value).OfType<Pawn>());
                        }
                added = added.Where(p => p.def == Mugirl_DefOf.Mugirl).Distinct().ToList();
                File.AppendAllText(Path.Combine(output, "generated-pawns.txt"), name + ": " + string.Join("; ", added.Select(p => p.kindDef.defName
                    + " stage=" + p.DevelopmentalStage + " faction=" + p.Faction + " downed=" + p.Downed + " dead=" + p.Dead)) + Environment.NewLine);
                Check(name + " generates exactly one adult factionless Mugirl", added.Count == 1 && added[0].def == Mugirl_DefOf.Mugirl
                    && added[0].Faction == null && added[0].DevelopmentalStage == DevelopmentalStage.Adult);
                if (name == "Mugirl_SlaveWandererJoins" && quest != null)
                {
                    var letter = Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_AcceptJoiner>().First(l => l.quest == quest);
                    Find.SignalManager.SendSignal(new Signal(letter.signalAccept));
                    Check("Accepting wandering Mugirl really joins and enters map", added[0].Faction == Faction.OfPlayer && added[0].Spawned);
                }
                else Check("Random rescue is alive and downed", added.Count == 1 && added[0].Downed && !added[0].Dead);
            }
        }
        private static void SpawnNear(Pawn pawn)
        {
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(Actor.Position, Map, 3), Map);
            pawn.SetForbidden(false, false);
        }
        private static int Count(ThingDef def) => Map.listerThings.ThingsOfDef(def).Sum(t => t.stackCount);
        private static void TestMilk()
        {
            // 每例在执行前生成并立即入场，避免不属于任务的临时世界人物在前一例 tick 中被清理。
            Func<Pawn>[] factories = {
                () => new QuestNode_Root_Mugirl_OpeningPodCrash().GeneratePawn(),
                () => new QuestNode_Root_Mugirl_RefugeePodCrash().GeneratePawn(),
                () => PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Mugirl_EscapeWildSlave"), null,
                    forceGenerateNewPawn: true, fixedGender: Gender.Female, fixedBiologicalAge: 24f, fixedChronologicalAge: 24f))
            };
            foreach (Func<Pawn> factory in factories)
            {
                Pawn source = factory();
                SpawnNear(source);
                Pawn actor = Actor;
                var comp = source.TryGetComp<CompMooMilkable>();
                Check(source.kindDef.defName + " production active before recruitment", source.Faction == null && comp.Active);
                Set(comp, "fullness", 0f);
                Set(comp, "lastResourceUpdateTick", -99999);
                comp.CompTick();
                Jump(Find.TickManager.TicksGame + 120);
                comp.CompTick();
                Check(source.kindDef.defName + " milk accumulates naturally without a faction", comp.Fullness > 0f);
                comp.DevFillToFull(false);
                var provider = new FloatMenuProvider_MilkMugirl();
                var context = new FloatMenuContext(new List<Pawn> { actor }, source.DrawPos, Map);
                var options = provider.GetOptionsFor(source, context).ToList();
                var drink = options.FirstOrDefault(o => o.Label == "Mugirl.Milk.FloatMenu.Drink".Translate(source.LabelShortCap).ToString());
                var gather = options.FirstOrDefault(o => o.Label == "Mugirl.Milk.FloatMenu.Gather".Translate(source.LabelShortCap).ToString());
                Check(source.kindDef.defName + " right-click provider accepts unrecruited target", provider.TargetPawnValid(source, context));
                Check(source.kindDef.defName + " drink and gather menu actions enabled", drink != null && !drink.Disabled && gather != null && !gather.Disabled);
                actor.needs.food.CurLevel = 0.05f;
                drink.action();
                Tick(900);
                Check(source.kindDef.defName + " actual drink job consumes milk and feeds actor", comp.Fullness < 0.98f && actor.needs.food.CurLevel > 0.4f);
                comp.DevFillToFull(false);
                int milkBefore = Count(Mugirl_DefOf.Mugirl_Milk);
                gather.action();
                Tick(2000);
                Check(source.kindDef.defName + " actual gather job produces milk", comp.Fullness < 0.9f && Count(Mugirl_DefOf.Mugirl_Milk) > milkBefore);
                Check(source.kindDef.defName + " interactions do not recruit target", source.Faction == null);
            }
            Pawn child = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Mugirl_Colony"), null,
                forceGenerateNewPawn: true, fixedGender: Gender.Female, fixedBiologicalAge: 5f,
                fixedChronologicalAge: 5f, developmentalStages: DevelopmentalStage.Child));
            Check("Unrecruited child still cannot produce milk", !child.TryGetComp<CompMooMilkable>().Active);
            Find.WorldPawns.PassToWorld(child, PawnDiscardDecideMode.Discard);
        }
        private static void Finish()
        {
            phase = 4;
            File.WriteAllText(Path.Combine(output, "complete.txt"), "PASS=" + passed + " FAIL=" + failed);
            Application.Quit();
        }
        private static void TestSharedGeneration()
        {
            var migration = (List<Pawn>)Call(typeof(IncidentWorker_Mugirl_Migration), "GenerateMigrationPawns", Map.Tile);
            Check("Migration retains 4-6 adults, factionless, female and unarmed", migration.Count >= 4 && migration.Count <= 6
                && migration.All(p => p.Faction == null && p.gender == Gender.Female && p.DevelopmentalStage == DevelopmentalStage.Adult
                    && !p.Dead && !p.Downed && p.equipment.Primary == null));
            var runaway = (Pawn)Call(typeof(QuestPart_SpawnRunawayFarm), "GenerateRunaway", Map.Tile);
            Check("Farm generation remains adult, factionless, female and unarmed", runaway.Faction == null && runaway.gender == Gender.Female
                && runaway.DevelopmentalStage == DevelopmentalStage.Adult && !runaway.Dead && !runaway.Downed && runaway.equipment.Primary == null);
            foreach (Pawn pawn in migration.Concat(new[] { runaway })) Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
        }
    }
}
