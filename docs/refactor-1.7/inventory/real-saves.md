## Summary

- **11 of 85 saves contain the mod, and all are 1.6 saves.** No 1.7 save with the mod exists anywhere on the machine. The 156 `.rws` in `%TEMP%` are 1.7 test runs, and none lists `har.mugirlrace`. So the "pre-refactor 1.7" save shape has no real sample to test the upgrader against.
- **S1–S4 are MooGirl-era saves from before git history starts.** They use namespaces `MooGirl`/`MooGirlSkinFix` and defNames `MooGirl_*`. Neither the frozen 1.6 build nor the 1.7 tree knows any of these names (`grep -i moogirl` = 0). They contain no MooGirl race pawns.
- **S5–S11 (Mugirl era) use 15 classes, all present unchanged in the 1.7 DLL.** Only two labels in them are read by legacy code alone: `milkFullness_lastResourceUpdateTick` and the def-less `Mugirl.Thought_Hediff` memories left by a 1.6 bug.
- **Only one retired def appears in Mugirl-era saves:** `Mugirl_MechanoidBulldog` (and its `Corpse_`), and only in storage filters.
- **DefNames are embedded in thing IDs and references.** There are 1,685 `Thing_Mugirl<n>` references. In 1.7, a reference that no longer matches is resolved by its number with one warning each, so renaming a def keeps the data but floods the log unless the upgrader rewrites these strings.
- **Do not pattern-match on "Mugirl".** Third-party mods put "Mugirl" in their own defNames, and local dependent mods reference our class and def names.

# Real saves: Mugirl-owned names (empirical catalogue)

**Scope:** `C:\Users\Fishiv\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Saves`, 85 `.rws`, read-only with a streaming parser.

**Terms used below** (repo paths are relative to `…\Mods\mugirl`):
- **1.7 tree:** `Source/MugirlRace` + `1.7/**`.
- **1.6 frozen:** `1.6/Source`, `1.6/Assemblies/MugirlRace.dll` and the 1.6 Defs, `Bio_1.6`, `Odyssey_1.6`, `Versions/1.6`.
- **git:** every blob reachable from `git rev-list --all` (66 commits, `d9b9bb9` 2026-05-19 … `50f7979` 2026-10-03).
- **workshop:** the published copy, `C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\3578882219`, DLL dated 2026-09-26.

**Universes the saves were checked against:**
- 916 defNames: 535 in the 1.7 tree, 531 in 1.6, 381 only in history.
- All types of 55 historical DLL blobs plus the 1.6, 1.7 and workshop DLLs.
- Every `Scribe_*` call with file:line in the 1.7, 1.6 and git sources.

## 1. Saves that mention the mod

Detection used `meta/modIds` containing `har.mugirlrace` (RimWorld stores package IDs in lower case), plus a case-insensitive byte scan for `mugirl|moogirl` over all 85 saves. Both methods give exactly these 11 files. No save records a mod version; `modSteamIds` is 0 for this mod (local folder, not the workshop copy).

| id | file | mtime | MB | gameVersion | mod idx/count | modName | HAR idx | era | our GameComponents | our MapComponents (per map) |
|---|---|---|---|---|---|---|---|---|---|---|
| S1 | 天穹现境续.rws | 2025-12-19 20:52 | 44.1 | 1.6.4633 rev1261 | 867/899 | MuGirlRace | 522 | MooGirl | MooGirl_ConvertComp, MooGirl_GameComp | - |
| S2 | 天穹现境-初始.rws | 2026-04-06 19:06 | 47.2 | 1.6.4633 | 759/986 | MuGirlRace | 568 | MooGirl | + MooGirl_XenotypeFix_GameComp, GameComponent_MooGirlSkinOnce | - |
| S3 | 幻象.rws | 2026-05-13 17:02 | 3.3 | 1.6.4633 | 16/18 | MuGirlRace | 9 | MooGirl | + AdultContentCleanupGameComponent (5 total) | - |
| S4 | 社长.rws | 2026-05-15 14:24 | 14.2 | 1.6.4633 | 15/27 | MuGirlRace | 9 | MooGirl | same 5 as S3 | - |
| S5 | 新移民10.rws | 2026-07-07 06:49 | 34.5 | 1.6.4871 rev591 | 775/1014 | Mugirl Race | 583 | Mugirl | BrainwashPerformance, GhoulRenderingRefresh, MugirlStoryState, Mugirl_XenotypeFix_GameComp | MugirlMigration, MapRopingIndex |
| S6 | 普尤斯蒂.rws | 2026-07-08 20:00 | 9.0 | 1.6.4871 | 12/13 | Mugirl Race | 9 | Mugirl | same 4 as S5 | MugirlMigration, MapRopingIndex |
| S7 | Quicksave1.rws | 2026-07-27 04:47 | 67.1 | 1.6.4871 | 761/996 | Mugirl Race | 575 | Mugirl | same 4 as S5 | + MapComponent_MugirlPunisher |
| S8 | 天穹现境.rws | 2026-09-03 19:44 | 45.9 | 1.6.4871 | 788/1028 | Mugirl Race | 587 | Mugirl | same 4 as S5 | Migration, Punisher, Roping |
| S9 | NavalLab-BeforeOcean-20260918-060643-fb73c4fb.rws | 2026-09-18 06:06 | 3.3 | 1.6.4871 | 36/44 | Mugirl Race | 14 | Mugirl | + CorporateIntroduction, CorporateNetwork (6 total) | Migration, Punisher, Roping (×7 maps) |
| S10 | 雪牛娘测试.rws | 2026-09-24 11:36 | 9.5 | 1.6.4871 | 36/37 | Mugirl Race | 14 | Mugirl | same 6 as S9 | Migration, Punisher, Roping |
| S11 | Autosave-3.rws | 2026-10-02 17:44 | 62.9 | 1.6.4871 | 797/1045 | Mugirl Race | 589 | Mugirl | same 6 as S9 | Migration, Punisher, Roping |

