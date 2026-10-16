# Module audit: Milk / Genes / Appearance / Apparel / AdvancedArmor

Scope: `Source/MugirlRace/Features/{Milk/**, Genes/**, Appearance/**, Apparel, AdvancedArmor}` (126 files, about 10.1k lines).
Method: I read every file that has save state, is referenced from XML, or carries a legacy marker. I diffed each against `1.6/Source` (the frozen snapshot) and against git history (`git log -S` / `git show <rev>:<path>`). I checked the vanilla behaviour I rely on in the decompiled 1.7 `Assembly-CSharp.dll`.
Paths below are relative to `Source/MugirlRace/Features/` unless they start with `1.6/`, `1.7/`, `Source/` or `docs/`.

## 0. Key facts that shape the upgrader (verified)

| # | Fact | Evidence |
|---|------|----------|
| F1 | **0.4.7 (324501c, 2026-07-03) renamed everything from `MooGirl` to `Mugirl` with no back-compat.** That covers the C# namespace (`MooGirl.*` → `Mugirl.*`), the race defName (`MooGirl` → `Mugirl`), about 335 defNames (`MooGirl_*`, `Consumed_MooGirlMilk`, `Job_DrinkMilkFromMooGirl` …) and class names (`MooGirl_XenotypeFix_GameComp`, `JobDriver_DrinkMilkFromMooGirl`, `JobDriver_MountMooGirl` …). The only packageId change was letter case (`HAR.MuGirlRace` → `HAR.MugirlRace`). No `GetBackCompatibleType`/`BackCompatibleDefName` handling exists in any revision. Saves from ≤0.4.6 have therefore been broken since 0.4.7. **Recommend: the upgrader's oldest supported source is 0.4.7.** | `git grep "namespace MooGirl"` counts: 161 at cb25751 (0.4.6), 0 at 324501c; `git show cb25751:…/MooGirl_Race.xml:25` `<defName>MooGirl</defName>`; About.xml diff |
| F2 | Save `<meta>` holds `gameVersion`, `modIds`, `modSteamIds` and `modNames`, but **no mod version**. The upgrader must tell source schemas apart by `gameVersion` (1.6.x versus 1.7.x) plus data markers (see D2). | `Verse.ScribeMetaHeaderUtility.WriteMetaHeader` (decompiled) |
| F3 | An unknown enum member name logs `Exception parsing node` and loads `default(T)` (for example `DunkState.Dribbling`). A float string in an int field fails the same way. | `Verse.ScribeExtractor.ValueFromNode` |
| F4 | An unknown `Class=` logs `Could not find class` and falls back via `GetBestFallbackType`. `BackCompatibility.GetBackCompatibleType` is consulted first, which is the hook the KB recommends postfixing. | `Verse.ScribeExtractor.SaveableFromNode` |
| F5 | Memories with a null def are silently removed in PostLoadInit. | `RimWorld.MemoryThoughtHandler.ExposeData` |
| F6 | Genes with a null def are removed in LoadingVars with `Log.Error("Removed null gene(s)")`. | `RimWorld.Pawn_GeneTracker.ExposeData` |
| F7 | `JobDriver.ExposeData` calls `SetupToils()` (that is, `MakeNewToils`) in PostLoadInit. `Job` saves `def` (JobDef), **`workGiverDef` (WorkGiverDef)** and `ability` (ref). | `Verse.AI.JobDriver.ExposeData`, `Verse.AI.Job.ExposeData` |
| F8 | The pawn renderer animation slot (`PawnRenderer.SetAnimation`) is **not** saved, so the milking AnimationDef names are not save-visible. | No `curAnimation`/animation Scribe literal in Assembly-CSharp |
| F9 | The freeze is enforced by the validator. `Source/Tests/MugirlRace.Validation/Baselines/save-types.txt:11` lists `Mugirl.Mugirl_XenotypeFix_GameComp`; other save-visible subclasses (and the enums `ThrowState`/`DunkState`) are derived automatically from the frozen 1.6 DLL (`save-types.txt:2`, `save-enums.txt:1-2`). The `dropped-*.txt` files are the "allowed deletions". Under the new direction these checks must become "every 1.6/pre-refactor name has an upgrader mapping or an explicit drop" checks. | baseline files |

## A. Legacy / backwards-compat code

Upgrader-need legend: (a) type rename map, (b) defName map, (c) Scribe label rename in a typed owner, (d) enum value rename, (e) structural XML transform, (f) post-load semantic step that needs live objects, (g) safe to drop.

### A.1 Code that exists only to read older data

