# Wave S plan — module `restraints` (Features/Restraints + Features/Roping)

Inputs: design.md (authoritative), understand/module-restraints-mount-weapons.md (report, IDs A1…A18, B, C), content.md,
real-saves.md/.json, engine-hooks.md. Rename spec: `spec/restraints.json` (88 types, 27 defs, 6 abstract Names, 28 Keyed keys).
Paths below use the **post-Wave-R** names unless a `file:line` cites today's tree (pre-Wave-R paths, verified 2026-10-15).
Abbreviations: `R/` = `Source/MugirlRace/Features/Restraints/`, `P/` = `Source/MugirlRace/Features/Roping/`, `D/` = `1.7/Defs/`.

Facts verified for this plan (decompiled rw17 = `RimWorld/Assembly-CSharp.dll` 1.7):
- `XmlInheritance.RecursiveNodeCopyOverwriteElements`: child `li` nodes are **appended**, never merged; `DirectXmlToObject.ValidateListNode`
  rejects list items not named `li`. ⇒ a `DefModExtension` cannot be partially inherited (two `li` = two extension instances).
- `DirectXmlToObject.ObjectFromXml` applies duplicate fields in document order (last wins) after logging "defines the same field twice".
- `Pawn_ApparelTracker.Lock` is a pure `lockedApparel.Add` (no side effects); `ExposeData` saves `lockedApparel` as `LookMode.Reference`.
- `RimWorld.ThinkNode_ConditionalRoped` / `Verse.AI.JobGiver_WanderRoped` are logically identical to our copies (only ctor values
  `wanderRadius 6 / 125~200` vs ours `4 / 500~800`; `JobGiver_Wander.DeepCopy` copies both fields, so XML values survive).
- `ThingFilter.ExposeData` saves ThingCategoryDef only through `overrideRootDef`.
- Every Mugirl-era build (git 324501c … 50f7979) has byte-identical Scribe calls/defaults for SlaveApparel, AdvancedSlaveApparel,
  Comp_BrainwashHelmet, Comp_MagneticShackles (incl. dead `tickCounter`), Comp_ShockCollar, the delayed-trigger hediff comps
  (`age`/`triggered`) — `git grep` per release tag. `needkeynumber` values unchanged since `2a4ad2f` ("3.0", pre-0.4.7)
  (`git log -G'<needkeynumber>'`). Pre-3.3 (`d42b58f`) MooGirl builds had `isLocked` default **false** and no `lockCount` default:
  MooGirl era is name-level only (design §1), so those defaults are not reproduced.

---------------------------------------------------------------------------------------------------------------------
## S1. Legacy code removal (every legacy site of the module)

Step ids: DOM steps live in `SaveUpgrade/Steps/Restraints/`, post-load steps in `SaveUpgrade/PostLoad/Restraints/`.
"Map" = generated LegacyTypes/LegacyDefs from the spec; "Label" = declarative owner-scoped LegacyLabels entry.