- No save has a Mugirl WorldComponent.
- `GameComponent_GhoulRenderingRefresh`, `MapRopingIndex`, `AdultContentCleanupGameComponent` and `GameComponent_MooGirlSkinOnce` are written as empty elements (they save no data).
- The other 74 saves (1.3–1.6) and `Config/Templates`/`WorldPresets` contain no marker.

### Version hints

RimWorld creates every non-abstract GameComponent/MapComponent subclass for each game or map. So the set of our component classes in a save equals the set in the DLL that wrote it, which fingerprints the build. Each save was compared against all 55 historical DLL blobs and the current builds; the def window uses the first and last git commits of defNames the save contains.

| id | component set equals DLL of commits | equals wt16 / workshop / wt17 | def lower bound | upper bound (retired def) |
|---|---|---|---|---|
| S1–S4 | none: pre-git build. The first git DLL `d9b9bb9` already adds `MooGirl.GameComponent_BrainwashPerformance`, which these saves lack | no/no/no | n/a | n/a |
| S5, S6 | `03439c8` (07-05) .. `4e76859` (07-13); S6 needs `886d46a` (`Mugirl_WildMugirl`) | no | 07-03 / 07-06 | - |
| S7 | `0a6c435` (07-15) .. `035a35d` (09-14) | no | `62a3d16` 07-15 | - |
| S8 | same window as S7 | no | `c66cc36` 08-28 (0.5.1) | `131ee89` |
| S9 | `e93677d` (09-16) .. `50f7979` | yes/yes/yes | 07-15 | - |
| S10 | same window as S9 | yes/yes/yes | `131ee89` 09-24 20:42, but saved 11:36 that day, so an uncommitted 0.6.7 tree | `131ee89` |
| S11 | same window as S9 | yes/yes/yes | `50f7979` 10-03, but saved 10-02, so an uncommitted pre-`50f7979` tree | - |

### MooGirl era vs current code

- The rename to `Mugirl` happened between `cacbc58` (0.4.4) and `71baea6` (0.4.8), with no compat code. `MooGirl.*` types exist in git only up to `cb25751`.
- The hidden legacy faction `MooGirl_GiantCorporations` was kept in `1.6/Defs/Compatibility/LegacyDefs.xml` (added `d9d2968`) and deleted in `e8495b8` (2026-06-07).
- S1–S4 contain no MooGirl race pawn (`<def>MooGirl</def>` = 0, `Thing_MooGirl<n>` = 0).

## 2. Save-visible Mugirl types (`Class="…"`)

A raw grep gives the same list. No Mugirl type appears inside a generic type argument or as a text value.

| Class | base | per-save count | path | wt17/wt16/workshop | git first..last |
|---|---|---|---|---|---|
| `MooGirl.AdultContentCleanupGameComponent` | GameComponent | S3×1, S4×1 | …/components/li | **no/no/no** | d9b9bb9..3cce0ea |
| `MooGirl.MooGirl_ConvertComp` | GameComponent | S1–S4 ×1 | …/components/li | **no/no/no** | d9b9bb9..3cce0ea |
| `MooGirl.MooGirl_GameComp` | GameComponent | S1–S4 ×1 | …/components/li | **no/no/no** | d9b9bb9..1113cd8 |
| `MooGirl.MooGirl_XenotypeFix_GameComp` | GameComponent | S2, S3, S4 | …/components/li | **no/no/no** | d9b9bb9..cb25751 |
| `MooGirlSkinFix.GameComponent_MooGirlSkinOnce` | GameComponent | S2, S3, S4 | …/components/li | **no/no/no** | d9b9bb9..c2b4a80 |
| `Mugirl.AdvancedSlaveApparel` | Mugirl.SlaveApparel | S6×1 | …/maps/li/things/thing | yes | 324501c..50f7979 |
| `Mugirl.CorporateIntroduction` | GameComponent | S9, S10, S11 | …/components/li | yes | e93677d..50f7979 |
| `Mugirl.CorporateNetwork` | GameComponent | S9, S10, S11 | …/components/li | yes | e93677d..50f7979 |
| `Mugirl.GameComponent_BrainwashPerformance` | GameComponent | S5–S11 | …/components/li | yes | 324501c.. |
| `Mugirl.GameComponent_GhoulRenderingRefresh` | GameComponent | S5–S11 | …/components/li | yes | 324501c.. |
| `Mugirl.MapComponent_MugirlMigration` | MapComponent | S5–S11 (S9×7) | …/maps/li/components/li | yes | 03439c8.. |
| `Mugirl.MapComponent_MugirlPunisher` | MapComponent | S7–S11 (S9×7) | …/maps/li/components/li | yes | 0a6c435.. |
| `Mugirl.MapRopingIndex` | MapComponent | S5–S11 (S9×7) | …/maps/li/components/li | yes | 324501c.. |
| `Mugirl.MugirlStoryState` | GameComponent | S5–S11 | …/components/li | yes | 324501c.. |
| `Mugirl.Mugirl_XenotypeFix_GameComp` | GameComponent | S5–S11 | …/components/li | yes | 324501c.. |
| `Mugirl.QuestPart_CorporateIntroduction` | QuestPart | S10 | …/questManager/quests/li/parts/li | yes | e93677d.. |
| `Mugirl.QuestPart_MugirlRescueJoin` | QuestPart | S5, S7, S10 | …/quests/li/parts/li | yes | 324501c.. |
| `Mugirl.QuestPart_SpawnCourier` | QuestPart | S7, S10 | …/quests/li/parts/li | yes | 324501c.. |
| `Mugirl.SlaveApparel` | Apparel | S5×7, S6×7, S10×8 | …/apparel/wornApparel/innerList/li and …/things/thing | yes | 324501c.. |
| `Mugirl.Thought_Hediff` | Thought_Memory | S6×8, S7×11 | …/thing/needs/needs/li/thoughts/memories/memories/li | yes | 324501c.. |