| ID | Site | Legacy shape handled (origin) | Canonical new shape | Upgrader need |
|----|------|-------------------------------|---------------------|---------------|
| L1 | `Milk/Production/CompMooHasBodyResource.cs:25-26` (field), `:59-62` (LoadingVars-only read of `{saveKey}_lastResourceUpdateTick`), `:76-80` (one-shot conversion in `CompTickInterval`), `:226` (reset); `Milk/Production/MilkProductionMath.cs:24-34` (`LegacyPendingTicks`); test `Source/Tests/MugirlRace.Tests/Milk/MilkProductionMathTests.cs:49-51` | All 1.6 releases (label present since the initial upload d9b9bb9) stored the absolute tick of the last accrual, `milkFullness_lastResourceUpdateTick`. 1.7 stores `milkFullness_pendingResourceTicks` instead (`:58`). | Only the pending-ticks counter. | **(e)**: read `<game><tickManager><ticksGame>` and replace the node with `pending = clamp(ticksGame - old, 0, 60)`. Dropping the node is also acceptable, since at most 60 ticks of accrual (≤0.1% of a bar) are lost. Then delete the field, the branch, `LegacyPendingTicks` and its test. |
| L2 | `Milk/Production/CompProperties_MooMilkable.cs:9-10` (`saveKey = "milkFullness"`), `CompMooMilkable.cs:26`, `CompMooHasBodyResource.cs:37,54-64` | The Scribe label prefix is an XML-configurable string, kept so the labels stay identical to 1.6. No XML overrides it (`1.7/Defs/ThingDefs_Races/Mugirl_Race.xml:1055-1060`). | Fixed labels in code (see C.3). | (c) only if the labels are renamed; otherwise (g): delete the `saveKey` field and hard-code the labels. |
| L3 | `Milk/Production/HediffComp_Lactation.cs:6-7,24,52` | `lastBirthTick` is kept as a **float** "because 1.6 saved float". Float loses tick precision above 2^24 (about 280 in-game days). | `int lastBirthTick = -1`. | **(e)** value transform `round(float)` → int. This is mandatory if the type changes, because of F3: `"1.2E+07"`-style text would load as 0 with an error. |
| L4 | `Milk/FoodEffects/Thought_Hediff.cs:9-15` | 1.6 omitted `base.ExposeData()`, so 1.6 saves contain `<li Class="Mugirl.Thought_Hediff"><added>…</added></li>` with no def. Vanilla drops such memories (F5). | Normal `Thought_Memory` with base data. | (g): no data is recoverable. If the class is renamed, map the old name (a) so that pre-refactor 1.7 saves (which do have a def) keep the memory. The 1.6 entries still vanish as before. |
| L5 | `Milk/FoodEffects/HediffComp_WhileHavingThoughts.cs:8-11,20-25` | Labels kept "as in 1.6" (`flagAmIThinking`, `checkingCounter`; the 1.6 class was copied from VEF, see C.2). `flagAmIThinking` is recomputed before every read (`:71-76`), so its saved value is never used. | Drop `flagAmIThinking` from saves; optionally rename `checkingCounter`. | (g) for `flagAmIThinking` (extra node ignored on load); (c) only if `checkingCounter` is renamed. |
| L6 | `Genes/Dunk/Thing_MugirlDunkProp.cs:11-20` (`DunkState.Bounce`, comment "兼容短暂弹跳版本"), `:207-210` (Bounce tick → `FinishImpact(explode:false)`), `Thing_MugirlDunkProp.Presentation.cs:60` | `state=Bounce` came from an **uncommitted test build** between 0.6.6 and 0.6.7: `git log -S Bounce` finds only 131ee89 (0.6.7), which adds it already as a compat member. | Enum without `Bounce`. | **(d)**+(f): map `Bounce` → resolved (see L10) before load. Without that, F3 logs an error and loads `Dribbling`. |
| L7 | `Thing_MugirlDunkProp.cs:261-264` (TickSpin `!followsCarrier`), `:271-281` (TickFlight: "旧存档的头沿独立抛物线飞行", 31-tick impact), `Presentation.cs:76-79,84-96`, `Genes/Dunk/DunkPresentationMath.cs:55-67` (`FlightPosition`, documented as "旧存档中…") | 0.5.0–0.6.6 (62c7605 → 8cd70f9): the payload flew its own parabola. Those saves have no `followsCarrier`, so it loads as false. | Payload always follows the carrier's flyer. After this cleanup `followsCarrier` is always true outside `Dribbling`, so the field and label can go. | (f) via L10, then delete the branches, `FlightPosition` and the `followsCarrier` label (g). |
| L8 | `Thing_MugirlDunkProp.cs:283-296` (comment `:290` "旧档中的普通飞行器落地后兜底"; `slamTracksCarrier = activeFlyer is PawnFlyer_MugirlDunk`) | 0.5.0–0.6.6 used `ThingDef Mugirl_DunkFlyer` with the vanilla `PawnFlyer` thingClass (`git show 8cd70f9:Bio_1.6/Defs/ThingDefs_Misc/Throwing.xml:33-38` has only a `workerClass`). In-flight saves therefore contain `<thing Class="PawnFlyer"><def>Mugirl_DunkFlyer</def>`. | `PawnFlyer_MugirlDunk` (since 0.6.7). | (e) optional: rewrite `Class="PawnFlyer"` → the new flyer class where `def=Mugirl_DunkFlyer`. Otherwise (f) via L10. The non-flyer fallback is still needed for "flyer already gone". |
| L9 | `Thing_MugirlDunkProp.cs:302-308` ("旧版保存在 Slam 的状态没有起点数据"), `Presentation.cs:61` (`SlamDrawPos` when `!slamAnimating`), and the last branch of `RenderSpinDegrees` (`Presentation.cs:38-41`) | 0.5.0–0.6.6 `Slam` had no `slamAnimating` or `slamStart*` data (absent from the 13 Scribe calls in `git show 8cd70f9:…/Thing_MugirlDunkProp.cs`). | `Slam` is always animated; drop the `slamAnimating` field and label. | (f) via L10, then (g). |
| L10 | `Thing_MugirlDunkProp.Impact.cs:149-187` (`ShatterPayload` for Pawn or other things, comment "旧版本的载荷可能是整个 Pawn 或其他物体"), `:53-64` (non-head branch of `FinishImpact`), `Presentation.cs:186-195` (drawing Pawn or other payloads) | In 0.5.0–0.6.6 `TryCreate(Pawn caster, Thing target, …)` despawned **the whole target Thing** (usually a living Pawn) into `innerContainer` (`git show 8cd70f9:…/Thing_MugirlDunkProp.cs:126-154`). Since 0.6.7 the payload is always a `Thing_MugirlDunkHead`. In those releases dunk was a normal ability; it became dev-only in 0.6.7 (`git show 131ee89:…/CompAbilityEffect_Dunk.cs:62-63`). | Payload is always a `DunkHead`. | **(f) "finalize legacy dunks"**: after load, for every `Thing_MugirlDunkProp` loaded from a pre-schema save whose payload is not a head, or whose state is Bounce, or whose `!followsCarrier && state>Dribbling` or `Slam && !slamAnimating` holds, place the payload with `PayloadPlacement.TryPlace` (or apply the existing fatal-impact rules), reset the ability cooldown and destroy the controller. All of L6–L10 can then be deleted, which is about 120 lines. |
| L11 | `Genes/Dunk/Thing_MugirlDunkHead.cs:181-194` (`ResolveSavedShader`), used at `:164` | `headShaderName` holds a Unity shader name with a path prefix in 1.6 saves (0.6.7 and the frozen build), and a `ShaderDef` name in 1.7 saves. | Store a `ShaderDef` reference (or a normalized name). | **(e)** value transform: strip everything up to the last `/`. Optional (c) `headShaderName` → `headShader` with Scribe_Defs. Then delete the slash-splitting and `GetNamedSilentFail` (allow-listed in `Baselines/dynamic-lookups.allow.txt:7,12`). |
| L12 | `Genes/Xenotype/Mugirl_XenotypeFix_GameComp.cs` (entire class, 1-69; comment `:7` "分开保存只为兼容旧存档的字段名"; flags `:8-13`; scan `:26-58`) plus `MugirlXenotypeService.FixLoadedPawnIfSafe` (`MugirlXenotypeService.cs:22-36`) | A one-shot repair of old saves. It gives Mugirl-race pawns that have no xenogenes the `Mugirl_Xenotype` and fills in missing endogenes. The flags are history: `xenotypeFixed` (initial upload), `throwGeneFixed`/`dunkGeneFixed` (62c7605, 0.5.0 groundwork), `throwSystemGeneFixed` (131ee89, 0.6.7, when `Mugirl_Throw` was added to the xenotype; see `git show 131ee89:Bio_1.6/Defs/GoldenRaceBioTitle.xml`). New games get the xenotype from `xenotypeSet` (`1.7/Defs/PawnKindDefs/PawnKinds_Base.xml:12` and others) and from the birth patch. | Delete the GameComponent. Its scan becomes an upgrader step. | **(f)**: run `FixLoadedPawnIfSafe` over `PawnsFinder.All_AliveOrDead` once per upgraded save (Biotech only). **(e)/(a)**: the `<li Class="Mugirl.Mugirl_XenotypeFix_GameComp">` element must be stripped or mapped to a sink. Otherwise every old save logs two errors (KB "存档中的类型名"). Also remove the entry at `save-types.txt:11`. |
| L13 | `Appearance/BodyAccessory/CompMugirlBodyAccessory.cs:67-73` (comment "其他下标来自改动过的存档或旧版本") | No older shape exists in git: the comp and its labels were introduced in 0.6.3 (a172c15), already with this check. | Invariant check. | (g) drop, or keep as cheap validation. **See gap G3 for the real legacy data, which lives in HAR, not here.** |
| L14 | `Milk/Device/CompMilkingDeviceReleaseEffect.cs:67-73` (null → `[]`, comment "旧存档或空列表读入为 null") | The labels and the comp have been on both devices since the initial upload (`git log -S CompProperties_MilkingDeviceReleaseEffect -- '*.xml'` → d9b9bb9 only), and the lists are never null at save time. | n/a | (g) defensive; no legacy shape. |
| L15 | `Milk/Device/Comp_MilkingDevice.cs:147-151` (clamp negatives) | Defensive; there is no older shape (labels unchanged since the initial upload). | n/a | (g). |