| # | Legacy site (today's file:line) | Legacy XML shape (evidence) | Upgrade mechanism | Domain code deleted |
|---|---|---|---|---|
| L1 (A1) | `R/HediffComps/HediffCompScribe.cs:3-24` (whole); `HediffComp_DelayedTrigger.cs:5,14-16,41-46`; per-comp label props `HediffCompProperties_AddMentalState.cs:23-25`, `…AddThoughts.cs:26-28`, `…AddTrait.cs:34-36`, `…ReduceWillorEnslave.cs:26-28`; `…SuppressionEnhancer.cs:51-55` | Hediff node of `Mugirl_BrainWashing_Level1/3/5` (renamed `Mugirl_Brainwashing_LevelN` by the def map) with shared direct children written by up to 4 comps: `<li Class="HediffWithComps"><def>Mugirl_BrainWashing_Level1</def>…<ticksToDisappear>…</ticksToDisappear><age>4210</age><triggered>True</triggered><performancePlayer><age>…</age>…</performancePlayer></li>`. Defaults are omitted (age 0, triggered false). No real save contains them (real-saves.md §4 last bullet, §6 "1.6-only labels absent") → synthetic golden fixture built from the 1.6 writers (`1.6/Source/Features/Restraints/HediffCompProperties_AddThoughts.cs:63-67` etc.). | DOM structural step **`Restraints.BrainwashTriggerLabels`** (owner selector HediffWithComp(any of HediffComp_AddMentalState/AddThought/AddTrait/ReduceWillOrEnslave/SuppressionEnhancer)). Reproduce exact 1.6 load semantics (every comp read the FIRST same-named node): `age0` = text of first direct-child `<age>` (absent → none), `trig0` = first direct-child `<triggered>`; for each of those comps in the (renamed) HediffDef's `comps`: write `<{prefix}Age>age0</…>` if age0 ≠ "0", and for delayed-trigger comps `<{prefix}Triggered>trig0</…>` if trig0 = True; then remove ALL direct-child `<age>`/`<triggered>`. Never touch nested `<brainwashPerformance>/<age>`. Idempotent (no direct `age`/`triggered` → no-op). Runs after the `performancePlayer` label rename (L9). Pre-refactor 1.7 interim labels `mugirl*Age/*Triggered` are NOT handled (design §1: never released). | Delete `HediffCompScribe.cs`; `HediffComp_DelayedTrigger.CompExposeData` becomes two plain `Scribe_Values.Look` with the S3 labels; remove `"age"`/`"triggered"` literals and the "1.6 存档" comments. |
| L2 (A2) | `R/Apparel/AdvancedSlaveApparel.cs:45-49` (PostLoadInit `isCracked && isLocked` → ClearLockState) | Cracked advanced restraint lying on the ground from 1.6: `<thing Class="Mugirl.AdvancedSlaveApparel"><def>Mugirl_ShockCollar</def>…<isCracked>True</isCracked></thing>` with `isLocked` absent (= true) and `lockCount` absent (= keyCount) or > 0. Real S6 sample is uncracked: `isLocked=False; lockCount=0; ticks=789` (real-saves.md §3 leaf values). | DOM structural step **`Restraints.LockState`** (owner ClassIs(RestraintApparel incl. AdvancedRestraintApparel, after type map; any element name: map `thing`, `wornApparel/innerList/li`, inventory/corpse/caravan/transporter/mount containers). Algorithm per node: `cracked = isCracked=="True"`; `locked = isLocked != "False"`; `count = lockCount ?? keyCount(def)` (def resolved through DefDatabase after the def map; keyCount from RestraintExtension, values identical to every legacy build); if `cracked` → locked=false, count=0 (A2); else if `locked && count<=0` → count=keyCount (A3); write `<lockCount>count</lockCount>` ALWAYS (new forceSave schema); then per S3 either write `<isLocked>False</isLocked>` when !locked (if S2.6b declined) or drop `<isLocked>` (S2.6b adopted, lock state = lockCount>0). Unknown def → leave node untouched, report. | Delete the two PostLoadInit blocks (`AdvancedSlaveApparel.cs:45-49`, `SlaveApparel.cs:51-54`) and `InitialLockCount` as a Scribe default. |
| L3 (A3) | `R/Apparel/SlaveApparel.cs:51-54` (`isLocked && lockCount<=0` repair, since 3.3/d42b58f) | `<li Class="Mugirl.SlaveApparel"><def>Mugirl_Handcuffs</def>…<lockCount>0</lockCount></li>` with isLocked absent. Real saves: worn restraints carry neither label ("absent on all 15 worn", ground ones `isLocked=False; lockCount=0`; real-saves.md §3, §8 "lockCount defaults to the def's needkeynumber"). | Same DOM step `Restraints.LockState` (A3 branch) — plus **materialisation of the old Def-dependent default** of `lockCount` (design §3.4 forceSave rule). | as L2 |
| L4 (A4) | `R/Patches/ApparelTrackerPatches.cs:20-31` (`Pawn_ApparelTracker_ExposeData_SlaveApparel_Patch`, runs on every pawn at every load); call into `SlaveApparelExtensions.cs:89-107` | Saves before b3074b3 (first shipped in 0.5.0): worn locked restraint missing from vanilla `lockedApparel`: `<apparel><wornApparel><innerList><li Class="Mugirl.SlaveApparel">…<id>Mugirl_Handcuffs27429</id>…</li></innerList></wornApparel><lockedApparel IsNull="True" /></apparel>` or a `lockedApparel` list lacking the ref. Real saves contain 15 `li<lockedApparel` refs (real-saves.md §5 context table). | DOM structural step **`Restraints.WornLockList`** (after `Restraints.LockState` and thing-ID rewriting; owner PathFrom(pawn, `apparel`)): for every `wornApparel/innerList/li` of a restraint class whose final state is effectively locked (lockCount>0 and not cracked), ensure `<lockedApparel>` (create it; drop `IsNull="True"`) contains `<li>Thing_<id></li>` (exact `<id>` text). Equivalent to `Lock()` (pure list add, verified). No post-load step needed. | Delete the patch class (temporary Wave R name `Harmony_Pawn_ApparelTracker_ExposeData`) and its patch-targets entry. `EnsureWornSlaveApparelLocks` stays only for the PawnGenerator fallback (runtime invariant, not legacy). |
| L5 (A5) | Comments `R/Comps/CompProperties_MagneticShackles.cs:245`, `R/HediffComps/HediffCompProperties_EleShock.cs:110` | Write-only labels of every Mugirl-era build: `<tickCounter>` on the magnetic-shackles apparel node; `<currentTickCount>`, `<remainingCycles>` on the electric-shock hediff node. Absent from all real saves (real-saves.md §6). | **Label Drop**: ThingWithComp(CompMagneticShackles) `tickCounter`→Drop; HediffWithComp(HediffComp_ElectricShock) `currentTickCount`,`remainingCycles`→Drop. | Delete both comments; remove the labels from `Baselines/dropped-scribe-labels.txt` (superseded by C.save-upgrade). |
| L6 (A6) | `R/Apparel/SlaveApparelDef.cs:4,7-20` (soul_type, gives_*, blocks_* except hands); `CompProperties_MagneticShackles.cs:24-26` (labelWhenActive/Inactive); `HediffCompProperties_EleShock.cs:20-22` (effectCycleInterval/maxCycles); `R/Apparel/AdultContentControl.cs:3-21` (3 types) | Def data only; never in saves (comps without state, extension). | None (not save-visible). Corpus disposition `Dropped(not save-visible)` for the 3 adult-content types. | Delete the fields, the 3 types and every XML use: `BondageApparel_Base.xml:30,38-42`, `BondageApparel_Spacer.xml:116,118,148-152,198,254-258`, `Hediffs_AdvancedRestraints.xml:57-58,111-112,175-176`, `gives_*`/`blocks_oral` lines in Head/OnSkin/Shell/Spacer; zh DefInjected `ThingDef/BondageApparel_Spacer.xml:24,26`; Keyed `Mugirl.Restraints.MagneticShackles.LabelActive/LabelInactive` (Misc_Gameplay.xml:21-22, both languages). Baseline `empty-types.allow.txt` entries. |
| L7 (A7) | `R/Jobs/JobDriver_UseItemOn.cs:6-7` (whole), `R/Comps/CompGetSlaveApparelGear.cs:3-4` (whole), JobDef `GiveSlaveApparelGear` `D/JobDefs/JobDefs.xml:20-25`, zh `DefInjected/JobDef/JobDefs.xml:5-6` | Would be `<curJob><def>GiveSlaveApparelGear</def>` + `<curDriver Class="Mugirl.JobDriver_UseItemOn">`; never issued by any build (`git log -S GiveSlaveApparelGear -- 1.6/Source` empty; no issuer in 1.7) and absent from real saves (real-saves.md §2). MooGirl era: `GiveSlaveApperalGear` (content.md A.7). | **Type map override** `Mugirl.JobDriver_UseItemOn` (+`MooGirl.` alias) → Drop(never issued); **Def map** JobDef `GiveSlaveApparelGear`, `GiveSlaveApperalGear` → Drop(never issued; if ever met, vanilla "Cleaning up invalid job state" is acceptable). | Delete both types, the JobDef, the `CompProperties_UseEffect` li in `BondageApparel_Base.xml:31-33` and the translation. |
| L8 (A8) | `P/MapRopingIndex.cs:6` + whole type; `RopingService.cs:117-154` (uses it) | `<maps><li>…<components><li Class="Mugirl.MapRopingIndex" /></components>` — present in S5–S11 (S9 ×7 maps), always empty (real-saves.md §1 table + bullet "written as empty elements", §2). | **Type map override** `Mugirl.MapRopingIndex` (+`MooGirl.MapRopingIndex`) → Drop(remove the `components/li`). Without it every map would log 2 red errors (engine-hooks.md §2 failure table). | Delete the MapComponent; pending-hitch state moves to a static set (S2.4). |
| L9 (A9) | Names frozen only because saves hold them: `R/Performance/GameComponent_BrainwashPerformance.cs:3,93`; `R/Apparel/SlaveApparel.cs:7`; `R/Performance/BrainwashPerformancePlayer.cs:7`; `P/MapRopingIndex.cs:6`; `P/Jobs/JobDriver_FollowRoper.cs:5`, `JobDriver_RemoveRopeMoo.cs:5`, `JobDriver_RopeMoo.cs:5`, `JobDriver_RopeToWallRopeHitch.cs:6` | `Class=` values listed in the spec (saveVisible=true): restraint apparel things, GameComponent, 7 JobDrivers; JobDef/ThingDef/HediffDef/TraitDef names in `<def>`, thing IDs `Mugirl_Slaveboots49288` etc. (real-saves.json defnodes), 45 `WallRopeHitch` allowedDefs refs. | **Type map + Def map** (generated from the spec) + thing-ID rewrite (design §5.4) for WallRopeHitch and the 4 Slave* casing renames incl. `Blueprint_`/`Blueprint_Install_`/`Frame_`/`Make_`. **Label renames** (S2.8): GameComponent `activeBrainwashPerformances`→`activePerformances`; hediff `performancePlayer`→`brainwashPerformance`; comp labels per S3. | Delete every "存档标签与 1.6 相同 / 类名不可改 / 类名作为 curDriver 写入存档" comment. |
| L10 (A10) | Names frozen for XML/DefInjected only: `CompProperties_BrainwashHelmet.cs:44`, `CompProperties_MagneticShackles.cs:86`, `CompProperties_ShockCollar.cs:46`, `CompStampedApparelKey.cs:7`, `HediffComp_BrainWashingStar.cs:3`, `HediffCompProperties_PerformanceEffect.cs:4`, `ApparelTrackerPatches.cs:5`, `Harmony_PawnGenerator_Patch.cs:5-6` | none (XML/DefInjected handles, patch order) | Wave R renames XML + DefInjected handles (spec notes); patch order made explicit (Wave A4: `[HarmonyPriority(Priority.Last)]` on the GeneratePawn lock fallback). | Delete the comments. |
| L11 (A11) | `R/Jobs/JobDriver_CrackBondageGear.cs:39-41`, `JobDriver_UnlockSlaveApparelGear.cs:43-45` (comments about the 1.6 toil trimming) | In-flight 1.6 job with old toil layout: `<jobs><curJob><def>UnlockSlaveApparel</def>…</curJob><curDriver Class="Mugirl.JobDriver_UnlockSlaveApparelGear"><curToilIndex>2</curToilIndex>…</curDriver>` (also `CrackBondageGear`). No real sample (real-saves.md §2). All published builds used the 1.6 layout (`1.6/Source/Features/Restraints/JobDriver_UnlockSlaveApparelGear.cs:83-131`, `…CrackBondageGear.cs:56-85`). ForceUnlock/Rope/Unrope/RopeToHitch/FollowRoper layouts are identical (report A11) → no step. | **Post-load phase B step `Restraints.EndLegacyRestraintJobs`** (gated: source schema 0; phase B because map pawns are only registered after spawning, engine-hooks.md §10(v)/§1.5): for every spawned pawn on every map with `jobs.curJob?.def` ∈ {`Mugirl_UnlockRestraint`, `Mugirl_CrackRestraint`} → `jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false)` (releases reservations through the normal cleanup). Queued jobs need nothing (no toil state). Count in the report. | Rewrite the two comments to describe the fixed-layout invariant only. |
| L12 (A14) | `P/Jobs/JobDriver_FollowRoper.cs:12` ("兼容旧存档…") | — | none (FailOn is a runtime invariant) | Comment rewrite only. |
| L13 (A15) | `P/RopeTrackerAccess.cs:13-23` (fallback to `BreakAllRopes`, described as 1.6 behaviour) | — | none | Bind once at startup; on failure log one `MugirlLog` startup error (reflection target missing in this RimWorld build) and keep the degraded `BreakAllRopes` path without any "1.6" wording. |
| L14 (A16) | 71 comment lines in 41 files under `R/`+`P/` mention 1.6/旧档/冻结 (`grep -nE '1\.6\|旧档\|旧存档\|存档标签\|不可改\|不能改\|沿用'`), e.g. `UnityMathf` parity notes `HediffCompProperties_EleShock.cs:208`, `…ReduceWillorEnslave.cs:71`, `…SuppressionEnhancer.cs:75`, `ForceUnlockRestraintUtility.cs:14,98`, `RestraintPawnType.cs:17`, `RopingService.cs:7-10`, `RopeGraphics.cs:5`, `Patch_RopingTick.cs:7`, `MapRopingIndex.cs:4-5,38` | — | none | Rewrite as current-reason comments (design §3.5); parity history goes to docs in Wave D. Test `Source/Tests/MugirlRace.Tests/Roping/WallRopeHitchGeometryTests.cs:6,21` renames its "1.6 anchor" wording. |
| L15 (A18) | `R/Apparel/SlaveApparelExtensions.cs:35-39` (`IsUnlockAdvancedApparel`, "名称沿用 1.6") | — | none | Member rename `IsCrackedAdvancedRestraint` (Wave S member rename). |
| L16 | `R/Comps/CompProperties_BrainwashHelmet.cs:182-185` and `CompProperties_MagneticShackles.cs:251-258` (PostLoadInit re-initialisation/clamp) | Every Mugirl-era build wrote these labels with the same defaults (git check above) → no legacy shape needs them. | none | Replace by lazy initialisation inside the tick (domain invariant: `ticksToChange<=0` → roll; `nextStateTick<0` → schedule), delete the PostLoadInit branches. |
| L17 | `R/Performance/HediffCompProperties_PerformanceEffect.cs:206-232` (`ClampTextColors`, "结果与 1.6 一致") | XML colours `(239, 70, 274)` ×39 in `D/HediffDefs/Hediffs_AdvancedRestraints.xml` (content.md E.5) | none | Fix XML to `(239, 70, 255)` (clamped result is already exactly 1.0) and delete `ClampTextColors`. |
| L18 | `R/Performance/GameComponent_BrainwashPerformance.cs:8-10` (unused parameterless ctor), `:56-59`/`:136-140` and `BrainwashPerformancePlayer.cs:136-143` (`??=` after load) | — | none | Delete the ctor. The `??=` guards cover `IsNull` lists, which current code never writes; delete them (fields are initialised and never set to null). |

Generated/override table entries owned by this module (hand part of LegacyNames, see S5-Q7):
- LegacyTypes overrides: `Mugirl.BrainWashSlaveApparel` → `Mugirl.Features.Restraints.AdvancedRestraintApparel` (if S2.7 adopted); `Mugirl.JobDriver_UseItemOn` → Drop; `Mugirl.MapRopingIndex` → Drop; MooGirl aliases (spec notes).
- LegacyDefs Drop: JobDef `GiveSlaveApparelGear`/`GiveSlaveApperalGear`; MentalStateDef `Mugirl_BrainWashing`; ThoughtDef `Mugirl_BrainWashing_End`; HediffDef `Mugirl_FullHeadCovering` (all three never generated by any build: grep shows no reference outside their own definition; S2.2). SoundDef `Mugirl_EleShock_Sound` is not save-visible.
- LegacyLabels: see S3 "from" column.
- LegacyEnums: none (report B3).

---------------------------------------------------------------------------------------------------------------------
## S2. Structural refactors

### S2.1 MUST — `SlaveApparelDef` (ThingDef subclass) → `RestraintExtension` (DefModExtension)
- Files: delete `R/Apparel/RestraintApparelDef.cs` (Wave R temp name); new `R/Apparel/RestraintExtension.cs`; `RestraintApparel.cs`,
  `RestraintApparelUtility.cs`, `ForceUnlockRestraintUtility.cs`, `JobDriver_UnlockRestraint.cs`; XML `Apparel_Bondage/{Base,Head,OnSkin,Shell,Spacer}.xml`.
- Design: `public sealed class RestraintExtension : DefModExtension` with camelCase fields that have readers only:
  `HediffDef? equippedHediff`, `List<BodyPartDef>? equippedHediffParts` (was HediffTargetBodyPartDefs), `List<BodyPartGroupDef>? boundBodyPartGroups`,
  `bool blocksHands`, `int keyCount = 2` (was needkeynumber, same default), `ThingDef? keyDef` (null = any key fits, unchanged semantics),
  `int unlockTicks = 100` (was unlockTick; no XML sets it today), `List<ThingDef>? nextStageDefs` (was NextSlaveApparelDefs),
  `bool producedCracked` (S2.10). `ConfigErrors`: keyCount ≥ 1; equippedHediff ⇒ equippedHediffParts non-empty. Accessor
  `RestraintApparel.Extension` (cached per instance, `def.GetModExtension<RestraintExtension>()`).
- XML: the 25 root elements become `<ThingDef>`; because extensions cannot be inherited partially (verified above), **only the 14 concrete
  defs** carry one complete `<modExtensions><li Class="Mugirl.Features.Restraints.RestraintExtension">`; abstract parents keep only vanilla
  ThingDef data. Resolved values to write (computed from today's XML inheritance, `spec/resolve_restraints.py`):

| def (new name) | keyCount | keyDef | equippedHediff | parts | boundGroups | blocksHands | nextStageDefs |
|---|---|---|---|---|---|---|---|
| Mugirl_Blindfold | 1 | Key_Medieval | Mugirl_Blindfold | Eye | Eyes | – | – |
| Mugirl_Gagstick | 1 | Key_Medieval | Mugirl_Gagstick | Jaw | HeadAttackTool, Teeth, Beak, Mouth | – | – |
| Mugirl_Earplugs | 1 | Key_Medieval | Mugirl_Earplugs | Ear | Ears | – | – |
| Mugirl_SlaveGauntlet | 2 | Key_Industrial | Mugirl_Gauntlet | Arm | LeftHand, RightHand | – | – |
| Mugirl_SlaveStockings | 2 | Key_Industrial | Mugirl_Stockings | Leg | Feet | – | – |
| Mugirl_Boxbinder / Handcuffs / Armbinder / Straitjacket | 2 | Key_Medieval | same-name hediff | Arm | Arms, LeftHand, RightHand | true | Mugirl_SlaveGauntlet |
| Mugirl_SlaveBoots / SlaveShackles | 2 | Key_Medieval | Mugirl_Legbinder | Leg | Legs, Feet | – | Mugirl_SlaveStockings |
| Mugirl_MagneticShackles | 1 | Key_Spacer | – | – | – | – | – |
| Mugirl_ShockCollar | 1 | Key_Spacer | – | – | – | – | – (+ producedCracked true, S2.10) |
| Mugirl_BrainwashHelmet | 1 | Key_Spacer | – | – | HeadAttackTool | – | – |
  (`Key_X` = `Mugirl_SlaveApparelKey_X`, public API, unchanged.)
- Save impact: none (defs are referenced by defName only). Behaviour: none. Interop fix: third-party `Defs/ThingDef[...]` XPath patches now
  match restraints (report C3).
- Upgrade steps: none. Dependents: the 1.6-only Subspecies mod (12 `<Mugirl.SlaveApparelDef>` elements, field `keytype`) must port — record
  in the migration notes (Wave D).
- Tests: validator content contract "every ThingDef whose thingClass is RestraintApparel or a subclass has exactly one RestraintExtension;
  no abstract def declares one"; unit/validation test comparing the resolved extension values of the 14 defs with the snapshot
  `DevData/Backups/PreRefactor-1.7-20261015` resolved `SlaveApparelDef` values (table above). Risk: **med** (14 hand-edited XML blocks).

### S2.2 MUST — delete dead code and dead content
- Types: `AdultContentExtension`, `CompProperties_AdultContentControl`, `CompAdultContentControl`, `CompGetSlaveApparelGear`,
  `JobDriver_UseItemOn`, `Harmony_Pawn_ApparelTracker_ExposeData`, `PawnSlaveStatusUtility` (merged), `HediffCompScribe`.
- Defs (with their DefInjected): JobDef `GiveSlaveApparelGear` (`JobDefs.xml:20-25`); MentalStateDef `Mugirl_BrainWashing` + Name
  `Mugirl_BrainWashingBase` + history comment (`Hediffs_AdvancedRestraints.xml:746-778`, zh `MentalStateDef/Hediffs_AdvancedRestraints.xml`
  → file deleted) — the brainwash flow uses vanilla `Berserk` (`:715-718`); ThoughtDef `Mugirl_BrainWashing_End` (only referenced by that
  mental state, `Thoughts_Traits.xml:70-83`, S4); HediffDef `Mugirl_FullHeadCovering` (`Hediffs_Bondage.xml:258-284`, no
  `equippedHediff` points to it); SoundDef `Mugirl_EleShock_Sound` (`Bondage_Sounds.xml:5-19`, unreferenced).
- Keyed (both languages): `Mugirl.CrackBondageGear_NoCrackableType`, `Mugirl.RopedByPawn`, `Mugirl.RopedToHitchingPost`,
  `Mugirl.RopedToSpot` (content.md A.6.2), `Mugirl.Restraints.MagneticShackles.LabelActive/LabelInactive`.
- Save impact: Drop entries listed in S1. Behaviour: none. Tests: corpus test proves the dropped defs never occur; build + validator.
  Risk: low.

### S2.3 MUST — brainwash delayed-trigger hediff comps: final labels, no fallback
- Files: `R/HediffComps/HediffComp_DelayedTrigger.cs` (+5 subclasses), delete `HediffCompScribe.cs`.
- Design: `HediffComp_DelayedTrigger` keeps `age`/`triggered` fields; abstract `ScribePrefix` (`addMentalState`, `addThought`, `addTrait`,
  `reduceWillOrEnslave`); labels `<prefix>Age`, `<prefix>Triggered` via plain `Scribe_Values.Look(ref age, ScribePrefix + "Age", 0)`.
  `HediffComp_SuppressionEnhancer`: `suppressionEnhancerAge`.
- Save schema: old `age`/`triggered` (1.6 shape) → per-comp labels (S1-L1 DOM step). Behaviour: none (fixes the 1.6 cross-comp overwrite
  only for new saves; legacy saves keep 1.6 semantics by design).
- Tests (golden XML, `Source/Tests/MugirlRace.Tests/SaveUpgrade/Restraints/`): Level1 node with `<age>`+`<triggered>True` → 3 comps get
  values, old nodes gone; Level5 node; node with only `<age>`; node whose `<brainwashPerformance><age>` must stay; idempotence; node of
  another def untouched. Risk: low.

### S2.4 MUST — remove `MapRopingIndex`
- Files: delete `P/MapRopingIndex.cs`; `P/RopingService.cs` gets `private static readonly HashSet<Pawn> PendingHitch` (+ `StaticCacheLifecycle:`
  comment) and registers a reset with the lifecycle hub (design §5.8: StartedNewGame/LoadedGame). `IsPendingSpotRope` validates
  `Spawned && !Destroyed` and self-cleans as today; the per-map overload `ClearPendingSpotRope(Pawn, Map)` collapses to one method.
- Behaviour: none — pending state exists only between two instant toils of the same tick (`JobDriver_RopeToWallRopeHitch.cs:47-86`) plus the
  finish action; fixes the unreset `totalPending` counter across games (`MapRopingIndex.cs:9-11`, harmless today).
- Save: type Drop (S1-L8). Tests: unit tests for mark/clear/despawned cleanup/reset; golden DOM test removing the component li from a
  trimmed S9 map node. Risk: low.

### S2.5 MUST — use vanilla `ThinkNode_ConditionalRoped` / `JobGiver_WanderRoped`
- Delete `P/AI/ThinkNode_ConditionalRoped.cs`, `P/AI/JobGiver_WanderRoped.cs`; XML in `D/ThinkTreeDefs/Mugirl_ThinkTreeDefs.xml` (S4):
  `:10` → `<li Class="ThinkNode_ConditionalRoped">`; `:38` → `<li Class="JobGiver_WanderRoped"><wanderRadius>4</wanderRadius><ticksBetweenWandersRange>500~800</ticksBetweenWandersRange></li>`.
- Behaviour: identical (decompiled bodies; the `pawn?.`/`roping?.` null checks are unreachable inside the roped branch). Save: none
  (ThinkTree not saved). Tests: in-game smoke (roped Mugirl wanders at the hitch); validator content contract for the class names. Risk: low.

### S2.6 MUST(a) / SHOULD(b) — restraint lock state
- (a) MUST: `lockCount` saved with `forceSave: true` (default 0 = initializer); PostLoadInit repairs and the ExposeData Harmony patch go (S1-L2..L4).
- (b) SHOULD: remove the redundant `isLocked` field: lock state is `lockCount > 0` (invariant already maintained by `UseKey`,
  `TryReleaseAndDrop` rollback ≥1, `DevUnlock`, `ClearLockState`; verified `SlaveApparel.cs:143-208`). `AdvancedRestraintApparel.IsEffectivelyLocked`
  = `lockCount > 0 && !isCracked` (crack sets 0 anyway). Read-only API for other features: `IsLocked`, `LockCount`, `RequiredKey` (S2.15).
- Save: (a) `lockCount` always written; (b) `isLocked` label dropped after `Restraints.LockState` folds it into lockCount.
- Behaviour: none. Tests: golden DOM tests for worn-without-labels (→ lockCount=keyCount), ground `isLocked False/lockCount 0` (→ 0),
  cracked-ground-locked (→ 0), `isLocked`+`lockCount 0` (→ keyCount), unknown def (untouched), `lockedApparel IsNull` creation, idempotence;
  in-game: load the S6 fixture, check worn restraints still block removal and ground ones are unlocked. Risk: med.

### S2.7 SHOULD — drop the empty `BrainwashHelmetApparel` subclass
- Identify the helmet by `CompBrainwashHelmet` (`RestraintApparelUtility.IsWearingBrainwashHelmet(cracked)`, crack message key choice in
  `CompTargetEffect_CrackRestraint`); XML `thingClass` of `Mugirl_SlaveApparel_Brainwash` → AdvancedRestraintApparel (or simply inherited
  from the Spacer parent).
- Save: type map override (S1 table). Behaviour: none. Tests: DOM type-map test; unit test of the predicate. Risk: low.

### S2.8 MUST — comp/owner label hygiene (design §3.4 shared-element rule)
- Apparel thing nodes and hediff nodes are shared with vanilla and other comps; give every comp label its comp prefix and name labels after
  fields. GameComponent label loses the redundant owner prefix. Full old→new map in S3 ("from" column). Fields renamed to match
  (e.g. `currentTicksToChange` → `ticksToChange`, `isActive` → `active`, `performancePlayer` → `brainwashPerformance`).
- Default change: `CompShockCollar` `lastManualUseTick` default −99999 → −999999 (unified with the other two comps). No materialisation
  needed: both values mean "cooldown long over" for every reachable game tick (cooldowns ≤ 600 ticks, TicksGame ≥ 0).
- Upgrade: Label entries only. Tests: one golden test per owner selector (ThingWithComp ×3, HediffWithComp ×2, GameComponent ×1). Risk: low.

### S2.9 SHOULD — remove transient counters from saves (design §3.4)
- `CompMagneticShackles`: drop `currentCycleInterval`/`currentBindDurationTicks`; roll the duration when scheduling `nextStateTick`
  (same distribution; only RNG call order differs). Labels → Drop.
- `CompShockCollar`: replace the per-tick countdown `ticks` with `CompTickInterval` + `parent.IsHashIntervalTick(checkIntervalTicks, delta)`;
  label `ticks` → Drop. Behaviour: identical chance per interval; **phase** of the check becomes hash-aligned (see S5-Q3).
- `CompBrainwashHelmet`: keep both counters (blocking resets progress, an absolute tick cannot express it) — lazy init only (S1-L16).
- `HediffComp_SuppressionEnhancer`: keep `age` (cycle runs only on CompPostTick; hash would change phase) — no change.
- Tests: unit tests for scheduling; in-game shackles cycle smoke. Risk: low.

### S2.10 SHOULD — XML contract hygiene (C6)
- `HediffCompProperties_AddTrait.PawnTypeTraitEntry.pawnType`: `string` → `RestraintPawnType` enum field (no `None`-parsing at runtime);
  XML `Hediffs_AdvancedRestraints.xml:723,727` `colonist`/`slave` → `Colonist`/`Slave`; delete `RestraintPawnTypeParser` and
  `Tests/…/RestraintPawnTypeParserTests.cs`; ConfigErrors for `None`.
- `CompProperties_MagneticShackles.activateSoundDefName` (string, manual lookup `:35-75`) → `SoundDef? activateSound` (XML `:129`); delete
  the ResolveReferences/ConfigErrors code (loader validates).
- `CompProperties_ShockCollar`: `powerhediffDefs` → `powerHediffDefs`, `rand` → `shockChancePercent`, `ticks` → `checkIntervalTicks`
  (XML `BondageApparel_Spacer.xml:413-437`).
- `AdvancedRestraintApparel.Notify_RecipeProduced` hard-codes `def == MugirlContentDefOf.Mugirl_ShockCollar` (`AdvancedSlaveApparel.cs:33`)
  → `Extension.producedCracked` (true only on Mugirl_ShockCollar). Behaviour: none.
- Tests: validator XML schema (unknown-field) run; unit test for enum mapping. Risk: low.

### S2.11 MUST — feature DefOfs
- New `R/RestraintsDefOf.cs` (internal, `[DefOf]`, `EnsureInitializedInCtor`): ThingDef `Mugirl_SlaveApparelKey_Medieval`, `…_Industrial`;
  JobDef `Mugirl_ForceUnlockRestraint`; TraitDef `Mugirl_BrainwashObey` (+ `Mugirl_ShockCollar` only if S2.10's flag is declined).
  New `P/RopingDefOf.cs`: ThingDef `Mugirl_WallRopeHitch`; JobDef `Mugirl_Rope`, `Mugirl_Unrope`, `Mugirl_RopeToHitch`, `Mugirl_FollowRoper`.
  No field for `Mugirl_Fleck_BrainwashEyes` (no C# reader; existence belongs to the validator).
- Old fields leave `Mugirl_DefOf`/`MugirlContentDefOf` (S4). Save: none. Risk: low (cross-feature callers in S4).

### S2.12 MUST — patches
- `[MugirlPatch]` module from the closed set (`Restraints`, `Roping`); `Harmony_PawnGenerator_GeneratePawn_LockRestraints` keeps
  `[HarmonyPriority(Priority.Last)]` (A4) and its `MugirlPatch` failure text without "slave apparel"; remove the ExposeData patch;
  `internal static` classes; patch-targets/transpiler baselines regenerated. Behaviour: none. Risk: low.

### S2.13 MUST — visibility and file layout
- `internal` by default; `public` only for reflection-instantiated types (Apparel subclasses, comps/props, hediff comps/props, JobDrivers,
  JobGiver, ThoughtWorkers, GameComponent, RestraintExtension, FloatMenuOptionProviders); `sealed` on leaves; `CompAdvancedRestraint`
  `abstract`; `RopingService`, `RestraintApparelUtility`, `RopingToils` internal; one top-level type per file (Wave R split).

### S2.14 SHOULD — content files
- Split the 7 restraint/rope JobDefs out of the shared `D/JobDefs/JobDefs.xml` into `JobDefs_Restraints.xml` (Mugirl_UnlockRestraint,
  Mugirl_ForceUnlockRestraint, Mugirl_CrackRestraint) and `JobDefs_Roping.xml` (Mugirl_Rope, Mugirl_Unrope, Mugirl_RopeToHitch,
  Mugirl_FollowRoper) with matching zh DefInjected files (S5-Q1).
- Move `Mugirl_MilkingDevice`/`Mugirl_MilkingDeviceHidden` (`BondageApparel_Spacer.xml:134-351`, plus their zh DefInjected entries incl.
  `comps.Comp_MilkingDevice` keys) and the milk SoundDefs `Mugirl_Milking_Sound`/`_Power_Sound` (`Bondage_Sounds.xml:53-84`) to milk-owned
  files (S5-Q2).
- `Building_Rope.xml`: remove duplicate `<uiOrder>2800</uiOrder>` (`:11`), keep the effective `2991` (`:37`; last field wins, verified).
- zh translation defect: `RopeToBuild.reportString` "解锁到建筑" (rope job, not an unlock) → "系到建筑" (verified defect fix).
- Keyed: `Keyed/Restraints.xml`, `Keyed/Roping.xml` (both languages) receive all `Mugirl.Restraints.*`/`Mugirl.Roping.*` keys (S4).

### S2.15 SHOULD — explicit API for other features
- `RestraintApparel` exposes `IsLocked`, `LockCount`, `RequiredKey` (ThingDef?) and keeps fields private; Incidents' courier reads these
  (S4). `FloatMenuOptionProvider_RopeMugirl` stops calling `MountedPawnUtility.HasCapacity` (`FloatMenuProvider_RopeMoo.cs:31`, an alias
  Mounting deletes) and checks `health.capacities.CapableOf(Moving)` itself. `RopingService` keeps its public member names.

### S2.16 SHOULD — hygiene
- `PawnSlaveStatusUtility.DisplayName` → private method of `KeyUnlockFloatMenu` (`pawn.LabelShortCap`; no untranslated "null"/"noname").
- `AdvancedRestraintApparel.IsCracked()` → property `IsCracked`; `IsUnlockAdvancedApparel` → `IsCrackedAdvancedRestraint`;
  `IsAdvancedApparel` → `IsAdvancedRestraint`; `SatisfiesKey`, `IsLockedSlaveApparel` → Restraint wording.
- `CompRopeToBuild.CompFloatMenuOptions` uses vanilla `"Reserved"`; module keeps `Mugirl.Reserved` elsewhere — unify on vanilla `Reserved`
  and `NoPath` where the text is identical (optional, low).
- Comments in Chinese, current reason only (S1-L14).

### S2.17 SHOULD — dead duplicate category (other owner)
- ThingCategoryDef `Mugirl_SlaveApparel` (`D/Apparel/Apparel_Category.xml:31-36`, label "Mugirl Slave Apparel", zero users) is a dead duplicate
  of the restraint category (renamed `Mugirl_Restraints` in Wave R) — delete (S4).

---------------------------------------------------------------------------------------------------------------------
## S3. NEW save schema of the module after Wave S

Assumes all MUST + recommended SHOULD items. "from" = legacy label consumed by the upgrader (L = label rename/drop, D = DOM step).

| Owner (selector) | Label | Kind / type | Default | from |
|---|---|---|---|---|
| `Mugirl.Features.Restraints.RestraintApparel` (ClassIs; `thing`/`li` element, any container) | `lockCount` | Values int, **forceSave** | 0 | `lockCount` + `isLocked` folded (D `Restraints.LockState`); if S2.6b declined also `isLocked` bool, default true |
| `…AdvancedRestraintApparel` (subclass, same element) | `isCracked` | Values bool | false | unchanged |
| ThingWithComp(`CompBrainwashHelmet`) | `brainwashHelmetNonBlockedTicks` | Values int | 0 | L `nonBlockedTicks` |
| | `brainwashHelmetTicksToChange` | Values int | 0 (lazy-rolled when ≤0) | L `currentTicksToChange` |
| | `brainwashHelmetLastManualUseTick` | Values int | −999999 | L `lastManualUseTick` |
| ThingWithComp(`CompMagneticShackles`) | `magneticShacklesActive` | Values bool | false | L `isActive` |
| | `magneticShacklesLastManualUseTick` | Values int | −999999 | L `lastManualUseTick` |
| | `magneticShacklesNextStateTick` | Values int (absolute game tick, −1 = unscheduled) | −1 | L `nextStateTick` |
| | — | — | — | Drop `tickCounter`, `currentCycleInterval`, `currentBindDurationTicks` (if S2.9 declined: `magneticShacklesCycleInterval`/`magneticShacklesBindDurationTicks`, int 0) |
| ThingWithComp(`CompShockCollar`) | `shockCollarLastManualUseTick` | Values int | −999999 | L `lastManualUseTick` (old default −99999, equivalent) |
| | — | — | — | Drop `ticks` (if S2.9 declined: `shockCollarTicksUntilCheck` int 0) |
| HediffWithComp(`HediffComp_AddMentalState`) | `addMentalStateAge` / `addMentalStateTriggered` | Values int / bool | 0 / false | D `Restraints.BrainwashTriggerLabels` (`age`/`triggered`) |
| HediffWithComp(`HediffComp_AddThought`) | `addThoughtAge` / `addThoughtTriggered` | int / bool | 0 / false | same |
| HediffWithComp(`HediffComp_AddTrait`) | `addTraitAge` / `addTraitTriggered` | int / bool | 0 / false | same |
| HediffWithComp(`HediffComp_ReduceWillOrEnslave`) | `reduceWillOrEnslaveAge` / `reduceWillOrEnslaveTriggered` | int / bool | 0 / false | same |
| HediffWithComp(`HediffComp_SuppressionEnhancer`) | `suppressionEnhancerAge` | int | 0 | same (`age` only) |
| HediffWithComp(`HediffComp_BrainwashPerformance`) | `brainwashPerformance` | Deep `BrainwashPerformancePlayer` (no Class) | new instance | L `performancePlayer` |
| HediffWithComp(`HediffComp_ElectricShock`) | `electricShockJitterTicksLeft` | Values int | 0 | L `jitterTicksLeft` |
| | `electricShockFilthTimers` | Collections `Dictionary<ThingDef,int>` (Def keys / Value values, `keys`+`values` children) | empty | L `filthTimers`; Drop `currentTickCount`, `remainingCycles` |
| HediffWithComp(`HediffComp_DisappearsAndAddHediff`) | vanilla `HediffComp_Disappears` labels (`ticksToDisappear`, `disappearsAfterTicks`, `seed`) | — | — | unchanged |
| PathFrom(hediff, `brainwashPerformance`) and PathFrom(GameComponent, `activePerformances/li/player`): `BrainwashPerformancePlayer` | `age` int 0; `stunTickTarget` int −1; `stunned` bool false; `stunEndTick` int −1; `stunMessageSent` bool false; `nextTextShowTicks`, `nextSoundTicks`, `nextFleckTicks` Collections `List<int>` Value; `active` bool false | | | unchanged |
| GameComponent(`Mugirl.Features.Restraints.GameComponent_BrainwashPerformance`) at `game/components/li` | `activePerformances` | Collections Deep `List<ActiveBrainwashPerformance>` (no Class) | empty | L `activeBrainwashPerformances` (present in S5–S11) |
| PathFrom(GameComponent, `activePerformances/li`): `ActiveBrainwashPerformance` | `pawn` References Pawn; `sourceHediffDef` Defs HediffDef (`Mugirl_Brainwashing_Level*`); `player` Deep `BrainwashPerformancePlayer` | | | unchanged (def map renames the value) |
| vanilla `Pawn_ApparelTracker` (`pawn/apparel`) | `lockedApparel` contains every effectively locked worn restraint | References | — | D `Restraints.WornLockList` |

Other schema elements:
- **Class= values written**: `Mugirl.Features.Restraints.RestraintApparel`, `…AdvancedRestraintApparel`, `…GameComponent_BrainwashPerformance`,
  JobDrivers `Mugirl.Features.Restraints.JobDriver_{UnlockRestraint,ForceUnlockRestraint,CrackRestraint}`,
  `Mugirl.Features.Roping.JobDriver_{RopeMugirl,UnropeMugirl,RopeToHitch,MugirlFollowRoper}`. No MapComponent, no WorldComponent.
- **JobDefs in `curJob/def`/`jobQueue`**: `Mugirl_UnlockRestraint`, `Mugirl_ForceUnlockRestraint`, `Mugirl_CrackRestraint`, `Mugirl_Rope`,
  `Mugirl_Unrope`, `Mugirl_RopeToHitch`, `Mugirl_FollowRoper`.
- **Toil layouts (schema, design §3.4)**: UnlockRestraint `[Reserve B]¹ Reserve C, AcquireItem A ×5, [Goto B]¹, Wait(unlockTicks), Finish` (8/10);
  CrackRestraint `Reserve B, AcquireItem A ×5, Goto B, Wait 60, Finish` (9); ForceUnlockRestraint `[Reserve A]¹ Reserve B, [Goto A]¹, Wait 3600, Finish` (3/5);
  RopeMugirl `Reserve A, Goto A, RopePawn` (3); UnropeMugirl `Goto A, Wait 50, Do` (3); RopeToHitch `Goto A, Do, Do, Wait 60` (4);
  MugirlFollowRoper = vanilla `JobDriver_FollowRoper` toils. (¹ omitted when the target is the actor.)
- **Defs saved**: ThingDef `Mugirl_WallRopeHitch` (+`Blueprint_`, `Blueprint_Install_`, `Frame_`), restraints incl. `Mugirl_SlaveBoots/SlaveShackles/SlaveGauntlet/SlaveStockings`
  (+`Make_`), keys `Mugirl_SlaveApparelKey_*`; HediffDef `Mugirl_Brainwashing_Level1..5`, bondage/shock hediffs; TraitDef
  `Mugirl_BrainwashPeace/Obey`; ThoughtDef `Mugirl_Roper/Roped/BrainwashHappy`; ThingCategoryDef `Mugirl_Restraints` (overrideRootDef only).
- **Thing IDs**: `<defName><n>` of the renamed ThingDefs (design §5.4).
- **Enums by name**: none. **Settings**: none. **Static state**: `RopingService.PendingHitch` (reset by the lifecycle hub, never saved),
  `RestraintAutoStageContext` depth (balanced per call, never saved).

---------------------------------------------------------------------------------------------------------------------
## S4. Shared-file edits needed from other owners

1. **Core / DefOf owner** (`Source/MugirlRace/DefOf/Mugirl_DefOf.cs`, `MugirlContentDefOf.cs`, after Wave R names): remove fields
   `Mugirl_WallRopeHitch` (:8), `Mugirl_Rope` (:36), `Mugirl_Unrope` (:38), `Mugirl_RopeToHitch` (:40), `Mugirl_FollowRoper` (:44),
   `Mugirl_ForceUnlockRestraint` (:53), `Mugirl_Fleck_BrainwashEyes` (:112, no reader), `Mugirl_BrainwashObey` (:114);
   `MugirlContentDefOf.Mugirl_SlaveApparelKey_Medieval/_Industrial` (:137-138) and `Mugirl_ShockCollar` (:48) once Restraints/Incidents use
   `RestraintsDefOf`. Order: my DefOfs land first, then the removal (duplicate DefOf fields are harmless meanwhile).
2. **Owner of `1.7/Defs/ThinkTreeDefs/Mugirl_ThinkTreeDefs.xml`** (race ThinkTree `MugirlLike`): line 10 → `Class="ThinkNode_ConditionalRoped"`;
   line 38 → vanilla `JobGiver_WanderRoped` with `<wanderRadius>4</wanderRadius><ticksBetweenWandersRange>500~800</ticksBetweenWandersRange>` (S2.5).
3. **Owner of `1.7/Defs/JobDefs/JobDefs.xml` + zh `DefInjected/JobDef/JobDefs.xml`**: remove the 7 restraint/rope JobDefs and their 7 zh keys
   plus `GiveSlaveApparelGear` (def + key) in the same merge in which I add `JobDefs_Restraints.xml`/`JobDefs_Roping.xml` (duplicate defNames
   otherwise) — or hand me the file for Wave S (S5-Q1).
4. **Owner of `1.7/Languages/{English,ChineseSimplified}/Keyed/Misc_Gameplay.xml`**: cut every `Mugirl.Restraints.*` and `Mugirl.Roping.*` key
   (post-Wave-R names: the 28 keys of spec `keyed[]` plus the existing `Mugirl.Restraints.*` keys at English :17-43, :170-177; the generic `Mugirl.SelfLabel`, `Reserved`, `NoPath`, `InvalidTarget`, `CannotPrioritizeWorkTypeDisabled`, `FloatMenu.*` stay) for me to paste into `Keyed/Restraints.xml`/`Roping.xml`; delete the dead keys
   `Mugirl.CrackBondageGear_NoCrackableType`, `Mugirl.RopedByPawn`, `Mugirl.RopedToHitchingPost`, `Mugirl.RopedToSpot`,
   `Mugirl.Restraints.MagneticShackles.LabelActive/LabelInactive`.
5. **Owner of `1.7/Defs/ThoughtDefs/Thoughts_Traits.xml`** (+ zh `DefInjected/ThoughtDef/Thoughts_Traits.xml:12,14`): delete ThoughtDef
   `Mugirl_BrainWashing_End` (:70-83).
6. **Milk owner**: receive `Mugirl_MilkingDevice`, `Mugirl_MilkingDeviceHidden` defs and their zh DefInjected entries (I cut them from
   `BondageApparel_Spacer.xml`/zh `ThingDef/BondageApparel_Spacer.xml` after removing the adult-content li/extension there) and the SoundDefs
   `Mugirl_Milking_Sound`, `Mugirl_Milking_Power_Sound` from `Bondage_Sounds.xml`. In `Items_Filth.xml` (milk-owned): fix the label of
   `Mugirl_Mote_BrainwashEyes` (currently milk-spray text, `:72`) and the out-of-range colour `274` (`:78`) — content.md E.5.
7. **Apparel owner**: delete ThingCategoryDef `Mugirl_SlaveApparel` (`Apparel_Category.xml:31-36`) + its zh DefInjected entry (S2.17).
8. **Incidents owner**: `Features/Incidents/Courier/QuestPart_SpawnCourier.cs:178-183` → `RestraintApparel.IsLocked/LockCount/RequiredKey`
   (S2.15; `SlaveDef.keytype` and the public fields disappear); `CourierConversationUtility.cs:66-67` and
   `RunawayFarm/QuestPart_SpawnRunawayFarm.Layout.cs:182` → `RestraintsDefOf`/`RopingDefOf`. `QuestPart_RunawayFarmCompletion.cs:95,136,252-253`
   keep compiling (RopingService member names unchanged).
9. **Grazing owner**: owns `Features/Grazing/Harmony_FoodUtility_IsAcceptablePreyFor.cs` after Wave R; set its `[MugirlPatch]` module to Grazing.
10. **SaveUpgrade framework owner (A1)**: provide (i) a per-module hand-table location for LegacyTypes/LegacyDefs/LegacyLabels overrides
    (S5-Q7), (ii) Document helpers: `HediffWithComp` selector that matches a set of comp classes, def resolution through the def map, safe
    child create/remove, `IsNull="True"` list handling, `Thing_<id>` reference formatting, (iii) phase-B registration for
    `Restraints.EndLegacyRestraintJobs`, (iv) lifecycle-hub reset registration for `RopingService`.
11. **Validator owner**: content contracts (S2.1 RestraintExtension, S2.5 vanilla ThinkTree classes, every `Mugirl.Features.Restraints.*`
    Class in XML resolves); drop module entries from `save-types.txt`, `dropped-scribe-labels.txt`, `scribe-defaults.txt`,
    `empty-types.allow.txt`, `forbidden-patterns.txt:16,20`; regenerate `patch-targets.txt`, `transpiler-hooks.txt`,
    `dynamic-lookups.allow.txt:13-18`. The negative-control class `MapRopingIndex` in `Validation/Controls/NegativeControl/EngineViolations.cs:45`
    is a fixture name only (keep or rename at the validator owner's choice).
12. **Mounting owner**: nothing required (I stop using `MountedPawnUtility.HasCapacity`).
13. No Wave S edit needed in `1.7/Patches/MugirlPatch.xml`, `TraitDefs.xml`, `Recipes_Mugirl.xml`, `CorporateFusionResearchSite.xml` (Wave R covers them).

---------------------------------------------------------------------------------------------------------------------
## S5. Open questions (with proposed answers)

- **Q1 JobDefs.xml ownership.** It mixes 8 restraint/rope JobDefs with Nuzzle and milk JobDefs. *Proposal:* restraints owns `JobDefs.xml` and
  its zh DefInjected in Wave S, extracts its JobDefs into `JobDefs_Restraints.xml`/`JobDefs_Roping.xml`; the leftover file (Nuzzle + milk) is
  renamed by its owners in a later pass.
- **Q2 Milking devices in `BondageApparel_Spacer.xml` and milk sounds in `Bondage_Sounds.xml`.** *Proposal:* restraints cuts them out at the
  start of Wave S into new milk files (`1.7/Defs/Apparel/Apparel_MilkingDevice.xml`, `1.7/Defs/SoundsDefs/Milk_Sounds.xml` + zh DefInjected),
  which the milk owner owns from then on; milk requests any further edit.
- **Q3 Phase-only behaviour changes (S2.9).** Shock-collar check moves from a per-pawn countdown to a hash-aligned interval (same chance per
  interval, different phase); shackles re-roll durations at scheduling. *Proposal:* accept (no observable gameplay difference, removes saved
  counters as design §3.4 asks). If rejected, keep the counters with the prefixed labels given in S3.
- **Q4 Dead MentalStateDef `Mugirl_BrainWashing`.** Delete (current behaviour uses vanilla Berserk) or wire it into the Level5 comp?
  *Proposal:* delete (design: no dead content; wiring it would be a behaviour change).
- **Q5 Subspecies dependency on `<Mugirl.SlaveApparelDef>`.** No alias can map a Def subclass element to a DefModExtension. *Proposal:* accept the
  break for this 1.6-only dependent and document the port (`<ThingDef>` + `RestraintExtension`, `keytype` → `keyDef`) in the migration notes.
- **Q6 Prey rule location.** *Proposal:* Grazing (diet behaviour); alternative `Core` if the Grazing owner objects.
- **Q7 Where module-specific upgrader table entries live.** *Proposal:* `SaveUpgrade/LegacyNames/` keeps one generated file per table plus
  per-module hand partials (`LegacyTypes.Restraints.cs`, `LegacyDefs.Restraints.cs`, `LegacyLabels.Restraints.cs`) owned by the module in
  Wave S; the generator lets a hand entry override a generated one (needed for BrainWashSlaveApparel, JobDriver_UseItemOn, MapRopingIndex).
- **Q8 `isLocked` removal (S2.6b).** *Proposal:* adopt; it removes a redundant saved flag whose only role is duplicated by `lockCount > 0`.