No save contains a Mugirl JobDriver, LordJob, Thing subclass (DunkProp, ThrownObject, DunkHead, `PawnFlyer_LanceCharge`), `MilkPartnerWait` or other QuestPart. Those have no real sample to test an upgrade against.

## 3. Scribe labels per Mugirl class, and who reads them

Paths without a prefix are under `Source/MugirlRace/`.

| Class | labels observed | 1.7 reader | 1.6 frozen |
|---|---|---|---|
| MooGirl_ConvertComp | `conversionDone` | — | — |
| MooGirl_GameComp | `courierRaidTimer` | — (same label in `Features/Incidents/Story/MugirlStoryState.cs:142`) | — |
| MooGirl_XenotypeFix_GameComp | `checkTimer`, `xenotypeFixed` | — (same labels in `Features/Genes/Xenotype/Mugirl_XenotypeFix_GameComp.cs:63,67`) | — |
| Mugirl_XenotypeFix_GameComp | `xenotypeFixed`, `throwGeneFixed`, `dunkGeneFixed`, `throwSystemGeneFixed`, `checkTimer` | `Mugirl_XenotypeFix_GameComp.cs:63-67` | `1.6/Source/Features/Genes/Mugirl_XenotypeFix_GameComp.cs:197-201` |
| MugirlStoryState | `openingCrashStarted`, `openingCrashCheckTimer`, `openingCrashPawns`, `courierRaidTriggered`, `courierRaidTimer`, `courierRaidQuestStarted`, `fusionInvestmentInvestor` | `MugirlStoryState.cs:138-150` | `1.6/Source/Features/Incidents/MugirlStoryState.cs:110-122` |
| GameComponent_BrainwashPerformance | `activeBrainwashPerformances` | `Features/Restraints/Performance/GameComponent_BrainwashPerformance.cs:54` | `…/GameComponent_BrainwashPerformance.cs:53` |
| CorporateIntroduction | `corporateIntroductionInitialized`, `completed`, `giftsPrepared`, `nextServiceTick`, `representativeArrivalTick`, `courier`, `representative`, `preferredMap`, `introductionQuest`, `gifts` | `Features/Corporate/Introduction/CorporateIntroduction.cs:778-795` | `1.6/Source/Features/Corporate/CorporateIntroduction.cs:506-523` |
| CorporateNetwork | `corporateVault`, `corporateNextRefresh`, `corporateSequence`, `corporateRecords`, `corporateStock`, `corporateOrders`, `corporatePeopleOffers`, `corporatePeopleAwaitingWorld`, `corporateMissions`, `nextMissionId`, `retainedFusionInvestor`, `corporateLoanFaction`, `corporateLoanCollateral`, `corporateCreditedPersonIds`, `corporateSupportCallsUsed` | `Network/CorporateNetwork.cs:131-134`, `Trade/CorporateNetwork.Trade.cs:564-565`, `People/CorporateNetwork.People.cs:20-21`, `Missions/CorporateNetwork.Quests.cs:46-51`, `Finance/CorporateNetwork.Finance.cs:95-96`, `Services/CorporateNetwork.Services.cs:258,262` | same labels in `1.6/Source/Features/Corporate/CorporateNetwork*.cs` |
| ↳ `corporateRecords/li` | `tick`, `key`, `detail` | `Network/CorporateRecord.cs:15-18` | `CorporateNetwork.cs:115-117` |
| ↳ `corporateStock/li` | `id`, `sample`, `count`, `unitPrice` | `Trade/CorporateStock.cs:14-17` | `CorporateNetwork.Trade.cs:20-23` |
| ↳ `corporatePeopleOffers/li` | `id`, `pawn`, `price`, `priceVersion` | `People/CorporatePersonOffer.cs:21-28` | `CorporateNetwork.People.cs:52-59` |
| ↳ `corporateMissions/li` | `id`, `kind`, `offeredTick`, `acceptByTick`, `duration`, `cost`, `reward`, `goodwill`, `count`, `salesGoal`, `highRisk`, `threatPoints`, `tile`, `product`, `nativeRoot`, `enemy`, `site`, `quest`, `investor`, `targets`, `ingredients`, `pendingGoods` | `Missions/CorporateMission.cs:132-165` | `1.6/Source/Features/Corporate/CorporateQuestContract.cs:125-158` |
| MapComponent_MugirlMigration | `migrationPawns`, `nextGrazeTicks`, `playerKilledMigrationPawnIds`, `dataVersion`(=2) | `Features/Incidents/Migration/MapComponent_MugirlMigration.cs:468-478` | `1.6/Source/Features/Incidents/IncidentWorker_Mugirl_Migration.cs:627-637` |
| MapComponent_MugirlPunisher | `punishers`, `punisherSpawnedAtTicks`, `punisherLeavingFlags` (all empty) | `Features/Incidents/Punisher/MapComponent_MugirlPunisher.cs:278-280` | `1.6/Source/Features/Incidents/MugirlPunisher.cs:244-246` |
| QuestPart_MugirlRescueJoin | `inSignalRescued`, `inSignalRecruited`, `pawns` | `Features/Incidents/OpeningCrash/QuestPart_MugirlRescueJoin.cs:113-115` | `…/QuestPart_MugirlRescueJoin.cs:117-119` |
| QuestPart_SpawnCourier | `map`, `faction`, `spawnCell`, `courier`, `inSignal` | `Features/Incidents/Courier/QuestPart_SpawnCourier.cs:223-227` | `1.6/Source/Features/Incidents/QuestPart_CourierRaid.cs:201-205` |
| SlaveApparel / AdvancedSlaveApparel | vanilla Thing/Apparel labels + `lockCount`, `isLocked` (+ `ticks` for ShockCollar) | `Features/Restraints/Apparel/SlaveApparel.cs:49-50`, `Features/Restraints/Comps/CompProperties_ShockCollar.cs:101` | `1.6/Source/Features/Restraints/SlaveApparelDef.cs:87-88`, `…/CompProperties_ShockCollar.cs:138` |
| Thought_Hediff | **only `added`** (no `def`, no `age`) | `Features/Milk/FoodEffects/Thought_Hediff.cs:11-15` | `1.6/Source/Features/Milk/Thought_Hediff.cs:13-16` (never calls `base.ExposeData()`) |