### A.2 Content and API kept only for old XML, old callers or old names

| ID | Site | What is kept | Upgrader need |
|----|------|--------------|---------------|
| K1 | `Milk/Device/CompProperties_MilkingDevice.cs:46-50` (`powerFilthDefName`, `powerFilthCountRange`: "字段保留供 XML 加载…运行时不读取") + `1.7/Defs/Apparel/Apparel_Bondage/BondageApparel_Spacer.xml:237,348` (`<powerFilthDefName></powerFilthDefName>`) | Dead config fields | (g): delete the fields and both XML lines. Not save-visible. |
| K2 | `Genes/Charge/JobDriver_CastCharge.cs:25-30` (`DoJump`, "1.6 的公开入口，骑枪冲锋仍在使用"), `Genes/Shared/MugirlJumpUtility.cs:3` | A forwarder kept for the 1.6 call site. The only caller is `Lances/Flight/LanceChargeImpactUtility.cs:112`. | (g): point Lances at `MugirlJumpUtility.DoJump` and delete the forwarder. |
| K3 | `Milk/FoodEffects/MugirlFoodEffectUtility.cs:6-9`, `Milk/Nurture/MugirlNurtureUtility.cs:14-17` | Empty `ResetDefCache()` stubs, kept for `Incidents/Story/MugirlStoryState.cs:75-76` | (g): delete the stubs and both calls. |
| K4 | `Milk/Interaction/JobDriver_FeedMilkToDowned.cs:6` ("类名沿用旧 JobDef 的 driverClass") | A misleading name: the driver also feeds standing colonists (`:50-55`). | (a)+(b) if renamed (C.2). |
| K5 | `Genes/Dunk/Thing_MugirlDunkProp.cs:7,11`, `Thing_MugirlDunkHead.cs:5`, `Genes/Throw/Thing_MugirlThrownObject.cs:6,10`, `Genes/Xenotype/Mugirl_XenotypeFix_GameComp.cs:3`, `Milk/Animation/MugirlMilkingAnimationWorker.cs:3` ("类名不可改"), `Milk/Device/CompProperties_MilkingDeviceReleaseEffect.cs:3` ("嵌套类名…不可改名") | Names frozen by comments. **Correction:** the nested `SoundWithParams`/`FleckWithParams`/`TextWithParams` class names are *not* XML-visible, because the XML uses plain `<li>` (`BondageApparel_Spacer.xml:200-210`); only their field names are. `MugirlMilkingAnimationWorker` is XML-visible only (AnimationDef `workerType`), not save-visible (F8). | (a) for the save-visible types in B.1; otherwise just XML edits. |
| K6 | `Milk/Dev/MilkFeedingLayoutXml.cs:8` ("元素名是已导出文件的格式约定，不可改名") | The format of the dev-only override file `Config/Mugirl/MilkFeedingLayout.xml`, identical to 1.6. | (g): not a save. Change it freely if useful (unchanged files then fall back to defaults with a one-time warning, `MilkFeedingLayout.cs:72-77`). |
| K7 | `Milk/Gauge/MilkGaugeTextures.cs:3-4` (root `Textures/UI/Mugirl_MilkGauge.png` "保持不变") | Shared-root texture still needed by the frozen 1.6 build | Keep while 1.6 ships. Not code. |
| K8 | Comments documenting 1.6 parity only, with no code to drop: `MilkingPoseMath.cs:9`, `MugirlMilkingAnimation.cs:102`, `…Interaction.cs:121`, `…Render.cs:8`, `…State.cs:110`, `MilkProductionMath.cs:3,36`, `MugirlBreastProfileUtility.cs:65`, `ChargeKnockbackUtility.cs:3`, `JobDriver_CastCharge.cs:17,38`, `MugirlGenesDefOf.cs:4`, `MugirlBodyAccessoryAddon.cs:88`, `MugirlApparelTagUtility.cs:55,107`, `Harmony_NeuralHelmetDarknessAccuracy.cs:6,12`, `Harmony_MugirlNurturedSkillLearnCap.cs:9`, `MilkNurtureDefOf.cs:4`, `MugirlMilkDefOf.cs:3` (in `Source/MugirlRace/DefOf/`) | Historical notes. | (g): reword them as plain rationale once 1.6 parity no longer matters. |

### A.3 Legacy data that the current code does NOT handle (gaps the upgrader should own)

| ID | Gap | Evidence | Upgrader need |
|----|-----|----------|---------------|
| G1 | **Removed GeneDef `Mugirl_Dunk`** (existed 0.5.0–0.6.6; folded into `Mugirl_Throw`, which now grants both abilities, in 0.6.7). A pawn that has it logs a Scribe_Defs error plus "Removed null gene(s)" (F6). Nothing in the code handles it. | `git show 8cd70f9:Bio_1.6/Defs/GeneDefs.xml` has `<defName>Mugirl_Dunk`; `1.7/Mods/Biotech/Defs/GeneDefs.xml:24-33` | **(e)** strip `<li>` genes with `<def>Mugirl_Dunk</def>` (endo- and xenogenes); **(f)** optionally add `Mugirl_Throw` if the pawn had `Mugirl_Dunk` but not `Mugirl_Throw`. Do not use a (b) map: it would duplicate `Mugirl_Throw`. |
| G2 | **1.6 mid-gather saves.** 1.6 never saved `activeGatherTarget`, `gatherEffectsActive` or `forcedWaitJobLoadId`, although it had the fields (`1.6/Source/Features/Milk/JobDriver_GatherBodyResources.cs:12-19,130-133`). After load, `CleanupGatherEffects` returns early (`Milk/Gathering/JobDriver_GatherBodyResources.cs:151-157`), so the milked pawn stays in `Job_MugirlMilkingTargetWait` for up to 15000 ticks. The file comment (`:8-9`) acknowledges this, but nothing converts it. | as cited | **(f)**: for each pawn whose `curDriver` is the gather driver in an upgraded 1.6 save, end its target's `Job_MugirlMilkingTargetWait` if that wait job's targetA is the gatherer. Alternatively relink: set `activeGatherTarget = job.targetA`, `gatherEffectsActive = true`, and `forcedWaitJobLoadId` = the target's current wait job loadID. |
| G3 | **Permanent body-accessory variants from 0.5.1–0.6.2.** In c66cc36 (0.5.1) `MugirlBodyAccessoryAddon.GetPath` rolled a random variant `1..VariantCountMax-1` at 35% for *every* eligible pawn. Those variants include Lovin bite (1) and handprint (2), which since 0.6.3 are temporary-only, and the manual-only variant 4. HAR saved them in `AlienComp.addonVariants` (label confirmed in AlienRace.dll). | `git show c66cc36:1.6/Source/Features/Appearance/MugirlBodyAccessoryAddon.cs`; `Appearance/BodyAccessory/MugirlBodyAccessoryVariants.cs:3-6` | **Product decision.** Optional (f): reset the saved variant at this addon's index to 0 when it is 1 or 2. That needs live HAR AlienComp access (Compatibility/AlienRace). |
| G4 | 0.6.3-only `Mugirl_NeuralCombatShoulders` (AdvancedArmor content, removed in 0.6.4) is handled outside this scope by `Source/MugirlRace/Compatibility/Savegames/NeuralShoulderSaveMigration.cs` (Harmony prefix on `Game.ExposeSmallComponents`, editing the XML tree). | `git log -S Mugirl_NeuralCombatShoulders` → a172c15, 37545af | Absorb it into the unified upgrader as an (e) step (strip things and bills, null the dangling refs). Flagged for the Compatibility auditor. |
| G5 | Pre-0.4.7 saves (F1). | F1 | Declare them unsupported, or add an (a)+(b) map for MooGirl → Mugirl. The (b) map alone is about 335 defNames; not recommended. |

