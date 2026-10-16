# Corporate module — Wave S plan (spec companion of `corporate.json`)

Scope: `Source/MugirlRace/Features/Corporate/**` (after Wave R: everything in `Mugirl.Features.Corporate`, incl. the moved
`Raids/` and `Pricing/CorporateForeignBehavior.cs`), corporate content listed in `corporate.json` → `ownedContentFiles`,
upgrade code in `SaveUpgrade/Steps/Corporate/**` and `SaveUpgrade/PostLoad/Corporate/**`, unit tests in `Source/Tests/MugirlRace.Tests/Corporate/**`.

Paths without prefix are relative to `Source/MugirlRace/Features/Corporate/` of the pre-refactor tree (= the snapshot
`DevData/Backups/PreRefactor-1.7-20261015`). Type names below are the post-Wave-R names (`corporate.json`).
Evidence keys: **S9–S11** = real saves (real-saves.md §1); **EvAll6** = `DevData/Evidence/CorporateValidation-AllDlc6/Saves/CorporateRoundtrip.rws`
(archived 1.6.4871 validation save; same shapes in AllDlc8/9, NoIdeology1/4); **EvBug** = `DevData/Evidence/BugReportFixes-20261011/*/Saves/CorporateBugReportRoundtrip.rws`.
I read these files read-only for this plan (shapes quoted below were extracted from them).

**Principle used for MUST/SHOULD:** every change that touches the save schema is MUST, so schema 1 is final for this module and no
`1 → 2` step is needed later. SHOULD items never change the schema.

All corporate DOM steps and post-load steps run only in a legacy session (schema 0, design §1) and detect applicability from shape;
each step is idempotent (second run = no-op, stated per step). Order inside the corporate block: C2, C3, C4, C5 (they rewrite
`li` items at their legacy paths), then C1 (regroup, moves the already-rewritten lists), then C6, C7. All run after the global type and
def renames (design §5.3 stages 1–3), so they select owners by the NEW class names.

---------------------------------------------------------------------------------------------------------------------
## S1. Legacy code removal