### Leaf values observed (enum names, flags, references)

| Class | values |
|---|---|
| MooGirl_ConvertComp | conversionDone=True |
| MooGirl_GameComp | courierRaidTimer=298799/299891 |
| MooGirl_XenotypeFix_GameComp | checkTimer=0/191; xenotypeFixed=True |
| AdvancedSlaveApparel | isLocked=False; lockCount=0; ticks=789 |
| CorporateIntroduction | completed=True; initialized=True; courier=Thing_Human27447/null; introductionQuest=Quest_1/null; preferredMap=Map_0/null; representative=Thing_Human27442/null; nextServiceTick=14337/2501/30001 |
| CorporateNetwork | missions/kind=**Distribution/Investment/Native/Purge**; missions/nativeRoot=OpportunitySite_ItemStash; missions/product=MealSurvivalPack; missions/salesGoal=56 (1 of 6); missions/count=69 (1 of 6); peopleOffers/pawn=Thing_Mugirl27505…; peopleOffers/priceVersion=1 (4/4); records/key=**Mugirl.CorporateIntro.Title**; records/detail=商业联系已建立; stock/sample=Thing_Chocolate27489…; corporateSequence=20; nextMissionId=7 |
| MapComponent_MugirlMigration | dataVersion=2; lists empty |
| MapComponent_MugirlPunisher | lists empty |
| MugirlStoryState | courierRaidTimer=0/239640/299640/299999; openingCrashCheckTimer=-1/1000/359; openingCrashPawns=Thing_Mugirl27424/27431 |
| Mugirl_XenotypeFix_GameComp | all flags True; checkTimer=0/202 |
| QuestPart_MugirlRescueJoin | inSignal*=Quest0/2/3.pawns.Rescued/Recruited; pawns=Thing_Mugirl… |
| QuestPart_SpawnCourier | inSignal=Quest10/Quest2.CourierRaid_Spawn; faction=Faction_300/Faction_8 |
| SlaveApparel | isLocked=False; lockCount=0 (only the 6 lying on the ground; absent on all 15 worn) |
| Thought_Hediff | added=True (19 nodes) |

## 4. Nodes whose `<def>` is ours: labels that vanilla nodes never have

| node | saves | extra labels | 1.7 writer |
|---|---|---|---|
| Mugirl race pawns (map `thing[Pawn]`; `li[Pawn]` in corporateVault) | S6, S7, S10 | `milkFullness` (S6 7, S7 2, S10 1); `milkFullness_lastResourceUpdateTick` (same 10); `weaponWheelReserveWeapons`/`Slots`/`UnlockedSlots` (S7 2, S10 7); `innerContainer` (every Mugirl pawn in S6, S7, S10) | `Features/Milk/Production/CompMooHasBodyResource.cs:55,61` (label `SaveKey`, set to `"milkFullness"` at `CompMooMilkable.cs:26`); `Features/WeaponWheel/Core/Comp_WeaponWheel.cs:80-82`; `Features/Mounting/Comp/Comp_MugirlMount.Storage.cs:9` |
| `Mugirl_MilkingDeviceHidden` (worn) | S6 | `storedCharges`, `storedMilkAmount`, `effectTicksRemaining`, `nextSoundTicks`, `nextFleckTicks`, `nextTextTicks` | `Features/Milk/Device/Comp_MilkingDevice.cs:143-144`, `Features/Milk/Device/CompMilkingDeviceReleaseEffect.cs:62-66` |
| `HediffWithComps` `Consumed_MugirlMilk` | S6, S7 | `flagAmIThinking`, `checkingCounter` | `Features/Milk/FoodEffects/HediffComp_WhileHavingThoughts.cs:23-24` |