## B. Save-visible surface

### B.1 Types written with `Class="…"` (all unchanged between frozen 1.6 and the current 1.7 tree)

| 1.7 full name (= 1.6 full name) | Game base | Where the element lives | Since | XML reference |
|---|---|---|---|---|
| `Mugirl.Thing_MugirlThrownObject` | `Thing` | map `<things>`; ThingDef `Mugirl_ThrowController` | 62c7605 (0.5.0) | `1.7/Mods/Biotech/Defs/ThingDefs_Misc/Throwing.xml` thingClass |
| `Mugirl.Thing_MugirlDunkProp` | `Thing` | map `<things>`; ThingDef `Mugirl_DunkProp` | 62c7605 | same file |
| `Mugirl.Thing_MugirlDunkHead` | `Thing` | inside `DunkProp.innerContainer` (`<li Class=…>`), or on the map after `TryDropSafely`; ThingDef `Mugirl_DunkHead` | 131ee89 (0.6.7) | same file |
| `Mugirl.PawnFlyer_MugirlDunk` | `PawnFlyer` | map `<things>` while the carrier is airborne; ThingDef `Mugirl_DunkFlyer` (before 0.6.7: vanilla `PawnFlyer`, see L8) | 131ee89 | same file |
| `Mugirl.JobDriver_GatherMilk` (abstract base `Mugirl.JobDriver_GatherBodyResources` is never written) | `JobDriver` | `<curDriver Class=…>` in pawn `jobs` | initial | `1.7/Defs/WorkGiverDefs/WorkGivers.xml:23` (JobDef `Job_GatherMilk`) |
| `Mugirl.JobDriver_Breastfeed` | `JobDriver` | curDriver | initial | `1.7/Defs/JobDefs/JobDefs.xml:74` |
| `Mugirl.JobDriver_DrinkMilkFromMugirl` | `JobDriver` | curDriver | 0.4.7 (was `MooGirl.JobDriver_DrinkMilkFromMooGirl`) | `JobDefs.xml:81` |
| `Mugirl.JobDriver_FeedMilkToDowned` | `JobDriver` | curDriver | initial | `JobDefs.xml:88` |
| `Mugirl.JobDriver_CastCharge` | `JobDriver` | curDriver | initial | `1.7/Mods/Biotech/Defs/JobDefs.xml:6` |
| `Mugirl.JobDriver_MugirlDunk` | `JobDriver` | curDriver | 62c7605 | `…/Biotech/Defs/JobDefs.xml:14` |
| `Mugirl.Thought_Hediff` (internal) | `Thought_Memory` | `needs/mood/thoughts/memories/memories` (Deep list) | initial | `1.7/Defs/ThingDefs_Misc/Items_Resource.xml:49` thoughtClass |
| `Mugirl.Mugirl_XenotypeFix_GameComp` | `GameComponent` | `<game><components><li Class=…>` in **every** save | 0.4.7 (was `MooGirl.MooGirl_XenotypeFix_GameComp`) | none (created by `Game.FillComponents`) |

Nothing else in scope is written by class name: no Verb, Ability, Gene, Hediff, MapComponent, WorldComponent, QuestPart, Letter, LordJob, Need or Bill subclasses. The payloads inside the throw and dunk `ThingOwner`s are written with their own runtime class (vanilla `Pawn`, `Corpse`, buildings …).

### B.2 Scribe calls per owner

**Class= owners**

| Owner (file:lines) | Label | Kind | Field type | Default | 1.6 frozen | Note |
|---|---|---|---|---|---|---|
| `Thing_MugirlThrownObject` (`Genes/Throw/Thing_MugirlThrownObject.cs:351-368`) | innerContainer | Deep (`ThingOwner<Thing>`, ctorArg this) | ThingOwner | — | same | payload of any category |
| | carrier | References | Pawn | — | same | |
| | sourceAbility | References | Ability | — | same | |
| | state | Values | enum `ThrowState` {Held, Flying, Resolving} | Held | same | saved by member name |
| | originalPosition / destination / lastFlightCell | Values | IntVec3 | default(IntVec3) | same | `lastFlightCell` field init is `Invalid`, but the Scribe default is (0,0,0) |
| | originalRotation | Values | Rot4 | North | same | |
| | heldTicks / flightTicks / ticksFlying | Values | int | 0 | same | |
| | mass | Values | float | 0 | same | |
| | flightStartGround / flightStartDraw | Values | UnityEngine.Vector3 | default | same | |
| `Thing_MugirlDunkProp` (`Genes/Dunk/Thing_MugirlDunkProp.cs:325-353`) | innerContainer | Deep | ThingOwner | — | same | payload: `DunkHead` (0.6.7+); whole Thing in 0.5.0–0.6.6 |
| | carrier, victim | References | Pawn | — | same | `victim` new in 0.6.7 |
| | sourceAbility | References | Ability | — | same | |
| | state | Values | enum `DunkState` {Dribbling, Spin, Flight, Slam, Finished, **Bounce**} | Dribbling | same | L6 |
| | originalPosition, impactCell, landingCell | Values | IntVec3 | default | same | |
| | originalRotation | Values | Rot4 | North | same | |
| | mass, launchSpinDegrees, slamStartSpinDegrees | Values | float | 0 | same | spin fields new in 0.6.7 |
| | launchDrawPos, launchCarrierDrawPos, slamStartDrawPos, slamStartCarrierDrawPos | Values | Vector3 | default | same | the last three are new in 0.6.7 |
| | followsCarrier, slamAnimating, slamTracksCarrier | Values | bool | false | same | new in 0.6.7; the first two become dead after L10 |
| | stateTicks, runningTicks | Values | int | 0 | same | |
| | resolving, pendingDrop, bleeds | Values | bool | false | same | the last two are new in 0.6.7 |
| | bloodDef | **Defs** | ThingDef | — | same | |
| `Thing_MugirlDunkHead` (`Genes/Dunk/Thing_MugirlDunkHead.cs:196-224`) | headType | **Defs** | HeadTypeDef | — | same | |
| | hairDef | **Defs** | HairDef | — | same | |
| | skinColor, hairColor, headGraphicColor, headGraphicColorTwo | Values | Color | White | same | |
| | sourcePawn | References | Pawn | — | same | the dead victim; may be discarded later, giving a ref warning |
| | headWidth, headHeight | Values | float | 1 | same | |
| | hairWidth, hairHeight | Values | float | 0 | same | |
| | headGraphicPath, headMaskPath, headShaderName | Values | string | null | same labels; **`headShaderName` semantics differ (L11)** | |
| | PostLoadInit `:213-223` | — | — | — | same | Redundant: resets non-saved caches on a freshly constructed instance |
| `JobDriver_GatherBodyResources` (applies to `JobDriver_GatherMilk`; `Milk/Gathering/JobDriver_GatherBodyResources.cs:26-33`) | gatherProgress | Values | float | 0 | same | |
| | activeGatherTarget | References | Pawn | — | **new in 1.7** | |
| | gatherEffectsActive | Values | bool | false | **new in 1.7** | |
| | forcedWaitJobLoadId (`Milk/Interaction/MilkPartnerWait.cs:12,41`) | Values | int | -1 | **new in 1.7** | |
| `JobDriver_Breastfeed`, `JobDriver_DrinkMilkFromMugirl`, `JobDriver_FeedMilkToDowned` (each `:18-22`) | forcedWaitJobLoadId | Values | int | -1 | **new in 1.7** (1.6 had no `ExposeData`) | |
| `JobDriver_MugirlDunk` (`Genes/Dunk/JobDriver_MugirlDunk.cs:122-126`) | dunkProp | References | Thing_MugirlDunkProp | — | same | |
| `JobDriver_CastCharge` | — | — | — | — | — | No state; `hitPawns` and `ticksSinceSmokeGeneration` are deliberately unsaved (`:17-21`) |
| `Thought_Hediff` (`Milk/FoodEffects/Thought_Hediff.cs:11-15`) | (base `Thought_Memory` labels) | — | — | — | **1.6 omitted base** | L4 |
| | added | Values | bool | false | same | |
| `Mugirl_XenotypeFix_GameComp` (`Genes/Xenotype/Mugirl_XenotypeFix_GameComp.cs:60-68`) | xenotypeFixed, throwGeneFixed, dunkGeneFixed, throwSystemGeneFixed | Values | bool | false | same | L12 |
| | checkTimer | Values | int | 300 | same | |

