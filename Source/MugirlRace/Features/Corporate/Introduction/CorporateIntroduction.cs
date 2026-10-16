using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Mugirl.Features.Incidents;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse.AI;
using Verse.AI.Group;

namespace Mugirl.Features.Corporate;

// 开户主线（类名随 GameComponent 写入存档）：运货员事件后巨企代表到访，交谈接受后解锁业务终端并发放见面礼。
// 主线独立于尚未开放的业务终端；解锁之前通过原版任务列表与实体交谈推进。
public class CorporateIntroduction : GameComponent, IThingHolder
{
    private bool contactRead;

    private readonly CorporateContactState contact = new();
    private ThingOwner<Thing> gifts;
    private bool factionCheckPending;
    private int nextFactionCheckTick;
    // 不保存：原版 QuestGen 出错返回 null 时的重试时刻，避免 GameComponentUpdate 每帧重复生成并刷错误日志。
    private int nextQuestAttemptTick;

    public CorporateIntroduction(Game game)
    {
        gifts = new ThingOwner<Thing>(this, false, LookMode.Deep);
    }

    public static CorporateIntroduction? Current => GameUtility.GameComponent<CorporateIntroduction>();
    public bool Completed => contact.accountOpened;
    internal bool IndependentContact => contact.usesIndependentContact;
    public Pawn? Representative => contact.representativePawn;
    public IThingHolder? ParentHolder => null;
    private static CorporateIntroductionConfigDef Config => CorporateDefOf.Mugirl_CorporateIntroductionConfig;
    public ThingOwner GetDirectlyHeldThings() => gifts;
    public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, gifts);

    internal int RemainingRepresentativeDelay => contact.accountOpened || contact.visitDueTick <= 0
        ? 0
        : Math.Max(0, contact.visitDueTick - CorporateNetwork.Now);

    public override void StartedNewGame()
    {
        contact.attached = true;
        contact.serviceResumeTick = 0;
    }

    public override void LoadedGame()
    {
        // 原版只补充官方 DLC 派系；中途启用模组的旧档可能完全没有巨企实例。
        factionCheckPending = true;
        nextFactionCheckTick = 0;
        if (contact.attached)
        {
            return;
        }

        contact.attached = true;
        contact.retrofitted = true;
        RecoverCourierHistory();
        GameComponent_Storyline? story = GameUtility.GameComponent<GameComponent_Storyline>();
        // 中途加装也遵循出场顺序；从已有任务事实恢复已发生的联系。
        contact.visitPending = contact.courierOutcome != 0 || contact.courierDead || (contact.courierPawn != null && !contact.courierPawn.Spawned)
            || (contact.courierPawn == null && story?.CourierTriggered == true);
        if (contact.visitPending)
        {
            ScheduleRepresentativeVisit(contact.lastContactTick >= 0 ? contact.lastContactTick : CorporateNetwork.Now);
        }

        UpdateIndependentContact(CorporateNetwork.Now);
        contact.serviceResumeTick = 0;
    }

    public override void GameComponentUpdate()
    {
        if (!GameUtility.IsPlaying())
        {
            return;
        }

        if (factionCheckPending && CorporateNetwork.Now >= nextFactionCheckTick)
        {
            nextFactionCheckTick = CorporateNetwork.Now + Config.retryTicks;
            factionCheckPending = EnsureCorporateFactionAvailable() == null;
        }

        // 暂停加载时也立即登记任务；实体入场在首个有效游戏 tick 尝试。
        if (contact.visitPending && !contact.accountOpened && contact.quest == null && GameUtility.Quests != null)
        {
            EnsureQuest();
        }
    }

    internal Faction? EnsureCorporateFactionAvailable()
    {
        FactionManager? manager = GameUtility.Factions;
        FactionDef definition = CorporateDefOf.Mugirl_GiantCorporation;
        if (manager == null || definition == null)
        {
            return null;
        }

        // 保留现存实例，包括已败亡的巨企；附着不复活派系，也不重置历史外交。
        Faction? existing = manager.AllFactionsListForReading.Find(f => f.def == definition);
        if (existing != null)
        {
            return existing;
        }

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
            MugirlLog.WarningOnce("CorporateIntroduction.FactionMigration",
                "Corporate faction could not be added to this older save; retrying later: " + ex.Message);
            return null;
        }
    }

    public override void GameComponentTick()
    {
        int now = CorporateNetwork.Now;
        if (now < contact.serviceResumeTick)
        {
            return;
        }

        contact.serviceResumeTick = now + Config.retryTicks;
        if (contact.accountOpened)
        {
            if (gifts.Count > 0)
            {
                TryDeliverGifts(ResolveMap());
            }

            return;
        }

        UpdateIndependentContact(now);
        if (!contact.visitPending && contact.courierPawn != null && (contact.courierPawn.Dead || !contact.courierPawn.Spawned))
        {
            contact.courierDead |= contact.courierPawn.Dead;
            contact.visitPending = true;
            if (contact.lastContactTick < 0)
            {
                contact.lastContactTick = now;
            }

            if (contact.visitDueTick < 0)
            {
                ScheduleRepresentativeVisit(now);
            }
        }

        if (!contact.visitPending)
        {
            return;
        }

        EnsureQuest();
        // 运货员事件后先给殖民地约两天的缓冲，代表才动身到访。
        if (contact.visitDueTick > 0 && now < contact.visitDueTick)
        {
            return;
        }

        if (contact.representativePawn != null && !contact.representativePawn.Dead && contact.representativePawn.Spawned)
        {
            return;
        }

        Map? map = ResolveMap();
        Faction? corporation = CorporateNetwork.Current?.CorporateFaction;
        if (map == null || corporation == null || corporation.defeated)
        {
            return;
        }

        if (contact.courierPawn?.Spawned == true && contact.courierPawn.InAggroMentalState && !contact.courierPawn.Downed)
        {
            return;
        }

        // 未苏醒或仍在迷雾中的古代危险不应永久阻断来访；遵循原版活跃威胁判定。
        if (GenHostility.AnyHostileActiveThreatToPlayer(map))
        {
            return;
        }

        TrySpawnRepresentative(map, corporation);
    }

    public void RememberCourier(Pawn? pawn, Map? map)
    {
        if (contact.accountOpened || pawn == null)
        {
            return;
        }

        if (contact.usesIndependentContact && contact.representativePawn == null)
        {
            contact.visitPending = false;
            contact.visitDueTick = -1;
        }

        contact.usesIndependentContact = false;
        contact.courierPawn = pawn;
        contact.visitMap = map;
    }

    private bool CanArrangeIndependentContact()
    {
        GameComponent_Storyline? story = GameUtility.GameComponent<GameComponent_Storyline>();
        return MugirlMod.Settings?.enableOpeningStoryline == false
            && story != null && !story.OpeningCrashStarted
            && !story.CourierTriggered && !story.CourierQuestStarted
            && contact.courierPawn == null && contact.courierOutcome == 0 && !contact.courierDead && contact.lastContactTick < 0;
    }

    private void UpdateIndependentContact(int now)
    {
        bool canArrange = CanArrangeIndependentContact();
        // 代表已经来过时保留路线与重访状态；尚未入场则尊重玩家重新开启剧情的选择。
        if (contact.usesIndependentContact && contact.representativePawn == null && !canArrange)
        {
            contact.usesIndependentContact = false;
            contact.visitPending = false;
            contact.visitDueTick = -1;
            UpdateQuestDescription();
        }

        if (contact.visitPending || !canArrange || ResolveMap() == null)
        {
            return;
        }

        contact.usesIndependentContact = true;
        contact.visitPending = true;
        ScheduleRepresentativeVisit(now);
    }

    public void NotifyCourierChoice(Pawn? pawn, bool released)
    {
        if (contact.accountOpened || pawn == null)
        {
            return;
        }

        RememberCourier(pawn, pawn.MapHeld);
        contact.courierOutcome = released ? 1 : 2;
        contact.lastContactTick = CorporateNetwork.Now;
        contact.visitPending = true;
        contact.serviceResumeTick = 0;
        ScheduleRepresentativeVisit(CorporateNetwork.Now);
    }

    // Pawn.PreApplyDamage 前缀对每次伤害调用；开户完成后第一步即返回。
    internal void NotifyDamage(Pawn target, DamageInfo damage)
    {
        if (contact.accountOpened || damage.Amount <= 0f || !IsPlayerFaction(damage.Instigator?.Faction))
        {
            return;
        }

        if (target == contact.representativePawn)
        {
            contact.independentlyAggressive = true;
            contact.serviceResumeTick = CorporateNetwork.Now + Config.attackRetryTicks;
            SendAway(contact.representativePawn);
        }
        else if (target != contact.courierPawn && target.kindDef != CorporateDefOf.Mugirl_GiantCorp_Courier
            && target.Faction != null && target.Faction == CorporateNetwork.Current?.CorporateFaction)
        {
            contact.independentlyAggressive = true;
        }
    }

    internal void NotifyKilled(Pawn? pawn, DamageInfo? damage)
    {
        if (contact.accountOpened || pawn == null)
        {
            return;
        }

        if (pawn == contact.courierPawn || pawn.kindDef == CorporateDefOf.Mugirl_GiantCorp_Courier)
        {
            RememberCourier(pawn, pawn.MapHeld ?? contact.visitMap);
            contact.courierDead = true;
            contact.playerKilledCourier |= IsPlayerFaction(damage?.Instigator?.Faction);
            contact.visitPending = true;
            if (contact.lastContactTick < 0)
            {
                contact.lastContactTick = CorporateNetwork.Now;
            }

            contact.serviceResumeTick = 0;
            ScheduleRepresentativeVisit(CorporateNetwork.Now);
        }
    }

    private static bool IsPlayerFaction([NotNullWhen(true)] Faction? faction)
        => faction != null && PlayerFactionUtility.IsPlayerFaction(faction);

    /// <summary>开发者动作：立即请代表到访。成功返回 null，否则返回失败原因代码。</summary>
    internal string? DevTriggerRepresentative(Map? map)
    {
        if (!Prefs.DevMode || map?.IsPlayerHome != true)
        {
            return "RequiresHomeMap";
        }

        if (contact.accountOpened || (contact.representativePawn != null && !contact.representativePawn.Dead && contact.representativePawn.Spawned))
        {
            return "AlreadyTriggered";
        }

        Faction? corporation = EnsureCorporateFactionAvailable();
        if (corporation == null || corporation.defeated)
        {
            return "NoFaction";
        }

        // 只提前来访，不伪造运货员结局、不重置开户状态，也不重新发放见面礼。
        contact.visitMap = map;
        contact.visitPending = true;
        contact.visitDueTick = CorporateNetwork.Now;
        contact.serviceResumeTick = CorporateNetwork.Now + Config.retryTicks;
        EnsureQuest();
        TrySpawnRepresentative(map, corporation);
        return contact.representativePawn?.Spawned == true ? null : "Failed";
    }

    private void ScheduleRepresentativeVisit(int now)
    {
        int delay = Math.Max(0, Config.contactDelayTicks);
        if (Config.contactDelayJitterTicks > 0)
        {
            delay += Rand.RangeInclusive(-Config.contactDelayJitterTicks, Config.contactDelayJitterTicks);
        }

        contact.visitDueTick = now + Math.Max(0, delay);
    }

    private void RecoverCourierHistory()
    {
        if (!GameUtility.TryGetQuestsListForReading(out List<Quest>? quests) || quests == null)
        {
            return;
        }

        Quest? oldQuest = quests.FindLast(q => q.root == IncidentsDefOf.Mugirl_CourierContact);
        QuestPart_SpawnCourier? part = oldQuest?.PartsListForReading.OfType<QuestPart_SpawnCourier>().FirstOrDefault();
        if (part != null)
        {
            RememberCourier(part.courier, part.map);
        }

        if (oldQuest?.State == QuestState.EndedSuccess)
        {
            contact.courierOutcome = 1;
            if (oldQuest.cleanupTick > 0)
            {
                contact.lastContactTick = oldQuest.cleanupTick;
            }
        }
        else if (oldQuest?.State == QuestState.EndedUnknownOutcome)
        {
            contact.courierOutcome = 2;
            if (oldQuest.cleanupTick > 0)
            {
                contact.lastContactTick = oldQuest.cleanupTick;
            }
        }

        if (contact.courierPawn?.Dead == true)
        {
            contact.courierDead = true;
        }

        // Unknown 也用于刚开始的交战，不能凭此恢复死亡责任。
    }

    private void EnsureQuest()
    {
        if (contact.quest != null)
        {
            RepairIntroductionQuestRoot();
            UpdateQuestDescription();
            return;
        }

        int now = CorporateNetwork.Now;
        if (now < nextQuestAttemptTick)
        {
            return;
        }

        Slate slate = new Slate();
        slate.Set("corporateIntroduction", true);
        Quest? quest = QuestUtility.GenerateQuestAndMakeAvailable(CorporateDefOf.Mugirl_CorporateIntroductionQuest, slate);
        if (quest == null)
        {
            // 原版已记录 QuestGen 的错误；稍后重试，主线本身仍可通过与代表交谈完成。
            nextQuestAttemptTick = now + Config.retryTicks;
            MugirlLog.WarningOnce("CorporateIntroduction.Quest",
                "The corporate introduction quest could not be generated; retrying later. The representative can still be met.");
            return;
        }

        contact.quest = quest;
        quest.name = "Mugirl.CorporateIntro.Title".Translate();
        UpdateQuestDescription();
    }

    private void UpdateQuestDescription()
    {
        if (contact.quest == null)
        {
            return;
        }

        contact.quest.description = contact.usesIndependentContact
            ? "Mugirl.CorporateIntro.Independent.QuestDescription".Translate()
            : "Mugirl.CorporateIntro.QuestDescription".Translate();
    }

    private void RepairIntroductionQuestRoot()
    {
        // 早期开发存档的 MakeRaw 任务缺少脚本根；原位补齐，不重建任务或重发奖励。
        if (contact.quest != null && contact.quest.root == null)
        {
            contact.quest.root = CorporateDefOf.Mugirl_CorporateIntroductionQuest;
        }
    }

    private Map? ResolveMap()
    {
        List<Map>? maps = GameUtility.LoadedMaps;
        if (contact.visitMap != null && maps?.Contains(contact.visitMap) == true && contact.visitMap.IsPlayerHome)
        {
            return contact.visitMap;
        }

        contact.visitMap = maps?.Find(m => m.IsPlayerHome);
        return contact.visitMap;
    }

    private void TrySpawnRepresentative(Map map, Faction corporation)
    {
        if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entry, map, 0f))
        {
            return;
        }

        if (contact.representativePawn == null || contact.representativePawn.Dead || contact.representativePawn.Destroyed)
        {
            contact.representativePawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(CorporateDefOf.Mugirl_CorporateRepresentative,
                corporation, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true,
                allowDead: false, allowDowned: false, canGeneratePawnRelations: false,
                mustBeCapableOfViolence: false, forceRecruitable: false, allowPregnant: false,
                developmentalStages: DevelopmentalStage.Adult));
        }

        Pawn? visitor = contact.representativePawn;
        if (visitor == null || visitor.IsPrisoner || visitor.IsColonist || visitor.IsSlave
            || visitor.holdingOwner != null || visitor.GetCaravan() != null)
        {
            return;
        }

        if (GameUtility.WorldPawns.Contains(visitor))
        {
            GameUtility.WorldPawns.RemovePawn(visitor);
        }

        // 代表以巨企派系身份入场；开户前的派系好感由主线逻辑自行处理。
        if (visitor.Faction != corporation)
        {
            visitor.SetFaction(corporation);
        }

        GenSpawn.Spawn(visitor, entry, map);
        IntVec3 center = map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center;
        if (!CellFinder.TryFindRandomCellNear(center, map, 10, c => c.Standable(map) && !c.Fogged(map), out IntVec3 waitCell))
        {
            waitCell = entry;
        }

        LordMaker.MakeNewLord(corporation, new LordJob_WaitForDurationThenExit(waitCell, Config.representativeStayTicks), map, Gen.YieldSingle(visitor));
        GameUtility.Letters.ReceiveLetter("Mugirl.CorporateIntro.ArrivalTitle".Translate(),
            contact.usesIndependentContact
                ? "Mugirl.CorporateIntro.Independent.ArrivalText".Translate(visitor.LabelShortCap)
                : "Mugirl.CorporateIntro.ArrivalText".Translate(visitor.LabelShortCap),
            LetterDefOf.NeutralEvent, visitor);
        GameUtility.TrySignalForceNormalSpeedShort();
    }

    public bool CanTalk([NotNullWhen(true)] Pawn? negotiator, [NotNullWhen(true)] Pawn? target)
    {
        return !contact.accountOpened && contact.visitPending && target == contact.representativePawn && target?.Spawned == true && !target.Dead
            && !target.Downed && !target.InAggroMentalState && !target.IsPrisoner && !target.IsSlave
            && negotiator?.Spawned == true && IsPlayerFaction(negotiator.Faction) && !negotiator.Dead
            && !negotiator.Downed && negotiator.Map == target.Map && negotiator.RaceProps.Humanlike
            && negotiator.health.capacities.CapableOf(PawnCapacityDefOf.Talking);
    }

    public void ShowDialog(Pawn? negotiator, Pawn? target)
    {
        if (!CanTalk(negotiator, target))
        {
            return;
        }

        DiaNode main = BuildIntroductionDialog(negotiator, target);
        GameUtility.TryAddWindow(new Dialog_NodeTree(main, delayInteractivity: true, title: "Mugirl.CorporateIntro.Title".Translate()));
    }

    internal DiaNode BuildIntroductionDialog(Pawn negotiator, Pawn target)
    {
        bool hasMugirl = PlayerHomesHaveMugirl();
        DiaNode main;
        if (contact.usesIndependentContact)
        {
            main = new DiaNode("Mugirl.CorporateIntro.Independent.Opening".Translate()
                + "\n\n" + (hasMugirl ? "Mugirl.CorporateIntro.Independent.MugirlsPresent".Translate() : "Mugirl.CorporateIntro.Independent.NoMugirls".Translate())
                + "\n\n" + "Mugirl.CorporateIntro.Proposal".Translate());
            AddQuestion(main, "Mugirl.CorporateIntro.Independent.Question.Visit", "Mugirl.CorporateIntro.Independent.Answer.Visit");
            AddQuestion(main, "Mugirl.CorporateIntro.Independent.Question.Mugirl", "Mugirl.CorporateIntro.Independent.Answer.Mugirl");
        }
        else
        {
            main = new DiaNode(OpeningText() + "\n\n" + ReactionText()
                + "\n\n" + (hasMugirl ? "Mugirl.CorporateIntro.KeepMugirl".Translate() : "Mugirl.CorporateIntro.NoReclaim".Translate())
                + "\n\n" + "Mugirl.CorporateIntro.Proposal".Translate());
            AddQuestion(main, "Mugirl.CorporateIntro.Question.Courier", "Mugirl.CorporateIntro.Answer.Courier");
            AddQuestion(main, "Mugirl.CorporateIntro.Question.Mugirl", "Mugirl.CorporateIntro.Answer.Mugirl");
        }

        AddQuestion(main, "Mugirl.CorporateIntro.Question.Services", "Mugirl.CorporateIntro.Answer.Services");
        main.options.Add(new DiaOption("Mugirl.CorporateIntro.Accept".Translate())
        {
            action = () => Complete(negotiator, target),
            resolveTree = true,
        });
        main.options.Add(new DiaOption("Mugirl.CorporateIntro.Postpone".Translate()) { resolveTree = true });
        return main;
    }

    private string OpeningText()
    {
        if (!contact.retrofitted)
        {
            return "Mugirl.CorporateIntro.Opening".Translate();
        }

        if (contact.lastContactTick >= 0 && CorporateNetwork.Now - contact.lastContactTick >= Config.longAbsenceTicks)
        {
            return "Mugirl.CorporateIntro.OpeningLong".Translate();
        }

        if (contact.lastContactTick < 0 && CorporateNetwork.Now >= Config.longAbsenceTicks)
        {
            return "Mugirl.CorporateIntro.OpeningLate".Translate();
        }

        return "Mugirl.CorporateIntro.OpeningOldSave".Translate();
    }

    private string ReactionText()
    {
        if (contact.playerKilledCourier)
        {
            return "Mugirl.CorporateIntro.Reaction.Killed".Translate();
        }

        if (contact.courierDead)
        {
            return "Mugirl.CorporateIntro.Reaction.DeadUnknown".Translate();
        }

        return contact.courierOutcome switch
        {
            1 => "Mugirl.CorporateIntro.Reaction.Released".Translate(),
            2 => "Mugirl.CorporateIntro.Reaction.Conflict".Translate(),
            _ => contact.courierPawn?.Spawned == true ? "Mugirl.CorporateIntro.Reaction.Unresolved".Translate() : "Mugirl.CorporateIntro.Reaction.Unknown".Translate(),
        };
    }

    private static bool PlayerHomesHaveMugirl()
    {
        List<Map>? maps = GameUtility.LoadedMaps;
        if (maps == null)
        {
            return false;
        }

        foreach (Map map in maps)
        {
            if (map.IsPlayerHome && map.mapPawns.AllPawnsSpawned.Any(p => MugirlIdentity.IsMugirlPawn(p) && IsPlayerFaction(p.Faction)))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddQuestion(DiaNode main, string questionKey, string answerKey)
    {
        DiaNode answer = new DiaNode(answerKey.Translate());
        answer.options.Add(new DiaOption("Mugirl.CorporateIntro.Back".Translate()) { link = main });
        main.options.Add(new DiaOption(questionKey.Translate()) { link = answer });
    }

    private void Complete(Pawn negotiator, Pawn target)
    {
        if (!CanTalk(negotiator, target) || !PrepareGifts())
        {
            return;
        }

        EnsureQuest();
        contact.accountOpened = true;
        contact.visitPending = false;
        contact.visitMap = negotiator.Map;
        Faction? corporation = CorporateNetwork.Current?.CorporateFaction;
        if (!contact.independentlyAggressive && corporation != null && corporation.PlayerGoodwill < 0)
        {
            corporation.TryAffectGoodwillWith(PlayerFactionUtility.PlayerFaction, -corporation.PlayerGoodwill, false, false);
        }

        TryDeliverGifts(contact.visitMap);
        List<ResearchProjectDef> unlockedResearch = UnlockResearchGifts();
        contact.quest?.End(QuestEndOutcome.Success, sendLetter: false, playSound: false);
        string text = "Mugirl.CorporateIntro.CompletedLetter".Translate();
        if (unlockedResearch.Count > 0)
        {
            text += "\n\n" + "Mugirl.CorporateIntro.ResearchGiftLine".Translate(unlockedResearch.Select(p => (string)p.LabelCap).ToCommaList());
        }

        if (corporation?.HostileTo(PlayerFactionUtility.PlayerFaction) == true)
        {
            text += "\n\n" + "Mugirl.CorporateIntro.HostilityRemains".Translate();
        }

        GameUtility.Letters.ReceiveLetter("Mugirl.CorporateIntro.CompletedStatus".Translate(), text, LetterDefOf.PositiveEvent, negotiator);
        CorporateNetwork? network = CorporateNetwork.Current;
        network?.Record("Mugirl.CorporateIntro.Title", "Mugirl.CorporateIntro.CompletedStatus".Translate());
        network?.EnsureWeeklyOffers();
        SendAway(target);
    }

    // 见面礼科技随开户一次性完成；已研究的项目跳过，避免重复触发解锁副作用。
    private static List<ResearchProjectDef> UnlockResearchGifts()
    {
        List<ResearchProjectDef> unlocked = new List<ResearchProjectDef>();
        ResearchManager? manager = GameUtility.ResearchManager;
        if (manager == null)
        {
            return unlocked;
        }

        foreach (ResearchProjectDef project in Config.researchGifts)
        {
            if (project == null || project.IsFinished)
            {
                continue;
            }

            manager.FinishProject(project);
            unlocked.Add(project);
        }

        return unlocked;
    }

    private bool PrepareGifts()
    {
        if (contact.giftBundlePrepared)
        {
            return true;
        }

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
            {
                if (!gifts.TryAdd(thing, false))
                {
                    throw new InvalidOperationException("Mugirl.CorporateIntro.GiftStorageError".Translate());
                }
            }

            contact.giftBundlePrepared = true;
            return true;
        }
        catch (Exception ex)
        {
            foreach (Thing thing in prepared)
            {
                if (thing.holdingOwner == gifts)
                {
                    gifts.Remove(thing);
                }

                if (!thing.Destroyed && thing.holdingOwner == null)
                {
                    thing.Destroy();
                }
            }

            MugirlLog.WarningOnce("CorporateIntroduction.Gifts", "Mugirl.CorporateIntro.GiftError".Translate(ex.Message));
            return false;
        }
    }

    private void TryDeliverGifts(Map? map)
    {
        if (map == null || gifts.Count == 0)
        {
            return;
        }

        var delivery = new CorporateTradeContext(map);
        foreach (Thing thing in gifts.ToList())
        {
            delivery.Deliver(thing);
        }
    }

    private static void SendAway(Pawn? pawn)
    {
        if (pawn?.Spawned != true || pawn.Dead || pawn.Downed)
        {
            return;
        }

        pawn.GetLord()?.RemovePawn(pawn);
        LordMaker.MakeNewLord(pawn.Faction, new LordJob_ExitMapBest(LocomotionUrgency.Jog, canDig: false, canDefendSelf: true), pawn.Map, Gen.YieldSingle(pawn));
    }

    public override void ExposeData()
    {
        base.ExposeData();
        CorporateStateScribe.Look(contact, "contact", ref contactRead);
        Scribe_Deep.Look(ref gifts, "gifts", this);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            gifts ??= new ThingOwner<Thing>(this, false, LookMode.Deep);
            RepairIntroductionQuestRoot();
        }
    }
}