- All other node types whose def is ours carry only vanilla labels: factions, traits, genes, abilities, the restraint hediffs (plain `Hediff`, no comps), apparel, filth, the generator, items.
- No save contains `age`/`triggered` (the 1.6 HediffComp labels) or the 1.7 `mugirl*Age`/`*Triggered` labels.
- Race pawn counts: S6 7 (map), S7 2 (map), S10 3 (map) + 4 (vault), S5 2 (world).
- HAR stores on each Mugirl pawn `AlienRaces_AlienComp/addonVariants`: 5 indices that follow the order of the race def's `bodyAddons`.

## 5. DefNames seen in saves

352 distinct names, 259 of them defined in the 1.7 tree.
- **Matching rules:** exact match; implied prefixes `Blueprint_`, `Blueprint_Install_`, `Frame_`, `Corpse_`, `Meat_`, `Make_`; or `[Thing_]<def><n>`.
- **Excluded:** HAR race-settings entries `Kind`, `Masochist`, `Beauty` and `CreepyBreathing`. These are vanilla trait names.
- **Markers:** † = not in the 1.7 tree; ‡ = vanilla def of the same name.

| type in our XML | n | names |
|---|---|---|
| ThingDef | 215 | Bullet_Mugirl_AR_Standard, Bullet_Mugirl_AT23_Autocannon, Bullet_Mugirl_MP_Standard, Bullet_Mugirl_Militia_Rifle, Bullet_OPC_Assault_Rifle, Bullet_OPC_Defense_Rifle, Bullet_OPC_Tactical_Shotgun, Bullet_OPC_Tranquilizer_Pistol, Bullet_PMC_Assault_Rifle, Bullet_PMC_Carbine, Bullet_PMC_DMR, Bullet_PMC_LMG, MooGirlMilkFilth†, MooGirlWashMentalEyesInHead†, MooGirlWaterFilth†, MooGirl_AgedCheese†, MooGirl_ArmorHelmetRecon†, MooGirl_BaotaSugar†, MooGirl_Bar†, MooGirl_Bikini†, MooGirl_Bikini_Stocking†, MooGirl_Bodystocking†, MooGirl_BunnyGirlHeaddress†, MooGirl_BunnyGirlStocking†, MooGirl_BunnyGirlTop†, MooGirl_Cheese†, MooGirl_Cheongsam†, MooGirl_ChildCheongsam†, MooGirl_Coat†, MooGirl_Combatant_BulletproofVest†, MooGirl_Combatant_Clothes†, MooGirl_Combatant_Helmet†, MooGirl_Combatant_Underwear†, MooGirl_CowCake†, MooGirl_Eroticpanties†, MooGirl_Gambeson†, MooGirl_Hipwrappingskirt†, MooGirl_ImmortalFairy†, MooGirl_KnightHelmet†, MooGirl_KnightlyOrnament†, MooGirl_Mask†, MooGirl_MilitaryDress†, MooGirl_MilitaryUniform†, MooGirl_Milk†, MooGirl_MilkCandy†, MooGirl_MilkCreamApple†, MooGirl_MilkPowder†, MooGirl_MilkPudding†, MooGirl_MilkTablet†, MooGirl_MilkingDevice†, MooGirl_MilkingDeviceHidden†, MooGirl_OL†, MooGirl_PlateArmor†, MooGirl_PowerArmor†, MooGirl_PowerArmorHelmet†, MooGirl_RA_Carrying_Equipment†, MooGirl_RA_MechanicBackpack(_B/_C/_D)†, MooGirl_RA_MechanicRecon†, MooGirl_RA_PowerArmor†, MooGirl_RA_Unionary†, MooGirl_ReconArmor†, MooGirl_ReverseBunnySticker†, MooGirl_ReverseBunnySuit†, MooGirl_Shirt†, MooGirl_SlaveApperalKey_Advance/Industrial/Medieval/Spacer†, MooGirl_Spacesuit†, MooGirl_SpacesuitHeadgear†, MooGirl_SpeciallyStockings†, MooGirl_Sunglasses†, MooGirl_Suspenders†, MooGirl_Uniform†, MooGirl_Veil†, MooGirl_WarriorCombatArmor†, MooGirl_WarriorCombatHelmet†, MooGirl_WeddingDress†, MooGirl_WhitePanties†, MooGirl_
| ResearchProjectDef | 13 | Mugirl_AdvancedDairy, Mugirl_AdvancedGear, Mugirl_Autoloading, Mugirl_BehavioralConditioning, Mugirl_CombatAttire, Mugirl_DailyAttire, Mugirl_DairyProcessing, Mugirl_Firearms, Mugirl_MilkPowerUtilization, Mugirl_NeuralCombatSystems, Mugirl_PneumaticWeapons, Mugirl_RegulatedMilking, Mugirl_RopeHandling |
| AlienRace.AlienBackstoryDef | 11 | Mugirl_Adult, Mugirl_Child, Mugirl_ChildWorker, Mugirl_ConstructionWorker, Mugirl_ExperimentalChild, Mugirl_FactoryWorker, Mugirl_FarmSlave, Mugirl_Gladiator, Mugirl_Merchant, Mugirl_MineWorker, Mugirl_UndergroundIdol |
| HeadTypeDef | 8 | Mugirl_Female_A…H |
| PawnKindDef | 7 | AI_GC_Courier, AI_GC_Elites, Mugirl_Beginning_Slave, Mugirl_CorporateRepresentative, Mugirl_CorporateShowcase, Mugirl_EscapeSpaceSlave, Mugirl_WildMugirl |
| HediffDef + SlaveApparelDef | 7+7 | Mugirl_ / MooGirl_† Armbinder, Blindfold, Boxbinder, Earplugs, Gagstick, Handcuffs, Straitjacket |
| SlaveApparelDef | 7+7 | Mugirl_ / MooGirl_† BrainwashHelmet, MagneticShackles, ShockCollar, Slaveboots, Slavegauntlet, Slaveshackles, Slavestockings |
| FacialAnimation.*TypeDef | 29 | Mugirl_EyeNormal(1–7), Mugirl_LidNormal(1–7), Mugirl_BrowNormal(1–6), Mugirl_HeadNormal2/3/5/6/7, Mugirl_MouthNormal3/4/5/7 |
| HediffDef | 5 | Hediff_MugirlMilkPowder, Mugirl_Abasia, Mugirl_Lactation, Mugirl_Legbinder, Mugirl_Stun |
| HairDef | 5 | Mugirl_ChoppyShort, Mugirl_GloomHair6, Mugirl_GloomHair7, Mugirl_LongCascade, Mugirl_ShoulderLength |
| FactionDef | 3 | MooGirl_GiantCorporations†, MooGirl_GiantCorporations_Hostile†, Mugirl_GiantCorporations_Hostile |
| other | | ThoughtDef: Consumed_MugirlRichMilkFlavor, Mugirl_Nuzzled. ThinkTreeDef: MugirlLike, HumanlikeConstant‡. ThingDef_AlienRace: Mugirl, MooGirl†. FactionDef/PawnKindDef: Mugirl_Colony, MooGirl_Colony†. TraitDef: Mugirl_SingleshotWeaponAversion, MooGirl_…†. QuestScriptDef: Mugirl_CorporateIntroductionQuest, Mugirl_SlaveOpeningPodCrash. GeneDef: Mugirl_NobelPrize, Mugirl_Shooting. AbilityDef/GeneDef: Mugirl_Shout, Mugirl_Throw. AbilityDef/GeneDef/HediffDef: Mugirl_Charge. AbilityDef: Mugirl_Dunk. HediffDef/ThoughtDef: Consumed_MugirlMilk. BodyDef: MugirlBody. IncidentDef/QuestScriptDef: Mugirl_CourierRaid. IncidentDef: Mugirl_Migration. RecipeDef: Mugirl_MakeMilkPowder. PawnKindDef/ThingDef: Mugirl_MechanoidBulldog†. InteractionDef: Mugirl_Nuzzle. PawnKindDef/ThingCategoryDef: Mugirl_Slave. XenotypeDef: Mugirl_Xenotype. WorkGiverDef: Work_GatherMilk |