**Inline savers** (no `Class=`; the labels land inside the parent element)

| Owner (file:lines) | Parent element | Label | Kind / type | Default | 1.6 |
|---|---|---|---|---|---|
| `CompMooMilkable` via `CompMooHasBodyResource` (`Milk/Production/CompMooHasBodyResource.cs:51-65`) | the Mugirl `Pawn`'s thing element (shared with all of the pawn's comps; vanilla `CompHasGatherableBodyResource` uses `fullness`, hence the prefix) | `milkFullness` | float | 0 | same |
| | | `milkFullness_fullNotified` | bool | false | same |
| | | `milkFullness_lastFullNotifyTick` | int | -99999 | same |
| | | `milkFullness_pendingResourceTicks` | int | 0 | **new in 1.7** |
| | | `milkFullness_lastResourceUpdateTick` | int, **read-only in LoadingVars** | -99999 | written by 1.6 (L1) |
| | | `milkFullness_MilkThreshold` | float | 0.8 | same |
| | `CompMooMilkable.PostExposeData` (`CompMooMilkable.cs:107-115`) | — | cache reset only (redundant on a fresh instance) | — | same |
| `Comp_MilkingDevice` (`Milk/Device/Comp_MilkingDevice.cs:140-152`) | the apparel thing (`Mugirl_MilkingDevice`, `Mugirl_MilkingDeviceHidden`) | storedCharges, storedMilkAmount | int | 0 | same |
| | | autoReleaseEnabled, powerReleaseEnabled | bool | false | same |
| `CompMilkingDeviceReleaseEffect` (`Milk/Device/CompMilkingDeviceReleaseEffect.cs:59-74`) | same apparel | effectTicksRemaining | int | 0 | same |
| | | effectAge | int | 0 | same |
| | | nextSoundTicks, nextFleckTicks, nextTextTicks | Collections `List<int>`, LookMode.Value | — | same |
| `CompMugirlBodyAccessory` (`Appearance/BodyAccessory/CompMugirlBodyAccessory.cs:61-74`) | Mugirl `Pawn` | temporaryBodyAccessoryVariant | int | 0 | same (since 0.6.3) |
| | | temporaryBodyAccessoryVariantExpiresAt | int | -1 | same |
| `HediffComp_Lactation` (`Milk/Production/HediffComp_Lactation.cs:49-53`) | hediff `Mugirl_Lactation` (`HediffWithComps`) | lastBirthTick | **float** | -1 | same (L3) |
| `HediffComp_MugirlNurtureProgress` (`Milk/Nurture/HediffComp_MugirlNurtureProgress.cs:35-39`) | hediff `Mugirl_MotherlyNurture` | completed | bool | false | same |
| `HediffComp_CureFoodEffects` (`Milk/FoodEffects/HediffComp_CureFoodEffects.cs:41-45`) | hediff `Hediff_MugirlAgedCheese` (the only user, `1.7/Defs/HediffDefs/Hediffs_Mugirl_MilkFood.xml:38`) | applied | bool | false | same |
| `HediffComp_WhileHavingThoughts` (`Milk/FoodEffects/HediffComp_WhileHavingThoughts.cs:20-25`) | HediffDef `Consumed_MugirlMilk` | flagAmIThinking | bool | false | same (L5: redundant) |
| | | checkingCounter | int | 600 | same |
| `HediffComp_CheckJobOrRemove` (`Genes/Charge/HediffComp_CheckJobOrRemove.cs`) | hediff `Mugirl_Charge` | (inherited vanilla `HediffComp_Disappears` labels only) | — | — | same |

**Stateless (nothing saved):** `Comp_LongCascadeBackHair`, `CompNeuralCombatArmor`, `CompNeuralShoulderDeflection`, `CompAbilityEffect_Dunk/PickupThrow/ForceJob` (their fields are UI caches only), `MugirlBodyAccessoryAddon` (HAR saves `addonVariants` itself; see G3), render nodes, workers, `IngestionOutcomeDoer_*`, `MugirlMilkingAnimationWorker`, `FloatMenuProvider_MilkMugirl`, WorkGivers, and all static state (`MugirlMilkingAnimation.states`, `MilkGaugePopupSession`, `ThrowControllerRegistry`, `MilkFeedingLayout`, `MugirlApparelTagUtility.apparelByTag`).

### B.3 Enums, Scribe_Defs and Scribe_References

* Enums saved by member name: `Thing_MugirlThrownObject.ThrowState` (`:11-16`) and `Thing_MugirlDunkProp.DunkState` (`:12-20`). Both are private nested types, so only the member names matter; the type name never appears in a save. `MugirlMilkingVisualRole` is **not** saved.
* `Scribe_Defs`: `ThingDef` (DunkProp `bloodDef`), `HeadTypeDef` and `HairDef` (DunkHead). Indirectly, through vanilla objects created by this module: `JobDef` and `WorkGiverDef` (F7), `ThoughtDef` (Thought_Hediff base), and the `HediffDef`/`AbilityDef`/`GeneDef`/`TraitDef` of the items listed in B.4.
* `Scribe_References` targets: `Pawn` (carrier, victim, sourcePawn, activeGatherTarget), `Ability` (sourceAbility ×2), `Thing_MugirlDunkProp` (`dunkProp`).

### B.4 Save-visible defNames owned by this scope (any rename needs (b))

