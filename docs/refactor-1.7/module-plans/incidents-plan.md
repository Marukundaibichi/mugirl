# Wave S plan — module `incidents` (Incidents, Storyline, Courier, Fusion investment, Migration, Punisher, Runaway farm, Wild Mugirls, Trade, Grazing, Social, LifeStages)

Inputs: `design.md` (authoritative), `understand/module-incidents.md` (A01–A22, B, C), `real-saves.md`, `engine-hooks.md`,
`validator-tooling.md`, `content.md`. Every code fact below was re-read in the working tree (paths relative to
`Source/MugirlRace/Features/` unless a root is given; line numbers are pre-Wave-R). Engine facts were re-checked on
`rw17-game` (1.7.4977): `StoryState.ExposeData` (`recentRandomIncidents`, `recentRandomQuests`, `lastFireTicks` keys/values),
`IncidentQueue.ExposeData` (`queuedIncidents`), `QueuedIncident.ExposeData` (`firingInc`), `FiringIncident.ExposeData` (`def`),
`Job.ExposeData` (`jobGiverThinkTree`, `lastJobGiverKey`, PostLoadInit warning "Could not find think node with key"),
`ThinkTreeDef.ResolveReferences` (`AssignKeys(thinkRoot, GenText.StableStringHash(defName))`), `Pawn.ChangeKind`,
`HediffComp_GiveRandomSituationalThought.CompExposeData` (`selectedThought`).

Type names below are the post-Wave-R names from `spec/incidents.json`. "T" = `game/tickManager/ticksGame` read from the DOM
(absent → 0). "Legacy session" = schema 0 per design §1. Step ids follow `SaveUpgrade/Steps/Incidents/<Id>.cs` (DOM) and
`SaveUpgrade/PostLoad/Incidents/<Id>.cs` (phase A/B/C); each gets `MugirlRace.Tests/SaveUpgrade/Incidents/<Id>Tests.cs`.

---------------------------------------------------------------------------------------------------------------------
## S1. Legacy code removal — every legacy site and what replaces it

Mechanism legend: **TM** generated type map · **TM-h** hand type entry · **DM** generated def map · **DM-h** hand def entry ·
**DOM** structural step (stage 5 unless noted) · **PL-A/C** post-load phase · **drop** nothing needed.

