using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Mugirl
{
    public class CorporateIntroductionDef : Def
    {
        public int longAbsenceTicks = 900000;
        public int retryTicks = 2500;
        public int representativeStayTicks = 240000;
        public int attackRetryTicks = 120000;
        public int contactDelayTicks = 120000;       // 运货员事件后约两天到访。
        public int contactDelayJitterTicks = 9000;   // ±3.6 小时随机浮动。
        public List<ThingDefCountClass> gifts = new List<ThingDefCountClass>();
    }

    [DefOf]
    public static class CorporateIntroductionDefOf
    {
        public static CorporateIntroductionDef Mugirl_CorporateIntroduction;
        public static QuestScriptDef Mugirl_CorporateIntroductionQuest;
        public static JobDef Mugirl_CorporateUseComms;
        public static JobDef Mugirl_CorporateTalkRepresentative;
        static CorporateIntroductionDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(CorporateIntroductionDefOf)); }
    }

    // 主线独立于尚未开放的业务终端；解锁之前通过原版任务列表与实体交谈推进。
    public class CorporateIntroduction : GameComponent, IThingHolder
    {
        private bool initialized;
        private bool migrated;
        private bool pending;
        private bool completed;
        private bool giftsPrepared;
        private bool independentAggression;
        private bool courierKilledByPlayer;
        private bool courierDied;
        private int courierChoice; // 0 未知，1 放行，2 冲突；冲突本身不证明送货员已经死亡。
        private int contactTick = -1;
        private int nextServiceTick;
        private Pawn courier;
        private Pawn representative;
        private Map preferredMap;
        private Quest introductionQuest;
        private ThingOwner<Thing> gifts;
        private bool factionMigrationPending;
        private int nextFactionMigrationTick;
        private int representativeArrivalTick = -1;

        public CorporateIntroduction(Game game) { gifts = new ThingOwner<Thing>(this, false, LookMode.Deep); }
        public static CorporateIntroduction Current => MugirlGameUtility.GameComponent<CorporateIntroduction>();
        public bool Completed => completed;
        public Pawn Representative => representative;
        public IThingHolder ParentHolder => null;
        private static CorporateIntroductionDef Config => CorporateIntroductionDefOf.Mugirl_CorporateIntroduction;
        public ThingOwner GetDirectlyHeldThings() => gifts;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, gifts);

        public override void StartedNewGame()
        {
            initialized = true;
            nextServiceTick = 0;
        }

        public override void LoadedGame()
        {
            // 原版只补充官方 DLC 派系；中途启用模组的旧档可能完全没有巨企实例。
            factionMigrationPending = true;
            nextFactionMigrationTick = 0;
            if (!initialized)
            {
                initialized = true;
                migrated = true;
                pending = true;
                RecoverCourierHistory();
                nextServiceTick = 0;
            }
        }

        public override void GameComponentUpdate()
        {
            if (MugirlGameUtility.IsPlaying() && factionMigrationPending && CorporateNetwork.Now >= nextFactionMigrationTick)
            {
                nextFactionMigrationTick = CorporateNetwork.Now + Config.retryTicks;
                factionMigrationPending = EnsureCorporateFactionAvailable() == null;
            }
            // 旧档暂停加载时也立即登记任务；实体入场在首个有效游戏 tick 尝试。
            if (MugirlGameUtility.IsPlaying() && pending && !completed && introductionQuest == null && MugirlGameUtility.Quests != null)
                EnsureQuest();
        }

        internal Faction EnsureCorporateFactionAvailable()
        {
            FactionManager manager = MugirlGameUtility.Factions;
            FactionDef definition = MugirlContentDefOf.Mugirl_GiantCorporations_Hostile;
            if (manager == null || definition == null) return null;
            // 保留现存实例，包括已败亡的巨企；迁移不复活派系，也不重置历史外交。
            Faction existing = manager.AllFactionsListForReading.FirstOrDefault(f => f.def == definition);
            if (existing != null) return existing;
            try
            {
                // 暂时隐藏可阻止原版生成器在旧世界强插据点；注册前恢复 Def 的默认可见性。
                Faction created = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(definition, hidden: true));
                created.hidden = null;
                manager.Add(created);
                return created;
            }
            catch (Exception ex)
            {
                MugirlLog.WarningOnce("CorporateIntroduction.FactionMigration", "巨企旧档派系初始化失败，将在稍后重试：" + ex.Message);
                return null;
            }
        }

        public override void GameComponentTick()
        {
            int now = CorporateNetwork.Now;
            if (now < nextServiceTick) return;
            nextServiceTick = now + Config.retryTicks;
            if (completed)
            {
                if (gifts.Count > 0) TryDeliverGifts(ResolveMap());
                return;
            }
            if (!pending && courier != null && (courier.Dead || !courier.Spawned))
            {
                courierDied |= courier.Dead;
                pending = true;
                if (contactTick < 0) contactTick = now;
                if (representativeArrivalTick < 0) ScheduleRepresentativeVisit(now);
            }
            if (!pending) return;
            EnsureQuest();
            // 运货员事件后先给殖民地约两天的缓冲，代表才动身到访。
            if (representativeArrivalTick > 0 && now < representativeArrivalTick) return;
            if (representative != null && !representative.Dead && representative.Spawned) return;
            Map map = ResolveMap();
            Faction corporation = CorporateNetwork.Current?.CorporateFaction;
            if (map == null || corporation == null || corporation.defeated) return;
            if (courier?.Spawned == true && courier.InAggroMentalState && !courier.Downed) return;
            // 未苏醒或仍在迷雾中的古代危险不应永久阻断来访；遵循原版活跃威胁判定。
            if (GenHostility.AnyHostileActiveThreatToPlayer(map)) return;
            TrySpawnRepresentative(map, corporation);
        }

        public void RememberCourier(Pawn pawn, Map map)
        {
            if (completed || pawn == null) return;
            courier = pawn;
            preferredMap = map;
        }

        public void NotifyCourierChoice(Pawn pawn, bool released)
        {
            if (completed || pawn == null) return;
            RememberCourier(pawn, pawn.MapHeld);
            courierChoice = released ? 1 : 2;
            contactTick = CorporateNetwork.Now;
            pending = true;
            nextServiceTick = 0;
            ScheduleRepresentativeVisit(CorporateNetwork.Now);
        }

        internal void NotifyDamage(Pawn target, DamageInfo damage)
        {
            if (completed || damage.Amount <= 0f || !MugirlWildSlaveUtility.IsPlayerFaction(damage.Instigator?.Faction)) return;
            if (target == representative)
            {
                independentAggression = true;
                nextServiceTick = CorporateNetwork.Now + Config.attackRetryTicks;
                SendAway(representative);
            }
            else if (target != courier && target.kindDef != MugirlContentDefOf.AI_GC_Courier
                && target.Faction != null && target.Faction == CorporateNetwork.Current?.CorporateFaction)
                independentAggression = true;
        }

        internal void NotifyKilled(Pawn pawn, DamageInfo? damage)
        {
            if (completed || pawn == null) return;
            if (pawn == courier || pawn.kindDef == MugirlContentDefOf.AI_GC_Courier)
            {
                RememberCourier(pawn, pawn.MapHeld ?? preferredMap);
                courierDied = true;
                courierKilledByPlayer |= MugirlWildSlaveUtility.IsPlayerFaction(damage?.Instigator?.Faction);
                pending = true;
                if (contactTick < 0) contactTick = CorporateNetwork.Now;
                nextServiceTick = 0;
                ScheduleRepresentativeVisit(CorporateNetwork.Now);
            }
        }

        internal string DevTriggerRepresentative(Map map)
        {
            if (!Prefs.DevMode || map?.IsPlayerHome != true) return "RequiresHomeMap";
            if (completed) return "AlreadyTriggered";
            if (representative != null && !representative.Dead && representative.Spawned) return "AlreadyTriggered";
            Faction corporation = EnsureCorporateFactionAvailable();
            if (corporation == null || corporation.defeated) return "NoFaction";

            // 只提前来访，不伪造运货员结局、不重置开户状态，也不重新发放见面礼。
            preferredMap = map;
            pending = true;
            representativeArrivalTick = CorporateNetwork.Now;
            nextServiceTick = CorporateNetwork.Now + Config.retryTicks;
            EnsureQuest();
            TrySpawnRepresentative(map, corporation);
            return representative?.Spawned == true ? null : "Failed";
        }

        private void ScheduleRepresentativeVisit(int now)
        {
            int delay = Math.Max(0, Config.contactDelayTicks);
            if (Config.contactDelayJitterTicks > 0)
                delay += Rand.RangeInclusive(-Config.contactDelayJitterTicks, Config.contactDelayJitterTicks);
            representativeArrivalTick = now + Math.Max(0, delay);
        }

        internal int RemainingRepresentativeDelay => completed || representativeArrivalTick <= 0
            ? 0
            : Math.Max(0, representativeArrivalTick - CorporateNetwork.Now);

        private void RecoverCourierHistory()
        {
            if (!MugirlGameUtility.TryGetQuestsListForReading(out List<Quest> quests)) return;
            Quest oldQuest = quests.LastOrDefault(q => q.root == MugirlContentDefOf.Mugirl_CourierRaid);
            QuestPart_SpawnCourier part = oldQuest?.PartsListForReading.OfType<QuestPart_SpawnCourier>().FirstOrDefault();
            if (part != null) RememberCourier(part.courier, part.map);
            if (oldQuest?.State == QuestState.EndedSuccess)
            {
                courierChoice = 1;
                if (oldQuest.cleanupTick > 0) contactTick = oldQuest.cleanupTick;
            }
            if (courier?.Dead == true) courierDied = true;
            // Unknown 也用于刚开始的交战，不能凭此恢复死亡责任。
        }

        private void EnsureQuest()
        {
            if (introductionQuest != null)
            {
                RepairIntroductionQuestRoot();
                return;
            }
            Slate slate = new Slate();
            slate.Set("corporateIntroduction", true);
            introductionQuest = QuestUtility.GenerateQuestAndMakeAvailable(
                CorporateIntroductionDefOf.Mugirl_CorporateIntroductionQuest, slate);
            introductionQuest.name = "Mugirl.CorporateIntro.Title".Translate();
            introductionQuest.description = "Mugirl.CorporateIntro.QuestDescription".Translate();
        }

        private void RepairIntroductionQuestRoot()
        {
            // 早期开发存档的 MakeRaw 任务缺少脚本根；原位补齐，不重建任务或重发奖励。
            if (introductionQuest != null && introductionQuest.root == null)
                introductionQuest.root = CorporateIntroductionDefOf.Mugirl_CorporateIntroductionQuest;
        }

        private Map ResolveMap()
        {
            if (preferredMap != null && MugirlGameUtility.LoadedMaps.Contains(preferredMap) && preferredMap.IsPlayerHome) return preferredMap;
            preferredMap = MugirlGameUtility.LoadedMaps.FirstOrDefault(m => m.IsPlayerHome);
            return preferredMap;
        }

        private void TrySpawnRepresentative(Map map, Faction corporation)
        {
            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, 0f)) return;
            if (representative == null || representative.Dead || representative.Destroyed)
            {
                representative = PawnGenerator.GeneratePawn(new PawnGenerationRequest(MugirlContentDefOf.Mugirl_CorporateRepresentative,
                    corporation, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true,
                    allowDead: false, allowDowned: false, canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: false, forceRecruitable: false, allowPregnant: false,
                    developmentalStages: DevelopmentalStage.Adult));
            }
            if (representative == null || representative.IsPrisoner || representative.IsColonist || representative.IsSlave
                || representative.holdingOwner != null || representative.GetCaravan() != null) return;
            if (MugirlGameUtility.WorldPawns.Contains(representative)) MugirlGameUtility.WorldPawns.RemovePawn(representative);
            // 代表以巨企派系身份入场；开户前的派系好感由主线逻辑自行处理。
            if (representative.Faction != corporation) representative.SetFaction(corporation);
            GenSpawn.Spawn(representative, entry, map);
            IntVec3 center = map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center;
            if (!CellFinder.TryFindRandomCellNear(center, map, 10, c => c.Standable(map) && !c.Fogged(map), out IntVec3 waitCell)) waitCell = entry;
            LordMaker.MakeNewLord(corporation, new LordJob_WaitForDurationThenExit(waitCell, Config.representativeStayTicks), map, Gen.YieldSingle(representative));
            MugirlGameUtility.Letters.ReceiveLetter("Mugirl.CorporateIntro.ArrivalTitle".Translate(),
                "Mugirl.CorporateIntro.ArrivalText".Translate(representative.LabelShortCap), LetterDefOf.NeutralEvent, representative);
            MugirlGameUtility.TrySignalForceNormalSpeedShort();
        }

        public bool CanTalk(Pawn negotiator, Pawn target)
        {
            return !completed && pending && target == representative && target?.Spawned == true && !target.Dead
                && !target.Downed && !target.InAggroMentalState && !target.IsPrisoner && !target.IsSlave
                && negotiator?.Spawned == true && MugirlWildSlaveUtility.IsPlayerFaction(negotiator.Faction) && !negotiator.Dead
                && !negotiator.Downed && negotiator.Map == target.Map && negotiator.RaceProps.Humanlike
                && negotiator.health.capacities.CapableOf(PawnCapacityDefOf.Talking);
        }

        public void ShowDialog(Pawn negotiator, Pawn target)
        {
            if (!CanTalk(negotiator, target)) return;
            string opening = "Mugirl.CorporateIntro.Opening";
            if (migrated)
            {
                if (contactTick >= 0 && CorporateNetwork.Now - contactTick >= Config.longAbsenceTicks) opening = "Mugirl.CorporateIntro.OpeningLong";
                else if (contactTick < 0 && CorporateNetwork.Now >= Config.longAbsenceTicks) opening = "Mugirl.CorporateIntro.OpeningLate";
                else opening = "Mugirl.CorporateIntro.OpeningOldSave";
            }
            string reaction = courierKilledByPlayer ? "Mugirl.CorporateIntro.Reaction.Killed" : courierDied ? "Mugirl.CorporateIntro.Reaction.DeadUnknown"
                : courierChoice == 1 ? "Mugirl.CorporateIntro.Reaction.Released" : courierChoice == 2 ? "Mugirl.CorporateIntro.Reaction.Conflict"
                : courier?.Spawned == true ? "Mugirl.CorporateIntro.Reaction.Unresolved" : "Mugirl.CorporateIntro.Reaction.Unknown";
            bool hasMugirl = MugirlGameUtility.LoadedMaps.Where(m => m.IsPlayerHome).Any(m => m.mapPawns.AllPawnsSpawned.Any(p => MugirlIdentity.IsMugirlPawn(p) && MugirlWildSlaveUtility.IsPlayerFaction(p.Faction)));
            DiaNode main = new DiaNode(opening.Translate() + "\n\n" + reaction.Translate()
                + "\n\n" + (hasMugirl ? "Mugirl.CorporateIntro.KeepMugirl" : "Mugirl.CorporateIntro.NoReclaim").Translate()
                + "\n\n" + "Mugirl.CorporateIntro.Proposal".Translate());
            AddQuestion(main, "Mugirl.CorporateIntro.Question.Courier", "Mugirl.CorporateIntro.Answer.Courier");
            AddQuestion(main, "Mugirl.CorporateIntro.Question.Mugirl", "Mugirl.CorporateIntro.Answer.Mugirl");
            AddQuestion(main, "Mugirl.CorporateIntro.Question.Services", "Mugirl.CorporateIntro.Answer.Services");
            main.options.Add(new DiaOption("Mugirl.CorporateIntro.Accept".Translate())
            {
                action = () => Complete(negotiator, target), resolveTree = true
            });
            main.options.Add(new DiaOption("Mugirl.CorporateIntro.Postpone".Translate()) { resolveTree = true });
            MugirlGameUtility.Windows.Add(new Dialog_NodeTree(main, delayInteractivity: true, title: "Mugirl.CorporateIntro.Title".Translate()));
        }

        private static void AddQuestion(DiaNode main, string questionKey, string answerKey)
        {
            DiaNode answer = new DiaNode(answerKey.Translate());
            answer.options.Add(new DiaOption("Mugirl.CorporateIntro.Back".Translate()) { link = main });
            main.options.Add(new DiaOption(questionKey.Translate()) { link = answer });
        }

        private void Complete(Pawn negotiator, Pawn target)
        {
            if (!CanTalk(negotiator, target) || !PrepareGifts()) return;
            EnsureQuest();
            completed = true;
            pending = false;
            preferredMap = negotiator.Map;
            Faction corporation = CorporateNetwork.Current?.CorporateFaction;
            if (!independentAggression && corporation != null && corporation.PlayerGoodwill < 0)
                corporation.TryAffectGoodwillWith(MugirlWildSlaveUtility.PlayerFaction, -corporation.PlayerGoodwill, false, false);
            TryDeliverGifts(preferredMap);
            introductionQuest?.End(QuestEndOutcome.Success, sendLetter: false, playSound: false);
            string text = "Mugirl.CorporateIntro.CompletedLetter".Translate();
            if (corporation?.HostileTo(MugirlWildSlaveUtility.PlayerFaction) == true) text += "\n\n" + "Mugirl.CorporateIntro.HostilityRemains".Translate();
            MugirlGameUtility.Letters.ReceiveLetter("Mugirl.CorporateIntro.CompletedStatus".Translate(), text, LetterDefOf.PositiveEvent, negotiator);
            CorporateNetwork.Current?.Record("Mugirl.CorporateIntro.Title", "Mugirl.CorporateIntro.CompletedStatus".Translate());
            CorporateNetwork.Current?.EnsureWeeklyOffers();
            SendAway(target);
        }

        private bool PrepareGifts()
        {
            if (giftsPrepared) return true;
            List<Thing> prepared = new List<Thing>();
            try
            {
                foreach (ThingDefCountClass reward in Config.gifts)
                {
                    int left = reward.count;
                    while (left > 0)
                    {
                        Thing thing = ThingMaker.MakeThing(reward.thingDef);
                        thing.stackCount = Math.Min(left, Math.Max(1, thing.def.stackLimit));
                        left -= thing.stackCount;
                        prepared.Add(thing);
                    }
                }
                foreach (Thing thing in prepared)
                    if (!gifts.TryAdd(thing, false)) throw new InvalidOperationException("Mugirl.CorporateIntro.GiftStorageError".Translate());
                giftsPrepared = true;
                return true;
            }
            catch (Exception ex)
            {
                foreach (Thing thing in prepared)
                {
                    if (thing.holdingOwner == gifts) gifts.Remove(thing);
                    if (!thing.Destroyed && thing.holdingOwner == null) thing.Destroy();
                }
                MugirlLog.WarningOnce("CorporateIntroduction.Gifts", "Mugirl.CorporateIntro.GiftError".Translate(ex.Message));
                return false;
            }
        }

        private void TryDeliverGifts(Map map)
        {
            if (map == null || gifts.Count == 0) return;
            var delivery = new CorporateTradeContext(map);
            foreach (Thing thing in gifts.ToList()) delivery.Deliver(thing);
        }

        private static void SendAway(Pawn pawn)
        {
            if (pawn?.Spawned != true || pawn.Dead || pawn.Downed) return;
            pawn.GetLord()?.RemovePawn(pawn);
            LordMaker.MakeNewLord(pawn.Faction, new LordJob_ExitMapBest(LocomotionUrgency.Jog, canDig: false, canDefendSelf: true), pawn.Map, Gen.YieldSingle(pawn));
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref initialized, "corporateIntroductionInitialized", false);
            Scribe_Values.Look(ref migrated, "migrated", false);
            Scribe_Values.Look(ref pending, "pending", false);
            Scribe_Values.Look(ref completed, "completed", false);
            Scribe_Values.Look(ref giftsPrepared, "giftsPrepared", false);
            Scribe_Values.Look(ref independentAggression, "independentAggression", false);
            Scribe_Values.Look(ref courierKilledByPlayer, "courierKilledByPlayer", false);
            Scribe_Values.Look(ref courierDied, "courierDied", false);
            Scribe_Values.Look(ref courierChoice, "courierChoice", 0);
            Scribe_Values.Look(ref contactTick, "contactTick", -1);
            Scribe_Values.Look(ref nextServiceTick, "nextServiceTick", 0);
            Scribe_Values.Look(ref representativeArrivalTick, "representativeArrivalTick", -1);
            Scribe_References.Look(ref courier, "courier");
            Scribe_References.Look(ref representative, "representative");
            Scribe_References.Look(ref preferredMap, "preferredMap");
            Scribe_References.Look(ref introductionQuest, "introductionQuest");
            Scribe_Deep.Look(ref gifts, "gifts", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (gifts == null) gifts = new ThingOwner<Thing>(this, false, LookMode.Deep);
                RepairIntroductionQuestRoot();
            }
        }
    }

    public class QuestNode_Root_CorporateIntroduction : QuestNode
    {
        protected override bool TestRunInt(Slate slate) => slate.Get<bool>("corporateIntroduction");
        protected override void RunInt()
        {
            QuestGen.quest.AddPart(new QuestPart_CorporateIntroduction());
        }
    }

    public class QuestPart_CorporateIntroduction : QuestPart
    {
        public override string DescriptionPart
        {
            get
            {
                string text = "Mugirl.CorporateIntro.QuestProgress".Translate();
                int remaining = CorporateIntroduction.Current?.RemainingRepresentativeDelay ?? 0;
                if (remaining > 0)
                    text += "\n" + "Mugirl.CorporateIntro.QuestProgressWait".Translate(GenDate.ToStringTicksToPeriod(remaining));
                return text;
            }
        }
        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                Pawn pawn = CorporateIntroduction.Current?.Representative;
                if (pawn?.Spawned == true) yield return pawn;
            }
        }
        public override bool QuestPartReserves(Pawn pawn) => CorporateIntroduction.Current?.Completed != true && pawn == CorporateIntroduction.Current?.Representative;
    }
}