* **JobDef** (`Job.def`): `Job_GatherMilk`, `Job_MugirlMilkingTargetWait` (both defined in `1.7/Defs/WorkGiverDefs/WorkGivers.xml:22,30`), `Job_Breastfeed`, `Job_DrinkMilkFromMugirl`, `Job_FeedMilkToDowned`, `Job_MugirlMilkInteractionWait` (`1.7/Defs/JobDefs/JobDefs.xml`), `Job_Charge`, `Job_MugirlDunk` (`1.7/Mods/Biotech/Defs/JobDefs.xml`).
* **WorkGiverDef** (`Job.workGiverDef`): `Work_GatherMilk`.
* **HediffDef**: `Mugirl_Lactation`, `Mugirl_MotherlyNurture`, `Mugirl_NurtureAfterglow`, `Hediff_MugirlAgedCheese`, `Hediff_MugirlCowCake`, `Hediff_MugirlMilkPowder`, `Hediff_MugirlMilkTablet`, `Consumed_MugirlMilk`, `Mugirl_MilkHealing`, `Mugirl_Stun`, `Mugirl_Charge`, `Mugirl_NeuralCombatConsciousness`.
* **ThoughtDef**: `Consumed_MugirlMilk`, `Mugirl_DrankMilk`, `Mugirl_FedMilk`. **TraitDef**: `Mugirl_NurturedByMugirl`.
* **AbilityDef**: `Mugirl_Charge`, `Mugirl_Shout`, `Mugirl_Throw`, `Mugirl_Dunk`. **GeneDef**: `Mugirl_Charge`, `Mugirl_Throw`, `Mugirl_Shout`, `Mugirl_Shooting`, `Mugirl_NobelPrize` (removed: `Mugirl_Dunk`, G1). **XenotypeDef**: `Mugirl_Xenotype`.
* **ThingDef**: `Mugirl_Milk`, `MugirlMilkStain` (filth), `Mugirl_ThrowController`, `Mugirl_DunkProp`, `Mugirl_DunkHead`, `Mugirl_DunkFlyer`, `Mugirl_MilkingDevice`, `Mugirl_MilkingDeviceHidden`, `Mugirl_NeuralCombatArmor`, `Mugirl_NeuralAmplifierHelmet`, plus the milk food items. **HairDef**: `Mugirl_LongCascade`, `Mugirl_Hairs10`. **BackstoryDef**: `Mugirl_ExperimentalChild`. **DamageDef** `Mugirl_ThrownImpact` (battle-log entries).
* Not save-visible: AnimationDefs `Mugirl_Milking*` (F8), FleckDefs, SoundDefs.
* Same defName across several Def types (legal, but confusing in logs and translations): `Mugirl_Charge` (Gene + Ability + Hediff), `Mugirl_Throw` and `Mugirl_Shout` (Gene + Ability), `Consumed_MugirlMilk` (Thought + Hediff).

### B.5 XML- and translation-visible names (rename means an XML edit, not a save upgrade)

* `CompProperties_MooMilkable`, `CompProperties_Lactation`, `CompProperties_MilkingDevice`, `CompProperties_MilkingDeviceReleaseEffect`, `HediffCompProperties_{CureFoodEffects, WhileHavingThoughts, MugirlNurtureProgress, CheckJobOrRemove}`, `IngestionOutcomeDoer_{AddOrRefreshHediff, RemoveHediffs}`, `CompProperties_AbilityEffect_{Dunk, ForceJob, PickupThrow}`, `PawnFlyerWorker_MugirlDunk`, `MugirlMilkingAnimationWorker` (10 AnimationDefs), `Features.Appearance.{MugirlBodyAccessoryAddon, CompProperties_MugirlBodyAccessory, CompProperties_LongCascadeBackHair, PawnRenderNode_NunVeilBack}`, `Features.AdvancedArmor.CompProperties_{NeuralCombatArmor, NeuralShoulderDeflection}`.
* **DefInjected keys use the comp class short name:** `<Mugirl.comps.CompMooMilkable.displayString>` and `<Mugirl_MilkingDevice(Hidden).comps.Comp_MilkingDevice.{inspectLabel,noChargeText,releaseLabel}>` in `1.7/Languages/*/DefInjected/ThingDef/*`. Renaming `CompMooMilkable` or `Comp_MilkingDevice` requires rewriting those keys in every language.

## C. Naming and architecture

### C.1 Inferred conventions

These come from the best existing code (`Features/Lances`, `Features/WeaponWheel`, the `Mugirl.Core.*` helpers referenced in `Baselines/architecture-rules.json`) and from vanilla:
* One namespace per feature, `Mugirl.Features.<Feature>`, flat across subfolders (Lances keeps all of `Charge/`, `Flight/` and `Rendering/` in `Mugirl.Features.Lances`, including its save-visible `JobDriver_LanceCharge` and `PawnFlyer_LanceCharge`). No `Mugirl` prefix or infix inside the namespace.
* Vanilla type shapes: `Comp<Name>` + `CompProperties_<Name>`; `HediffComp_<Name>` + `HediffCompProperties_<Name>`; `JobDriver_`, `WorkGiver_`, `Thought_`, `PawnFlyer_`, `PawnFlyerWorker_`, `PawnRenderNode(_Worker|Properties)_`; `<Name>Utility` for static helpers; `<Feature>DefOf` per feature.
* Harmony patch classes: `Harmony_<Subject>` (91 uses) with `[MugirlPatch]` metadata. There are currently 3 styles in the tree (`Harmony_` 91, `*_Patch` 25, `Patch_` 6). Inside this scope, `Harmony_<Target>_<Method>_Mugirl`, `Harmony_Mugirl<Feature>` and `Harmony_<Feature>_<Part>` all appear.
* Def names: `Mugirl_<PascalName>`, without type prefixes (`Job_`, `Hediff_`, `Work_`).

### C.2 Proposed renames (only where they buy clarity or consistency)