| # | Legacy site (current code) | Legacy XML shape (evidence) | Replacement | Domain code deleted |
|---|---|---|---|---|
| A01 | Retired IncidentDef `Mugirl_WildManWandersIn`: `1.7/Defs/Compatibility/RetiredEvents.xml:3-13`, worker `Compatibility/Savegames/IncidentWorker_RetiredMugirlEvent.cs:5-10`, zh DefInjected `IncidentDef/Incidents_Mugirl.xml:8` | Every `storyState` element (world, each map, each caravan): `recentRandomIncidents/li` text; `lastFireTicks/keys/li` + `values/li` at the same index; `game/storyteller/incidentQueue/queuedIncidents/li` whose `firingInc/def` is the name. Live in 0.4.7–0.6.7 (HEAD worker `IncidentWorker_Mugirl_WildManWandersIn`); not present in the 11 local saves (real-saves §5 "Retired names") but expected in workshop saves | **DM-h** Drop(RetiredIncident) consumed by **core's DOM step `Steps/Core/RemoveRetiredIncidentReferences`** (core-plan L3/S4-5): remove list items, remove dictionary key **and** value pairwise, remove queued incidents. This module adds IncidentDef `Mugirl_CourierRaid`/`MooGirl_CourierRaid` (S2-6) to that step's name list. Not `WasDefRemoved` only (dict null key still errors, module-core A3) | `RetiredEvents.xml` + worker (core); zh line `Incidents_Mugirl.xml:8` (this module) |
| A02 | PawnKinds `Mugirl_EscapeWildSlave` / `Mugirl_PreEscapeWildSlave`: `PawnKinds_Slave.xml:93-124`; `DefOf/Mugirl_DefOf.cs:124,126`; `WildSlaves/MugirlWildSlaveUtility.cs:11-27,35-46` | `thing[Class=Pawn]/kindDef`, `pawnData/kind`, `firstPawnData/kind` (tales), `AlienRaces_AlienComp/originalKindDef` (real-saves §5 contexts for kind defs). Neither name occurs in local saves; WildManWandersIn (≤0.6.7) generated EscapeWildSlave; PreEscape was assigned to recruited wild slaves before `0a6c435` | **DM-h** both (and `MooGirl_` forms) → `Mugirl_WildMugirl` (Scribe_Defs everywhere via BackCompatibleDefName postfix + DOM def pass). Player-faction ones are normalized by A03 | Both PawnKindDefs + zh DefInjected `PawnKindDef/PawnKinds_Slave.xml:10-13`; DefOf fields; `IsEscapeWildSlave`, `HasLegacyPlayerMugirlKind`, kind list in `IsMugirlPawn` (→ `MugirlIdentity.IsMugirlPawn`, all three kinds are race `Mugirl`, `PawnKinds_Slave.xml:5`) |
| A03 | `Story/MugirlStoryState.cs:48-54` `LoadedGame` → `MugirlWildSlaveUtility.NormalizeLoadedPlayerPawnKinds` (`:146-160`), full scan every load | Player-faction humanlike pawns whose `kindDef` is a wild kind (pre-`0a6c435` joins) | **PL-C `Incidents.PlayerEventPawns`** (legacy session, once): for `PawnsFinder.All_AliveOrDead`, player faction, humanlike, `!IsQuestLodger()`, `kindDef == Mugirl_WildMugirl` → `ChangeKind(Faction.def.basicMemberKind)` + `WildMugirlUtility.RefreshPawn`. Same predicate/timing as today (LoadedGame ≙ phase C). Accepted deviation: a *non-player* former colonist that still had `PreEscapeWildSlave` becomes a wild Mugirl (theoretical, no save has the kind) | `NormalizeLoadedPlayerPawnKinds`; `LoadedGame` override; live `NormalizePlayerKindToFactionDefault` stays (predicate `kindDef == Mugirl_WildMugirl`) |
| A04 | `Shared/MugirlEventUtility.cs:118-131` `ClearPlayerMigrationPawns`, called `MugirlStoryState.cs:52` every load | `thing/questTags/li` = `Mugirl_Migration` on a player-faction pawn (03439c8–9e8ae83 joins). S6 has 4 `Mugirl_Migration` questTags (real-saves defs scan; faction unknown) | Same **PL-C `Incidents.PlayerEventPawns`**: player-faction pawn → `EventPawnTags.ClearMigration(pawn)` (runs before A03 in the same pass; old order was identical) | `ClearPlayerMigrationPawns`, call site |
| A05 | `Migration/MapComponent_MugirlMigration.cs:10,27,337-368,478,486-495` (`dataVersion`, `ReleaseLegacyBikiniLocks`) | `maps/li/components/li[@Class=MapComponent_MugirlMigration]/dataVersion` absent (0, builds 03439c8–b3074b3) or `1`; all 13 local instances have `<dataVersion>2</dataVersion>` (real-saves §3) | **DOM `Incidents.MigrationRecords`**: `dataVersion` absent/<2 ⇒ write `outcomeFailed=True` and set session flag `LegacyMigrationBikiniLocks`; always delete `dataVersion`. **PL-A `Incidents.MigrationBikiniLocks`** (only if flag): pawns with tag `Mugirl_Migration` and without `Mugirl_RunawayFarm` → `pawn.apparel.Unlock(worn Mugirl_Bikini)` (old code ran in PostLoadInit ≙ phase A) | constants, field, label, both methods, PostLoadInit branch |
| A06 | Parallel lists: Migration `:17-19,311-335,468-470`; Punisher `:12-14,239-273,278-280` | Migration: `<migrationPawns><li>Thing_Mugirl123</li>…</migrationPawns><nextGrazeTicks><li>N</li>…</nextGrazeTicks><playerKilledMigrationPawnIds><li>123</li>…</playerKilledMigrationPawnIds>` + `ticksUntilDeparture`, `killWindowTicksRemaining`, `originalMigrationCount`, `killedMigrationPawns`, `migrationOutcomeFailed`, `departureStarted`, `exitCell`. Punisher: `<punishers><li>Thing_…</li></punishers><punisherSpawnedAtTicks>…<punisherLeavingFlags>…`. All local instances: empty lists (real-saves §3) | **DOM `Incidents.MigrationRecords`** → new schema S3: zip by index; reference text `null` ⇒ drop the entry and set `outcomeFailed=True` (old tick loop: null/destroyed pawn ⇒ failed); missing graze tick ⇒ `T`; `killedByPlayer` = trailing digits of the reference ∈ ID list; `departureTick = T + ticksUntilDeparture` (only if migrants and `departureStarted` false); `killWindowEndTick = T + killWindowTicksRemaining` (if > 0); labels renamed per S3. **DOM `Incidents.PunisherRecords`**: zip; `null` ⇒ drop; missing spawn tick ⇒ `T`; `leaving=True` ⇒ `spawnTick = min(spawnTick, T − 30000)` (leaving is derived from spawn time after S2-3). Both steps detect legacy shape by label presence (idempotent) | `EnsureGrazeTicksAligned`, `EnsureStateListsAligned`, `RemoveMigrationPawnAt/RemovePunisherAt` pair bookkeeping, `playerKilledMigrationPawnIds` |
| A07 | `Punisher/MapComponent_MugirlPunisher.cs:16,287-288` ("old saves lack the nodes") | 1.6 saves have no `punisherHeldLoot`/`punisherLootBursts` | **drop**: absent ⇒ empty (generic null guard stays, comment rewritten). Labels become `heldLoot`/`lootBursts`; the pre-refactor 1.7 tree is not a source (design §1), so no map entry | comment only |
| A08 | `Courier/QuestPart_SpawnCourier.cs:138-164` fallback `FindOpeningCrashPawns`; `MugirlStoryState.cs:152-156` | Pre-0.6.6 saves have no `openingCrashPawns` (S5, S6); S7/S10 have it (`Thing_Mugirl27424/27431`) | **DOM `Incidents.StorylineSchema1`**: if the list is absent/empty, copy the non-`null` `pawns/li` of the first quest (`questManager/quests/li` order) with `root`=`Mugirl_SlaveOpeningPodCrash` that has a non-empty `parts/li[@Class="Mugirl.Features.Incidents.QuestPart_JoinOnRescue"]/pawns` (S5 has it; S6's quest has no such part → stays empty, same result as today) | fallback loop; reads become `Storyline.OpeningCrash.Pawns` |
| A09 | `Story/MugirlStoryService.cs:122-125` clamp of `courierRaidTimer` | ≤0.6.5 initial 300000; local samples 239640/299640/299999/0 (real-saves §3) | **DOM `Incidents.StorylineSchema1`**: when opening crash started and contact not started, `courierContact/dueTick = T + min(timer(absent→60000), 60000)` | the clamp |
| A10 | Two flags `courierRaidTriggered`/`courierRaidQuestStarted` (`MugirlStoryState.cs:14-16,107-111`, `MugirlStoryService.cs:117,132-136`, `MugirlStoryDebugActions.cs:46`, `IncidentWorker_Mugirl_CourierRaid.cs:44-49`; Corporate `CorporateIntroduction.cs:78,236`) | Both bools; 1.6 frozen manual incident issued the quest without marking state (`1.6/Source/Features/Incidents/IncidentWorker_Mugirl_CourierRaid.cs:17-27`) | **DOM `Incidents.StorylineSchema1`**: `courierContact/started = triggered ∨ questStarted ∨ ∃ quest root = Mugirl_CourierContact` (after DM). The last clause **fixes a verified defect** (duplicate courier after a manual 1.6 incident) — record it | second flag and its branches |
| A11 | `FusionInvestment/ChoiceLetter_MugirlFusionInvestment.cs` (never created in git history) | Theoretical `archive/archivables/li[@Class=…]` (+ `letterStack/letters/li` reference) | **TM-h** `Mugirl.ChoiceLetter_MugirlFusionInvestment` → `Verse.StandardLetter` (assignable to IArchivable/Letter; extra `<map>` ignored) | the class (and the `investor: null` path of `TryAcceptInvestment`) |
| A12 | `Shared/MugirlEventMarkers.cs:16-17`, `RunawayFarm/QuestPart_RunawayFarmCompletion.cs:249` | `questTags/li` = `Mugirl_RunawayReward` on reward pawns (1.6 incl. frozen, `1.6/Source/Features/Incidents/QuestPart_RunawayFarm.cs:752`); tags are not in the defName scan, so frequency unknown | **DOM `Incidents.RunawayRewardTag`**: delete those `li` (only under elements named `questTags`) | comments |
| A13 | "Tag strings frozen" comments (`MugirlEventMarkers.cs:16`, `MugirlEventUtility.cs:6`) | `Mugirl_Migration`, `Mugirl_RunawayFarm`, `Mugirl_FusionInvestor` | **drop**: values are good names and stay schema (S3); only the "cannot rename" rationale goes. DOM passes must never treat `questTags` text as defNames (`Mugirl_Migration` is also an IncidentDef name, real-saves: `li<questTags` ×4) | comments |
| A14 | `Misc/Grazing/Harmony_MugirlFoodPolicyGrass.cs:16-30` (every load) + label table `MugirlFoodPolicyUtility.cs:10-12,42,72-84` | Saves before 0.6.2 (bf577ca) or mid-game installs lack the policy; the policy is vanilla data (`foodRestrictionDatabase`) identified only by label | **PL-C `Grazing.GrassPolicy`** (legacy session): if no FoodPolicy label ∈ {`草`, `Grass`, current translation} → `GrassFoodPolicyUtility.Create(db)`. New games keep the `GenerateStartingFoodRestrictions` postfix; mid-game installs use the hub signal "Mugirl added to an existing game" (S5-3). **Behaviour fix**: a deleted or renamed preset is no longer recreated on every load (acknowledged defect `MugirlFoodPolicyUtility.cs:41`) — record it | ExistingSave patch, legacy label table |
| A15 | `Misc/Pregnancy/Harmony_MugirlPregnancyMood.cs:15-27` (each comp on every load) | Pre-0.6.6 Mugirl `hediffs/li[def=PregnancyMood]/selectedThought` may name a negative thought | **PL-A `Social.PregnancyMoodPositive`** (legacy session): every pawn's PregnancyMood comp → `PregnancyMoodUtility.EnsurePositiveThought` (same call, same phase as today's PostLoadInit) | the Load patch |
| A16 | `MugirlStoryState.cs:40-46,62-69` → `LifeStageVisualService.NormalizeLoadedPawns` on every new game/load | not legacy data: an invariant repair that also covers external body-type changes (e.g. body genes) | **no step**. Kept as a LifeStages per-game callback registered with the core hub `FinalizeInit` (S2-11), comment states that reason. Behaviour unchanged | the call from the story component |
| A17 | `QuestPart_SpawnCourier.cs:189-218` comment "old couriers carry fake stackCount keys" | — | **drop** (logic is current domain behaviour) | comment |
| A18 | "class/label is saved, cannot rename" comments (17 sites, module-incidents A18) | — | **drop**; names now change through the map | comments |
| A19 | "same as/different from 1.6" comments (22 sites) | — | **drop**: rewrite as current rationale or delete | comments |
| A20 | Settings key `enableStructuralCrashEvent` | settings file | core owner (settings upgrade, design §5.7) | — (S4) |
| A21 | Already gone in 1.7 (`HediffComp_JoinWhenRescued`, `IncidentWorker_MugirlStructuralCrashMission`) | `joinedAlready`/`ticksUntilNextCheck` never reach a save (no Def uses them) | **drop** | — |
| A22 | `MugirlStoryState.cs:75-76` calls to empty `ResetDefCache` stubs | — | **drop** (milk owner deletes the stubs) | the two calls |
| X1 | Save-visible class names (all unchanged since 1.6) | `game/components/li[@Class=Mugirl.MugirlStoryState]`, `maps/li/components/li[@Class=Mugirl.MapComponent_MugirlMigration|MapComponent_MugirlPunisher]`, `questManager/quests/li/parts/li[@Class=Mugirl.QuestPart_SpawnCourier|QuestPart_MugirlRescueJoin|QuestPart_SpawnRunawayFarm|QuestPart_RunawayFarmCompletion]`, `jobs/curDriver[@Class=Mugirl.JobDriver_ReadCourierDiary|TalkCourier|TalkFusionInvestor|Nuzzle]`, memories `li[@Class=Mugirl.Thought_MugirlOnly]` (real-saves §2: StoryState S5–S11, Migration S5–S11, Punisher S7–S11, SpawnCourier S7/S10, RescueJoin S5/S7/S10) | **TM** generated from `incidents.json` (QuestParts are mandatory: an unresolved part shifts later part load IDs, `QuestPart_RunawayFarmCompletion.spawnPart` is index-based) | the "frozen name" comments |
| X2 | MooGirl era (name-level, design §1) | `MooGirl.MooGirl_GameComp` (S1–S4: `<courierRaidTimer>298799</courierRaidTimer>`; full label set `MooGirl_Structural_crash_mission_variables`, `courierRaidTriggered`, `courierRaidTimer`, `courierRaidQuestStarted`, `courierRaidLegacyFixApplied`, `legacySaveUpgradeApplied`), `MooGirl.MooGirlStoryState` (git e8495b8–cb25751), `MooGirl.QuestPart_MooGirlRescueJoin`, `MooGirl.QuestPart_SpawnCourier`, `MooGirl.QuestPart_CourierDemand` (labels `faction`,`inSignal`), `MooGirl.JobDriver_{Nuzzle,ReadCourierDiary,TalkCourier}`, `MooGirl.Thought_MooGirlOnly`, `MooGirl.GameComponent_GhoulRenderingRefresh` (type names from the 55 historical DLLs) | **TM-h** (rows live in content's `spec/moogirl-types.json`, which agrees): both MooGirl story components → `GameComponent_Storyline` (merge: `StorylineSchema1` reads `MooGirl_Structural_crash_mission_variables` as `openingCrashStarted`; the two `*Applied` labels are Dropped; the classes never co-exist (last `1113cd8`, first `e8495b8`), the step still keeps only the first storyline element defensively); RescueJoin/SpawnCourier/JobDrivers/Thought/Ghoul component compose with the final names; `QuestPart_CourierDemand` → Drop(remove part `li`; courier quests hold no part references, so later index shifts are harmless). **DM-h** (`moogirl-defs.json`) MooGirl defNames must point to final names: `MooGirl_CourierRaid` (QuestScriptDef) → `Mugirl_CourierContact`, (IncidentDef) → Drop, `MooGirl_EscapeWildSlave`/`MooGirl_PreEscapeWildSlave` → `Mugirl_WildMugirl`, `MooGirl_TalkCourier`/`MooGirl_ReadCourierDiary` → `Mugirl_*`, `MooGirl_Nuzzle` (InteractionDef) → `Mugirl_Nuzzle`, thoughts/kinds/quests by prefix with unchanged targets | — |
| X3 | ThinkTreeDef `MugirlPunisher` → `Mugirl_Punisher` (spec) | `curJob/jobGiverThinkTree` + `lastJobGiverKey` (keys hash the defName). No local save has a punisher job (real-saves: `jobGiverThinkTree<curJob` only MugirlLike/HumanlikeConstant) | **DM** + **DOM `Incidents.PunisherJobGiverKeys`** (legacy session): every Job element whose `jobGiverThinkTree` is `Mugirl_Punisher` loses its `lastJobGiverKey` (absent = −1 ⇒ no lookup, no "Could not find think node" warning; `jobGiver` is null either way). Better as a framework rule for every ThinkTreeDef rename (S4) | — |
| X4 | JobDef `Job_Nuzzle` → `Mugirl_Nuzzle`; QuestScriptDef `Mugirl_CourierRaid` → `Mugirl_CourierContact` | `curJob/def`, `jobQueue/jobs/li/job/def`; `quests/li/root` (S6,S7,S10), `storyState/recentRandomQuests/li`, queued `parms/questScriptDef` | **DM** generated (type-aware: the IncidentDef of the same old name is A01's Drop, never this rename) | DefOf field names follow |
| X5 | `GameComponent_GhoulRenderingRefresh` (decision S2-12: keep, renamed by core) | `<li Class="Mugirl.GameComponent_GhoulRenderingRefresh" />` in S5–S11 | **TM** generated from `core.json` → `Mugirl.Rendering.GameComponent_PawnRenderRefresh`; `MooGirl.` name composes (moogirl-types.json) | the "Ghoul"/freeze comments (core) |
| X6 | `QuestPart_SpawnCourier.spawnCell` historical Scribe default `default(IntVec3)` (labels.json shows both variants) | a cell `(0,0,0)` would have been omitted | **drop**: absent now reads `Invalid`, the part re-finds an entry cell (equivalent) | — |
| X7 | `QuestPart_RunawayFarmCompletion.ticksUntilNextCheck` (S2-7) | `<ticksUntilNextCheck>N</ticksUntilNextCheck>` | **drop** (Dropped: replaced by hash-interval check; the extra node is ignored and never re-written) | field + label |
| X8 | `fusionInvestmentInvestorActive` (redundant with the reference) and the countdown timers (S2-1) | flat labels on the story component | consumed by **DOM `Incidents.StorylineSchema1`** (S2-1 mapping) | fields |

---------------------------------------------------------------------------------------------------------------------
## S2. Structural refactors

**S2-1 MUST — Storyline component with nested sub-states and absolute ticks.**
Files: `Incidents/Storyline/{GameComponent_Storyline, OpeningCrashState, CourierContactState, FusionInvestmentState, FusionInvestmentStage, StorylineService, StorylineSchedule, StorylineDebugActions}.cs`, callers in FusionInvestment/Courier/OpeningCrash.
Design: the component owns three `IExposable` sealed sub-states (`Scribe_Deep`, no `Class`), created by field initializers and
null-guarded in PostLoadInit; intent methods replace the 13 public fields (`OpeningCrash.Started`, `.Pawns`, `.Record(pawns, now)`,
`CourierContact.Started`, `.IsDue(now)`, `FusionInvestment.Stage`, `.Investor`, `.Accept(amount, resultTick)`…). Countdowns become
absolute ticks (design §3.4): `nextCheckTick`, `dueTick`, `resultTick`; `StorylineService` wakes at the minimum due tick, so
`AdvanceTimer` and `storyServiceLastTick` disappear (`StorylineSchedule` keeps `SafeAddTicks`/`NextWakeTick`). `fusionInvestmentInvestorActive`
is replaced by `Investor != null`; `accepted/pending` become `FusionInvestmentStage {NotInvested, AwaitingResult, Delivered}` (default
member = absent label). `LoadedGame`, `NormalizeJuvenileGraphics`, `ResetTransientRuntimeState` leave the component (S2-11, S4).
Schema: old flat labels → S3 nested labels. Mapping in `Incidents.StorylineSchema1`: `openingCrashStarted→openingCrash/started`;
`openingCrashCheckTimer t (absent 360)→openingCrash/nextCheckTick = T+max(t,0)` (omitted when started); `openingCrashPawns→openingCrash/pawns` (+A08);
`courierRaid*→courierContact/started` (A10), `dueTick` (A09; −1 when the opening crash has not started);
`fusionInvestmentAccepted/Pending→fusionInvestment/stage` (pending⇒AwaitingResult; accepted∧¬pending⇒Delivered; else NotInvested);
`fusionInvestmentAmount→amount`; `fusionInvestmentTimer t→resultTick = T+max(t,0)` (AwaitingResult only); `fusionInvestmentNextOfferTick→nextOfferTick`;
`fusionInvestmentInvestor→investor` only when `…InvestorActive=True`; `InvestorActive=True` with a `null`/absent reference and stage NotInvested ⇒
`nextOfferTick = max(nextOfferTick, T + 7 days)` (replicates `ClearStaleInvestor`). MooGirl labels per X2. Old labels removed.
Behaviour: identical; `nextCheckTick` initializer 360 equals the old first check for new games (tick 0). Only change: A10 fix.
Tests: golden XML from S7/S10/S5 (StoryState), S1 (MooGirl_GameComp), synthetic pending-investment and active-investor-null cases; idempotence;
`StorylineScheduleTests` rewritten for absolute ticks; service unit tests for due-tick wake. Risk: medium.

**S2-2 MUST — Migration records + absolute ticks.** File `Incidents/Migration/MapComponent_Migration.cs`.
Private nested sealed `Migrant : IExposable {pawn, nextGrazeTick, killedByPlayer}`; `List<Migrant> migrants`; `departureTick`, `killWindowEndTick`
(window handling consumes the end tick once: `outcomeFailed=true; killWindowEndTick=0; abandon if departing`). `NotifyMigrationPawnKilled` sets
the record flag; `dataVersion`, `playerKilledMigrationPawnIds`, alignment code and `ReleaseLegacyBikiniLocks` are deleted. Labels renamed:
`originalMigrationCount→originalMigrantCount`, `killedMigrationPawns→playerKillCount`, `migrationOutcomeFailed→outcomeFailed`.
Upgrade: `Incidents.MigrationRecords` + `Incidents.MigrationBikiniLocks` (A05/A06). Behaviour: identical (countdowns ran every map tick).
Tests: golden XML (S9 empty instances, synthetic 2-pawn list with a killed id and a `null` ref, dataVersion 0/1/2/absent). Risk: medium.

**S2-3 MUST — Punisher records.** Files `Incidents/Punisher/{MapComponent_Punisher, PunisherLootBurst}.cs`.
Private nested `TrackedPunisher {pawn, spawnTick}`; `IsLeaving(now) = now − spawnTick ≥ ActiveDurationTicks` (the old flag was only ever set by that
test, `MapComponent_MugirlPunisher.cs:138-142`); labels `activePunishers`, `heldLoot`, `lootBursts`. SHOULD (low risk): in
`PunisherLootBurst.MoveLootInto`, a thing that `ThingOwner.TryAdd` rejects is placed immediately instead of staying reference-only (closes the
unsaved-loot path noted in module-incidents R12; unreachable in practice, no reachable behaviour change).
Upgrade: `Incidents.PunisherRecords`. Tests: golden XML (S9 empty), synthetic leaving/non-leaving entries. Risk: low.
S2-3b SHOULD: `PunisherDeathEffectUtility` is also used by the Dunk ability (`Genes/Dunk/Thing_MugirlDunkProp.Impact.cs:68`). Wave R keeps it in
Incidents (core-member-moves binds its `ShockwaveFast` to `IncidentsDefOf` by folder). Cleaner: move it to `Mugirl.Rendering` as a generic impact-burst
VFX with `ShockwaveFast` bound in core's Rendering DefOf (needs core's agreement, S4). No save impact.

**S2-4 MUST — Merged event-pawn tag utility.** `Shared/EventPawnTags.cs` (static) + `Shared/EventPawnFlags.cs`; fold `MugirlEventMarkers`
(tag constants private, `Parse/FlagOf` internal for tests) and delete it. Move `ClearRunawayPanic` to RunawayFarm (`RunawayFarmUtility`), keep
`PreparePassiveWildPawn` as `EventPawnTags.MarkPassive…` companion or in Migration/RunawayFarm (single owner each). Tag values unchanged (schema).
No save impact. Tests: rename `MugirlEventMarkersTests` → `EventPawnTagsTests`. Risk: low.

**S2-5 MUST — Wild Mugirls and shared helpers.** (a) After Wave R, `Incidents/WildMugirls/WildMugirlUtility.cs` already holds the wild members
(core-member-moves extracted `PlayerFaction/IsPlayerFaction/IsHostileToPlayer×2/TrySetPlayerFaction` into `Core/PlayerFactionUtility.cs`). Wave S deletes the
legacy kinds and helpers (A02/A03: `IsEscapeWildSlave`, `HasLegacyPlayerMugirlKind`, `NormalizeLoadedPlayerPawnKinds`); `IsMugirlPawn` → `MugirlIdentity.IsMugirlPawn`;
`NormalizePlayerKindToFactionDefault` becomes `NormalizeKindToFactionDefault` (predicate `kindDef == Mugirl_WildMugirl`), reused by `Incidents.PlayerEventPawns`.
(b) Move event-only request builders (`CreateFactionlessAdultRequest`, `CreateVisitorRequest`, `HasMugirlSlaveBackstories`) from `Core/GeneratedPawnUtility.cs`
to `Incidents/Shared/EventPawnRequestUtility.cs`; Core keeps `TryPassToWorld`/`Discard` (used by Corporate). Content: delete the two PawnKindDefs + zh lines;
fix duplicate `chemicalAddictionChance` in `Mugirl_SlaveBase` (`PawnKinds_Slave.xml:7,17`, keep the value the 1.7 loader actually applies — expected the
last, 0.01; verify via DefDatabase in the smoke run). Upgrade: A02/A03. Behaviour: identical. Risk: low (needs S5-4).

**S2-6 MUST — Remove legacy/dev-only content.** Delete the zh line for `Mugirl_WildManWandersIn` (core deletes `RetiredEvents.xml` + worker), IncidentDef `Mugirl_CourierRaid`
(`Incidents_CourierRaid.xml` + zh file) and `IncidentWorker_CourierContact` (baseChance 0, duplicated by `StorylineDebugActions.TriggerCourier`; in 1.6 it was
the duplicate-courier path of A10), `ChoiceLetter_FusionInvestment`, `Harmony_FoodRestrictionDatabase_ExposeData_GrassPolicy`,
`Harmony_HediffComp_GiveRandomSituationalThought_CompExposeData_PregnancyMood`, the two `ResetDefCache` calls. Upgrade: A01 (incl. this IncidentDef), A11, A14, A15.
Behaviour: dev "Execute incident: courier contact" entry disappears (the Mugirl debug action remains). Risk: low.

**S2-7 MUST — Runaway farm completion without a saved tick counter.** `QuestPart_RunawayFarmCompletion`: check every 240 ticks via
`quest`/`Find.TickManager` hash interval instead of `ticksUntilNextCheck` (design §3.4); movement refresh unchanged on non-check ticks. Schema: label dropped (X7).
Behaviour: only the 240-tick phase of the completion check shifts (≤240 ticks). Tests: unit test of the interval predicate. Risk: low.

**S2-8 SHOULD — Split `FusionInvestmentUtility` (455 lines).** `FusionInvestmentService` (state transitions driven by StorylineService),
`FusionInvestmentDialog` (DiaNode UI), `FusionInvestmentRewards` (drop pods), and a silver helper (`ColonySilverUtility`, check with Corporate for an
existing payment helper before adding one). No save impact. Behaviour identical (keep the reverse ListerThings order of silver removal). Risk: medium (pure move).

**S2-9 SHOULD — Courier diary comp.** Drop `CompProperties_CourierDiary.bookTitle` and Keyed `Mugirl.CourierDiary.Title`; the dialog title is
`parent.LabelCap` (EN "Courier diary", zh "运货员日记本" — identical output, `Mugirl_Diary.xml:6,34`, zh `ThingDef/Mugirl_Diary.xml:4,8`). Removes the
DefInjected compClass handle. No save impact. Risk: low.

**S2-10 MUST — Grass food policy created once.** `GrassFoodPolicyUtility.Ensure(db)` (create unless a policy carries the current translated label);
called from the new-game postfix and from a `[GameLifecycleHook]` that runs only when Mugirl was added to an existing game (core-plan S2-1 has no such
phase yet: S4/S5-3); legacy handled by `Grazing.GrassPolicy` (A14). Behaviour fix as A14.
Tests: unit test for the plant filter predicate; in-game: new game has one preset, delete it, save/load → stays deleted. Risk: low.

**S2-11 MUST — Lifecycle duties out of the story component.** Storyline keeps only its own schedule reset in its `FinalizeInit` override. The other
callees of `ResetTransientRuntimeState` (`MugirlStoryState.cs:72-80`) become `[GameLifecycleHook(GameLifecyclePhase.FinalizeInit)]` methods in their owners
(core-plan S2-1/S4). LifeStages gets `[GameLifecycleHook(FinalizeInit)] LifeStageBodyTypeUtility.NormalizeAllPawns` (same scan, same timing, log key
`Mugirl.LifeStages.Log.CorrectedBodyTypes`, comment: repairs body types changed by other code, e.g. body genes). Behaviour identical. Risk: low.

**S2-12 (decision requested of this module) — `GameComponent_GhoulRenderingRefresh` is not removed.** Core renames it to
`Mugirl.Rendering.GameComponent_PawnRenderRefresh` (generated type map) and keeps it as the per-game owner of the non-saved refresh schedule. Removing it would
only save one empty element per save but would push a per-game tick registry into the hub, whose constructor core keeps trivial. Accepted; nothing for this module
to do except dropping the `ClearPendingRefreshes` call (core deletes the utility). Risk: none.

**S2-13 MUST — DefOf follow-up.** Wave R (core-member-moves) already moves my fields into `IncidentsDefOf`, `SocialDefOf`, `TradeDefOf`, `LifeStagesDefOf`.
Wave S: delete `IncidentsDefOf.Mugirl_EscapeWildSlave/Mugirl_PreEscapeWildSlave` with their defs; give the JobDef field the alias form
`[DefAlias("Mugirl_Nuzzle")] Mugirl_NuzzleJob` if Wave R did not (the InteractionDef field `Mugirl_Nuzzle` occupies the name); drop fields left unused after the
refactor; `[MayRequire*]` where DLC. Cross-feature defs (`Mugirl_GiantCorporations_Hostile`, `AI_GC_Courier`, `WallRopeHitch`, `Mugirl_Milk`) are read from the
owner's DefOf. No save impact. Risk: low.

**S2-14 MUST — Hygiene.** Comments per A17–A19 and design §3.5 (Chinese, current rationale); `internal` by default (keep `public` only for XML/reflection types);
`sealed` leaves; `[MugirlPatch]` module from the closed set (Incidents, Grazing, Social, LifeStages, Trade; Appearance/Milk for moved files);
`QuestPart_SpawnCourier.spawnCell` initializer `IntVec3.Invalid` (validator default rule; same schema); private `TryAddSpawnPawnsInOnePod`, `SendLetter_NewTemp`
overload → `SendArrivalLetter` (QuestNode_Root_OpeningPodCrash:87,223); new-quest signal literal `CourierRaid_Spawn` → `CourierContact_Spawn` (existing quests keep their
stored signal, self-consistent); English log texts "CourierRaid quest aborted…" → "Courier contact…". Risk: low.

**S2-15 SHOULD — Content tidy.** Delete ThoughtDef `Mugirl_BrainWashing_End` (`Thoughts_Traits.xml:70-83`, zh `ThoughtDef/Thoughts_Traits.xml:12,14`;
requested by content L8 and the restraints plan). Split the rest of `ThoughtDefs/Thoughts_Traits.xml` by topic: `Thoughts_Mugirl.xml` (SingleshotWeaponAversionUnhappy,
AteHay, AteNutrient, Nuzzled — this module), `Thoughts_Restraints.xml` (BrainwashHappy — restraints), `Thoughts_Roping.xml` (Roper, Roped — roping), zh DefInjected
following; merge `Hediffs_Normal.xml` (only `Mugirl_Abasia`) into `Hediffs_Mugirl_Events.xml` and fix its duplicate `minSeverity` (stage 1, `:49,56`; keep the loader's
effective value, expected 0.5); merge `Keyed/StoryDebug.xml` into `Keyed/Incidents.xml` (the Misc_Gameplay split itself is content's Wave R tool,
`content-keyed-split.json`, which already deletes `Mugirl.FusionInvestment.MessagePostponed` and `Mugirl.Punisher.Departed`); delete `Mugirl.CourierDiary.Title`
with S2-9. No save impact. Risk: low.

**S2-16 SHOULD — Cross-module string contract.** `StorylineDebugActions.ParseCorporateOutcome` (`MugirlStoryDebugActions.cs:124-134`) consumes an enum once Corporate
returns one (S4). Risk: low.

Recorded, not changed this wave: `Harmony_Pawn_MindState_Notify_DamageTaken_EventPawn` skips the whole vanilla method for passive event pawns (needs in-game
verification before narrowing); `QuestNode_Root_MugirlWandererJoin` stores per-run signals on the Def-owned node (vanilla pattern, single-threaded QuestGen: add the
reason comment); punisher registration via a ThinkTree JobGiver (works; a comp would change the ThingDef).

---------------------------------------------------------------------------------------------------------------------
## S3. New save schema after Wave S (owner → labels)

`kind/type/default`. Labels absent ⇒ default. All defaults equal field initializers.

**`Mugirl.Features.Incidents.GameComponent_Storyline`** (`game/components/li`, ctor `(Game)`)
- `openingCrash` Deep `OpeningCrashState` (no Class): `started` Values bool false · `nextCheckTick` Values int 360 · `pawns` Collections/Reference `List<Pawn>` empty
- `courierContact` Deep `CourierContactState`: `started` Values bool false · `dueTick` Values int −1
- `fusionInvestment` Deep `FusionInvestmentState`: `stage` Values enum `FusionInvestmentStage` NotInvested (members `NotInvested`, `AwaitingResult`, `Delivered`) · `amount` Values int 0 · `resultTick` Values int −1 · `nextOfferTick` Values int 0 · `investor` References Pawn null
- Not saved: wake schedule (`nextWakeTick`, `nextStaleCheckTick`).

**`Mugirl.Features.Incidents.MapComponent_Migration`** (`maps/li/components/li`, ctor `(Map)`)
- `migrants` Collections/Deep `List<Migrant>` (no Class): `pawn` References Pawn · `nextGrazeTick` Values int 0 · `killedByPlayer` Values bool false
- `exitCell` Values IntVec3 Invalid · `departureTick` Values int 0 · `killWindowEndTick` Values int 0 · `originalMigrantCount` Values int 0 · `playerKillCount` Values int 0 · `outcomeFailed` Values bool false · `departureStarted` Values bool false

**`Mugirl.Features.Incidents.MapComponent_Punisher`** (`maps/li/components/li`, ctor `(Map)`, IThingHolder)
- `activePunishers` Collections/Deep `List<TrackedPunisher>`: `pawn` References Pawn · `spawnTick` Values int 0
- `heldLoot` Deep `ThingOwner<Thing>` (ctorArgs this) · `lootBursts` Collections/Deep `List<PunisherLootBurst>`: `originCell` Values IntVec3 default(IntVec3) · `startTick` Values int 0 · `arcs` Collections/Deep `List<DebrisArc>`: `thing` References Thing · `targetCell` Values IntVec3 default · `launchHeight`/`sideSwing`/`spin` Values float 0

**QuestParts** (`questManager/quests/li/parts/li[@Class]`, labels unchanged; part insertion order in each QuestNode is schema)
- `Mugirl.Features.Incidents.QuestPart_SpawnCourier`: `map` Ref Map · `faction` Ref Faction · `spawnCell` Values IntVec3 Invalid · `courier` Ref Pawn · `inSignal` Values string null
- `Mugirl.Features.Incidents.QuestPart_JoinOnRescue`: `inSignalRescued`, `inSignalRecruited` Values string null · `pawns` Collections/Reference `List<Pawn>`
- `Mugirl.Features.Incidents.QuestPart_SpawnRunawayFarm`: `inSignal` string · `site` Ref Site · `protectedTargetTag` string · `runawayPawns` RefList Pawn · `hitches`, `farmThings` RefList Thing · `rancher` Ref Pawn · `rancherFaction` Ref Faction · `spawned` bool false
- `Mugirl.Features.Incidents.QuestPart_RunawayFarmCompletion` (QuestPartActivable base labels): `site` Ref Site · `spawnPart` Ref QuestPart (index-based ID) · `failSignal` string · `resolved` bool false

**Class-only owners (no own labels):** JobDrivers `Mugirl.Features.Incidents.JobDriver_ReadCourierDiary` (toils: Goto, Do), `…JobDriver_TalkToCourier`, `…JobDriver_TalkToFusionInvestor` (base `JobDriver_TalkToEventPawn`: Goto, Do), `Mugirl.Features.Social.JobDriver_MugirlNuzzle` (Goto, WaitToBeAbleToInteract, Do) — toil order is schema; `Mugirl.Features.Social.Thought_MugirlOnly` (vanilla Thought_Memory labels).

**Saved values owned by this module:** questTags `Mugirl_Migration`, `Mugirl_RunawayFarm`, `Mugirl_FusionInvestor`, `Quest<id>.RunawayFarm_Protected`; signal strings stored in parts (`Quest<id>.CourierContact_Spawn` for new quests, `CourierRaid_Spawn` in old ones, `Quest<id>.RunawayFarm_{Success,Fail,Expired}`, `Quest<id>.pawns.{Rescued,Recruited,Killed,LeftBehind}`).
**Save-visible defNames:** QuestScriptDef `Mugirl_SlaveOpeningPodCrash`, `Mugirl_SlaveRefugeePodCrash`, `Mugirl_SlaveWandererJoins`, `Mugirl_CourierContact`, `Mugirl_RunawayFarmQuest`; IncidentDef `Mugirl_SlaveRefugeePodCrash`, `Mugirl_SlaveWandererJoins`, `Mugirl_Migration`, `Mugirl_FusionInvestment`, `Mugirl_RunawayFarmQuest`; SitePartDef `Mugirl_RunawayFarm`; PawnKindDef `Mugirl_WildMugirl`, `Mugirl_Beginning_Slave`, `Mugirl_EscapeSpaceSlave`, `Mugirl_EscapeWanderSlave`, `Mugirl_Slave`, `Mugirl_Punisher`; JobDef `Mugirl_ReadCourierDiary`, `Mugirl_TalkCourier`, `Mugirl_TalkFusionInvestor`, `Mugirl_Nuzzle`; ThinkTreeDef `Mugirl_Punisher` (+ key hash); InteractionDef `Mugirl_Nuzzle`; ThoughtDef `Mugirl_Nuzzled`, `Mugirl_AteHay`, `Mugirl_AteNutrient`; HediffDef `Mugirl_Abasia`, `Mugirl_RunawayPanic`, `Mugirl_PunisherKnockout`; ThingDef `Mugirl_CourierDiary`, `Mugirl_PunisherRace` (+`Corpse_`); generated RecipeDef `Administer_Mugirl_Milk` (milk owner).
**Grazing / LifeStages / Trade:** no save-visible owners (the grass preset is vanilla `FoodPolicy` data; `StockGenerator_MugirlSlaves` lives in Def data only).

---------------------------------------------------------------------------------------------------------------------
## S4. Edits needed from other owners

Already covered by other specs (verified in `spec/`): DefOf member moves into `IncidentsDefOf`/`SocialDefOf`/`TradeDefOf`/`LifeStagesDefOf` and the
`PlayerFactionUtility` extraction (`core-member-moves.json`); settings field renames (`enableOpeningStoryline`, `enableMigrationEvent`,
`enableFusionInvestmentEvent`, `enableRunawayFarmQuest`, core S2-2); `JobDefs_Social.xml` and the `MugirlPatch.xml` split (content S2-1/S2-2, Wave R); the
`Misc_Gameplay.xml` split into `Keyed/Incidents.xml` and `Keyed/Grazing.xml` (`content-keyed-split.json`); `KindApparelUtility` into `Incidents/Shared`
(milk.json); MooGirl-era rows (`moogirl-types.json`, `moogirl-defs.json`). Remaining requests:

- **Core / lifecycle hub (core-plan S2-1)**: a way to run a hook only when Mugirl is added to an existing game (new `GameLifecyclePhase.AddedToExistingGame`
  raised from `LoadedGame` when the hub's `ExposeData` did not run during the load, or a `GameComponent_Mugirl.WasInSave` flag). Needed by S2-10.
- **Core / SocialDefOf (core-member-moves)**: the JobDef field moved from `Mugirl_DefOf.Job_Nuzzle` must become `[DefAlias("Mugirl_Nuzzle")] Mugirl_NuzzleJob`
  when the defName coupling renames it (the InteractionDef field `Mugirl_Nuzzle` occupies the name) — otherwise Wave R does not compile.
- **Core / `RemoveRetiredIncidentReferences` step**: add IncidentDef `Mugirl_CourierRaid` and `MooGirl_CourierRaid` to its name list (scoped to IncidentDef
  contexts: `storyState/recentRandomIncidents`, `storyState/lastFireTicks` pairs, `incidentQueue/queuedIncidents/li/firingInc/def`; never `quests/li/root`).
- **Core / Rendering**: drop the `GhoulRenderingRefreshUtility.ClearPendingRefreshes` caller (it is mine: `MugirlStoryState.cs:79`, deleted with S2-11); optional
  S2-3b (punisher impact VFX to Rendering, bind `ShockwaveFast` in the Rendering DefOf).
- **Core / Core files from this module**: `Core/GeneratedPawnUtility.cs` loses `CreateFactionlessAdultRequest`, `CreateVisitorRequest`,
  `HasMugirlSlaveBackstories` to `Incidents/Shared/EventPawnRequestUtility.cs` (S2-5b) — core deletes them or lets this module edit the file (S5-4).
- **SaveUpgrade framework (A1)**: DOM helpers for `Thing_<def><n>` id parsing and player-faction load-id lookup; DOM→PostLoad session flags
  (`LegacyMigrationBikiniLocks`); generic rule "ThinkTreeDef rename ⇒ drop `lastJobGiverKey` of jobs on that tree" (else my step X3); per-module partial
  LegacyNames files (core S5-Q3); generator honours the `TRANSITIONAL` marker for save-visible rows (`ChoiceLetter_MugirlFusionInvestment`).
- **Corporate**: read story state through the new API (`CorporateIntroduction.cs:75-78,233-237` → `Storyline.OpeningCrash.Started`, `CourierContact.Started`;
  `CorporateNetwork.Quests.cs:958-959` → `FusionInvestment.Stage == Delivered`); `DevTriggerRepresentative` returns an enum (S2-16); `RecoverCourierHistory`
  (`CorporateIntroduction.cs:364-376`) is corporate legacy logic and, if it becomes an upgrade step, must run after `Incidents.StorylineSchema1`. Calls into
  Corporate (`RememberCourier`, `NotifyCourierChoice`, `RememberFusionInvestor`) keep their signatures.
- **Milk / Appearance owner**: accept the seven `Features/Appearance/Apparel/*` files (FixedColor, pregnant variants) and
  `Features/Milk/Recipes/Harmony_RecipeDefGenerator_DrugAdministerDefs_AdministerMilk.cs`; delete the empty `ResetDefCache` stubs (MI A22); add
  `[GameLifecycleHook(FinalizeInit)]` to `MugirlMilkingAnimation.ResetTransientState`; own core's planned `Harmony_Hediff_PostAdd_PregnantApparel` (it calls
  `PregnantApparelUtility.NotifyPregnancyChanged`).
- **Mounting (combat)**: `[GameLifecycleHook(FinalizeInit)]` on `MountedCombatController.ResetTransientState`; comment `MountedCombatController.VerbState.cs:10`.
- **Restraints / Roping**: accept `Thoughts_Restraints.xml` / `Thoughts_Roping.xml` from the S2-15 split (or send edits to me as owner of `Thoughts_Traits.xml`).
- **Owners of comments naming `MugirlStoryState`**: `Core/MugirlLog.cs:19`, `Milk/Animation/MugirlMilkingAnimation.State.cs:14,43`,
  `Milk/Gauge/MilkGaugePopupSession.cs:5`, `Mounting/Combat/MountedCombatController.VerbState.cs:10`.

---------------------------------------------------------------------------------------------------------------------
## S5. Open questions (with proposed answers)

1. **`Mugirl.Features.Apparel` shadows `RimWorld.Apparel`** (CS0118 in all `Mugirl.Features.*` code). → Already resolved the same way by milk.json: apparel-look
   code in `Features/Appearance/Apparel`, `KindApparelUtility` in Incidents. Add `Apparel` to the forbidden namespace segments of design §3.1.
2. **Transitional types (moved in Wave R, deleted in Wave S) that are save-visible.** → Same `TRANSITIONAL` marker as core.json; the generator emits no
   old→new row for them, the hand entry decides (`ChoiceLetter_MugirlFusionInvestment` → `Verse.StandardLetter`).
3. **Hook for "Mugirl added to an existing game"** (grass preset now; any future first-install setup). → New lifecycle phase raised by the core hub; the
   upgrader stays inert for saves without Mugirl.
4. **Who edits `Core/GeneratedPawnUtility.cs` in Wave S to move the event-only builders out?** → Core does it on request (or grants this module that one edit).
5. **Delete IncidentDef `Mugirl_CourierRaid`?** → Yes (dev-only, baseChance 0, ThreatSmall category for a peaceful visit, and the path that produced duplicate
   couriers in 1.6). If rejected, add IncidentDef `Mugirl_CourierRaid → Mugirl_CourierContact` to the spec (explicitRefs `1.7/Defs/Storyteller/Incidents_CourierRaid.xml:5`,
   zh `DefInjected/IncidentDef/Incidents_CourierRaid.xml:4`) and keep the worker.
6. **`GameComponent_GhoulRenderingRefresh` removal?** → No: accept core's rename to `Mugirl.Rendering.GameComponent_PawnRenderRefresh` (S2-12).
7. **Keep the per-load body-type normalization (A16)?** → Keep as a LifeStages `[GameLifecycleHook(FinalizeInit)]` (invariant against external body-type
   changes; no behaviour change).
8. **Countdown → absolute ticks in Storyline/Migration (S2-1/S2-2)** changes label names and needs `ticksGame` in the DOM step. → Do it (design §3.4); the
   conversion is exact because the countdowns ran every tick.
