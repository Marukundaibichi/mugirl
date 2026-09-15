// 仅在隔离验证构建中编译；由带存档路径安全门的 CorporateRuntimeValidation 调用。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public static class CorporateIntroRuntimeChecks
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run(Map map, CorporateNetwork network, CorporateTradeContext context, Action<string, bool> check)
        {
            CorporateIntroduction intro = CorporateIntroduction.Current;
            Dictionary<FieldInfo, object> original = typeof(CorporateIntroduction).GetFields(PrivateInstance)
                .ToDictionary(field => field, field => field.GetValue(intro));
            HashSet<Window> windows = new HashSet<Window>(Find.WindowStack.Windows);
            List<Quest> questsBefore = Find.QuestManager.QuestsListForReading.ToList();
            List<Pawn> fixtures = new List<Pawn>();
            Building_CommsConsole console = null;
            Caravan caravan = null;
            int longAbsence = CorporateIntroductionDefOf.Mugirl_CorporateIntroduction.longAbsenceTicks;
            try
            {
                CheckGiftDefs(check);
                Pawn actor = MakePawn(map, Faction.OfPlayer, true);
                Pawn employee = MakePawn(map, null, true);
                Pawn courier = MakePawn(map, null, true);
                fixtures.AddRange(new[] { actor, employee, courier });
                ResetIntro(intro, map, employee);
                intro.NotifyCourierChoice(courier, true);
                check("intro release records pending visit without death or player aggression", Get<bool>(intro, "pending")
                    && Get<int>(intro, "courierChoice") == 1 && !Get<bool>(intro, "courierDied") && !Get<bool>(intro, "independentAggression"));
                string released = ReadDialog(intro, actor, employee);
                check("actual release dialogue acknowledges release and preserves gift proposal", Contains(released, "Mugirl.CorporateIntro.Reaction.Released")
                    && Contains(released, "Mugirl.CorporateIntro.Proposal") && HasNoReclaimPromise(released));

                ResetIntro(intro, map, employee);
                intro.NotifyCourierChoice(courier, false);
                check("intro conflict does not claim courier death", Get<int>(intro, "courierChoice") == 2
                    && !Get<bool>(intro, "courierDied") && !Get<bool>(intro, "courierKilledByPlayer"));
                string conflict = ReadDialog(intro, actor, employee);
                check("actual conflict dialogue differs from killed dialogue", Contains(conflict, "Mugirl.CorporateIntro.Reaction.Conflict")
                    && !Contains(conflict, "Mugirl.CorporateIntro.Reaction.Killed"));
                courier.Kill(new DamageInfo(DamageDefOf.Bullet, 9999f, instigator: actor));
                check("actual courier death hook attributes player killing and schedules visit", courier.Dead
                    && Get<bool>(intro, "courierDied") && Get<bool>(intro, "courierKilledByPlayer") && Get<bool>(intro, "pending")
                    && !Get<bool>(intro, "independentAggression"));
                string killed = ReadDialog(intro, actor, employee);
                check("actual killed dialogue changes reaction but keeps Mugirl and gifts", Contains(killed, "Mugirl.CorporateIntro.Reaction.Killed")
                    && Contains(killed, "Mugirl.CorporateIntro.Proposal") && HasNoReclaimPromise(killed) && killed != released);

                Set(intro, "migrated", true);
                CorporateIntroductionDefOf.Mugirl_CorporateIntroduction.longAbsenceTicks = 10;
                Set(intro, "contactTick", CorporateNetwork.Now);
                string recent = ReadDialog(intro, actor, employee);
                // 即使测试在第零 tick 暂停，也能分别进入实际的短期与久别对白分支。
                CorporateIntroductionDefOf.Mugirl_CorporateIntroduction.longAbsenceTicks = 0;
                Set(intro, "contactTick", CorporateNetwork.Now);
                string old = ReadDialog(intro, actor, employee);
                check("actual old-save dialogue distinguishes recent contact and long absence", Contains(recent, "Mugirl.CorporateIntro.OpeningOldSave")
                    && Contains(old, "Mugirl.CorporateIntro.OpeningLong") && recent != old);
                Set(intro, "contactTick", -1);
                check("old save with no reliable courier history uses late arrival wording", Contains(ReadDialog(intro, actor, employee), "Mugirl.CorporateIntro.OpeningLate"));

                ResetIntro(intro, map, employee);
                Set(intro, "initialized", false);
                Set(intro, "pending", false);
                Set(intro, "introductionQuest", null);
                intro.LoadedGame();
                intro.GameComponentUpdate();
                Quest migratedQuest = Get<Quest>(intro, "introductionQuest");
                check("old save immediately registers accepted introduction with a real quest root", Get<bool>(intro, "migrated")
                    && Get<bool>(intro, "pending") && migratedQuest != null && migratedQuest.root == CorporateIntroductionDefOf.Mugirl_CorporateIntroductionQuest
                    && migratedQuest.State == QuestState.Ongoing && Find.QuestManager.QuestsListForReading.Contains(migratedQuest));
                intro.LoadedGame();
                intro.GameComponentUpdate();
                check("repeated old-save initialization does not duplicate intro quest", ReferenceEquals(migratedQuest, Get<Quest>(intro, "introductionQuest"))
                    && Find.QuestManager.QuestsListForReading.Count(q => !questsBefore.Contains(q)) == 1);
                CheckDormantArrival(intro, map, employee, fixtures, check);

                console = MakeConsole(map);
                actor.jobs.StopAll();
                actor.Position = GenRadial.RadialCellsAround(console.InteractionCell, 9f, true).First(cell => cell.InBounds(map)
                    && cell.Standable(map) && cell.DistanceTo(console.InteractionCell) >= 5f
                    && map.reachability.CanReach(cell, console, PathEndMode.InteractionCell, TraverseParms.For(actor)));
                Set(intro, "completed", false);
                FloatMenuOption locked = ConsoleOption(console, actor);
                check("actual comms menu contains disabled introduction gate", locked != null && locked.Disabled);
                Set(intro, "completed", true);
                FloatMenuOption enabled = ConsoleOption(console, actor);
                check("actual comms menu enables corporate contact after introduction", enabled != null && !enabled.Disabled);
                enabled?.action?.Invoke();
                // 从交互格外发起，避免原版 StartPath 在已抵达时同步完成整个通讯作业。
                check("comms menu starts dedicated save-safe communication job and real approach path (job=" + actor.CurJob?.def?.defName
                    + ", driver=" + actor.jobs.curDriver?.GetType().Name + ", moving=" + actor.pather.Moving + ")",
                    actor.CurJob?.def == CorporateIntroductionDefOf.Mugirl_CorporateUseComms && actor.CurJob.targetA.Thing == console
                    && actor.CurJob.commTarget == null && actor.jobs.curDriver is JobDriver_CorporateUseComms && actor.pather.Moving);
                actor.jobs.StopAll();
                actor.Position = console.InteractionCell;
                console.TryGetComp<CompPowerTrader>().PowerOn = false;
                check("power loss disables the actual comms menu", ConsoleOption(console, actor)?.Disabled == true);
                console.TryGetComp<CompPowerTrader>().PowerOn = true;
                CorporateCommsAccess.OpenConsole(console, actor);
                Window_CorporateComms terminal = Find.WindowStack.Windows.OfType<Window_CorporateComms>().LastOrDefault();
                CorporateTradeContext terminalContext = terminal == null ? null : (CorporateTradeContext)typeof(Window_CorporateComms)
                    .GetField("context", PrivateInstance).GetValue(terminal);
                check("console opens a live map context using the communicating pawn", terminalContext?.IsValid == true && terminalContext.Negotiator == actor);
                console.TryGetComp<CompPowerTrader>().PowerOn = false;
                check("opened console context becomes invalid immediately after power loss", terminalContext != null && !terminalContext.IsValid);
                terminal?.Close();
                console.TryGetComp<CompPowerTrader>().PowerOn = true;

                CheckMissingFactionMigration(intro, network, console, actor, check);

                Settlement settlement = Find.WorldObjects.SettlementBases.FirstOrDefault(s => s.Faction == network.CorporateFaction);
                check("generated-world fixture contains an actual corporate settlement", settlement != null);
                if (settlement != null)
                {
                    Pawn traveler = MakePawn(map, Faction.OfPlayer, false);
                    fixtures.Add(traveler);
                    caravan = CaravanMaker.MakeCaravan(new[] { traveler }, Faction.OfPlayer, settlement.Tile, true);
                    check("corporate settlement accepts only an actually arrived stopped caravan", CorporateCommsAccess.AtSettlement(caravan, settlement));
                    Set(intro, "completed", false);
                    check("actual settlement command is disabled before introduction", SettlementCommand(settlement, caravan)?.Disabled == true);
                    Set(intro, "completed", true);
                    Command_Action command = SettlementCommand(settlement, caravan);
                    check("actual settlement command is enabled after introduction", command != null && !command.Disabled);
                    command?.action?.Invoke();
                    Window_CorporateComms remote = Find.WindowStack.Windows.OfType<Window_CorporateComms>().LastOrDefault();
                    CorporateTradeContext remoteContext = remote == null ? null : (CorporateTradeContext)typeof(Window_CorporateComms).GetField("context", PrivateInstance).GetValue(remote);
                    check("settlement command opens same terminal with caravan context", remoteContext?.Caravan == caravan && remoteContext.IsValid && remoteContext.Negotiator == traveler);
                    caravan.Tile = map.Tile;
                    check("departure invalidates arrival gate and already-open terminal", !CorporateCommsAccess.AtSettlement(caravan, settlement)
                        && remoteContext != null && !remoteContext.IsValid && SettlementCommand(settlement, caravan) == null);
                    remote?.Close();
                }
            }
            finally
            {
                // 先还原主线，再清理夹具，避免死亡/离场回调污染真实测试状态。
                foreach (KeyValuePair<FieldInfo, object> field in original) field.Key.SetValue(intro, field.Value);
                CorporateIntroductionDefOf.Mugirl_CorporateIntroduction.longAbsenceTicks = longAbsence;
                foreach (Window window in Find.WindowStack.Windows.Where(w => !windows.Contains(w)).ToList()) window.Close();
                foreach (Quest quest in Find.QuestManager.QuestsListForReading.Where(q => !questsBefore.Contains(q)).ToList()) Find.QuestManager.Remove(quest);
                if (caravan != null)
                {
                    foreach (Pawn pawn in caravan.PawnsListForReading.ToList()) caravan.RemovePawn(pawn);
                    if (caravan.Spawned) Find.WorldObjects.Remove(caravan);
                }
                if (console != null && !console.Destroyed) console.Destroy();
                foreach (Pawn pawn in fixtures) DiscardFixture(pawn);
                context.Invalidate();
            }
        }

        private static void CheckGiftDefs(Action<string, bool> check)
        {
            List<ThingDefCountClass> gifts = CorporateIntroductionDefOf.Mugirl_CorporateIntroduction.gifts;
            var expected = new Dictionary<string, int> { { "Silver", 500 }, { "Steel", 100 }, { "ComponentIndustrial", 10 }, { "MedicineIndustrial", 10 } };
            check("intro gifts resolve all four actual ThingDefs with their intended quantities", gifts != null && gifts.Count == expected.Count
                && gifts.All(g => g.thingDef != null && expected.TryGetValue(g.thingDef.defName, out int count) && count == g.count)
                && gifts.Select(g => g.thingDef).Distinct().Count() == expected.Count);
        }

        private static void CheckMissingFactionMigration(CorporateIntroduction intro, CorporateNetwork network,
            Building_CommsConsole console, Pawn actor, Action<string, bool> check)
        {
            FactionManager manager = Find.FactionManager;
            List<Faction> original = manager.AllFactionsListForReading.ToList();
            Dictionary<Faction, int> goodwill = original.Where(f => !f.IsPlayer).ToDictionary(f => f, f => f.PlayerGoodwill);
            List<Faction> corporate = original.Where(f => f.def == MugirlContentDefOf.Mugirl_GiantCorporations_Hostile).ToList();
            List<Faction> removalQueue = (List<Faction>)typeof(FactionManager).GetField("toRemove", PrivateInstance).GetValue(manager);
            List<Faction> removalQueueBefore = removalQueue.ToList();
            Faction created = null;
            Pawn createdLeader = null;
            int settlements = Find.WorldObjects.SettlementBases.Count;
            try
            {
                // 临时遮蔽 Def 身份，保留旧派系的世界引用和关系生成，避免制造悬空据点关系。
                FactionDef alternate = original.First(f => !f.IsPlayer && f.def != MugirlContentDefOf.Mugirl_GiantCorporations_Hostile).def;
                foreach (Faction faction in corporate) faction.def = alternate;
                typeof(FactionManager).GetMethod("RecacheFactions", PrivateInstance).Invoke(manager, null);
                check("old-save faction fixture has no corporate instance", network.CorporateFaction == null);
                intro.LoadedGame();
                intro.GameComponentUpdate();
                created = network.CorporateFaction;
                createdLeader = created?.leader;
                check("old save generates exactly one missing corporate faction without adding settlements", created != null
                    && !created.Hidden && manager.AllFactionsListForReading.Count(f => f.def == MugirlContentDefOf.Mugirl_GiantCorporations_Hostile) == 1
                    && Find.WorldObjects.SettlementBases.Count == settlements);
                check("missing faction migration retains every pre-existing faction's goodwill", goodwill.All(pair => pair.Key.PlayerGoodwill == pair.Value));
                check("corporate console is usable without a settlement after faction migration", created != null
                    && !Find.WorldObjects.SettlementBases.Any(s => s.Faction == created) && ConsoleOption(console, actor)?.Disabled == false);
                intro.LoadedGame();
                intro.GameComponentUpdate();
                check("repeated load preserves migrated faction identity and does not duplicate it", ReferenceEquals(created, network.CorporateFaction)
                    && manager.AllFactionsListForReading.Count(f => f.def == MugirlContentDefOf.Mugirl_GiantCorporations_Hostile) == 1);
            }
            finally
            {
                if (created != null)
                {
                    created.temporary = true;
                    typeof(FactionManager).GetMethod("Remove", PrivateInstance).Invoke(manager, new object[] { created });
                    DiscardFixture(createdLeader);
                }
                manager.AllFactionsListForReading.Clear();
                foreach (Faction faction in corporate) faction.def = MugirlContentDefOf.Mugirl_GiantCorporations_Hostile;
                manager.AllFactionsListForReading.AddRange(original);
                // 原版清理回调可能重新登记待移除项；夹具派系不能进入后续完整存档。
                removalQueue.Clear();
                removalQueue.AddRange(removalQueueBefore);
                typeof(FactionManager).GetMethod("RecacheFactions", PrivateInstance).Invoke(manager, null);
            }
        }

        private static void CheckDormantArrival(CorporateIntroduction intro, Map map, Pawn originalRepresentative,
            List<Pawn> fixtures, Action<string, bool> check)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.AllDefsListForReading.First(d => d.race?.race?.IsMechanoid == true
                && d.race.comps.Any(c => typeof(CompCanBeDormant).IsAssignableFrom(c.compClass)));
            Pawn threat = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfMechanoids,
                PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: false));
            fixtures.Add(threat);
            IntVec3 cell = GenRadial.RadialCellsAround(map.Center, 15f, true).First(c => c.InBounds(map) && c.Standable(map) && !c.Fogged(map));
            GenSpawn.Spawn(threat, cell, map);
            CompCanBeDormant dormancy = threat.TryGetComp<CompCanBeDormant>();
            dormancy.WakeUp();
            Set(intro, "representative", null);
            Set(intro, "courier", null);
            Set(intro, "pending", true);
            Set(intro, "completed", false);
            Set(intro, "nextServiceTick", 0);
            check("awake hostile fixture is an actual active threat", GenHostility.IsActiveThreatToPlayer(threat));
            intro.GameComponentTick();
            check("active hostile threat postpones the real representative service", intro.Representative == null);
            dormancy.ToSleep();
            bool sleeping = !dormancy.Awake && !GenHostility.IsActiveThreatToPlayer(threat);
            check("dormant hostile remains present but is excluded by vanilla active-threat checks", sleeping && threat.Spawned && threat.HostileTo(Faction.OfPlayer));
            Set(intro, "nextServiceTick", 0);
            intro.GameComponentTick();
            Pawn arriving = intro.Representative;
            check("real representative arrives while dormant hostile remains alive on map", sleeping && threat.Spawned && !threat.Dead
                && arriving?.Spawned == true && arriving != originalRepresentative);
            if (arriving != null && arriving != originalRepresentative)
            {
                fixtures.Add(arriving);
                if (arriving.Spawned) arriving.DeSpawn();
            }
            Set(intro, "representative", originalRepresentative);
            threat.DeSpawn();
        }

        private static Pawn MakePawn(Map map, Faction faction, bool spawn)
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, faction,
                PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: false, allowPregnant: false, forceNoIdeo: true, developmentalStages: DevelopmentalStage.Adult));
            if (spawn) GenSpawn.Spawn(pawn, CellFinder.StandableCellNear(map.Center, map, 15), map);
            return pawn;
        }

        private static Building_CommsConsole MakeConsole(Map map)
        {
            var console = (Building_CommsConsole)ThingMaker.MakeThing(ThingDefOf.CommsConsole);
            console.SetFaction(Faction.OfPlayer);
            IntVec3 cell = GenRadial.RadialCellsAround(map.Center, 30f, true).First(c => c.InBounds(map)
                && GenAdj.OccupiedRect(c, Rot4.North, console.def.Size).Cells.All(t => t.InBounds(map) && t.Standable(map) && t.GetEdifice(map) == null)
                && (c + console.def.interactionCellOffset).InBounds(map) && (c + console.def.interactionCellOffset).Standable(map));
            GenSpawn.Spawn(console, cell, map, Rot4.North);
            console.TryGetComp<CompPowerTrader>().PowerOn = true;
            return console;
        }

        private static FloatMenuOption ConsoleOption(Building_CommsConsole console, Pawn pawn) => console.GetFloatMenuOptions(pawn)
            .FirstOrDefault(o => o.Label.StartsWith("Mugirl.CorporateUI.CallOption".Translate().ToString(), StringComparison.Ordinal));

        private static Command_Action SettlementCommand(Settlement settlement, Caravan caravan) => settlement.GetCaravanGizmos(caravan)
            .OfType<Command_Action>().FirstOrDefault(c => c.defaultLabel == "Mugirl.CorporateUI.SettlementOption".Translate().ToString());

        private static void ResetIntro(CorporateIntroduction intro, Map map, Pawn employee)
        {
            foreach (string field in new[] { "completed", "migrated", "courierDied", "courierKilledByPlayer", "independentAggression" }) Set(intro, field, false);
            Set(intro, "pending", true);
            Set(intro, "courierChoice", 0);
            Set(intro, "contactTick", -1);
            Set(intro, "courier", null);
            Set(intro, "representative", employee);
            Set(intro, "preferredMap", map);
        }

        private static string ReadDialog(CorporateIntroduction intro, Pawn actor, Pawn employee)
        {
            HashSet<Window> old = new HashSet<Window>(Find.WindowStack.Windows);
            intro.ShowDialog(actor, employee);
            Dialog_NodeTree dialog = Find.WindowStack.Windows.OfType<Dialog_NodeTree>().LastOrDefault(w => !old.Contains(w));
            if (dialog == null) throw new InvalidOperationException("Intro did not create a real dialogue");
            DiaNode node = (DiaNode)typeof(Dialog_NodeTree).GetField("curNode", PrivateInstance).GetValue(dialog);
            string result = node.text.ToString();
            dialog.Close();
            return result;
        }

        private static bool HasNoReclaimPromise(string text) => Contains(text, "Mugirl.CorporateIntro.KeepMugirl") || Contains(text, "Mugirl.CorporateIntro.NoReclaim");
        private static bool Contains(string text, string key) => text.Contains(key.Translate().ToString());
        private static T Get<T>(CorporateIntroduction intro, string field) => (T)typeof(CorporateIntroduction).GetField(field, PrivateInstance).GetValue(intro);
        private static void Set(CorporateIntroduction intro, string field, object value) => typeof(CorporateIntroduction).GetField(field, PrivateInstance).SetValue(intro, value);
        private static void DiscardFixture(Pawn pawn)
        {
            if (pawn == null) return;
            pawn.jobs?.StopAll();
            if (pawn.Spawned) pawn.DeSpawn();
            if (pawn.Corpse != null && !pawn.Corpse.Destroyed) pawn.Corpse.Destroy();
            MugirlGeneratedPawnUtility.Discard(pawn);
        }
    }
}