### Where the references sit

Top contexts (element<parent, refs):

| context | refs | context | refs |
|---|---|---|---|
| `li<allowedDefs` (storage, bill, outfit and food filters) | 5486 | `def<li` | 408 |
| `originalTargetPawn`/`recipientPawn`/`pawn`/`otherPawn`/`initiator`/`recipient<li` (play log, memories) | ~1,100 | `body<li`/`body<part` (MugirlBody + part index) | 320 |
| `pawn`/`kind`/`app`/`peq<pawnData` (vanilla `TaleData_Pawn`) | ~650 | `id<li`/`id<thing` | 205 |
| `stuff<li` | 53 | `li<keys` (research progress, dictionaries) | 51 |
| `Workgiver<li` (WorkTab mod) | 50 | `li<ingredients` | 49 |
| `source<li` (`Hediff_Injury.source`) | 38 | `ownerDef<li` (`BattleLogEntry_MeleeCombat`) | 36 |
| `intDef<li` | 36 | `li<ignoredFactions` (VEF) | 18 |
| `hairDef`/`headType`/`childhood`/`adulthood<story` | 62 | `xenotype<genes` | 18 |
| `jobGiverThinkTree<curJob` | 15 | `li<lockedApparel` | 15 |
| `kindDef`, `originalKindDef<AlienRaces_AlienComp` | 35 | `root<li` (quest roots) | 8 |
| `*ControllerComp_faceType` (Facial Animation) | 30 | `defName<defData`, `thingDef<li` (incl. `Precept_Relic.thingDef` = Mugirl_Gun_Defense_Rifle in S8) | 22 |