| ID | Site (pre-refactor) | Legacy shape handled (evidence) | Mechanism | Domain code deleted |
|---|---|---|---|---|
| A1 | `Trade/CorporateOrder.cs:31` (Scribe default −1), `:46-50` | `corporateOrders/li` without `<creditedTurnover>` (≤0.6.1; EvAll6 orders `id 21/22`: `<id>21</id><def>DevilstrandCloth</def><savedLabel>魔菇布</savedLabel><quantity>1</quantity><deliveredQuantity>1</deliveredQuantity><paid>10</paid>…<state>Completed</state><goods/>`) | **DOM step C3**: missing ⇒ `floor(paid × min(quantity, deliveredQuantity) / quantity)` (0 when quantity ≤ 0; absent `paid`/`deliveredQuantity` = 0), written only if ≠ 0 | PostLoadInit backfill; Scribe default becomes 0 |
| A2 | `Trade/CorporateOrder.cs:35` comment | orders without `speed`/`expediteFee` (pre-50f7979; EvAll6) | **drop** (defaults Standard/0 are the right meaning) | comment |
| A3 | `Trade/CorporateNetwork.Trade.cs:37-67` `MigrateLegacyStockSamples`, called ungated from `Network/CorporateNetwork.cs:118` | unique-weapon stock samples with random traits (pre-50f7979 saves with Odyssey) | **post-load phase C step P4** `RespecifyUniqueWeaponSamples`: for every stock offer whose sample has `CompUniqueWeapon` and `CanAutomaticallySupply`, call domain `CorporateNetwork.ReplaceStockSample(offer)` (generic: rebuilds the sample from def/stuff/quality through the product factory; discards the old sample unless an order's goods reference it) | the migration method and the `LoadedGame` call |
| A3b | `Trade/CorporateNetwork.Trade.cs:182-203` non-destroyable branch | 0.6.0 samples of non-destroyable defs | **keep as invariant** (other mods can make a def non-destroyable later); reword comment, no upgrader | “旧样品” wording |
| A4 | `Trade/CorporateTradeSettings.cs:13-15,26-28` | dead config fields (no reader; not set by `CorporateTradeSettings.xml`) | **drop** | `specialOrderPriceFactor`, `specialOrderMinimumUnitPrice`, `rareOrderDefs`, `specialOrderDefs` |
| A5 | `People/CorporatePersonOffer.cs:15-16,28`, `People/CorporateNetwork.People.cs:67-77` | offers without `<priceVersion>` (≤0.6.6; EvAll6 `<li><id>101</id><pawn>Thing_Mugirl37498</pawn><price>4710</price></li>`); S10 has `priceVersion=1` on all 4 offers | **DOM C4** removes `priceVersion`, records the offer ids lacking it in the session (`Corporate.LegacyPriceOffers`); **post-load phase C P3** sets `price = PeopleBasePrice(pawn)` | `priceVersion` field/label, the `== 0` branch |
| A6 | `People/CorporateNetwork.People.cs:59-65` (ungated), `People/CorporateShowcaseApparel.cs:31-39` | unpaid showcase offers wearing pre-0.6.7 random apparel | **P3** (same offers, before repricing): unpaid ∧ kind = showcase ∧ `!IsCompleteOutfit` ⇒ `CorporateShowcaseApparel.TryDress(pawn)` | migration loop, `TryEnsureOutfit` |
| A7 | `People/CorporatePersonOffer.cs:12,25` (sentinel −1); fallbacks `People.cs:38,343`, `Services/CorporateNetwork.Services.cs:113-116` | `paid=True` without `<paidAmount>` (0.6.0) | **DOM C4** (must precede A5 repricing, hence XML phase): write `paidAmount = price`. Old default −1 for unpaid offers needs no materialisation (unpaid offers ignore it; new default 0) | 3× `paidAmount < 0 ? price : paidAmount`; default becomes 0 |
| A8 | `People/CorporateNetwork.People.cs:324-335` (“旧档可能…已完成 PreTraded”) | paid ∧ ¬delivered offers from ≤0.6.6 whose pawn may already be transferred (0.6.6 always bought as slave, `buyAsColonist` absent = false is correct) | **P3**: for those offers `transferApplied = IsPlayerFaction(pawn.Faction) && pawn.IsSlaveOfColony` | the faction/colonist re-check and comment; `if (!offer.transferApplied) { PreTraded…; transferApplied = true; }` remains |
| A9 | `People/CorporateNetwork.People.cs:30-43` | not legacy (any pawn reference can die) | **keep** as domain invariant | only the A7 fallback inside |
| A10 | `Services/CorporateNetwork.Services.cs:262` (default −1), `:270-274` | `corporateSupportCallsUsed` absent ⇒ 0.6.0, where `corporateSupportWeek` is absent too (EvAll6 has neither); real S9–S11 write `0` | **drop**: new default 0 gives the same value; C1 just moves the label | sentinel branch |
| A11 | `Services/CorporateNetwork.Services.cs:268-269`, `Network/CorporateNetwork.cs:144` | none (defensive normalisation, allocates per load) | **drop**; `creditedPawnIds` becomes `HashSet<int>` | `Max(0)`, `Where/Distinct`, `RemoveAll(null)` |
| A12 | `Services/LordJob_CorporateSupport.cs:10,21-23,40-65,71-72` | `lords/li/lordJob[@Class=…LordJob_CorporateSupport]` without `supportGraphVersion` (0.6.1.1–0.6.4): vanilla `Lord.SetJob` appended PanicFlee with `AddTransition(highPriority:true)` (decompiled 1.7 `Verse.AI.Group.Lord.SetJob`): toils `[0 hunt, 1 exit, 2 flee]`, flattened triggers `[0 FractionPawnsLost(exit→flee), 1 FractionPawnsLost(hunt→flee), 2 TicksPassed(leave)]`; saved by `Lord.ExposeData_StateGraph` as dictionaries `lordToilData`/`triggerData` (`keys`/`values` li) + `curLordToilIdx`. No real or archived save contains this lord. | **DOM C7** + **post-load phase C P5** (details below) | `supportGraphVersion`, the `AddFleeToil` override's version logic (becomes `=> false`), the whole `LordJobTick` migration |
| A13 | `Missions/CorporateMission.cs:62-63,172-175`, `Missions/CorporateNetwork.Quests.cs:493-496`, `Pricing/CorporateMoney.cs:71-72`, test `CorporateMoneyTests.cs:82-97` | Distribution mission without `<salesGoal>` — **present in EvAll6** (`<li><id>4</id><kind>Distribution</kind><state>Expired</state>…<cost>764</cost><goodwill>3</goodwill><count>53</count><product>ComponentIndustrial</product>…</li>`), i.e. in the archived fixtures the in-game upgrade suite will load (module report said “no released save”) | **DOM C5**: `salesGoal = max(1, ceil(count × 0.8))` (decimal) | `SalesTarget` fallback, `LegacySalesTarget`, its test |
| A14 | `Missions/CorporateNetwork.Quests.cs:57-60,833,840-848`; `Introduction/CorporateIntroduction.cs:407,447-454,799` | quests with `root` null (dev fixtures only) | **drop** (QuestScriptDefs `Mugirl_CorporateMission`, `Mugirl_CorporateIntroductionQuest` keep their names) | `RepairMissionQuestRoot`, `RepairIntroductionQuestRoot` |
| A15 | `Missions/CorporateNetwork.Quests.cs:62,67-79` (ungated on every load) | ≤0.6.3 lost one-time fusion invitation (latest fusion mission Failed ∧ ¬spawned ∧ site has map) | **post-load phase A P1** `ReopenLostFusionInvitation` (same condition, once) + domain fix S2-8 | `RepairFusionSiteGenerationLockout` |
| A16 | `Introduction/CorporateIntroduction.cs:14-15,62-86,364-401,569-587,778-779` | Mugirl saves without a `CorporateIntroduction` component (pre-0.6.0: real S5–S8); the `corporateIntroductionInitialized`/`migrated` labels (S9–S11: `corporateIntroductionInitialized=True`) | **DOM C6** (drop/rename labels; detect a missing component and set session flag `Corporate.RetrofitIntroduction`) + **post-load phase B P2** `RetrofitCorporateIntroduction` | `initialized`, `RecoverCourierHistory`, the `!initialized` branch of `LoadedGame` |
| A17 | `Introduction/CorporateIntroduction.cs:32-33,64-66,95-99,108-138` (ungated) | saves without the corporate faction (pre-faction Mugirl saves; any game the mod is added to) | **not legacy**: kept as the domain invariant “the corporate faction exists” (cheap `Find` once per game on the first update while playing, transient retry tick); comments rewritten, fields renamed (`factionMigrationPending` → `factionCheckPending`, not saved) | “migration/旧档” wording |
| A18 | `Introduction/CorporateIntroduction.cs:18` | — | **drop** comment | — |
| A19 | `Network/CorporateRecord.cs:13-19` | null-default shape freeze | **drop** (type replaced, S2-2) | whole type |
| A20 | `UI/Terminal/Pages/CorporateHistoryPage.cs:108-110`; `Baselines/dynamic-lookups.allow.txt:42`; `C.frozen-keys` | `corporateRecords/li/key` = Keyed key (17 values, all releases; S10: `<tick>9509</tick><key>Mugirl.CorporateIntro.Title</key><detail>商业联系已建立</detail>`; EvAll6: Purchase/Sale/Refund/Finance/People/CQ keys) | **DOM C2** value map key → `CorporateHistoryKind` | `record.key.Translate()` |
| A21 | `Network/CorporateNetwork.cs:5-7` (flat labels by design) | 40 flat `corporate*` labels (S10 element children list) | **DOM C1** regroup | flat Scribe calls in 6 partials |
| A22 | “cannot rename” comments: `Introduction/CorporateIntroduction.cs:10`, `Introduction/CorporateIntroductionQuest.cs:7`, `Missions/CorporateMissionQuest.cs:7`, `Missions/CorporateMission.cs:7`, `Services/LordJob_CorporateSupport.cs:6`, `Services/JobDriver_CorporateSupportTend.cs:7`, `UI/Entry/JobDriver_CorporateTalkRepresentative.cs:6`, `UI/Entry/JobDriver_CorporateUseComms.cs:6`, `UI/Terminal/Window_CorporateComms.cs:12` | names frozen for saves | **type map** (Wave R, generated from the spec) + enum map for `Native` | the comments |
| A23 | ~30 “same as 1.6” comments (module report A23 list) | — | **drop** (rewrite as intent) | comment text |
| A24 | `Compatibility/Savegames/CorporateValidationSaveMigration.cs` (core-owned) | `game/components/li[@Class="Mugirl.CorporateRuntimeValidation"\|"Mugirl.CorporateVisualValidation"]` (EvAll6/8/9, NoIdeology1/4) | **type map Drop(RemoveNode)** in `LegacyTypes` (owner: SaveUpgrade/core, see S4) | whole class + its test → step test |
| A25 | `Introduction/CorporateIntroduction.cs:26,789` `nextServiceTick` (saved; S9 2501, S10 14337, S11 30001) | label carries both a 2500-tick throttle and the 2-day attack cooldown (`NotifyDamage :288`) | **owner-scoped label rename** in C6: `nextServiceTick` → `serviceResumeTick` (value copied; a throttle value only delays ≤ 2500 ticks, exactly as today) | — (field split, S2-5) |
| A26 | `1.7/Languages/*/Keyed/CorporateServices.xml:6` `Mugirl.CorporateServices.Counting` (“Existing saves start…”) | text describing old-save behaviour | **drop** the sentence (content) | — |

### DOM steps (`SaveUpgrade/Steps/Corporate/`)

**C2 `CorporateHistoryKinds`** — owner `GameComponent(CorporateNetwork)`, path `corporateRecords/li` (before C1 moves it).
Map `key` → `<kind>`:
`Mugirl.CorporateIntro.Title`→AccountOpened, `Mugirl.Corporate.RecordSale`→ProductSold, `.RecordPurchase`→Purchase, `.RecordRefund`→Refund,
`Mugirl.CorporateFinance.LoanSigned`→LoanSigned, `.Payment`→LoanPayment, `.DefaultTitle`→LoanDefaulted, `.Liquidated`→CollateralLiquidated,
`Mugirl.CorporatePeople.Purchased`→PersonPurchased, `.Sold`→PersonSold, `Mugirl.CorporateServices.Unlocked`→ServiceLevelReached,
`.GiftRecord`→WeeklyGiftClaimed, `.SupportRecord`→SupportCalled, `Mugirl.CQ.AcceptedRecord`→MissionAccepted, `.ClaimedRecord`→MissionClaimed,
`.FailedRecord`→MissionFailed, `.ProtectedRecord`→ResearchersProtected.
`detail` → `<note>` verbatim, except: AccountOpened drops it (the text is the translated `CompletedStatus`, rebuilt at display time);
ServiceLevelReached parses it as int into `<count>` (falls back to `<note>` if not an int); empty detail (finance rows write `<detail></detail>`) is dropped.
`tick`/`amount` copied. Unknown key ⇒ remove the `li`, one report line. Idempotent: legacy path gone after C1.

**C3 `CorporateOrderLedger`** — path `corporateOrders/li`: A1 backfill; `paid` → `paidAmount`; remove `savedLabel`. Idempotent (path moved).

**C4 `CorporatePersonOfferReceipts`** — path `corporatePeopleOffers/li`: A7; for each li without `priceVersion` record `id` in session list
`Corporate.LegacyPriceOffers` (with flags paid/delivered); remove `priceVersion`. Idempotent (path moved).

**C5 `CorporateMissionKinds`** — path `corporateMissions/li` (no `Class` today; `kind` absent ⇒ `Processing`, `state` absent ⇒ Available).
Set `Class`, rename and prune per kind, remove `kind`. Labels common to every kind and kept as is: `id, state, offeredTick, acceptByTick,
acceptedTick, deadline, duration, reward, goodwill, quest, pendingGoods`.

| legacy `kind` | new `Class` (`Mugirl.Features.Corporate.`…) | renames | kept | removed |
|---|---|---|---|---|
| Processing (absent) | `CorporateProcessingMission` | `count`→`quantity`, `progress`→`delivered`, `cost`→`deposit` | `recipe, product, stuff, batches, ingredients` | all others |
| Distribution | `CorporateDistributionMission` | `count`→`quantity`, `progress`→`sold`, `cost`→`wholesaleCost` | `product, salesGoal` (A13 backfill) | all others (incl. `stuff`, never set) |
| Investment | `CorporateInvestmentMission` | `cost`→`investment` | `highRisk` | all others |
| Native | `CorporateReferralMission` | `nativeRoot`→`questRoot` | — | all others |
| Purge | `CorporatePurgeMission` | `count`→`targetCount`, `progress`→`deadTargetCount` | `enemy, threatPoints, tile, site, targets, spawned` | all others |
| Fusion | `CorporateFusionMission` | `count`→`targetCount`, `progress`→`deadTargetCount` | `tile, site, targets, spawned, investor, originalInvestor, choice, dialogueShown, gratitudeRevoked` | all others |

“Removed” labels in real/archived saves are only `null` references and empty lists (S10 Purge li: `<enemy>Faction_3</enemy><site>null</site><quest>null</quest><investor>null</investor><targets/><ingredients/><pendingGoods/>`); a removed label with a non-default value is reported. `tile` text (`91788,0`) is moved unparsed. Unknown kind ⇒ drop li + report. Idempotent (path moved; a li that already has `Class` is skipped).

**C1 `CorporateNetworkRegroup`** — owner `GameComponent(CorporateNetwork)`; always creates `history`, `trade`, `loan`, `people`, `missionBoard`, `missionBoard/fusion`, `services` (empty if nothing moves into them).

| legacy label | new path | value rule |
|---|---|---|
| `corporateVault` | `vault` | unchanged (ThingOwner, `innerList`) |
| `corporateNextRefresh` | `nextRefreshTick` | — |
| `corporateSequence`, `nextMissionId` | `nextId` | `max(sequence + 1, nextMissionId)` with absent = 0 / 1 (S10: 20 / 7 ⇒ 21) |
| `corporateRecords` | `history` | items already rewritten by C2 |
| `corporateStock`, `corporateOrders` | `trade/stock`, `trade/orders` | — |
| `corporateLoanPrincipal`, `…Interest`, `…OverdueInterestTotal` | `loan/principal`, `loan/interest`, `loan/overdueInterestTotal` | float text → `long` subunits: `round(decimal(text) × 10000, AwayFromZero)` |
| `corporateLoanOriginalPrincipal`, `…LastAccrualTick`, `…DueTick`, `…NextRaidTick`, `…Defaulted`, `…CollateralValue`, `…CollateralLiquidated` | `loan/originalPrincipal`, `lastAccrualTick`, `dueTick`, `nextRaidTick`, `defaulted`, `collateralValue`, `collateralLiquidated` | — |
| `corporateLoanThreeDayReminder`, `…OneDayReminder` | `loan/threeDayReminderSent`, `loan/oneDayReminderSent` | — |
| `corporateLoanSignedDailyRate`, `…SignedOverdueRate`, `…SignedLiquidationRatio` | `loan/dailyRate`, `loan/overdueDailyRate`, `loan/liquidationRatio` | absent ⇒ materialise old defaults 0.02 / 0.03 / 0.5 (new labels are `forceSave`) |
| `corporateLoanFaction`, `corporateLoanCollateral`, `corporateFinancePendingSilver` | `loan/creditor`, `loan/collateral`, `loan/pendingSilver` | — |
| `corporatePeopleOffers`, `corporatePeopleAwaitingWorld`, `corporatePeoplePendingSilver` | `people/offers`, `people/awaitingWorld`, `people/pendingSilver` | offers rewritten by C4 |
| `corporateMissions` | `missionBoard/missions` | items rewritten by C5 |
| `fusionQualificationSeen`, `fusionInvitationCreated`, `fusionInvitationTick`, `retainedFusionInvestor`, `retainedFusionInvestorName` | `missionBoard/fusion/qualified`, `created`, `dueTick`, `investor`, `investorName` | — |
| `corporateTradeTurnover`, `corporateCreditedPersonIds` | `services/turnover`, `services/creditedPawnIds` | ids de-duplicated |
| `corporateGiftWeek`, `corporateFreePersonWeek`, `corporateSupportWeek`, `corporateSupportCallsUsed` | `services/giftClaimedWeek`, `freePersonClaimedWeek`, `supportWeek`, `supportCallsUsed` | absent calls ⇒ omitted (= 0, A10) |

Idempotent: no legacy label left after one run.

**C6 `CorporateIntroductionState`** — owner `GameComponent(CorporateIntroduction)`: remove `corporateIntroductionInitialized`; `migrated` → `retrofitted`;
`completed=True` ⇒ `<stage>Completed</stage>`, else `pending=True` ⇒ `<stage>AwaitingRepresentative</stage>`, else omitted; remove `pending`, `completed`;
`courierChoice` 1 ⇒ `<courierOutcome>Released</courierOutcome>`, 2 ⇒ `Confronted`, 0/absent ⇒ omitted (other ints reported, omitted);
`nextServiceTick` → `serviceResumeTick`. If the save has Mugirl but `game/components` has no `CorporateIntroduction` li ⇒ session flag
`Corporate.RetrofitIntroduction`. Idempotent (legacy labels gone; the flag is per session).

**C7 `CorporateSupportLordGraph`** — every `maps/li/lordManager/lords/li` whose `lordJob@Class` is `LordJob_CorporateSupport`: remove
`lordJob/supportGraphVersion`; if `triggerData/keys` contains `2` (legacy flee graph; the new graph has a single trigger 0): keep the
`triggerData` pair with key 2 and rewrite its key to `0`, remove the pairs with keys 0 and 1 (keys/values edited pairwise); remove the
`lordToilData` pair with key 2; if `curLordToilIdx` = 2 set it to 0. Record the lord `loadID` (+ whether it was 2) in session list
`Corporate.SupportLords`. Idempotent: after the rewrite no trigger key 2 exists.

### Post-load steps (`SaveUpgrade/PostLoad/Corporate/`)

| Step | Phase | Action | Domain API used (generic, no legacy names) |
|---|---|---|---|
| P1 `ReopenLostFusionInvitation` (A15) | A (after FinalizeLoading) | latest Fusion mission by id is Failed ∧ ¬spawned ∧ `site?.HasMap == true` ⇒ reopen | `CorporateMissionBoard.ReopenFusionInvitation(int now)` (also used by S2-8) |
| P2 `RetrofitCorporateIntroduction` (A16) | B (Prefix `Game.FinalizeInit`, maps spawned) | if session flag: build `CorporateCourierFacts` from the last quest whose root is the courier-contact QuestScriptDef (Incidents' post-rename DefOf) and its `QuestPart_SpawnCourier` (`courier`, `map`); `EndedSuccess` ⇒ Released, `EndedUnknownOutcome` ⇒ Confronted (contactTick = `cleanupTick` when > 0); `courier.Dead` ⇒ died; call attach | `CorporateIntroduction.AttachToExistingGame(CorporateCourierFacts? facts)` |
| P3 `RepriceShowcaseOffers` (A5, A6, A8) | C (after LoadedGame; pawns fully initialised, market value available) | for recorded offers: unpaid ∧ showcase kind ∧ `!IsCompleteOutfit` ⇒ `TryDress`; then `price = PeopleBasePrice(pawn)`; paid ∧ ¬delivered ⇒ `transferApplied = IsPlayerFaction(pawn.Faction) && pawn.IsSlaveOfColony` | `CorporateShowcaseApparel.IsCompleteOutfit/TryDress`, `CorporateNetwork.PeopleBasePrice`, `CorporatePeopleDesk.Find(id)` |
| P4 `RespecifyUniqueWeaponSamples` (A3) | C | see A3 | `CorporateNetwork.ReplaceStockSample(CorporateStockOffer)` |
| P5 `ReassignSupportLordDuties` (A12) | C | for recorded lords: if it was fleeing ⇒ `lord.GotoToil(lord.Graph.lordToils[0])`; for owned pawns: was fleeing ∨ `CurJobDef == JobDefOf.TendPatient` ⇒ `jobs.EndCurrentJob(InterruptForced)` (exactly the old first-tick behaviour) | vanilla only |

The domain `CorporateIntroduction.LoadedGame` attaches with `null` facts only when the component was not loaded from the save (transient
flag set in `ExposeData` during LoadingVars) and P2 has not attached it in this load — this is the “mod added to an existing game” path
(design §3.4 “constructed but never exposed is valid”), not legacy code.

---------------------------------------------------------------------------------------------------------------------
## S2. Structural refactors

Each: files → design → schema → upgrade → behaviour → tests → risk.

**S2-1 MUST — `CorporateNetwork` aggregate with per-domain state objects.**
Files: `Network/CorporateNetwork.cs` + 6 partials; new `Network/CorporateHistoryKind.cs`, `Trade/CorporateTradeDesk.cs`, `Finance/CorporateLoanAccount.cs`,
`People/CorporatePeopleDesk.cs`, `Missions/CorporateMissionBoard.cs`, `Missions/CorporateFusionInvitation.cs`, `Services/CorporateClientServices.cs`.
Design: the component keeps `vault` (it is the `IThingHolder`), `nextRefreshTick`, `nextId` (one id source for orders, stock offers, person
offers and missions; `NewId() => nextId++`) and `history`, plus five `public sealed … : IExposable` objects (public: Scribe instantiates them),
always non-null (field initialisers; `??= new()` after load as an invariant, no shape logic). Each owns its state, its invariants (null-list
pruning) and its pure queries (`Loan.HasLoan/Balance`, `Services.Level`, `MissionBoard.Find/ActiveCount`, `People.Find`); the partials keep the
cross-domain orchestration (trade context, vault, history, turnover, faction). `Collateral may exist without a loan` ⇒ `CorporateLoanAccount`
is an always-present account (Finance.cs:281-291,379-391).
Schema: S3. Upgrade: C1 (+C2–C5 for items). Behaviour: none (mission ids continue from `max(sequence+1, nextMissionId)`, numbering of NEW
missions jumps — cosmetic, see S5-1). Tests: golden DOM S10 element (trimmed) and EvAll6 element, idempotence, full-label coverage check;
round-trip unit test of each state object with a fake Scribe where available. Risk: medium (mechanical, many call sites).

**S2-2 MUST — history entries without stored keys/translations.**
`CorporateHistoryEntry { tick, kind (CorporateHistoryKind), amount, thingDef, stuff, count, missionKind, highRisk, note }`; writers record
typed arguments (`ProductSold/Purchase/Refund`: def, stuff, count; `Mission*`: mission kind, product, highRisk; `PersonPurchased/Sold`: pawn
short name as `note` (a name, not translation); `WeeklyGiftClaimed/SupportCalled`: destination label/cell as `note`; `ServiceLevelReached`: level
in `count`). A `CorporateHistoryPresenter` (UI) maps kind → existing Keyed title key and composes the detail line at display time;
`note` (verbatim) wins when present (upgraded rows). `Network.Record(...)` becomes typed overloads.
Schema: S3. Upgrade: C2. Behaviour: fixes a verified defect — history text was frozen in the language active when recorded (S10 stores
`商业联系已建立`); new rows render in the current language. Cosmetic: quality/hit points no longer appear in sale lines.
Tests: C2 golden with all 17 keys (EvAll6 + S10 rows), unknown-key drop, presenter unit test per kind. Risk: low–medium.

**S2-3 MUST — `CorporateMission` per-kind subclasses (chosen over payload objects).**
`abstract CorporateMission` (common labels) → `CorporateProcessingMission`, `CorporateDistributionMission`, `CorporateInvestmentMission`,
`CorporateReferralMission`, `abstract CorporateSiteMission` (tile, site, targets, spawned, targetCount, deadTargetCount, `RecordDeadTargets`,
escaped-target check) → `CorporatePurgeMission`, `CorporateFusionMission`. Kind branches move into virtuals: `Kind`, `UpfrontPayment`
(deposit / wholesaleCost / investment / 0), `ClaimPayment`, `CanAbandon`, `UsesSite`, `PrepareGoods`, `OnAccepted`, `Tick`, `Progress`/`ProgressTarget`.
`Title/Status/Description` leave the model for `CorporateMissionPresenter` (UI; also feeds `quest.name/description` at accept and the
quest part `DescriptionPart`). `IsSide` disappears (`is CorporateFusionMission`). The weekly board, accept/claim/fail orchestration stays in
`CorporateNetwork.Missions.cs` (renamed from `.Quests.cs`; methods `Missions*`). `CorporateMissionKind` member `Native` → `Referral`
(enum still saved in history rows).
Why subclasses: kind-specific fields get their real names and types, invalid combinations become unrepresentable, and the 6 scattered
`switch`es (Mission.cs:86-109, Quests.cs:441-504, 745-774, 790-800, 876-937) become overrides; payload objects would keep a generic shell
with dual dispatch. Cost: the subclass names become schema (normal type-map territory).
Schema: S3 (`Class` per li). Upgrade: C5. Behaviour: none. Tests: C5 golden per kind (EvAll6 has all six kinds incl. Expired ones; EvBug has
Processing Active/Completed), salesGoal backfill, unknown kind; unit tests of `UpfrontPayment/ClaimPayment/CanAbandon` per subclass;
in-game: accept/claim each kind on an upgraded EvBug fixture. Risk: medium–high (largest code change; keep orchestration order identical).

**S2-4 MUST — loan money as fixed-point `long` (1/10 000 silver).**
`CorporateMoney.SubunitsPerSilver = 10_000`; new pure `Finance/CorporateLoanMath.cs` (decimal intermediates): `Accrue(state, normalDays,
overdueDays)`, `ApplyPayment(state, silver)`, `Balance = ceil((principal + interest) / 10 000)`, `ProjectedBalance`. The `0.0001f` epsilon and
`UnityMathf` float money disappear (`HasLoan => principal > 0 || interest > 0`). Rates stay `float` coefficients (copied from the Def at
signing, `forceSave`). Justification against keeping float: pricing is already exact decimal; float money needed an epsilon hack and its
justification (“1.6 float fields”) is gone; doing it now avoids a later schema step.
Schema: `loan/principal|interest|overdueInterestTotal` long. Upgrade: C1 value transform. Behaviour: equal except float artefacts
(e.g. `315.00003` now reads as 315.0000 ⇒ balance 315, not 316 — verified defect class the epsilon tried to mask). Tests: math unit
tests (accrual, overdue cap = originalPrincipal, partial/over payments, ceil), C1 transform cases, EvAll6 loan round trip. Risk: medium.

**S2-5 MUST — `CorporateIntroduction` state.**
`CorporateIntroductionStage { Dormant, AwaitingRepresentative, Completed }` replaces `pending`+`completed`; `CourierOutcome { Unknown, Released,
Confronted }` replaces `courierChoice` int; `retrofitted` replaces `migrated`; `serviceResumeTick` (saved: aggression cooldown and throttle
carried from old saves) + transient `nextServiceTick` throttle (reset on load); `initialized` removed (transient `loadedFromSave` +
`AttachToExistingGame(CorporateCourierFacts?)`); `NotifyCourierChoice(pawn, bool)` → `NotifyCourierOutcome(pawn, CourierOutcome)`;
`DevTriggerRepresentative` returns `RepresentativeVisitResult` enum (replaces string codes parsed by `MugirlStoryDebugActions.ParseCorporateOutcome`);
faction invariant as in A17. `OpeningText` keys off `retrofitted`.
Schema: S3. Upgrade: C6, P2. Behaviour: none (stage invariant `completed ⇒ ¬pending` holds in all writers: Complete/RememberCourier/
Notify*/UpdateIndependentContact/DevTrigger). Tests: C6 golden (S9/S10/S11 elements), P2 with a S7/S8-like fixture (courier quest present,
`Quest.State` EndedSuccess/EndedUnknownOutcome/ongoing), unit test of the stage transitions. Risk: medium.

**S2-6 MUST — trade/people records.** `CorporateOrder.paid` → `paidAmount` (same word as `CorporatePersonOffer.paidAmount`, which was the bool/int
naming clash), drop `savedLabel` (translated text as data, §3.4; a missing def shows `Mugirl.Corporate.UnavailableGoods`), `creditedTurnover`
default 0; `CorporatePersonOffer.paidAmount` default 0, drop `priceVersion`; `creditedPawnIds` `HashSet<int>`. Upgrade: C1/C3/C4/P3.
Behaviour: only the label of orders whose ThingDef was uninstalled. Tests: C3/C4 golden from EvAll6. Risk: low.

**S2-7 MUST — support lord graph.** `AddFleeToil => false`; `LordJobTick` override removed; `supportGraphVersion` removed. Upgrade C7/P5.
Tests: C7 golden built from a synthetic 0.6.4 lord (graph shape derived above), idempotence; P5 unit test with a stub lord where possible.
Risk: low (no real save has such a lord).

**S2-8 MUST (defect fix) — fusion site generation failure reopens the invitation immediately.** In `SpawnMissionSite`, every failure path
before `spawned = true` of a `CorporateFusionMission` calls `MissionBoard.ReopenFusionInvitation(now)`. Verified defect: today the catch
(`Missions/CorporateNetwork.MissionSite.cs:62-77`) only fails the mission; the invitation is re-opened only by the ungated load-time repair
(`Quests.cs:62,67-79`), i.e. after a reload. Tests: unit test of the board method; in-game: force a generation exception on the dev map.
Risk: low.

**S2-9 MUST — hot-path component lookups.** `CorporateNetwork.Current`/`CorporateIntroduction.Current` resolve through a per-game slot: the
`(Game)` constructor stores `(game, this)`; `Current` returns it when `ReferenceEquals(slot.game, Verse.Current.Game)` (last constructed wins:
FillComponents in `Game()` then the loaded instance), cleared through the lifecycle hub (StaticCacheLifecycle comment). Hot callers:
`Harmony_Pawn_PreApplyDamage_CorporateIntroduction` (every damage), `Harmony_Thing_TakeDamage_CorporateResearchTargets` (every player hit),
`JobDriver_CorporateTalkRepresentative` `FailOn` (every tick of the job), float-menu provider, quest part UI. Prefer a Core helper
`GameComponentSlot<T>` if core provides one (S4). Debt overlay keeps its static projection, reset via the hub instead of the ctor.
Tests: unit test of the slot semantics (new game, load, game switch). Risk: low.

**S2-10 MUST — one `CorporateDefOf`** (internal, `[MayRequire]` none needed): merge the five DefOfs + corporate fields of `MugirlContentDefOf`;
`Mugirl_Wool` / `Furniture` / `Mugirl_Milk` read via their owners' DefOfs or as `CorporateDefOf` fields; vanilla `TradeRequest`, `Mercenary_Gunner`
stay here. No unused fields. Risk: low.

**S2-11 MUST — visibility/files.** `internal` for patches, utilities, UI; `public` only for: components, QuestNode/QuestPart, SitePartWorker,
LordJob/LordToil, JobDriver/JobGiver, FloatMenuOptionProvider, Def subclasses, state IExposables, mission subclasses. One type per file
(done by the spec). Risk: low.

**S2-12 SHOULD — product factory out of the component.** `CorporateProductFactory` (`MakeProduct`, `MakeOrderProduct` + seed scope,
`TryMakeGoods`, `DiscardUnspawnedProduct(s)`), used by `CorporateUniforms` and `CorporateShowcaseApparel` instead of `CorporateNetwork` statics
(Services/CorporateUniforms.cs:23, People/CorporateShowcaseApparel.cs:80,83,140). Risk: low.

**S2-13 SHOULD — balance constants into config Defs.** Service thresholds/discounts/extra stock/squad size/support limits
(Services.cs:12-22,43-59) → new fields on a `CorporateServicesConfigDef` (`Mugirl_CorporateServicesConfig`, added to `Corporate/CorporateTrade.xml` or
a new `Corporate/CorporateServices.xml`); order caps 2500/200/quality (Trade.cs:310,388), weekly stock thresholds (Trade.cs:506-513,523-524),
ingredient cap 5000 (Quests.cs:234), purge formula (Quests.cs:301-303), distribution value range 700–1800 (Quests.cs:325), investment
multipliers (Quests.cs:500-503), 3 guards (MissionSite.cs:105), history caps → existing config Defs. Values identical ⇒ no behaviour change;
`DayTicks` → `GenDate.TicksPerDay`. Tests: config load test, `ServiceBenefitKey` test kept. Risk: low.

**S2-14 SHOULD — data-driven raid tiers.** Replace `CorporateRaidTierRules` constants (mirror of XML `maxTotalPoints`) by tiers derived from
the faction's Combat `pawnGroupMakers` (sorted distinct `maxTotalPoints`), enabled by a marker `CorporateRaidTiersExtension : DefModExtension`
on the FactionDef. Same results for current XML. Tests: existing rule tests ported to derived tiers. Risk: low.

**S2-15 SHOULD — terminal pages.** `enum CorporateTerminalPageId` replaces the int constants and the parallel `PageScopes`/`PageKeys` arrays;
UiSmokeSuite uses an internal API via `InternalsVisibleTo` instead of reflection by name. Risk: low.

**S2-16 SHOULD — talk-to-pawn dedupe.** `JobDriver_CorporateTalkRepresentative` and `FloatMenuOptionProvider_CorporateRepresentative` derive from
the shared talk-to-pawn bases once Incidents moves them to Core (identical toil list: GotoThing(Touch) + Do ⇒ `curToilIndex` compatible).
Risk: low.

**S2-17 SHOULD — dead content/code.** Delete 19 unused Keyed keys (both languages; verified by literal grep, no dynamic composition):
`Mugirl.Corporate.{BeaconHint,CaravanWeight,EmptyGoods,OrderHint,OrderReceipt,SelectGoods,StockHint,StockRow}`,
`Mugirl.CorporatePeople.{AgeHealth,Apparel,BuyConfirm,Health,Relations,SellConfirm,Skills,Traits}`,
`Mugirl.CorporateTradeBrowser.{RareSourcing,SpecialSourcing,StandardSourcing}`; `IsDebtFactionPair` (Finance.cs:476-477); `record.key ?? ""`;
move misfiled DefInjected entries (`Mugirl_CorporateShowcase.label` → `PawnKinds_CorporateShowcase.xml`; `Mugirl_CorporateSupportTend.reportString`
→ a `JobDef/CorporateSupportDuty.xml`); move `Mugirl.CorporateUI.Page.Services` to `CorporateUI.xml`; rewrite the A26 text. Risk: low.

**S2-18 SHOULD — comments.** Rewrite A22/A23 comments as current intent in Chinese; no history references. Risk: none.

Considered and rejected: merging `CorporateIntroduction` into `CorporateNetwork` (separate lifecycle, own gifts holder; merge costs a
two-component DOM merge and vault re-parenting for no behaviour gain); a unified pending-silver account (UI shows them separately; the
free Ready receipts are goods, not silver).

---------------------------------------------------------------------------------------------------------------------
## S3. New save schema (schema 1) — owners, labels, kind, type, default

Notation: V = `Scribe_Values`, D = `Scribe_Deep`, R = `Scribe_References`, Df = `Scribe_Defs`, CV/CD/CR = `Scribe_Collections` Value/Deep/Reference.
“always” = Deep object always written. Every V default equals the field initialiser (validator IL rule). Namespace of all types: `Mugirl.Features.Corporate`.

**`CorporateNetwork`** — `game/components/li[@Class=Mugirl.Features.Corporate.CorporateNetwork]`
| label | kind | type | default |
|---|---|---|---|
| vault | D (ctorArgs owner) | ThingOwner&lt;Thing&gt; | always |
| nextRefreshTick | V | int | -1 |
| nextId | V | int | 1 |
| history | CD | List&lt;CorporateHistoryEntry&gt; | empty |
| trade | D | CorporateTradeDesk | always |
| loan | D | CorporateLoanAccount | always |
| people | D | CorporatePeopleDesk | always |
| missionBoard | D | CorporateMissionBoard | always |
| services | D | CorporateClientServices | always |

**`CorporateHistoryEntry`** — `…/history/li`: tick V int 0; kind V `CorporateHistoryKind` AccountOpened; amount V int 0; thingDef Df ThingDef null;
stuff Df ThingDef null; count V int 0; missionKind V `CorporateMissionKind` Processing; highRisk V bool false; note V string null.

**`CorporateTradeDesk`** — `…/trade`: stock CD List&lt;CorporateStockOffer&gt;; orders CD List&lt;CorporateOrder&gt;.
- `CorporateStockOffer` (`trade/stock/li`): id V int 0; sample R Thing; count V int 0; unitPrice V int 0.
- `CorporateOrder` (`trade/orders/li`): id V int 0; def Df ThingDef; quantity V int 0; deliveredQuantity V int 0; creditedTurnover V int 0;
  paidAmount V int 0; createdTick V int 0; readyTick V int 0; speed V `CorporateOrderSpeed` Standard; expediteFee V int 0;
  state V `CorporateOrderState` Preparing; goods CR List&lt;Thing?&gt;.

**`CorporateLoanAccount`** — `…/loan`: principal V long 0; interest V long 0; overdueInterestTotal V long 0 (all 1/10 000 silver);
originalPrincipal V int 0; lastAccrualTick V int 0; dueTick V int 0; nextRaidTick V int 0; defaulted V bool false;
threeDayReminderSent V bool false; oneDayReminderSent V bool false; collateralValue V int 0; collateralLiquidated V bool false;
dailyRate V float 0.02 (forceSave); overdueDailyRate V float 0.03 (forceSave); liquidationRatio V float 0.5 (forceSave);
creditor R Faction; collateral CR List&lt;Thing&gt;; pendingSilver V int 0.

**`CorporatePeopleDesk`** — `…/people`: offers CD List&lt;CorporatePersonOffer&gt;; awaitingWorld CR List&lt;Pawn&gt;; pendingSilver V int 0.
- `CorporatePersonOffer` (`people/offers/li`): id V int 0; pawn R Pawn; price V int 0; paid V bool false; delivered V bool false;
  paidAmount V int 0; buyAsColonist V bool false; transferApplied V bool false.

**`CorporateMissionBoard`** — `…/missionBoard`: missions CD List&lt;CorporateMission&gt; (polymorphic, `Class` per li); fusion D `CorporateFusionInvitation` always.
- `CorporateFusionInvitation` (`missionBoard/fusion`): qualified V bool false; created V bool false; dueTick V int -1; investor R Pawn; investorName V string null.
- `CorporateMission` (abstract; every li): id V int 0; state V `CorporateMissionState` Available; offeredTick V int 0; acceptByTick V int 0;
  acceptedTick V int 0; deadline V int 0; duration V int 420000; reward V int 0; goodwill V int 0; quest R Quest; pendingGoods CR List&lt;Thing&gt;.
- `CorporateProcessingMission`: recipe Df RecipeDef; product Df ThingDef; stuff Df ThingDef; batches V int 0; quantity V int 0; delivered V int 0;
  deposit V int 0; ingredients CD List&lt;ThingDefCountClass&gt;.
- `CorporateDistributionMission`: product Df ThingDef; quantity V int 0; salesGoal V int 0; sold V int 0; wholesaleCost V int 0.
- `CorporateInvestmentMission`: investment V int 0; highRisk V bool false.
- `CorporateReferralMission`: questRoot Df QuestScriptDef.
- `CorporateSiteMission` (abstract): tile V PlanetTile Invalid; site R Site; targets CR List&lt;Pawn?&gt;; spawned V bool false; targetCount V int 0; deadTargetCount V int 0.
- `CorporatePurgeMission`: enemy R Faction; threatPoints V float 0.
- `CorporateFusionMission`: investor R Pawn; originalInvestor V bool false; choice V `CorporateResearchChoice` Undecided; dialogueShown V bool false; gratitudeRevoked V bool false.

**`CorporateClientServices`** — `…/services`: turnover V long 0; creditedPawnIds CV HashSet&lt;int&gt;; giftClaimedWeek V int -1;
freePersonClaimedWeek V int -1; supportWeek V int -1; supportCallsUsed V int 0.

**`CorporateIntroduction`** — `game/components/li[@Class=Mugirl.Features.Corporate.CorporateIntroduction]`: stage V `CorporateIntroductionStage` Dormant;
retrofitted V bool false; independentContact V bool false; giftsPrepared V bool false; independentAggression V bool false; courier R Pawn;
courierOutcome V `CourierOutcome` Unknown; courierDied V bool false; courierKilledByPlayer V bool false; contactTick V int -1;
representativeArrivalTick V int -1; serviceResumeTick V int 0; representative R Pawn; preferredMap R Map; introductionQuest R Quest;
gifts D ThingOwner&lt;Thing&gt; (ctorArgs owner, always).

**Other owners**
- `QuestPart_CorporateMission` (`quests/li/parts/li`): base QuestPart labels + missionId V int 0.
- `QuestPart_CorporateIntroduction`: base QuestPart labels only.
- `LordJob_CorporateSupport` (`lords/li/lordJob`): fallback V IntVec3 default(IntVec3). Graph (schema): toils [0 `LordToil_CorporateSupportHunt` (LordToilData_HuntEnemies), 1 `LordToil_ExitMap`], triggers [0 `Trigger_TicksPassed` (TriggerData_TicksPassed)]; no flee toil.
- `JobDriver_CorporateSupportTend` (`curDriver`): JobDriver_TendPatient labels + patientWaitJobId V int -1; toil order [CorporatePatientWait, base toils].
- `JobDriver_CorporateUseComms`, `JobDriver_CorporateTalkRepresentative`: base JobDriver labels; toils [Goto, Do].

**Enums saved by member name** (member names and order are schema):
`CorporateOrderState {Preparing, Ready, Completed, Cancelled, RefundDue, Refunded}`; `CorporateOrderSpeed {Standard, Express, Priority}`;
`CorporateMissionState {Available, Active, Ready, Completed, Failed, Expired, Abandoned}`; `CorporateResearchChoice {Undecided, Execute, Protect}`;
`CorporateMissionKind {Processing, Purge, Investment, Distribution, Referral, Fusion}` (only in history rows); `CorporateHistoryKind {AccountOpened,
ProductSold, Purchase, Refund, LoanSigned, LoanPayment, LoanDefaulted, CollateralLiquidated, PersonPurchased, PersonSold, ServiceLevelReached,
WeeklyGiftClaimed, SupportCalled, MissionAccepted, MissionClaimed, MissionFailed, ResearchersProtected}`; `CourierOutcome {Unknown, Released, Confronted}`;
`CorporateIntroductionStage {Dormant, AwaitingRepresentative, Completed}`.

**Type names written as `Class`:** `CorporateNetwork`, `CorporateIntroduction`, `QuestPart_CorporateMission`, `QuestPart_CorporateIntroduction`,
`LordJob_CorporateSupport`, `JobDriver_CorporateSupportTend`, `JobDriver_CorporateUseComms`, `JobDriver_CorporateTalkRepresentative`, and the six
mission subclasses (`CorporateProcessingMission`, `CorporateDistributionMission`, `CorporateInvestmentMission`, `CorporateReferralMission`,
`CorporatePurgeMission`, `CorporateFusionMission`).

**DefNames reaching saves** (post-spec): QuestScriptDef `Mugirl_CorporateMission`, `Mugirl_CorporateIntroductionQuest`; SitePartDef
`Mugirl_CorporatePurgeSite`, `Mugirl_CorporateResearchSite`; JobDef `Mugirl_CorporateUseComms`, `Mugirl_CorporateTalkRepresentative`,
`Mugirl_CorporateSupportTend`; DutyDef `Mugirl_CorporateSupportHunt`; TraitDef `Mugirl_CorporateDiehard`; ThoughtDef `Mugirl_CorporateSoldMugirl`;
PawnKindDef `Mugirl_CorporateRepresentative`, `Mugirl_CorporateSupport`, `Mugirl_CorporateShowcase`, `Mugirl_GiantCorp_{Grunt, OPCEnforcer,
OPCSapper, Courier, PMCRecruit, Soldier, Sapper, Elite, MugirlRaider}`; FactionDef `Mugirl_GiantCorporation`. Config Defs, TraderKindDef
`Mugirl_CorporatePersonnelTrader`, PrefabDef `Mugirl_CorporateFusionResearchSite` are never saved.

---------------------------------------------------------------------------------------------------------------------
## S4. Edits needed from other owners

1. **Core — `DefOf/MugirlContentDefOf.cs`:** remove the corporate fields (`:70`, `:73-80`, `:83`, `:86-87`) once `CorporateDefOf` has them; Incidents
   declares its own fields for `Mugirl_GiantCorporation` and `Mugirl_GiantCorp_Courier` in `IncidentsDefOf` (used by `Courier/*`, `Story/MugirlStoryService.cs:98,138`)
   — no cross-feature DefOf use.
2. **Core — settings:** the renamed setting of `enableStructuralCrashEvent` (e.g. `enableOpeningStoryline`) is read by `CorporateIntroduction.CanArrangeIndependentContact` (`Introduction/CorporateIntroduction.cs:234`).
3. **Core — patch metadata:** closed module set must contain `Corporate`; the moved raid-tier patch changes from `"Incidents"` to Corporate.
4. **Core — lifecycle hub:** registration API for per-game resets used by: the debt overlay (`Finance/CorporateNetwork.Finance.cs:17-21`), the two
   component slots (S2-9), `CorporateDefCache`/catalogue caches if they hold per-game data. Optional `GameComponentSlot<T>` helper.
5. **Core:** `MugirlGeneratedPawnUtility` (`TryPassToWorld`, `Discard`), the player-faction helpers (`MugirlWildSlaveUtility.PlayerFaction/IsPlayerFaction`)
   and `MugirlGameUtility` stay available from Core (used in ~15 corporate places).
6. **SaveUpgrade framework (A1):** session payload channel DOM → post-load (`Corporate.LegacyPriceOffers`, `Corporate.SupportLords`,
   `Corporate.RetrofitIntroduction`); phases A/B/C as in design §5.2; `LegacyTypes` Drop(RemoveNode) for `Mugirl.CorporateRuntimeValidation` and
   `Mugirl.CorporateVisualValidation` (A24) and deletion of `Compatibility/Savegames/CorporateValidationSaveMigration.cs` + its test.
7. **Incidents:** (a) read-only story queries replacing direct field reads of `MugirlStoryState` (`fusionInvestmentAccepted/Pending` at
   `Missions/CorporateNetwork.Quests.cs:958-959`; `openingCrashStarted`, `courierRaidTriggered`, `courierRaidQuestStarted` at
   `Introduction/CorporateIntroduction.cs:75-78,233-237`); (b) `CourierConversationUtility.cs:87,112` call `NotifyCourierOutcome(courier, CourierOutcome.X)`;
   (c) `MugirlStoryDebugActions.cs:64-134` consumes `RepresentativeVisitResult` instead of parsing strings; (d) move
   `JobDriver_MugirlTalkToPawn`/`FloatMenuProvider_MugirlTalkToPawn` to Core (S2-16); (e) keep `QuestPart_SpawnCourier.courier/map` readable for P2
   and tell me the final names of the courier QuestScriptDef/QuestPart; delete the comment `Courier/QuestPart_SpawnCourier.cs:6`;
   (f) `MugirlFusionInvestmentUtility.cs:82` keeps calling `RememberFusionInvestor` (API unchanged).
8. **Restraints/roping and weapons/apparel specs:** def renames referenced from my files (`WallRopeHitch` at `CorporateFusionResearchSite.xml:483`;
   `PMC_*`, `Mugirl_Combatant_*` in pawn kinds) are applied by the tool; please do not hand-edit my files in Wave S.
9. **TraitDefs.xml owner (optional):** move `Mugirl_CorporateDiehard` (`1.7/Defs/TraitDefs/TraitDefs.xml:63-75`) and its DefInjected entries
   (`DefInjected/TraitDef/TraitDefs.xml:16-18`) into `1.7/Defs/Corporate/CorporateSupportDuty.xml` / a corporate DefInjected file.
10. **Validator/tests owner:** baselines `save-types.txt:4-5`, `save-enums.txt`, `patch-targets.txt` (19 corporate rows), `transpiler-hooks.txt:3`,
    `dynamic-lookups.allow.txt:38-48` (drop `:42`; history no longer translates stored keys); `C.frozen-keys` for the 17 history keys;
    `UiSmokeSuite.cs:66-83` (S2-15); delete `CorporateMoneyTests.cs:82-97`; legacy corpus must contain the EvAll6 shapes (mission without
    `salesGoal`, orders without `creditedTurnover`, offers without `priceVersion`).
11. **Docs (Wave D):** `docs/features/giant-corporation-*.md`, record the defect fixes (S2-2 history language, S2-4 float rounding, S2-8 fusion invitation).

---------------------------------------------------------------------------------------------------------------------
## S5. Open questions (proposed answers)

1. **One id counter for all corporate records (missions included)?** New mission numbers jump (S10: 7 → 21). Proposed: yes (one concept, one label).
2. **Drop `CorporateOrder.savedLabel` (translated text)?** Only orders whose ThingDef was uninstalled lose their item label. Proposed: yes, generic fallback key.
3. **Rename FactionDef `Mugirl_GiantCorporations_Hostile` → `Mugirl_GiantCorporation`?** No external dependent found (local mods + 2735 workshop items). Proposed: yes.
4. **PawnKind names `Mugirl_GiantCorp_<Role>` (content.md B.5) vs `Mugirl_GiantCorp<Role>`?** Proposed: content.md names, consistent with other specs.
5. **Support archived dev-build shapes (EvAll6 mission without `salesGoal`)?** They are the designated in-game fixtures (design §6). Proposed: yes, C5 backfill.
6. **Mid-game install detection local (`loadedFromSave` transient) instead of a hub signal?** Proposed: local; no hub API needed.
7. **Keep upgraded history `detail` as verbatim `note`?** Proposed: yes (cannot be reconstructed; displayed as is).
8. **`Window_CorporateComms` → `Dialog_CorporateTerminal` despite the test coupling?** Proposed: yes, together with S2-15.
9. **Who owns `CorporatePricingCompatibility`?** Core report proposes a different target name. Proposed: corporate owns it as `CorporateForeignBehavior` (only caller is corporate pricing).