| Kind | Old | New | Reason | Save-visible? | XML-visible? |
|---|---|---|---|---|---|
| namespace | `Mugirl` for 53 files in Milk, 27 in Genes, 4 in Appearance, 5 in AdvancedArmor, 1 in Apparel (counts of `namespace Mugirl;`) | `Mugirl.Features.Milk` / `.Abilities` / `.Xenotype` / `.Appearance` / `.AdvancedArmor` | The root namespace was only kept for the freeze (`docs/architecture/ADR-004-root-namespace-markers.md`). Currently Milk and Genes mix root and `Mugirl.Features.*` in the same folder. | yes for the B.1 types | yes (every `Class="Mugirl.X"`) |
| class | `CompMooHasBodyResource` + `CompMooMilkable` (merge) | `CompMilkProduction` (sealed) | "Moo" is a leftover of the MooGirl era; an abstract base with a single subclass; the name avoids clashing with vanilla `RimWorld.CompMilkable` | inline only | DefInjected key (B.5) |
| class | `CompProperties_MooMilkable` | `CompProperties_MilkProduction` (drop `saveKey`) | as above, plus L2 | no | yes |
| class | `CompProperties_Lactation` | `HediffCompProperties_Lactation` | It is a `HediffCompProperties`; vanilla prefix | no | yes (`Hediffs_Lactation.xml:14`) |
| class | `Comp_MilkingDevice` | `CompMilkingDevice` | Matches its sibling `CompMilkingDeviceReleaseEffect` | inline | DefInjected keys |
| class | `JobDriver_GatherBodyResources` + `JobDriver_GatherMilk`; `WorkGiver_GatherBodyResources` + `WorkGiver_GatherMilk` | `JobDriver_GatherMilk`, `WorkGiver_GatherMilk` (merged) | Single-subclass abstractions; `WorkGiver_GatherMilk.HasJobOnThing` (`:8-21`) duplicates base checks | JobDriver yes (a) | yes |
| class | `JobDriver_Breastfeed` | `JobDriver_ChildDrinkMilk` | Shadows vanilla `RimWorld.JobDriver_Breastfeed` (verified), whose semantics are inverted (there the actor is the mother) | yes (a) | yes |
| class | `JobDriver_FeedMilkToDowned` | `JobDriver_FeedMilk` | Also feeds standing pawns (K4) | yes (a) | yes |
| class | `Thought_Hediff` | `Thought_MemoryAddsHediff` | Copied from VEF (`VEF.Cooking.Thought_Hediff` exists with the same short name); the name reads as "thought caused by a hediff" | yes (a) | yes (thoughtClass) |
| class | `HediffComp(Properties)_WhileHavingThoughts` | `HediffComp(Properties)_RequiresMemory` | Clearer intent; also delete the unused `removeThoughtDefs`, `hediffReduction` and `reductionAmount` (no XML uses them; `HediffCompProperties_WhileHavingThoughts.cs:8-13`) | inline | yes |
| class | `HediffComp(Properties)_MugirlNurtureProgress` | `HediffComp(Properties)_NurtureProgress` | Redundant infix | inline | yes |
| class | `MugirlMilkingAnimation(+Worker)`, `MugirlMilkingVisualRole`, `Mugirl{MilkInteraction,MilkEffect,MilkOutput,BreastProfile,Lactation,Nurture,FoodEffect,BabyFeeding}Utility` | drop the `Mugirl` prefix | Redundant inside `Mugirl.Features.Milk` | no | Worker only (AnimationDef) |
| class | `MugirlMilkDefOf` (in `Source/MugirlRace/DefOf/`), `MilkNurtureDefOf`, plus the milk entries of `Mugirl_DefOf` (`Mugirl_Milk`, `Job_GatherMilk`, `Job_Mugirl*Wait`, 5 AnimationDefs) | one `MilkDefOf` | Milk defs are currently spread over 3 DefOf classes | no | no |
| class | `Thing_MugirlThrownObject` | `ThrowController` | Matches its ThingDef `Mugirl_ThrowController` and its role (a controller holding the payload) | yes (a) | yes |
| class | `Thing_MugirlDunkProp` | `DunkController` | "Prop" is a dev term; parallel to the throw controller | yes (a) | yes |
| class | `Thing_MugirlDunkHead`, `PawnFlyer_MugirlDunk`, `PawnFlyerWorker_MugirlDunk`, `JobDriver_MugirlDunk` | `DunkHead`, `PawnFlyer_Dunk`, `PawnFlyerWorker_Dunk`, `JobDriver_Dunk` | Redundant infix | yes (a), except the worker | yes |
| class | `JobDriver_CastCharge` | `JobDriver_Charge` | Not a cast (it is a run); parallel to `JobDriver_LanceCharge` and JobDef/AbilityDef `*Charge` | yes (a) | yes |
| class | `HediffComp(Properties)_CheckJobOrRemove` | `HediffComp(Properties)_RequiresJob` | Describes the rule | inline | yes |
| class | `Mugirl_XenotypeFix_GameComp` | **delete** (L12) | `Mugirl_` prefix and `GameComp` suffix break conventions, and the class only exists for legacy repair | yes (strip/sink) | no |
| class | `MugirlXenotypeService`, `Harmony_PawnUtility_TrySpawnHatchedOrBornPawn_MugirlXenotype` | `XenotypeBirthUtility`, `Harmony_BirthXenotype` | Naming consistency. Also move the lactation notification (`Harmony_…:19-22`) out of the xenotype patch into a Milk-owned birth hook | no | no |
| folder / ns | `Features/Genes/{Charge,Dunk,Shout,Throw,Shared}` | `Features/Abilities/…` (`Mugirl.Features.Abilities`); `Xenotype/` → `Features/Xenotype` | The folder holds gene-granted **abilities**, not Gene subclasses | via the types above | — |
| class | `MugirlPawnSpinRenderer`, `MugirlThrowUtility`, `MugirlDunkUtility`, `MugirlJumpUtility`, `MugirlGenesDefOf` | `PawnSpinRenderer`, `ThrowUtility`, `DunkUtility`, `JumpUtility`, `AbilitiesDefOf` | Redundant prefix; `MugirlPawnSpinRenderer` and two utilities sit in the root namespace inside `Genes/Shared` | no | no |
| class | `Comp_LongCascadeBackHair`, `CompProperties_/PawnRenderNode_/PawnRenderNodeProperties_/PawnRenderNodeWorker_LongCascadeBackHair` | `…BackHair` | Now generic (also used for `Mugirl_Hairs10`, `Mugirl_Race.xml:1026-1030`); `Comp_` prefix is inconsistent | no | yes |
| class | `CompMugirlBodyAccessory`, `CompProperties_MugirlBodyAccessory`, `MugirlBodyAccessoryAddon`, `MugirlBodyAccessoryVariants` | `CompBodyAccessory`, `CompProperties_BodyAccessory`, `BodyAccessoryAddon`, `BodyAccessoryVariant` | Redundant infix (already in `Mugirl.Features.Appearance`) | inline | yes |
| class / file | `Harmony_PawnHairColors_HasGreyHair_Mugirl` in `Harmony_MugirlHairColor.cs`; `Harmony_ExtraOutline_Body` in `Harmony_ExtraOutline.cs`; `MugirlExtraOutline` (root namespace) | file name = class name; `ExtraOutline` in `Mugirl.Features.Appearance` | Consistency | no | no |
| class | AdvancedArmor `Harmony_*` (×4) and `AdvancedArmorUtility` (root namespace) | move into `Mugirl.Features.AdvancedArmor` | The comps are already there | no | no |
| folder | `Features/Apparel/MugirlApparelTagUtility.cs` | `Features/Incidents/…` (e.g. `KindApparelUtility`) | Only the 4 Incidents generators use it (`OpeningCrash`, `RefugeePod`, `Trade`, `Wanderer`); the name `Apparel` collides with `Appearance/Apparel` | no | no |
| member | `CompMooHasBodyResource.MilkThreshold` (public field, PascalCase) | `AutoMilkThreshold` property, clamped on load | Field style; the Gizmo writes it directly (`Gizmo_MilkGauge.cs:172,178`) | label: see C.3 | no |
| member | `DevFillToFull` | `FillToFull` | Gameplay code uses it (`Incidents/Migration/IncidentWorker_Mugirl_Migration.cs:94`) | no | no |
| defName | `Job_GatherMilk`, `Job_Breastfeed`, `Job_DrinkMilkFromMugirl`, `Job_FeedMilkToDowned`, `Job_MugirlMilkInteractionWait`, `Job_MugirlMilkingTargetWait`, `Job_Charge`, `Job_MugirlDunk`, `Work_GatherMilk` | `Mugirl_GatherMilk`, `Mugirl_ChildDrinkMilk`, `Mugirl_DrinkMilk`, `Mugirl_FeedMilk`, `Mugirl_MilkInteractionWait`, `Mugirl_MilkingTargetWait`, `Mugirl_Charge`(JobDef), `Mugirl_Dunk`(JobDef), `Mugirl_GatherMilk`(WorkGiverDef) | `Job_Charge` and `Job_Breastfeed` are generic names that risk collisions with other mods; the type prefixes are unusual for RimWorld | yes (b) | yes, plus DefInjected |
| defName | `Hediff_MugirlAgedCheese/CowCake/MilkPowder/MilkTablet`, `MugirlMilkStain`, HediffDef `Mugirl_Charge` | `Mugirl_AgedCheeseEffect` …, `Mugirl_MilkStain`, `Mugirl_ChargeSpeed` (parallel to `Mugirl_LanceChargeSpeed`) | Prefix consistency; drop the cross-type duplicate | yes (b) | yes |
| enum | `DunkState.Bounce` | remove | L6 | yes (d) | no |