**Names shared across def types** (renames must be done per type): `Mugirl_Blindfold` etc. (HediffDef + SlaveApparelDef), `Mugirl_Charge`, `Mugirl_Shout`, `Mugirl_Throw`, `Mugirl_Slave`, `Mugirl_Colony`, `Mugirl_CourierRaid`, `Consumed_MugirlMilk`, `Mugirl_MechanoidBulldog`.

### Retired names (in saves, not in the 1.7 tree)

| name | saves | where | 1.7 successor |
|---|---|---|---|
| `Mugirl_MechanoidBulldog` + `Corpse_` | S8, S10 | `allowedDefs` only (3 refs) | none (git `c66cc36`..`131ee89`) |
| `MooGirl_GiantCorporations` | S1, S2 | a live faction in `world/factionManager/allFactions/li/def`, plus `factions`, `ignoredFactions`, `factionCountsStrings` | none |
| `MooGirl_GiantCorporations_Hostile` | S4 | `allFactions`, `ignoredFactions` | `Mugirl_GiantCorporations_Hostile` |
| `MooGirl_SingleshotWeaponAversion` | S1, S2 | traits on world pawns | `Mugirl_…` |
| `MooGirl_Wool` | S1, S2 | `stuff` of apparel (14) + filters | `Mugirl_Wool` |
| `MuGirl_Gun_Defense_Rifle` | S3 | `thingDef<li` | `Mugirl_Gun_Defense_Rifle` |
| `MooGirl_Colony`, `MooGirl` (+`Corpse_`/`Meat_`) | S1, S2, S4 | `ignoredFactions`, filters | `Mugirl_Colony`, `Mugirl` |
| 84 more `MooGirl*` names | S1–S4 | filters (and `ingredients`) | `MooGirl`→`Mugirl` (one exception: `SlaveApperalKey`→`SlaveApparelKey`) |
| `HumanlikeConstant` | S6, S7 | `jobGiverThinkTree` | vanilla def; not ours |

In total, MooGirl-era saves contain 91 retired names; 90 map to a 1.7 def by rule and `MooGirl_GiantCorporations` has no successor.

Some retired names exist in git or in the 1.7 compat files but appear in **no** real save:
- `Mugirl_NeuralCombatShoulders` and its `Make_` recipe, targeted by `Compatibility/Savegames/NeuralShoulderSaveMigration.cs:11-12`.
- `Mugirl_WildManWandersIn`, kept in `1.7/Defs/Compatibility/RetiredEvents.xml:5`.
- The `CorporateRuntimeValidation`/`CorporateVisualValidation` components, targeted by `Compatibility/Savegames/CorporateValidationSaveMigration.cs:12-13`.

## 6. Cross-check against the 1.7 and 1.6 sources

### Labels in saves that only legacy code reads

| label | where | saves | 1.7 handling |
|---|---|---|---|
| `milkFullness_lastResourceUpdateTick` | race pawns | S6, S7, S10 (10 pawns) | Read only during loading and converted on the first tick (`Features/Milk/Production/CompMooHasBodyResource.cs:59-62,76-79`, `MilkProductionMath.cs:26-33`) |
| `Thought_Hediff` with only `added` | memories | S6 ×8, S7 ×11 | Vanilla `RimWorld.MemoryThoughtHandler.ExposeData` (decompiled 1.7) silently removes memories with a null `def` at PostLoadInit. The class name must still resolve, or the upgrader must remove the element |
| MooGirl component labels | S1–S4 | | Types gone; `courierRaidTimer`, `checkTimer` and `xenotypeFixed` have same-named successors |

### Other cross-checks

- **No other gaps:** every other label observed has a reader in 1.7.
- **1.6-only labels absent from saves:** none of the 1.6-only labels that 1.7 ignores occurs in a real save. These are `tickCounter`, `currentTickCount`/`remainingCycles`, and the HediffComp `age`/`triggered` (read via `HediffCompScribe.cs:9-19`).
- **Labels that only 1.7 writes**, so no 1.6 save has them:
  - `punisherHeldLoot` and `punisherLootBursts` (`MapComponent_MugirlPunisher.cs:281-282`).
  - `milkFullness_pendingResourceTicks`.
  - `mugirl{SuppressionAge, AddMentalState/AddThought/AddTrait/ReduceWill}{Age,Triggered}`.
  - `activeGatherTarget` and `gatherEffectsActive` (`Features/Milk/Gathering/JobDriver_GatherBodyResources.cs:30-31`).
  - `forcedWaitJobLoadId` (`Features/Milk/Interaction/MilkPartnerWait.cs:41`).
  - The `MugirlPunisherLootBurst`/`DebrisArc` fields (`Features/Incidents/Punisher/MugirlPunisherLootBurst.cs:184-217`).

### Existing compatibility code vs what real saves contain

| Compatibility code (1.7) | Present in real saves? |
|---|---|
| The milk tick conversion | **Yes** (S6, S7, S10) |
| Punisher missing lists | **Yes** (all saves with the component) |
| Corporate validation migration | No |
| Neural-shoulder migration | No |
| Retired-event def | No |
| `HediffCompScribe` legacy labels | No |
| `priceVersion` 0 | No (all 4 offers = 1) |
| Null mount `innerContainer` | No |

## 7. DefNames embedded in IDs (decompiled from 1.7 `Assembly-CSharp.dll`)

How the game handles them:
- **`Verse.Thing.ThingID`:** the getter is `def.defName + thingIDNumber`. The setter parses the trailing digits. References are written as `Thing_<ThingID>`.
- **`Verse.LoadedObjectDirectory.ObjectWithLoadID<T>`:** an unmatched `Thing_` reference falls back to `thingIDNumber`. It logs "Could not resolve reference … Resolving reference by using thingIDNumber instead (back-compat)", once per reference.
- **`Verse.VerbTracker.ExposeData`:** a verb whose load ID doesn't match is re-created with the warning "Replaced verb …; may have been changed through a version update".

Counts in real saves:
- **`Thing_Mugirl<n>` (race) references: 1,685.** S6 712, S7 629, S5 179, S10 165.
- **`Thing_<other our def><n>` references: 28.**
- **`<id>` values of our things: 211.**
- **Verb IDs embedding our ThingIDs: 75.** These look like `Thing_Mugirl<n>_<i>_Stab`, `Verb_Thing_…`, `CompEquippable_Mugirl_Gun_Defense_Rifle<n>_0`, MVCF `…_Managed`, `TerrainVerbs_Mugirl<n>_…`.

Renaming a def that has instances keeps the data but logs about 700 warnings for S6, unless the upgrader rewrites `<id>`, `Thing_…` and the verb IDs before references are resolved.

## 8. Other strings and implicit structure stored in saves

- **A translation key stored as data:** `CorporateRecord.key` = `Mugirl.CorporateIntro.Title`. The UI translates the stored key (`Features/Corporate/UI/Terminal/Pages/CorporateHistoryPage.cs:106-110`). `detail` stores already-translated text.
- **Enum member names:** `CorporateMissionKind` (`Features/Corporate/Missions/CorporateMission.cs:8`).
- **A missing label means the default value**, because Scribe doesn't write values equal to the default:
  - `CorporateMission.state` = `Available` is never written.
  - `SlaveApparel.lockCount` defaults to the def's `needkeynumber` (`SlaveApparel.cs:18,49`); `isLocked` defaults to `true`.
  - So changing a default value, an enum's first member, or the def field behind a default silently changes what old saves mean.
- **Indices:** `MugirlBody` part indices and HAR `addonVariants` depend on the order of parts and body addons in our defs.
- **Quest signal strings** (`Quest<N>.CourierRaid_Spawn`, `…pawns.Rescued`) are consistent within each saved quest. `quests/li/name` holds the defName as plain text.
- **Other mods store our defNames:** WorkTab, research progress, the ideology relic, VEF `ignoredFactions`, Facial Animation comps, HAR `originalKindDef`, tales and battle logs.

## 9. Names that look like ours but are not

**Third-party defNames found in the saves:**
- `UFC_BIO_Mugirl*` (S7, S8, S11): workshop 3698316804, `Kindseal.UFPC`.
- `KT_Mugirl_powersuit` (S8, S11): workshop 3555662938, `KindSeal.LOL`.
- `Corpse_/Meat_WhiteFluff_Mugirl` (S9): local mod `乳牛娘 白绒物语`, `MRK.WhiteFluff`.
- The modId `luca.mugirlfacialanimation` (S2) belongs to a different mod.

**Local mods whose XML references our names:**
- `KindSeal.LOL`: `Class="Mugirl.StockGenerator_Mugirl_Slaves"` (`3555662938/1.6|1.7/Defs/Trader/KT_Trader.xml:480`).
- `MRK.WhiteFluff`: `Class="Mugirl.CompProperties_MugirlMount"` (`Mods/乳牛娘 白绒物语/1.6/Defs/WhiteFluff_Race.xml:103`), plus `Mugirl_Shout`, `Mugirl_NobelPrize`, `Mugirl_Charge`.
- `har.mugirlrace.subspecies`: `Class="Mugirl.CompProperties_MooMilkable"` (`Mods/MooGirl_Subspecies/1.6/Defs/AlienRaceDefs/Races.xml:321-322,486,739`), plus `Mugirl`, `Mugirl_Hair`, `Mugirl_Horn`, `Mugirl_Tail`, `Mugirl_OnSkin`, `Mugirl_Milk`, `Mugirl_Wool`, `Mugirl_Colony`, `Mugirl_SlaveApparelKey_Medieval`.

## 10. What this means for the upgrader's maps

1. **Source versions that actually exist** are MooGirl-era pre-git saves (S1–S4) and Mugirl builds 0.4.8 through 0.6.7 or a working tree (S5–S11). Nothing exists to test a "pre-refactor 1.7" source against.
2. **MooGirl era, if in scope:** decide what happens to 5 GameComponent classes, apply the `MooGirl*`→`Mugirl*` def rule (with the `SlaveApperalKey` exception), and handle the live `MooGirl_GiantCorporations` faction. There are no MooGirl pawns to convert.
3. **Thing def renames** need ID and reference rewrites (§7) and must be per def type. The order of `MugirlBody` parts and race `bodyAddons` is part of the save format.
4. **Enum names, stored Keyed keys and Scribe defaults** are part of the save format too.
5. **Use explicit name lists per def type, not a "Mugirl" substring rule.**

**Limits of this analysis:**
- Labels at their default value are invisible in a save.
- Labels built at runtime (the milk `SaveKey`, `HediffCompScribe`) were checked by hand.
- Workshop users may have saves from builds not seen here.