### C.3 Scribe labels (optional; principled changes only)

* `CompMilkProduction`: `milkFullness`, `milkFullness_fullNotified`, `milkFullness_lastFullNotifyTick`, `milkFullness_pendingResourceTicks`, `milkFullness_MilkThreshold` → `milkFullness`, `milkFullNotified`, `milkLastFullNotifyTick`, `milkPendingTicks`, `milkAutoThreshold`. Keep a `milk` prefix, because these labels share the Pawn element with every other comp (vanilla gatherable comps use `fullness`). (c) with the Pawn as typed owner.
* DunkController: drop `followsCarrier` and `slamAnimating` after L10. DunkHead: `headShaderName` → `headShader` (ShaderDef, L11). `HediffComp_RequiresMemory`: drop `flagAmIThinking`, rename `checkingCounter` → `ticksSinceCheck`. `HediffComp_Lactation.lastBirthTick` becomes an int (L3).
* Everything else is already clean camelCase and unambiguous within its owner; keep it.

### C.4 Other production-quality restructuring

1. **Back-hair design**: the race carries two `CompProperties_LongCascadeBackHair` entries (`Mugirl_Race.xml:1024-1030`). That gives every pawn one comp plus one render node per configured hairstyle, with a magic default ("empty `southPath` means the LongCascade default", `Appearance/Hair/CompProperties_LongCascadeBackHair.cs:45-60`). Better: a `DefModExtension` on the `HairDef` (south and side paths) and **one** render node that reads `pawn.story.hairDef`. That costs nothing per pawn for hairs without back art.
2. **Milking device config**: `releaseThingDefName` and `filthDefName` are strings resolved through `GetNamedSilentFail` (`CompProperties_MilkingDevice.cs:36,40,78-106`). Make them `ThingDef` cross-references, so vanilla reports typos and the `dynamic-lookups.allow.txt:5` exemption disappears. Delete K1. Translation-key-or-literal fields go through `MugirlText.Resolve`; prefer real labels and DefInjected. **Possible duplicate sound (not verified in game):** the release plays `releaseSounds` (`Mugirl_Milking_Sound`, `Comp_MilkingDevice.cs:204`), and the triggered `ReleaseEffect` also plays `Mugirl_Milking_Sound` at tick 0 (`BondageApparel_Spacer.xml` ReleaseEffect sounds, `startTick 0 / endTick 0`).
3. **Dead or redundant code:** PostLoadInit cache resets on freshly constructed instances (`CompMooMilkable.cs:110-114`, `Thing_MugirlDunkHead.cs:213-223`); `JobDriver_GatherMilk.WorkTotal` has two equal constants (`:8-9`); after L6–L10, `DunkPresentationMath.FlightPosition` (`:55-67`) and the `Pawn` and other-thing payload paths of the dunk.
4. **Visibility and sealing:** Harmony classes are `public` in Milk and Genes but `internal` in AdvancedArmor and Appearance. Several leaf types are unsealed (`Thing_MugirlDunkProp`, `Thing_MugirlThrownObject`, `JobDriver_CastCharge`, `JobDriver_MugirlDunk`, `CompAbilityEffect_*`). `MugirlMilkingAnimation` and `MugirlNurtureUtility` expose public API that nothing outside the assembly needs (only `MugirlBabyFeedingUtility` is used by `Compatibility/LactationExpansion`).
5. **Per-game transient reset:** milk and animation statics are reset from the story GameComponent (`Incidents/Story/MugirlStoryState.cs:72-80`). Move this to a dedicated lifecycle hook (for example the upgrader or bootstrap's `FinalizeInit`). Also reset `ThrowControllerRegistry` there; its counter can drift upward across games (`Genes/Throw/ThrowControllerRegistry.cs:7-11`).
6. **`HediffComp_CheckJobOrRemove`** runs every tick (`CompPostTick`, `Genes/Charge/HediffComp_CheckJobOrRemove.cs:9-24`); `CompPostTickInterval` suffices, as the other comps in scope already do.

### C.5 Cross-module notes (for the other auditors)

* `Lances/Flight/LanceChargeImpactUtility.cs:112` → `JobDriver_CastCharge.DoJump` (K2). `Genes/Shared/MugirlJumpUtility.cs:54-68` depends on Mounting (`Comp_MugirlMount`). `Thing_MugirlDunkProp.Impact.cs:68` → Incidents' `MugirlPunisherDeathEffectUtility`. `Genes/Shout/CompAbilityEffect_ForceJob.cs:73` → Incidents' `MugirlWildSlaveUtility.IsPlayerFaction`. Fix the layering by moving the shared helpers to Core.
* `Compatibility/Savegames/NeuralShoulderSaveMigration.cs` belongs to the unified upgrader (G4).
* The validator baselines (F9) and ADR-004 must be rewritten to match the new policy.

## D. What the unified upgrader needs from this scope

### D.1 Checklist

1. **(a) type map** for every B.1 type renamed under C.2. That is 11 types; the 12th, the XenotypeFix comp, is deleted: strip its element or map it to an internal no-op sink. Source names are identical in 1.6 (≥0.4.7) and the pre-refactor 1.7 tree. XML-visible renames also need the `GenTypes.GetTypeInAnyAssembly` postfix only if old XML must keep loading, which does not apply to Defs shipped in the same build.
2. **(b) defName map** for the B.4 renames (JobDef, WorkGiverDef, HediffDef …). Use (e) removal, not (b), for `GeneDef Mugirl_Dunk` (G1).
3. **(c) label renames** on Pawn (milk comp), DunkController and DunkHead (C.3).
4. **(d)** `DunkState`: `Bounce` → pre-resolve (L6/L10).
5. **(e)** `lastBirthTick` float → int (L3). `headShaderName` normalization (L11). `milkFullness_lastResourceUpdateTick` → pending ticks, or drop (L1). Strip `Mugirl_Dunk` genes (G1). Strip the XenotypeFix comp element (L12). Neural shoulders (G4). Optionally `Class="PawnFlyer"` → the dunk flyer class (L8).
6. **(f)** Run the xenotype and endogene repair once (L12). Finalize legacy in-flight dunks (L10). Release or relink milked pawns stuck in `Job_MugirlMilkingTargetWait` (G2). Optionally clear Lovin variants from HAR `addonVariants` (G3, product decision).
7. **(g)** L4, L5, L13–L15, K1–K3, K6, K8.

### D.2 Markers that identify the source schema (no mod version exists in saves, F2)

| Marker in save XML | Implies |
|---|---|
| `meta/gameVersion` 1.6.x | 1.6-era (≥0.4.7 if `Mugirl` defs resolve; otherwise pre-0.4.7, F1) |
| `milkFullness_lastResourceUpdateTick` present | 1.6-era milk comp |
| `milkFullness_pendingResourceTicks` present / `Thought_Hediff` entry with `<def>` | pre-refactor 1.7 |
| XenotypeFix comp without `throwSystemGeneFixed` | pre-0.6.7 |
| `Thing_MugirlDunkProp` without `followsCarrier`, or with `Bounce` | pre-0.6.7 dunk (or the unreleased bounce build) |
| `GeneDef Mugirl_Dunk` | 0.5.0–0.6.6 |
