# Cross-module consistency check of the Wave A3 rename specs (2026-10-15)

Inputs: `spec/{core,corporate,incidents,milk,restraints,combat,content}.json` and `-plan.md`, `core-member-moves.json`,
`moogirl-types.json`, `moogirl-defs.json`, `content-legacy-defs.json`, `content-keyed-split.json`; design `../design.md`;
repo `Source/MugirlRace` and `1.7/` as of 2026-10-15 (no `.cs` file is newer than `design.md`, i.e. the specs' baseline).
Output: this report and `spec/merged.json` (union of all arrays; every disagreement kept and tagged `"conflict": ["Cnn"]`;
the 32 items are listed in `merged.json#conflicts` with status `unresolved` and a proposed resolution — nothing was silently resolved).
Scripts (re-runnable, read-only on the repo): `spec/_cc/*.py` (`scan_types.py` C# top-level type scanner, `final.py` final layout,
`dups.py`, `defof.py` DefOf split simulation, `defnames.py`, `patchmod.py`, `harmonynames.py`, `histtypes.py`, `implied.py`,
`content_owner.py`, `build_merged.py`).

## Result overview

| Check | Result |
|---|---|
| Root namespace `Mugirl` coverage | **431/431** top-level types in **353** files (`grep -E '^\s*namespace Mugirl\s*(;\|\{\|$)'`, no block-form namespace exists) are covered by a spec `types` row or by `core-member-moves.json#dissolvedTypes`. 0 uncovered. 0 spec `old` names that do not exist. |
| Namespace vs target folder (all 646 production types after the merged renames) | 2 mismatches (C29: `Mugirl.Compatibility.ModCompat`, `CompatAccessors`). Final feature folders = {Abilities, AdvancedArmor, Appearance, Corporate, Grazing, Incidents, Lances, LifeStages, Milk, Mounting, Restraints, Roping, Social, Trade, WeaponWheel, Weapons, Xenotype} ⊂ design §3.1 map; no leftover `Features/{Misc,Genes,Newborn,Apparel}`, `DefOf/`, `UI/` (top level), `Incidents/{Raids,VanillaFixes,Trade}`, `Compatibility/{AlienRace,CorporatePricing}`. No forbidden namespace segment. |
| Same old type in two modules | 3 identical duplicates (`MugirlGeneratedPawnUtility`, `Mugirl_IdeoUtility` core+incidents; `Patch_DrugAdministerDefs` incidents+milk), **1 conflict (C01)**. |
| Two olds → one new type / new = existing kept type | none, except the DefOf create-vs-rename **C02**. No new short name is ambiguous across namespaces. |
| newFile / fileMove collisions | only the TRANSITIONAL self-patch files (C27 note); no fileMove conflicts. |
| ownedSourceFolders overlap | none. Every production `.cs` file has exactly one owner (only validator/GameValidation test infra is unowned, owned by A2). |
| ownedContentFiles overlap | none exact/glob; but split/claim inconsistencies **C09, C10**, unowned files **C11**, core Q7 vs content **C24**. |
| contentFileMoves | 11 identical duplicates (milk/combat/incidents ∩ content), 0 conflicts; all sources exist, no target exists yet. |
| defs | 25 identical duplicates, 0 different-target conflicts, 1 saveVisible conflict **C12**, 7 wrong `sharedNameWithOtherDefTypes` **C13**; no same-type collision with a def that stays. |
| DefOf split simulation (208 fields in 16 `[DefOf]` classes) | every field is moved, copied or deleted; **1 compile collision C05** (`SocialDefOf.Mugirl_Nuzzle`); timing **C03**, placement rule **C04**, stale target **C06**. |
| keyed | 0 duplicates; every renamed key exists; no new key exists already; every Misc_Gameplay key is either split (338) or deleted (19); split targets are owned by the renaming module. |
| Harmony class names | 125 patch classes checked against `[HarmonyPatch(typeof(T), nameof(T.M))]`: all non-transitional new names start with `Harmony_<T>_<M>`; the 9 mismatches/7 unresolvable are all TRANSITIONAL rows or `TargetMethod()` patches. `PatchModule` enum covers every final patch folder; member `Apparel` is dead (**C25**). |
| Public API (§2) | no frozen defName/abstract Name/type is renamed; the three §2 XML classes are renamed with aliases recorded (`merged.json#handTypeDispositions`); XML field names used by dependents are kept (`displayString/milkDef/…`, `slaveCountRange`). Interpretation question on abstract Names in §5 below. |
| Save-visible union | 91 type rows (Mugirl era 50 + validation 2 + MooGirl 39), 409 def rows in 31 def types (Appendix A/B). **No new name equals any old name** (types: also checked against the 928 historical Mugirl*/MooGirl* names in `understand/dlls/types.tsv`; defs: against every spec/MooGirl/retired old name). No chains, no old name with two targets. 17 case-only def renames (ordinal comparison required). |

---------------------------------------------------------------------------------------------------------------------
## 1. Duplicate or conflicting entries

**C01 (blocker) — `Mugirl.CorporatePricingCompatibility` renamed twice.**
core.json → `Mugirl.Features.Corporate.OpaqueBehaviorAnalysis` (`Features/Corporate/Pricing/OpaqueBehaviorAnalysis.cs`) plus test row
`Mugirl.Tests.Compatibility.CorporatePricingCompatibilityTests` → `Mugirl.Tests.Corporate.OpaqueBehaviorAnalysisTests`;
corporate.json → `Mugirl.Features.Corporate.CorporateForeignBehavior` (`Pricing/CorporateForeignBehavior.cs`) plus a *file-only* move of the test to
`Tests/Corporate/CorporateForeignBehaviorTests.cs` (class and namespace would stay `Mugirl.Tests.Compatibility.CorporatePricingCompatibilityTests`).
Source: `Source/MugirlRace/Compatibility/CorporatePricing/CorporatePricingCompatibility.cs`. Both plans saw it (corporate-plan.md:396 S5-Q9, corporate.json notes, core-plan.md:169).
*Proposed:* corporate (owner of the target folder) wins: `CorporateForeignBehavior`; delete core's two rows; corporate replaces its test fileMove by a types row → `Mugirl.Tests.Corporate.CorporateForeignBehaviorTests`.

**C02 (blocker) — DefOf types listed by corporate / create-vs-rename on `Mugirl.Features.Corporate.CorporateDefOf`.**
corporate.json `types` contains `CorporateFinanceDefOf`, `CorporateIntroductionDefOf`, `CorporatePeopleDefOf`, `CorporateTradeDefOf` (each split to its own file) and
`CorporateQuestDefOf → Mugirl.Features.Corporate.CorporateDefOf`; core.json notes[0] forbids DefOf rows in module specs and core-member-moves.json
dissolves those five and *creates* `Mugirl.Features.Corporate.CorporateDefOf` in the same file `Features/Corporate/CorporateDefOf.cs`.
*Proposed:* drop the five rows from corporate.json.

**Identical duplicates (no action, collapsed in merged.json):** types `Mugirl.MugirlGeneratedPawnUtility`, `Mugirl.Mugirl_IdeoUtility`, `Mugirl.Patch_DrugAdministerDefs`;
contentFileMoves `Apparel_0920/0926` (Defs+zh), `HediffDefs/Items_Resource.xml` (Defs+zh), `MeleeWeapons_0920`, `RangedWeapons_0920`, zh `Weapons_0920`,
zh `HediffDef/Mugirl_Punisher.xml`, Biotech zh `JobDefs_Biotech.xml`; defs PMC/OPC weapons+bullets (combat=content), PMC apparel and
`Mugirl_MugirlMilkIntoCheese` (milk=content), `MugirlMilkStain` (milk=content, the earlier `Mugirl_MilkStain` disagreement is already resolved to `Mugirl_Filth_MilkStain`),
traits `Mugirl_BrainWash{Peace,Obey}` and the brainwash mote/fleck (restraints=content); abstract `Mugirl_0920CasualBase` (milk=content).

**C12 — saveVisible disagreement.** ThingDef `MugirlWashMentalEyesInHead → Mugirl_Mote_BrainwashEyes`: restraints `false`, content `true`.
It derives from `MoteBase` (`1.7/Defs/ThingDefs_Misc/Items_Filth.xml:69`), vanilla `isSaveable=false` (content.md B.2). *Proposed:* `false`.

**C13 — `sharedNameWithOtherDefTypes` wrong (7 rows).** New name equals a def of another type: MaterialDef `Mugirl_ExtraOutline`, `Mugirl_LanceMotionBlur`
(= ShaderDefs, `Mugirl_Shaders.xml:21,45`); JobDef `Mugirl_Nuzzle` (= InteractionDef); ThinkTreeDef `Mugirl_Punisher` (= PawnKindDef); JobDef `Mugirl_Dunk`
(= AbilityDef); JobDef + WorkGiverDef `Mugirl_GatherMilk` (two milk rows → same new name, different types). All flagged `false`. *Proposed:* set `true`
(the tool must scope these renames by def type; core/incidents explain the scoping only in prose).
Related tool hazard (correctly flagged): ThingCategoryDef `Mugirl_Slave → Mugirl_Restraints` must not touch PawnKindDef `Mugirl_Slave`
(`Mugirl_DefOf.Mugirl_Slave` is a PawnKindDef → TradeDefOf, name unchanged); QuestScriptDef `Mugirl_CourierRaid` is renamed while IncidentDef `Mugirl_CourierRaid` is dropped.

**C14 — defType format** (corporate uses post-Wave-R full class names; restraints maps `<Mugirl.SlaveApparelDef>` defs to `ThingDef`). Not save-visible; document one rule.

**C09/C10/C11/C24 — content ownership.**
- C09: `JobDefs.xml` (+zh) claimed by content for Wave S while content-plan S2-1 dissolves it in Wave R and restraints-plan S5-Q1 wants it; `Patches/MugirlPatch.xml` claimed by content but
  split by content-plan S2-2 into 5 files of which 3 (Roping_CaravanPackingSpot, BrainwashHelmet_TraderStock, Restraints_ElectricSmelter) nobody claims; `Keyed/Misc_Gameplay.xml` (en+zh) owned by nobody.
  *Proposed:* all three splits are Wave R element moves, sources deleted, restraints claims its three patch files.
- C10: Thoughts_Restraints.xml / Thoughts_Roping.xml (incidents-plan S2-15) unclaimed by restraints.
- C11: unowned existing files: `Defs/RecipeDefs/Recipes_Mugirl.xml` (+zh) → restraints; `Defs/ThinkTreeDefs/Mugirl_ThinkTreeDefs.xml` (+en/zh) → content; `Shaders/Mugirl/Mugirl_LanceMotionBlurUniforms.glsl` → core.
- C24: core-plan Q7 gives race/body/backstory files to milk; content.json claims them. *Proposed:* content.
- Files claimed but created only by Wave R/S splits (expected, listed for the tool): Keyed `Core/Incidents/Grazing/Milk/Abilities/Restraints/Roping/Mounting/WeaponWheel/Weapons.xml`,
  `JobDefs_{Social,Milk,Restraints,Roping}.xml` (+zh), `Apparel_{MilkingDevice,AdvancedArmor}.xml` (+zh), `SoundsDefs/Milk_Sounds.xml`, two incidents patch files,
  zh `PawnKindDef/PawnKinds_CorporateShowcase.xml`, zh `ThingDef/RangedWeapons_Autocannon.xml`.

---------------------------------------------------------------------------------------------------------------------
## 2. Convention violations (design §3) and public API (§2)

- **C25** `Apparel` feature: namespace `Mugirl.Features.Apparel` would shadow `RimWorld.Apparel` (CS0118, verified by incidents/milk); both put apparel code into `Features/Appearance/Apparel`
  (`Mugirl.Features.Appearance`). Design §3.1 still lists `Apparel`; `PatchModule.Apparel` maps to no folder; core-plan.md:134,167 still speak of an "Apparel-owned" patch.
  *Proposed:* amend §3.1 (forbid the segment), drop the enum member, put `Harmony_Hediff_PostAdd_PregnantApparel` into Features/Appearance/Apparel.
- **C26** `Bullet_Mugirl_*` (8 renames + 4 existing) puts the category before the prefix, against §3.3 and against the `Mugirl_Filth_*`/`Mugirl_Mote_*`/`Mugirl_Fleck_*` style of content/restraints/milk. Decide once.
- **C27** one-type-per-file: 5 non-transitional top-level types stay in foreign files without a machine-readable split row:
  `PawnVisualMetrics`, `InteractionPose`, `InteractionPairPose` (`Features/Milk/Animation/MilkingPoseMath.cs`), `BreastProfile` (`Features/Milk/Production/MilkProductionMath.cs`),
  `WeaponWheelTransferMode` (`Features/WeaponWheel/Core/WeaponWheelTransferScope.cs`), `WeaponWheelWeaponPose` (`Features/WeaponWheel/Rendering/WeaponWheelAnimationCurves.cs`) —
  combat mentions its two only in notes, milk not at all. (`RestraintPawnTypeParser` is deleted in Wave S.) *Proposed:* add old==new rows with the split newFile.
- **C28** `Mugirl.Tests.Incidents.GiantCorpRaidTierRulesTests` only file-moved → class ≠ file name, namespace ≠ folder. Add a types row.
- **C29** `Mugirl.Compatibility.ModCompat` / `CompatAccessors` have no `<OtherMod>` segment (design gap; amend §3.1 or move to Core).
- **C30** BodyPartGroupDef `Ears` is the only unprefixed non-FA def that no spec renames; deliberate cross-mod shared name (content.md:203) → add to §3.3 exceptions.
- **C32** transitional names between Wave R and S: `Mugirl.Features.Roping.JobGiver_WanderRoped` / `ThinkNode_ConditionalRoped` repeat vanilla short names of the same role (§3.2b);
  `Mugirl.Features.Incidents.MugirlEventMarkers` keeps the `Mugirl` prefix; the 10 TRANSITIONAL compatibility self-patch classes keep non-conforming Harmony names and share files.
  Acceptable only because Wave S deletes them; the validator's naming rules must run after Wave S (or allow-list them).
- Checked and fine: all short names containing `Mugirl` carry meaning or avoid a vanilla collision (`JobDriver_MountMugirl`, `InteractionWorker_MugirlNuzzle`, `QuestNode_Root_MugirlRefugeePodCrash`,
  `QuestNode_Root_MugirlWandererJoin`, `StockGenerator_MugirlSlaves`, `MugirlJumpUtility`, `JobGiver/JobDriver_MugirlFollowRoper`, `Thought_MugirlOnly`, `MugirlOnlyEquipmentExtension`, Core statics);
  `MugirlXenotypeDefOf` deviates from `<Feature>DefOf` because `RimWorld.XenotypeDefOf` exists (§3.2b, justified). No `Moo` left in any final type name or new defName
  (`PregnancyMood` is a false positive). No `Comp_*` shape left; `HediffCompScribe` (Restraints) is a helper, not a comp.
- **§2 public API:** no frozen def, Name or class is renamed; aliases recorded for `StockGenerator_Mugirl_Slaves`, `CompProperties_MugirlMount`, `CompProperties_MooMilkable`.
  Open interpretation: design §2 freezes "every abstract Name used as ParentName by dependents (content.md B.1 list)", but the B.1 list (content.md:132) enumerates *all* inheritable
  Names, including the 9 renamed by milk/restraints (`Mugirl_0920CasualBase`, `Mugirl_Stocking`, `Mugirl_TacticalGoggles`, `Mugirl_BrainWashApparel`, `Mugirl_OnSkin_{,Upper_,Lower_}SlaveApparel`,
  `Mugirl_Base_SlaveApparelKey`, `Make_MugirlKeys`). content.md:197 states no local external mod uses them. *Proposed:* read §2 as "Names verified in dependents (content.md C)" and say so in the ADR.
  Known accepted breaks for 1.6-only dependents: `<Mugirl.SlaveApparelDef>` (Subspecies, restraints-plan S5-Q5), `Mugirl.MountedPawnUtility.IsMugirl` Harmony target (WhiteFluff, combat-plan S2-11).

---------------------------------------------------------------------------------------------------------------------
## 3. Completeness

- Root namespace: complete (431/431). The 3 types that keep a non-final shape after Wave R are listed in C32.
- Namespace ≠ folder after the merged renames: only C29 (2 types). Tests: only C28 (and C01's test).
- Every save-shaped current type (JobDriver, GameComponent, MapComponent, QuestPart, Thing/Apparel/PawnFlyer subclasses, Thought_*, LordJob, Letter) is flagged `saveVisible`
  in its row; every historical Mugirl*/MooGirl* save-shaped type in `understand/dlls/types.tsv` is either in the current source, in a spec row or in moogirl-types.json.
- **C23** transitional/hand dispositions are not machine-readable: three different reason prefixes, and the overrides for the 5 save-visible transitional types live only in notes
  (`ChoiceLetter_MugirlFusionInvestment → Verse.StandardLetter`, `Mugirl_XenotypeFix_GameComp → Consumed`, `JobDriver_UseItemOn`/`MapRopingIndex → Drop`,
  `BrainWashSlaveApparel → AdvancedRestraintApparel` although its Wave R name `BrainwashHelmetApparel` carries no marker). merged.json adds `transitional` and `handTypeDispositions`.
- **C15/C16** MooGirl tables: 38 def rows needed composition (done in `merged.json#mooGirlDefs.finalTarget`), 5 of them only through prose hand entries
  (now `handDefDispositions`); type rows: 4 compose onto hand overrides, `MooGirl.JobDriver_GatherBodyResources` is abstract in all 24 historical DLLs (not save-visible),
  and the three `settings` rows contradict core-plan S1-L7 ("no import").
- **C20** IncidentDef `Mugirl_CourierRaid`/`MooGirl_CourierRaid` drops must reach core's `RemoveRetiredIncidentReferences` (core-plan L3 lists only WildManWandersIn).
- Implied defs (design §5.4) derived from the 1.7 XML inheritance (`merged.json#impliedDefRenames`, 22 rows): `Make_` for the 4 milk apparel + 3 PMC apparel + 4 restraint apparel + 8 PMC/OPC weapons,
  `Blueprint_`/`Blueprint_Install_`/`Frame_` for `WallRopeHitch`. They agree with the module notes.

---------------------------------------------------------------------------------------------------------------------
## 4. Cross-module references

- **C07 (blocker) hook API** core-plan S2-3 (core-plan.md:68-119) vs combat-plan S2-1 (combat-plan.md:62-147): different method names, delegate types (`Comp_WeaponWheel` vs `Pawn`), swap-guard
  contract (targetSlot; End always vs only with a token), ticker semantics (every tick incl. early returns vs `IsHashIntervalTick(interval)` at the end), duplicate-provider policy,
  registration time (`[StaticConstructorOnStartup]` vs Mod constructor), folders (`Integration/` vs `Hooks/`). core-plan also uses the pre-Wave-R names `Comp_WeaponWheel`/`MountedPawnMeleeSupport`
  in a post-Wave-R interface. *Proposed:* combat's API (it owns the interface), core-plan S2-3/S4.3 rewritten; decide registration time and early-return semantics jointly.
- **C03/C04/C05/C06 DefOf:** timing (Wave R per core vs Wave S per corporate/combat/restraints), placement rule (owner vs consumer vs per-consumer copies; corporate-plan.md:356-358),
  the SocialDefOf `Mugirl_Nuzzle` collision (Mugirl_DefOf.cs:42/:130; incidents asks for `[DefAlias]`, core deletes DefAlias usage), and `memberMoves.to = Mugirl.Comp_MugirlMount`
  (old name; final is `Mugirl.Features.Mounting.CompMount`; combat would delete the helpers anyway).
  DefOf split result otherwise consistent: all 208 fields of the 16 `[DefOf]` classes are moved/copied/deleted; renamed defNames rename their fields (simulation in `_cc/defof.py`).
- **C08** `PawnGenerator.GeneratePawn` order: restraints keeps the lock postfix last (`Features/Restraints/Patches/Harmony_PawnGenerator_Patch.cs:5-6`), combat-plan.md:236 says "independent, no attribute".
- **C17** "Mugirl added to an existing game": incidents wants a hub phase, core has none, corporate builds a local flag.
- **C18** talk-to-pawn bases: corporate expects them in Core, incidents keeps them in Incidents/Shared (would create Corporate → Incidents inheritance).
- **C19** incidents edits core-owned `Core/GeneratedPawnUtility.cs` (S2-5b) without a core-side entry.
- **C21** unanswered requests: `Mugirl.Core.EquipmentTransferScope` and a Core home for `IsHumanlike/HasCapacity/IsAwake` (combat→core); settings window vs `WeaponWheelDevLog`
  (use core's `SettingsWindow.AddDeveloperAction`); LactationExpansion follows `BabyFeedingUtility` member renames + order pin (milk→core); `DevFillToFull → FillToFull` (milk→incidents);
  Mount API call sites in `CorporateNetwork.People.cs:372-379` (combat→corporate, while corporate asks nobody to edit its files); `IsSwordDanceStrikePending` (combat→core).
  *Proposed:* put cross-owner member renames into a Wave R `memberRenames` list like `core-member-moves.json#memberRenames`.
- **C22** layout of per-module upgrader tables and their tests differs (core `LegacyNames/<Module>/`, milk `LegacyNames/Milk/`, restraints flat suffix files); only core claims a LegacyNames folder.
- Agreements verified: settings field renames (core memberRenames ↔ incidents/corporate readers); lifecycle hook attribute `[GameLifecycleHook(FinalizeInit)]` used identically by core/incidents/milk;
  shared helpers `Mugirl.Core.{PlayerFactionUtility,PlayerIdeoUtility,GeneratedPawnUtility}` consistent in core/incidents/corporate; keyed split ↔ module key renames; story API between
  incidents and corporate (incidents-plan S4 ↔ corporate-plan S4.7a); final courier names requested by corporate S4.7(e): QuestScriptDef `Mugirl_CourierContact`,
  QuestPart `Mugirl.Features.Incidents.QuestPart_SpawnCourier`; `KindApparelUtility` and the Appearance/Apparel types placed by milk/incidents into each other's folders are acknowledged by both.
- **C31** stale prose only (listed in merged.json).

---------------------------------------------------------------------------------------------------------------------
## 5. Save-visible renames

Union tables: `merged.json#saveVisibleTypeMap` (91 rows) and `#saveVisibleDefMap` (409 rows, 31 def types), reproduced in Appendix A/B.
Rules applied: spec rows with `saveVisible=true`; transitional rows replaced by their hand disposition (C23); MooGirl rows composed to the FINAL name (C15/C16);
retired defs from content-legacy-defs.json; implied `Make_/Blueprint_/Frame_` names; `Mugirl.Corporate{Runtime,Visual}Validation` (legacy-extra).
- **No reuse:** no new type/def name equals any old name of any era (types also vs 928 historical names); no chains; no old name with two targets.
- Case-only renames (17): `Mugirl_Eroticpanties`, `Mugirl_Slave{boots,shackles,gauntlet,stockings}` (+ their `Make_`), `Mugirl_BrainWashing_Level1..5`, `Mugirl_BrainWash{Peace,Obey}`,
  5 `Mugirl_BrainWashing_*` SoundDefs. DefDatabase lookups and the DOM pass are ordinal; the validator's "no reuse"/"no chain" checks must also be ordinal.
- Same old name in two def types with different fates: `Consumed_MugirlMilk` (HediffDef → `Mugirl_MilkVigor`, ThoughtDef → `Mugirl_ConsumedMilk`), `Mugirl_CourierRaid` (QuestScriptDef → `Mugirl_CourierContact`, IncidentDef → Drop),
  `Mugirl_Slave` (ThingCategoryDef → `Mugirl_Restraints`, PawnKindDef unchanged). The BackCompatibleDefName postfix and DOM step 2 must key on (def type, name) (design §5.2.3 already says so).


---------------------------------------------------------------------------------------------------------------------
## Conflict index

| id | severity | kind | summary (short) |
|---|---|---|---|
| C01 | blocker | type-renamed-differently | Mugirl.CorporatePricingCompatibility is renamed by two modules to different names/files: core -> Mugirl.Features.Corporate.OpaqueBehaviorAnalysis (Pricing/OpaqueBehaviorAnalysis.cs), corporate -> Mugirl.Features.Corpo... |
| C02 | blocker | defof-listed-as-type / create-vs-rename | corporate.json lists five DefOf types (CorporateFinanceDefOf, CorporateIntroductionDefOf, CorporatePeopleDefOf, CorporateTradeDefOf -> own files in Mugirl.Features.Corporate; CorporateQuestDefOf -> Mugirl.Features.Cor... |
| C03 | high | wave-timing | DefOf split timing disagrees: core-member-moves.json/core-plan S2-5 do it in Wave R ('Wave S verifies'); corporate (notes, plan S2-10), combat (notes, plan S2-9 + S4.1 'fields leave for the feature DefOfs'), restraint... |
| C04 | high | rule-disagreement | DefOf placement rule: core-plan S2-5 states 'a field lives in the DefOf of the feature that owns the Def; other features reference that DefOf'. core-member-moves data places many fields by CONSUMER instead (milk foods... |
| C05 | blocker | compile-collision | SocialDefOf would contain two fields named Mugirl_Nuzzle: JobDef Job_Nuzzle (Mugirl_DefOf.cs:42) is renamed to Mugirl_Nuzzle by incidents.json and the defNameCoupling rule renames the field; InteractionDef field Mugir... |
| C06 | high | stale-target-name | core-member-moves.json memberMoves moves MugirlTickUtility.Add/ConsumeReady 'to' Mugirl.Comp_MugirlMount, although symbolResolution says every 'to' is the FINAL type; combat.json renames that type to Mugirl.Features.M... |
| C07 | blocker | api-disagreement | WeaponWheel/Mounting hook API differs between core-plan S2-3 and combat-plan S2-1: method names (AddCastPresentationOwner vs AddCastInterceptionVeto; AddPrimarySwapParticipant/IWeaponWheelPrimarySwapParticipant vs Add... |
| C08 | medium | patch-order | PawnGenerator.GeneratePawn postfix order: restraints requires [HarmonyPriority(Priority.Last)] on Harmony_PawnGenerator_GeneratePawn_LockRestraints (restraints.json notes[8]; today's ordering is deliberate: Harmony_Pa... |
| C09 | high | file-ownership/timing | Shared files that are split: (a) JobDefs.xml (+zh DefInjected) is claimed by content.json for Wave S ('the one truly shared file') while content-plan S2-1 dissolves it in Wave R into JobDefs_{Roping,Restraints,Social,... |
| C10 | low | file-ownership | incidents-plan S2-15/S4 splits Thoughts_Traits.xml into Thoughts_Restraints.xml / Thoughts_Roping.xml for restraints; restraints.json does not claim them and restraints-plan S4.5 assumes Thoughts_Traits.xml stays with... |
| C11 | medium | unowned-files | Existing 1.7 files claimed by no module: Defs/RecipeDefs/Recipes_Mugirl.xml + zh DefInjected (restraint key/smelting recipes; milk.json notes point to restraints), Defs/ThinkTreeDefs/Mugirl_ThinkTreeDefs.xml + en/zh D... |
| C12 | medium | flag-conflict | ThingDef MugirlWashMentalEyesInHead -> Mugirl_Mote_BrainwashEyes: restraints saveVisible=false, content saveVisible=true. The def derives from MoteBase (1.7/Defs/ThingDefs_Misc/Items_Filth.xml:69), which is isSaveable... |
| C13 | low | flag-wrong | sharedNameWithOtherDefTypes=false although the NEW name equals another def type's name: MaterialDef Mugirl_ExtraOutline/Mugirl_LanceMotionBlur = ShaderDef (Mugirl_Shaders.xml:21,45); JobDef Mugirl_Nuzzle = Interaction... |
| C14 | low | format | defType format differs: corporate uses post-Wave-R C# full names (Mugirl.Features.Corporate.CorporateFinanceConfigDef ...); restraints uses the root ThingDef for <Mugirl.SlaveApparelDef> defs; others use vanilla short... |
| C15 | medium | composition-gap | moogirl-defs.json rows with composeWithMergedSpec=true have new == current17 (38 rows) and must be composed with the merged def map; 5 of them land on names whose disposition exists only in prose notes (IncidentDef Mu... |
| C16 | medium | composition-gap | moogirl-types.json: (a) MooGirl.MooGirl_XenotypeFix_GameComp, BrainWashSlaveApparel, JobDriver_UseItemOn, MapRopingIndex compose to Mugirl-era names whose final disposition is a hand override (Consumed / AdvancedRestr... |
| C17 | medium | missing-api | 'Mugirl added to an existing game' detection: incidents-plan S4/S5-Q3 requests GameLifecyclePhase.AddedToExistingGame (or GameComponent_Mugirl.WasInSave); core-plan S2-1 enum has only GameStarting/FinalizeInit/Started... |
| C18 | medium | type-location | Talk-to-pawn bases: corporate-plan S2-16/S4.7(d) expects Incidents to move JobDriver_MugirlTalkToPawn/FloatMenuProvider_MugirlTalkToPawn to Core and derives corporate classes from them; incidents.json moves them to Mu... |
| C19 | low | cross-owner-edit | incidents-plan S2-5b/S4 moves CreateFactionlessAdultRequest, CreateVisitorRequest, HasMugirlSlaveBackstories out of the core-owned Core/GeneratedPawnUtility.cs into Incidents/Shared/EventPawnRequestUtility.cs; core-pl... |
| C20 | low | step-scope | Steps/Core/RemoveRetiredIncidentReferences: core-plan L3 lists only Mugirl_WildManWandersIn/MooGirl_WildManWandersIn; incidents adds IncidentDef Mugirl_CourierRaid/MooGirl_CourierRaid (scoped to IncidentDef contexts, ... |
| C21 | medium | unacknowledged-requests | Cross-module requests with no answer in the receiving plan: combat -> core: Mugirl.Core.EquipmentTransferScope (combat-plan S2-12), a Core home for IsHumanlike/HasCapacity/IsAwake (S4.1), settings window must not refe... |
| C22 | low | layout | Per-module upgrader tables: core-plan S5-Q3 'SaveUpgrade/LegacyNames/<Module>/Legacy*.<Module>.cs'; milk-plan 'LegacyNames/Milk/'; restraints-plan Q7 flat 'LegacyNames/LegacyTypes.Restraints.cs'. Only core claims its ... |
| C23 | high | machine-readability | Transitional rows are marked only in reason prose with three spellings ('TRANSITIONAL', 'TRANSITIONAL:', 'TEMPORARY Wave R name'); the five save-visible ones need hand overrides that exist only in notes (ChoiceLetter_... |
| C24 | low | file-ownership | core-plan S5-Q7 proposes milk-genes-appearance as owner of Mugirl_Race.xml, BodyDef.xml, BackstoryDef.xml; content.json claims them and milk.json does not. |
| C25 | medium | design-deviation | Feature 'Apparel' (design §3.1) cannot exist as namespace Mugirl.Features.Apparel (shadows RimWorld.Apparel, CS0118); incidents and milk place apparel code in Features/Appearance/Apparel (namespace Mugirl.Features.App... |
| C26 | low | convention | Projectiles keep the vanilla role prefix first (Bullet_Mugirl_*, 8 renamed by combat/content, 4 existing) while design §3.3 requires Mugirl_ first and other modules put the category AFTER the prefix (Mugirl_Filth_*, M... |
| C27 | low | convention/completeness | Multi-type files that keep their types' full names are not split by any machine-readable row: MilkingPoseMath.cs (PawnVisualMetrics, InteractionPose, InteractionPairPose), MilkProductionMath.cs (BreastProfile) [milk, ... |
| C28 | low | test-rename | Mugirl.Tests.Incidents.GiantCorpRaidTierRulesTests is only file-moved (corporate fileMoves[1]) to Tests/Corporate/CorporateRaidTierRulesTests.cs; class name and namespace (Mugirl.Tests.Incidents) stay, so file name !=... |
| C29 | low | convention | Mugirl.Compatibility.ModCompat and Mugirl.Compatibility.CompatAccessors (Compatibility/ModCompat.cs, CompatAccessors.cs) live in namespace Mugirl.Compatibility without an <OtherMod> segment; design §3.1 only names Mug... |
| C30 | low | design-gap | BodyPartGroupDef 'Ears' (1.7/Defs/Bodies/BodyDef.xml) has no Mugirl_ prefix and is deliberately shared with other mods (content.md B.6), but design §2/§3.3 does not list it as an exception; it is the only remaining un... |
| C31 | low | stale-references | Stale prose (no data impact): content.json notes say milk uses Mugirl_MilkStain (milk.json now has Mugirl_Filth_MilkStain); corporate notes rename DefOf fields AI_GC_Grunts/Soldiers/Elites/MugirlRaider that core-membe... |
| C32 | low | convention (transitional) | Transitional Wave R names equal vanilla short names of the same role: Mugirl.Features.Roping.JobGiver_WanderRoped (Verse.AI.JobGiver_WanderRoped), Mugirl.Features.Roping.ThinkNode_ConditionalRoped (RimWorld.ThinkNode_... |

Full text, references and proposed resolutions: `merged.json#conflicts`.

---------------------------------------------------------------------------------------------------------------------
## Appendix A. Save-visible type map (union)

| old full name | era | disposition | target | modules / note |
|---|---|---|---|---|
| `Mugirl.GameComponent_GhoulRenderingRefresh` | Mugirl | Map | `Mugirl.Rendering.GameComponent_PawnRenderRefresh` | core |
| `Mugirl.CorporateNetwork` | Mugirl | Map | `Mugirl.Features.Corporate.CorporateNetwork` | corporate |
| `Mugirl.CorporateIntroduction` | Mugirl | Map | `Mugirl.Features.Corporate.CorporateIntroduction` | corporate |
| `Mugirl.QuestPart_CorporateIntroduction` | Mugirl | Map | `Mugirl.Features.Corporate.QuestPart_CorporateIntroduction` | corporate |
| `Mugirl.QuestPart_CorporateMission` | Mugirl | Map | `Mugirl.Features.Corporate.QuestPart_CorporateMission` | corporate |
| `Mugirl.JobDriver_CorporateSupportTend` | Mugirl | Map | `Mugirl.Features.Corporate.JobDriver_CorporateSupportTend` | corporate |
| `Mugirl.LordJob_CorporateSupport` | Mugirl | Map | `Mugirl.Features.Corporate.LordJob_CorporateSupport` | corporate |
| `Mugirl.JobDriver_CorporateTalkRepresentative` | Mugirl | Map | `Mugirl.Features.Corporate.JobDriver_CorporateTalkRepresentative` | corporate |
| `Mugirl.JobDriver_CorporateUseComms` | Mugirl | Map | `Mugirl.Features.Corporate.JobDriver_CorporateUseComms` | corporate |
| `Mugirl.MugirlStoryState` | Mugirl | Map | `Mugirl.Features.Incidents.GameComponent_Storyline` | incidents |
| `Mugirl.JobDriver_ReadCourierDiary` | Mugirl | Map | `Mugirl.Features.Incidents.JobDriver_ReadCourierDiary` | incidents |
| `Mugirl.JobDriver_TalkCourier` | Mugirl | Map | `Mugirl.Features.Incidents.JobDriver_TalkToCourier` | incidents |
| `Mugirl.QuestPart_SpawnCourier` | Mugirl | Map | `Mugirl.Features.Incidents.QuestPart_SpawnCourier` | incidents |
| `Mugirl.ChoiceLetter_MugirlFusionInvestment` | Mugirl | Map | `Verse.StandardLetter` | incidents [C23] (Wave R name Mugirl.Features.Incidents.ChoiceLetter_FusionInvestment) |
| `Mugirl.JobDriver_TalkFusionInvestor` | Mugirl | Map | `Mugirl.Features.Incidents.JobDriver_TalkToFusionInvestor` | incidents |
| `Mugirl.MapComponent_MugirlMigration` | Mugirl | Map | `Mugirl.Features.Incidents.MapComponent_Migration` | incidents |
| `Mugirl.QuestPart_MugirlRescueJoin` | Mugirl | Map | `Mugirl.Features.Incidents.QuestPart_JoinOnRescue` | incidents |
| `Mugirl.MapComponent_MugirlPunisher` | Mugirl | Map | `Mugirl.Features.Incidents.MapComponent_Punisher` | incidents |
| `Mugirl.QuestPart_RunawayFarmCompletion` | Mugirl | Map | `Mugirl.Features.Incidents.QuestPart_RunawayFarmCompletion` | incidents |
| `Mugirl.QuestPart_SpawnRunawayFarm` | Mugirl | Map | `Mugirl.Features.Incidents.QuestPart_SpawnRunawayFarm` | incidents |
| `Mugirl.JobDriver_Nuzzle` | Mugirl | Map | `Mugirl.Features.Social.JobDriver_MugirlNuzzle` | incidents |
| `Mugirl.Thought_MugirlOnly` | Mugirl | Map | `Mugirl.Features.Social.Thought_MugirlOnly` | incidents |
| `Mugirl.Thought_Hediff` | Mugirl | Map | `Mugirl.Features.Milk.Thought_MemoryWithHediff` | milk |
| `Mugirl.JobDriver_GatherMilk` | Mugirl | Map | `Mugirl.Features.Milk.JobDriver_GatherMilk` | milk |
| `Mugirl.JobDriver_Breastfeed` | Mugirl | Map | `Mugirl.Features.Milk.JobDriver_ChildDrinkMilk` | milk |
| `Mugirl.JobDriver_DrinkMilkFromMugirl` | Mugirl | Map | `Mugirl.Features.Milk.JobDriver_DrinkMilk` | milk |
| `Mugirl.JobDriver_FeedMilkToDowned` | Mugirl | Map | `Mugirl.Features.Milk.JobDriver_FeedMilk` | milk |
| `Mugirl.JobDriver_CastCharge` | Mugirl | Map | `Mugirl.Features.Abilities.JobDriver_CastCharge` | milk |
| `Mugirl.JobDriver_MugirlDunk` | Mugirl | Map | `Mugirl.Features.Abilities.JobDriver_Dunk` | milk |
| `Mugirl.PawnFlyer_MugirlDunk` | Mugirl | Map | `Mugirl.Features.Abilities.PawnFlyer_Dunk` | milk |
| `Mugirl.Thing_MugirlDunkHead` | Mugirl | Map | `Mugirl.Features.Abilities.DunkHead` | milk |
| `Mugirl.Thing_MugirlDunkProp` | Mugirl | Map | `Mugirl.Features.Abilities.DunkController` | milk |
| `Mugirl.Thing_MugirlThrownObject` | Mugirl | Map | `Mugirl.Features.Abilities.ThrowController` | milk |
| `Mugirl.Mugirl_XenotypeFix_GameComp` | Mugirl | Consumed | — | milk [C23] (Wave R name Mugirl.Features.Xenotype.GameComponent_XenotypeRepair) |
| `Mugirl.SlaveApparel` | Mugirl | Map | `Mugirl.Features.Restraints.RestraintApparel` | restraints |
| `Mugirl.AdvancedSlaveApparel` | Mugirl | Map | `Mugirl.Features.Restraints.AdvancedRestraintApparel` | restraints |
| `Mugirl.BrainWashSlaveApparel` | Mugirl | Map | `Mugirl.Features.Restraints.AdvancedRestraintApparel` | restraints [C23] (Wave R name Mugirl.Features.Restraints.BrainwashHelmetApparel) |
| `Mugirl.JobDriver_CrackBondageGear` | Mugirl | Map | `Mugirl.Features.Restraints.JobDriver_CrackRestraint` | restraints |
| `Mugirl.JobDriver_ForceUnlockSlaveApparel` | Mugirl | Map | `Mugirl.Features.Restraints.JobDriver_ForceUnlockRestraint` | restraints |
| `Mugirl.JobDriver_UnlockSlaveApparelGear` | Mugirl | Map | `Mugirl.Features.Restraints.JobDriver_UnlockRestraint` | restraints |
| `Mugirl.JobDriver_UseItemOn` | Mugirl | Drop | — | restraints [C23] (Wave R name Mugirl.Features.Restraints.JobDriver_UseItemOn) |
| `Mugirl.GameComponent_BrainwashPerformance` | Mugirl | Map | `Mugirl.Features.Restraints.GameComponent_BrainwashPerformance` | restraints |
| `Mugirl.JobDriver_FollowRoper` | Mugirl | Map | `Mugirl.Features.Roping.JobDriver_MugirlFollowRoper` | restraints |
| `Mugirl.JobDriver_RemoveRopeMoo` | Mugirl | Map | `Mugirl.Features.Roping.JobDriver_UnropeMugirl` | restraints |
| `Mugirl.JobDriver_RopeMoo` | Mugirl | Map | `Mugirl.Features.Roping.JobDriver_RopeMugirl` | restraints |
| `Mugirl.JobDriver_RopeToWallRopeHitch` | Mugirl | Map | `Mugirl.Features.Roping.JobDriver_RopeToHitch` | restraints |
| `Mugirl.MapRopingIndex` | Mugirl | Drop | — | restraints [C23] (Wave R name Mugirl.Features.Roping.MapRopingIndex) |
| `Mugirl.JobDriver_MountMugirl` | Mugirl | Map | `Mugirl.Features.Mounting.JobDriver_MountMugirl` | combat |
| `Mugirl.JobDriver_DismountMugirl` | Mugirl | Map | `Mugirl.Features.Mounting.JobDriver_DismountMugirl` | combat |
| `Mugirl.JobDriver_LoadWeaponWheel` | Mugirl | Map | `Mugirl.Features.WeaponWheel.JobDriver_LoadWeaponWheel` | combat |
| `Mugirl.CorporateRuntimeValidation` | Mugirl (validation builds, legacy-extra) | Drop | — | core |
| `Mugirl.CorporateVisualValidation` | Mugirl (validation builds, legacy-extra) | Drop | — | core |
| `MooGirl.AdultContentCleanupGameComponent` | MooGirl | Drop | — | drop |
| `MooGirl.AdvancedSlaveApparel` | MooGirl | Map | `Mugirl.Features.Restraints.AdvancedRestraintApparel` | compose via Mugirl.AdvancedSlaveApparel |
| `MooGirl.BrainWashSlaveApparel` | MooGirl | Map | `Mugirl.Features.Restraints.AdvancedRestraintApparel` | compose via Mugirl.BrainWashSlaveApparel [C16] |
| `MooGirl.GameComponent_BrainwashPerformance` | MooGirl | Map | `Mugirl.Features.Restraints.GameComponent_BrainwashPerformance` | compose via Mugirl.GameComponent_BrainwashPerformance |
| `MooGirl.GameComponent_GhoulRenderingRefresh` | MooGirl | Map | `Mugirl.Rendering.GameComponent_PawnRenderRefresh` | compose via Mugirl.GameComponent_GhoulRenderingRefresh |
| `MooGirl.JobDriver_Breastfeed` | MooGirl | Map | `Mugirl.Features.Milk.JobDriver_ChildDrinkMilk` | compose via Mugirl.JobDriver_Breastfeed |
| `MooGirl.JobDriver_CastCharge` | MooGirl | Map | `Mugirl.Features.Abilities.JobDriver_CastCharge` | compose via Mugirl.JobDriver_CastCharge |
| `MooGirl.JobDriver_CrackBondageGear` | MooGirl | Map | `Mugirl.Features.Restraints.JobDriver_CrackRestraint` | compose via Mugirl.JobDriver_CrackBondageGear |
| `MooGirl.JobDriver_DismountMooGirl` | MooGirl | Map | `Mugirl.Features.Mounting.JobDriver_DismountMugirl` | compose via Mugirl.JobDriver_DismountMugirl |
| `MooGirl.JobDriver_DrinkMilkFromMooGirl` | MooGirl | Map | `Mugirl.Features.Milk.JobDriver_DrinkMilk` | compose via Mugirl.JobDriver_DrinkMilkFromMugirl |
| `MooGirl.JobDriver_FeedMilkToDowned` | MooGirl | Map | `Mugirl.Features.Milk.JobDriver_FeedMilk` | compose via Mugirl.JobDriver_FeedMilkToDowned |
| `MooGirl.JobDriver_FollowRoper` | MooGirl | Map | `Mugirl.Features.Roping.JobDriver_MugirlFollowRoper` | compose via Mugirl.JobDriver_FollowRoper |
| `MooGirl.JobDriver_GatherBodyResources` | MooGirl | NotSaveVisible (abstract in every historical DLL) | — | compose via Mugirl.JobDriver_GatherBodyResources [C16] |
| `MooGirl.JobDriver_GatherMilk` | MooGirl | Map | `Mugirl.Features.Milk.JobDriver_GatherMilk` | compose via Mugirl.JobDriver_GatherMilk |
| `MooGirl.JobDriver_MountMooGirl` | MooGirl | Map | `Mugirl.Features.Mounting.JobDriver_MountMugirl` | compose via Mugirl.JobDriver_MountMugirl |
| `MooGirl.JobDriver_Nuzzle` | MooGirl | Map | `Mugirl.Features.Social.JobDriver_MugirlNuzzle` | compose via Mugirl.JobDriver_Nuzzle |
| `MooGirl.JobDriver_ReadCourierDiary` | MooGirl | Map | `Mugirl.Features.Incidents.JobDriver_ReadCourierDiary` | compose via Mugirl.JobDriver_ReadCourierDiary |
| `MooGirl.JobDriver_RemoveRopeMoo` | MooGirl | Map | `Mugirl.Features.Roping.JobDriver_UnropeMugirl` | compose via Mugirl.JobDriver_RemoveRopeMoo |
| `MooGirl.JobDriver_RopeMoo` | MooGirl | Map | `Mugirl.Features.Roping.JobDriver_RopeMugirl` | compose via Mugirl.JobDriver_RopeMoo |
| `MooGirl.JobDriver_RopeToWallRopeHitch` | MooGirl | Map | `Mugirl.Features.Roping.JobDriver_RopeToHitch` | compose via Mugirl.JobDriver_RopeToWallRopeHitch |
| `MooGirl.JobDriver_TalkCourier` | MooGirl | Map | `Mugirl.Features.Incidents.JobDriver_TalkToCourier` | compose via Mugirl.JobDriver_TalkCourier |
| `MooGirl.JobDriver_UnlockSlaveApparelGear` | MooGirl | Map | `Mugirl.Features.Restraints.JobDriver_UnlockRestraint` | compose via Mugirl.JobDriver_UnlockSlaveApparelGear |
| `MooGirl.JobDriver_UnlockSlaveApperalGear` | MooGirl | Map | `Mugirl.Features.Restraints.JobDriver_UnlockRestraint` | compose via Mugirl.JobDriver_UnlockSlaveApparelGear |
| `MooGirl.JobDriver_UseItemOn` | MooGirl | Drop | — | compose via Mugirl.JobDriver_UseItemOn [C16] |
| `MooGirl.MapRopingIndex` | MooGirl | Drop | — | compose via Mugirl.MapRopingIndex [C16] |
| `MooGirl.MechanoidWorkControlSettings` | MooGirl | settings (contradicts core-plan S1-L7: no import) | — | settings [C16] |
| `MooGirl.MooGirlMod` | MooGirl | settings (contradicts core-plan S1-L7: no import) | — | settings via Mugirl.MugirlMod [C16] |
| `MooGirl.MooGirlSettings` | MooGirl | settings (contradicts core-plan S1-L7: no import) | — | settings via Mugirl.MugirlSettings [C16] |
| `MooGirl.MooGirlStoryState` | MooGirl | Map | `Mugirl.Features.Incidents.GameComponent_Storyline` | compose via Mugirl.MugirlStoryState |
| `MooGirl.MooGirl_ConvertComp` | MooGirl | Drop | — | drop |
| `MooGirl.MooGirl_GameComp` | MooGirl | Map | `Mugirl.Features.Incidents.GameComponent_Storyline` | merge via Mugirl.MugirlStoryState |
| `MooGirl.MooGirl_XenotypeFix_GameComp` | MooGirl | Consumed | — | compose via Mugirl.Mugirl_XenotypeFix_GameComp [C16] |
| `MooGirl.QuestPart_CourierDemand` | MooGirl | Drop | — | drop |
| `MooGirl.QuestPart_MooGirlRescueJoin` | MooGirl | Map | `Mugirl.Features.Incidents.QuestPart_JoinOnRescue` | compose via Mugirl.QuestPart_MugirlRescueJoin |
| `MooGirl.QuestPart_SpawnCourier` | MooGirl | Map | `Mugirl.Features.Incidents.QuestPart_SpawnCourier` | compose via Mugirl.QuestPart_SpawnCourier |
| `MooGirl.SlaveApparel` | MooGirl | Map | `Mugirl.Features.Restraints.RestraintApparel` | compose via Mugirl.SlaveApparel |
| `MooGirl.Thought_Hediff` | MooGirl | Map | `Mugirl.Features.Milk.Thought_MemoryWithHediff` | compose via Mugirl.Thought_Hediff |
| `MooGirl.Thought_MooGirlOnly` | MooGirl | Map | `Mugirl.Features.Social.Thought_MugirlOnly` | compose via Mugirl.Thought_MugirlOnly |
| `MooGirlSkinFix.GameComponent_MooGirlSkinOnce` | MooGirl | Drop | — | drop |

## Appendix B. Save-visible defName map (union, per def type)

**AbilityDef** (2)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_Charge` | `Mugirl_Charge` | MooGirl | Map |
| `MooGirl_Shout` | `Mugirl_Shout` | MooGirl | Map |

**AlienRace.RaceSettings** (1)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_Settings` | `Mugirl_Settings` | MooGirl | Map |

**BackstoryDef** (19)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_Adult` | `Mugirl_Adult` | MooGirl | Map |
| `MooGirl_CattleSlave` | `Mugirl_CattleSlave` | MooGirl | Map |
| `MooGirl_Child` | `Mugirl_Child` | MooGirl | Map |
| `MooGirl_ChildWorker` | `Mugirl_ChildWorker` | MooGirl | Map |
| `MooGirl_Colonist` | `Mugirl_Colonist` | MooGirl | Map |
| `MooGirl_ConstructionWorker` | `Mugirl_ConstructionWorker` | MooGirl | Map |
| `MooGirl_ExperimentalChild` | `Mugirl_ExperimentalChild` | MooGirl | Map |
| `MooGirl_FactoryWorker` | `Mugirl_FactoryWorker` | MooGirl | Map |
| `MooGirl_FarmSlave` | `Mugirl_FarmSlave` | MooGirl | Map |
| `MooGirl_FarmWorker` | `Mugirl_FarmWorker` | MooGirl | Map |
| `MooGirl_GangMember` | `Mugirl_GangMember` | MooGirl | Map |
| `MooGirl_Gladiator` | `Mugirl_Gladiator` | MooGirl | Map |
| `MooGirl_HouseServant` | `Mugirl_HouseServant` | MooGirl | Map |
| `MooGirl_Housekeeper` | `Mugirl_Housekeeper` | MooGirl | Map |
| `MooGirl_Merchant` | `Mugirl_Merchant` | MooGirl | Map |
| `MooGirl_MineWorker` | `Mugirl_MineWorker` | MooGirl | Map |
| `MooGirl_Newborn` | `Mugirl_Newborn` | MooGirl | Map |
| `MooGirl_NightclubStaff` | `Mugirl_NightclubStaff` | MooGirl | Map |
| `MooGirl_UndergroundIdol` | `Mugirl_UndergroundIdol` | MooGirl | Map |

**BodyDef** (2)

| old | new | era | disposition / note |
|---|---|---|---|
| `Mugirl_MechanoidBulldogBody` | — (drop) | Mugirl (retired) | Drop |
| `MooGirlBody` | `MugirlBody` | MooGirl | Map |

**FacialAnimation.BrowTypeDef** (8)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_BrowNormal` | `Mugirl_BrowNormal` | MooGirl | Map |
| `MooGirl_BrowNormal1` | `Mugirl_BrowNormal` | MooGirl | Map |
| `MooGirl_BrowNormal2` | `Mugirl_BrowNormal2` | MooGirl | Map |
| `MooGirl_BrowNormal3` | `Mugirl_BrowNormal3` | MooGirl | Map |
| `MooGirl_BrowNormal4` | `Mugirl_BrowNormal4` | MooGirl | Map |
| `MooGirl_BrowNormal5` | `Mugirl_BrowNormal5` | MooGirl | Map |
| `MooGirl_BrowNormal6` | `Mugirl_BrowNormal6` | MooGirl | Map |
| `MooGirl_BrowNormal7` | `Mugirl_BrowNormal7` | MooGirl | Map |

**FacialAnimation.EmotionTypeDef** (7)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_EmotionNormal` | `Mugirl_EmotionNormal` | MooGirl | Map |
| `MooGirl_EmotionNormal2` | `Mugirl_EmotionNormal2` | MooGirl | Map |
| `MooGirl_EmotionNormal3` | `Mugirl_EmotionNormal3` | MooGirl | Map |
| `MooGirl_EmotionNormal4` | `Mugirl_EmotionNormal4` | MooGirl | Map |
| `MooGirl_EmotionNormal5` | `Mugirl_EmotionNormal5` | MooGirl | Map |
| `MooGirl_EmotionNormal6` | `Mugirl_EmotionNormal6` | MooGirl | Map |
| `MooGirl_EmotionNormal7` | `Mugirl_EmotionNormal7` | MooGirl | Map |

**FacialAnimation.EyeballTypeDef** (8)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_EyeNormal` | `Mugirl_EyeNormal` | MooGirl | Map |
| `MooGirl_EyeNormal1` | `Mugirl_EyeNormal` | MooGirl | Map |
| `MooGirl_EyeNormal2` | `Mugirl_EyeNormal2` | MooGirl | Map |
| `MooGirl_EyeNormal3` | `Mugirl_EyeNormal3` | MooGirl | Map |
| `MooGirl_EyeNormal4` | `Mugirl_EyeNormal4` | MooGirl | Map |
| `MooGirl_EyeNormal5` | `Mugirl_EyeNormal5` | MooGirl | Map |
| `MooGirl_EyeNormal6` | `Mugirl_EyeNormal6` | MooGirl | Map |
| `MooGirl_EyeNormal7` | `Mugirl_EyeNormal7` | MooGirl | Map |

**FacialAnimation.HeadTypeDef** (8)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_HeadNormal` | `Mugirl_HeadNormal` | MooGirl | Map |
| `MooGirl_HeadNormal1` | `Mugirl_HeadNormal` | MooGirl | Map |
| `MooGirl_HeadNormal2` | `Mugirl_HeadNormal2` | MooGirl | Map |
| `MooGirl_HeadNormal3` | `Mugirl_HeadNormal3` | MooGirl | Map |
| `MooGirl_HeadNormal4` | `Mugirl_HeadNormal4` | MooGirl | Map |
| `MooGirl_HeadNormal5` | `Mugirl_HeadNormal5` | MooGirl | Map |
| `MooGirl_HeadNormal6` | `Mugirl_HeadNormal6` | MooGirl | Map |
| `MooGirl_HeadNormal7` | `Mugirl_HeadNormal7` | MooGirl | Map |

**FacialAnimation.LidOptionTypeDef** (7)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_LidOptionNormal` | `Mugirl_LidOptionNormal` | MooGirl | Map |
| `MooGirl_LidOptionNormal2` | `Mugirl_LidOptionNormal2` | MooGirl | Map |
| `MooGirl_LidOptionNormal3` | `Mugirl_LidOptionNormal3` | MooGirl | Map |
| `MooGirl_LidOptionNormal4` | `Mugirl_LidOptionNormal4` | MooGirl | Map |
| `MooGirl_LidOptionNormal5` | `Mugirl_LidOptionNormal5` | MooGirl | Map |
| `MooGirl_LidOptionNormal6` | `Mugirl_LidOptionNormal6` | MooGirl | Map |
| `MooGirl_LidOptionNormal7` | `Mugirl_LidOptionNormal7` | MooGirl | Map |

**FacialAnimation.LidTypeDef** (8)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_LidNormal` | `Mugirl_LidNormal` | MooGirl | Map |
| `MooGirl_LidNormal1` | `Mugirl_LidNormal` | MooGirl | Map |
| `MooGirl_LidNormal2` | `Mugirl_LidNormal2` | MooGirl | Map |
| `MooGirl_LidNormal3` | `Mugirl_LidNormal3` | MooGirl | Map |
| `MooGirl_LidNormal4` | `Mugirl_LidNormal4` | MooGirl | Map |
| `MooGirl_LidNormal5` | `Mugirl_LidNormal5` | MooGirl | Map |
| `MooGirl_LidNormal6` | `Mugirl_LidNormal6` | MooGirl | Map |
| `MooGirl_LidNormal7` | `Mugirl_LidNormal7` | MooGirl | Map |

**FacialAnimation.MouthTypeDef** (8)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_MouthNormal` | `Mugirl_MouthNormal` | MooGirl | Map |
| `MooGirl_MouthNormal1` | `Mugirl_MouthNormal` | MooGirl | Map |
| `MooGirl_MouthNormal2` | `Mugirl_MouthNormal2` | MooGirl | Map |
| `MooGirl_MouthNormal3` | `Mugirl_MouthNormal3` | MooGirl | Map |
| `MooGirl_MouthNormal4` | `Mugirl_MouthNormal4` | MooGirl | Map |
| `MooGirl_MouthNormal5` | `Mugirl_MouthNormal5` | MooGirl | Map |
| `MooGirl_MouthNormal6` | `Mugirl_MouthNormal6` | MooGirl | Map |
| `MooGirl_MouthNormal7` | `Mugirl_MouthNormal7` | MooGirl | Map |

**FactionDef** (4)

| old | new | era | disposition / note |
|---|---|---|---|
| `Mugirl_GiantCorporations_Hostile` | `Mugirl_GiantCorporation` | Mugirl | Map (corporate) |
| `MooGirl_Colony` | `Mugirl_Colony` | MooGirl | Map |
| `MooGirl_GiantCorporations` | — (drop) | MooGirl | Drop |
| `MooGirl_GiantCorporations_Hostile` | `Mugirl_GiantCorporation` | MooGirl | Map |

**GeneDef** (4)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_Charge` | `Mugirl_Charge` | MooGirl | Map |
| `MooGirl_NobelPrize` | `Mugirl_NobelPrize` | MooGirl | Map |
| `MooGirl_Shooting` | `Mugirl_Shooting` | MooGirl | Map |
| `MooGirl_Shout` | `Mugirl_Shout` | MooGirl | Map |

**HairDef** (6)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_Base` | `Mugirl_Base` | MooGirl | Map |
| `MooGirl_EyeCovering` | `Mugirl_EyeCovering` | MooGirl | Map |
| `MooGirl_GloomHair6` | `Mugirl_GloomHair6` | MooGirl | Map |
| `MooGirl_GloomHair7` | `Mugirl_GloomHair7` | MooGirl | Map |
| `MooGirl_Hightail` | `Mugirl_Hightail` | MooGirl | Map |
| `MooGirl_ShoulderLength` | `Mugirl_ShoulderLength` | MooGirl | Map |

**HeadTypeDef** (6)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_Female_A` | `Mugirl_Female_A` | MooGirl | Map |
| `MooGirl_Female_B` | `Mugirl_Female_B` | MooGirl | Map |
| `MooGirl_Female_C` | `Mugirl_Female_C` | MooGirl | Map |
| `MooGirl_Female_D` | `Mugirl_Female_D` | MooGirl | Map |
| `MooGirl_Female_E` | `Mugirl_Female_E` | MooGirl | Map |
| `MooGirl_Female_F` | `Mugirl_Female_F` | MooGirl | Map |

**HediffDef** (44)

| old | new | era | disposition / note |
|---|---|---|---|
| `Hediff_MugirlAgedCheese` | `Mugirl_AgedCheeseEffect` | Mugirl | Map (milk) |
| `Hediff_MugirlCowCake` | `Mugirl_CowCakeEffect` | Mugirl | Map (milk) |
| `Hediff_MugirlMilkPowder` | `Mugirl_MilkPowderEffect` | Mugirl | Map (milk) |
| `Hediff_MugirlMilkTablet` | `Mugirl_MilkTabletEffect` | Mugirl | Map (milk) |
| `Consumed_MugirlMilk` | `Mugirl_MilkVigor` | Mugirl | Map (milk) |
| `Mugirl_BrainWashing_Level1` | `Mugirl_Brainwashing_Level1` | Mugirl | Map (restraints) |
| `Mugirl_BrainWashing_Level2` | `Mugirl_Brainwashing_Level2` | Mugirl | Map (restraints) |
| `Mugirl_BrainWashing_Level3` | `Mugirl_Brainwashing_Level3` | Mugirl | Map (restraints) |
| `Mugirl_BrainWashing_Level4` | `Mugirl_Brainwashing_Level4` | Mugirl | Map (restraints) |
| `Mugirl_BrainWashing_Level5` | `Mugirl_Brainwashing_Level5` | Mugirl | Map (restraints) |
| `Mugirl_FullHeadCovering` | — (drop) | Mugirl (retired) | Drop |
| `Consumed_MooGirlMilk` | `Mugirl_MilkVigor` | MooGirl | Map |
| `Hediff_MooGirlAgedCheese` | `Mugirl_AgedCheeseEffect` | MooGirl | Map |
| `Hediff_MooGirlCowCake` | `Mugirl_CowCakeEffect` | MooGirl | Map |
| `Hediff_MooGirlMilkPowder` | `Mugirl_MilkPowderEffect` | MooGirl | Map |
| `Hediff_MooGirlMilkTablet` | `Mugirl_MilkTabletEffect` | MooGirl | Map |
| `MooGirl_Abasia` | `Mugirl_Abasia` | MooGirl | Map |
| `MooGirl_Armbinder` | `Mugirl_Armbinder` | MooGirl | Map |
| `MooGirl_Blindfold` | `Mugirl_Blindfold` | MooGirl | Map |
| `MooGirl_Boxbinder` | `Mugirl_Boxbinder` | MooGirl | Map |
| `MooGirl_BrainWashing_Level1` | `Mugirl_Brainwashing_Level1` | MooGirl | Map |
| `MooGirl_BrainWashing_Level2` | `Mugirl_Brainwashing_Level2` | MooGirl | Map |
| `MooGirl_BrainWashing_Level3` | `Mugirl_Brainwashing_Level3` | MooGirl | Map |
| `MooGirl_BrainWashing_Level4` | `Mugirl_Brainwashing_Level4` | MooGirl | Map |
| `MooGirl_BrainWashing_Level5` | `Mugirl_Brainwashing_Level5` | MooGirl | Map |
| `MooGirl_Breastfed` | — (drop) | MooGirl | Drop |
| `MooGirl_Charge` | `Mugirl_Charge` | MooGirl | Map |
| `MooGirl_Earplugs` | `Mugirl_Earplugs` | MooGirl | Map |
| `MooGirl_EleShock` | `Mugirl_EleShock` | MooGirl | Map |
| `MooGirl_FullHeadCovering` | — (drop) | MooGirl | Drop |
| `MooGirl_Gagstick` | `Mugirl_Gagstick` | MooGirl | Map |
| `MooGirl_Gauntlet` | `Mugirl_Gauntlet` | MooGirl | Map |
| `MooGirl_Handcuffs` | `Mugirl_Handcuffs` | MooGirl | Map |
| `MooGirl_Lactation` | `Mugirl_Lactation` | MooGirl | Map |
| `MooGirl_Legbinder` | `Mugirl_Legbinder` | MooGirl | Map |
| `MooGirl_MagneticShacklesBondage` | `Mugirl_MagneticShacklesBondage` | MooGirl | Map |
| `MooGirl_MilkHealing` | `Mugirl_MilkHealing` | MooGirl | Map |
| `MooGirl_MotherlyNurture` | `Mugirl_MotherlyNurture` | MooGirl | Map |
| `MooGirl_NurtureAfterglow` | `Mugirl_NurtureAfterglow` | MooGirl | Map |
| `MooGirl_PowerEleShock` | `Mugirl_PowerEleShock` | MooGirl | Map |
| `MooGirl_PowerEleShock_NoMilk` | `Mugirl_PowerEleShock_NoMilk` | MooGirl | Map |
| `MooGirl_Stockings` | `Mugirl_Stockings` | MooGirl | Map |
| `MooGirl_Straitjacket` | `Mugirl_Straitjacket` | MooGirl | Map |
| `MooGirl_Stun` | `Mugirl_Stun` | MooGirl | Map |

**IncidentDef** (4)

| old | new | era | disposition / note |
|---|---|---|---|
| `Mugirl_CourierRaid` | — (drop) | Mugirl | Drop [C20] |
| `Mugirl_WildManWandersIn` | — (drop) | Mugirl | Drop |
| `MooGirl_WildManWandersIn` | — (drop) | MooGirl | Drop [C15] |
| `MooGirl_CourierRaid` | — (drop) | MooGirl | Drop [C20,C15] |

**InteractionDef** (1)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_Nuzzle` | `Mugirl_Nuzzle` | MooGirl | Map |

**JobDef** (28)

| old | new | era | disposition / note |
|---|---|---|---|
| `Job_Nuzzle` | `Mugirl_Nuzzle` | Mugirl | Map (incidents) [C13,C05] |
| `Job_GatherMilk` | `Mugirl_GatherMilk` | Mugirl | Map (milk) [C13] |
| `Job_MugirlMilkingTargetWait` | `Mugirl_MilkingTargetWait` | Mugirl | Map (milk) |
| `Job_Breastfeed` | `Mugirl_ChildDrinkMilk` | Mugirl | Map (milk) |
| `Job_DrinkMilkFromMugirl` | `Mugirl_DrinkMilk` | Mugirl | Map (milk) |
| `Job_FeedMilkToDowned` | `Mugirl_FeedMilk` | Mugirl | Map (milk) |
| `Job_MugirlMilkInteractionWait` | `Mugirl_MilkInteractionWait` | Mugirl | Map (milk) |
| `Job_MugirlDunk` | `Mugirl_Dunk` | Mugirl | Map (milk) [C13] |
| `JobDriver_RopeMoo` | `Mugirl_Rope` | Mugirl | Map (restraints) |
| `JobDriver_RemoveRopeMoo` | `Mugirl_Unrope` | Mugirl | Map (restraints) |
| `UnlockSlaveApparel` | `Mugirl_UnlockRestraint` | Mugirl | Map (restraints) |
| `ForceUnlockSlaveApparel` | `Mugirl_ForceUnlockRestraint` | Mugirl | Map (restraints) |
| `CrackBondageGear` | `Mugirl_CrackRestraint` | Mugirl | Map (restraints) |
| `RopeToBuild` | `Mugirl_RopeToHitch` | Mugirl | Map (restraints) |
| `Job_FollowRoper` | `Mugirl_FollowRoper` | Mugirl | Map (restraints) |
| `Job_MountMugirl` | `Mugirl_Mount` | Mugirl | Map (combat) |
| `Job_DismountMugirl` | `Mugirl_Dismount` | Mugirl | Map (combat) |
| `Job_LoadWeaponWheel` | `Mugirl_LoadWeaponWheel` | Mugirl | Map (combat) |
| `Job_MugirlLancePointCharge` | `Mugirl_LancePointCharge` | Mugirl | Map (combat) |
| `Job_MugirlLanceLineCharge` | `Mugirl_LanceLineCharge` | Mugirl | Map (combat) |
| `GiveSlaveApperalGear` | — (drop) | MooGirl | Drop |
| `GiveSlaveApparelGear` | — (drop) | Mugirl (retired) | Drop |
| `Job_DismountMooGirl` | `Mugirl_Dismount` | MooGirl | Map |
| `Job_DrinkMilkFromMooGirl` | `Mugirl_DrinkMilk` | MooGirl | Map |
| `Job_MountMooGirl` | `Mugirl_Mount` | MooGirl | Map |
| `MooGirl_ReadCourierDiary` | `Mugirl_ReadCourierDiary` | MooGirl | Map |
| `MooGirl_TalkCourier` | `Mugirl_TalkCourier` | MooGirl | Map |
| `UnlockSlaveApperal` | `Mugirl_UnlockRestraint` | MooGirl | Map |

**MentalStateDef** (2)

| old | new | era | disposition / note |
|---|---|---|---|
| `Mugirl_BrainWashing` | — (drop) | Mugirl (retired) | Drop |
| `MooGirl_BrainWashing` | — (drop) | MooGirl | Drop |

**PawnKindDef** (20)

| old | new | era | disposition / note |
|---|---|---|---|
| `AI_GC_Grunts` | `Mugirl_GiantCorp_Grunt` | Mugirl | Map (corporate) |
| `AI_GC_OPC_Enforcers` | `Mugirl_GiantCorp_OPCEnforcer` | Mugirl | Map (corporate) |
| `AI_GC_OPC_Sapper` | `Mugirl_GiantCorp_OPCSapper` | Mugirl | Map (corporate) |
| `AI_GC_Courier` | `Mugirl_GiantCorp_Courier` | Mugirl | Map (corporate) |
| `AI_GC_PMC_Recruits` | `Mugirl_GiantCorp_PMCRecruit` | Mugirl | Map (corporate) |
| `AI_GC_Soldiers` | `Mugirl_GiantCorp_Soldier` | Mugirl | Map (corporate) |
| `AI_GC_Sapper` | `Mugirl_GiantCorp_Sapper` | Mugirl | Map (corporate) |
| `AI_GC_Elites` | `Mugirl_GiantCorp_Elite` | Mugirl | Map (corporate) |
| `AI_GC_MugirlRaider` | `Mugirl_GiantCorp_MugirlRaider` | Mugirl | Map (corporate) |
| `Mugirl_EscapeWildSlave` | `Mugirl_WildMugirl` | Mugirl | Map |
| `Mugirl_PreEscapeWildSlave` | `Mugirl_WildMugirl` | Mugirl | Map |
| `Mugirl_MechanoidBulldog` | — (drop) | Mugirl (retired) | Drop |
| `AI_GC_MooGirlRaider` | `Mugirl_GiantCorp_MugirlRaider` | MooGirl | Map |
| `MooGirl_Beginning_Slave` | `Mugirl_Beginning_Slave` | MooGirl | Map |
| `MooGirl_Colony` | `Mugirl_Colony` | MooGirl | Map |
| `MooGirl_EscapeSpaceSlave` | `Mugirl_EscapeSpaceSlave` | MooGirl | Map |
| `MooGirl_EscapeWanderSlave` | `Mugirl_EscapeWanderSlave` | MooGirl | Map |
| `MooGirl_EscapeWildSlave` | `Mugirl_WildMugirl` | MooGirl | Map [C15] |
| `MooGirl_PreEscapeWildSlave` | `Mugirl_WildMugirl` | MooGirl | Map [C15] |
| `MooGirl_Slave` | `Mugirl_Slave` | MooGirl | Map |

**PipeSystem.ProcessDef** (2)

| old | new | era | disposition / note |
|---|---|---|---|
| `Mugirl_MugirlMilkIntoCheese` | `Mugirl_MilkIntoCheese` | Mugirl | Map (milk,content) |
| `MooGirl_MooGirlMilkIntoCheese` | `Mugirl_MilkIntoCheese` | MooGirl | Map |

**QuestScriptDef** (5)

| old | new | era | disposition / note |
|---|---|---|---|
| `Mugirl_CourierRaid` | `Mugirl_CourierContact` | Mugirl | Map (incidents) |
| `MooGirl_CourierRaid` | `Mugirl_CourierContact` | MooGirl | Map |
| `MooGirl_SlaveOpeningPodCrash` | `Mugirl_SlaveOpeningPodCrash` | MooGirl | Map |
| `MooGirl_SlaveRefugeePodCrash` | `Mugirl_SlaveRefugeePodCrash` | MooGirl | Map |
| `MooGirl_SlaveWandererJoins` | `Mugirl_SlaveWandererJoins` | MooGirl | Map |

**RecipeDef** (34)

| old | new | era | disposition / note |
|---|---|---|---|
| `Make_Mugirl_Bar` | `Make_Mugirl_Bra` | Mugirl (implied) | Map (milk) |
| `Make_Mugirl_Eroticpanties` | `Make_Mugirl_EroticPanties` | Mugirl (implied) | Map (milk) |
| `Make_Mugirl_Hipwrappingskirt` | `Make_Mugirl_PencilSkirt` | Mugirl (implied) | Map (milk) |
| `Make_Mugirl_SpeciallyStockings` | `Make_Mugirl_SpecialStockings` | Mugirl (implied) | Map (milk) |
| `Make_PMC_Helmet` | `Make_Mugirl_PMC_Helmet` | Mugirl (implied) | Map (milk) |
| `Make_PMC_CaptainHelmet` | `Make_Mugirl_PMC_CaptainHelmet` | Mugirl (implied) | Map (milk) |
| `Make_PMC_CombatUniform` | `Make_Mugirl_PMC_CombatUniform` | Mugirl (implied) | Map (milk) |
| `Make_Mugirl_Slaveboots` | `Make_Mugirl_SlaveBoots` | Mugirl (implied) | Map (restraints) |
| `Make_Mugirl_Slaveshackles` | `Make_Mugirl_SlaveShackles` | Mugirl (implied) | Map (restraints) |
| `Make_Mugirl_Slavegauntlet` | `Make_Mugirl_SlaveGauntlet` | Mugirl (implied) | Map (restraints) |
| `Make_Mugirl_Slavestockings` | `Make_Mugirl_SlaveStockings` | Mugirl (implied) | Map (restraints) |
| `Make_PMC_Carbine` | `Make_Mugirl_PMC_Carbine` | Mugirl (implied) | Map (combat) |
| `Make_PMC_Assault_Rifle` | `Make_Mugirl_PMC_AssaultRifle` | Mugirl (implied) | Map (combat) |
| `Make_PMC_DMR` | `Make_Mugirl_PMC_DMR` | Mugirl (implied) | Map (combat) |
| `Make_PMC_LMG` | `Make_Mugirl_PMC_LMG` | Mugirl (implied) | Map (combat) |
| `Make_OPC_Tranquilizer_Pistol` | `Make_Mugirl_OPC_TranquilizerPistol` | Mugirl (implied) | Map (combat) |
| `Make_OPC_Defense_Rifle` | `Make_Mugirl_OPC_DefenseRifle` | Mugirl (implied) | Map (combat) |
| `Make_OPC_Tactical_Shotgun` | `Make_Mugirl_OPC_TacticalShotgun` | Mugirl (implied) | Map (combat) |
| `Make_OPC_Assault_Rifle` | `Make_Mugirl_OPC_AssaultRifle` | Mugirl (implied) | Map (combat) |
| `Make_Mugirl_NeuralCombatShoulders` | — (drop) | Mugirl | Drop |
| `MooGirl_MakeAgedCheese` | `Mugirl_MakeAgedCheese` | MooGirl | Map |
| `MooGirl_MakeBaotaSugar` | `Mugirl_MakeBaotaSugar` | MooGirl | Map |
| `MooGirl_MakeCheese` | `Mugirl_MakeCheese` | MooGirl | Map |
| `MooGirl_MakeCowCake` | `Mugirl_MakeCowCake` | MooGirl | Map |
| `MooGirl_MakeKey_Advance` | `Mugirl_MakeKey_Advance` | MooGirl | Map |
| `MooGirl_MakeKey_Industrial` | `Mugirl_MakeKey_Industrial` | MooGirl | Map |
| `MooGirl_MakeKey_Medieval` | `Mugirl_MakeKey_Medieval` | MooGirl | Map |
| `MooGirl_MakeKey_Spacer` | `Mugirl_MakeKey_Spacer` | MooGirl | Map |
| `MooGirl_MakeMilkCandy` | `Mugirl_MakeMilkCandy` | MooGirl | Map |
| `MooGirl_MakeMilkCreamApple` | `Mugirl_MakeMilkCreamApple` | MooGirl | Map |
| `MooGirl_MakeMilkPowder` | `Mugirl_MakeMilkPowder` | MooGirl | Map |
| `MooGirl_MakeMilkPudding` | `Mugirl_MakeMilkPudding` | MooGirl | Map |
| `MooGirl_MakeMilkTablet` | `Mugirl_MakeMilkTablet` | MooGirl | Map |
| `MooGirl_SmeltBondageApparel` | `Mugirl_SmeltBondageApparel` | MooGirl | Map |

**ThingCategoryDef** (1)

| old | new | era | disposition / note |
|---|---|---|---|
| `Mugirl_Slave` | `Mugirl_Restraints` | Mugirl | Map (restraints) |

**ThingDef** (137)

| old | new | era | disposition / note |
|---|---|---|---|
| `MugirlMilkStain` | `Mugirl_Filth_MilkStain` | Mugirl | Map (milk,content) |
| `Mugirl_Bar` | `Mugirl_Bra` | Mugirl | Map (milk) |
| `Mugirl_Eroticpanties` | `Mugirl_EroticPanties` | Mugirl | Map (milk) |
| `Mugirl_Hipwrappingskirt` | `Mugirl_PencilSkirt` | Mugirl | Map (milk) |
| `Mugirl_SpeciallyStockings` | `Mugirl_SpecialStockings` | Mugirl | Map (milk) |
| `PMC_Helmet` | `Mugirl_PMC_Helmet` | Mugirl | Map (milk,content) |
| `PMC_CaptainHelmet` | `Mugirl_PMC_CaptainHelmet` | Mugirl | Map (milk,content) |
| `PMC_CombatUniform` | `Mugirl_PMC_CombatUniform` | Mugirl | Map (milk,content) |
| `WallRopeHitch` | `Mugirl_WallRopeHitch` | Mugirl | Map (restraints) |
| `Mugirl_Slaveboots` | `Mugirl_SlaveBoots` | Mugirl | Map (restraints) |
| `Mugirl_Slaveshackles` | `Mugirl_SlaveShackles` | Mugirl | Map (restraints) |
| `Mugirl_Slavegauntlet` | `Mugirl_SlaveGauntlet` | Mugirl | Map (restraints) |
| `Mugirl_Slavestockings` | `Mugirl_SlaveStockings` | Mugirl | Map (restraints) |
| `MugirlWashMentalEyesInHead` | `Mugirl_Mote_BrainwashEyes` | Mugirl | Map (restraints,content) [C12] |
| `PMC_Carbine` | `Mugirl_PMC_Carbine` | Mugirl | Map (combat,content) |
| `Bullet_PMC_Carbine` | `Bullet_Mugirl_PMC_Carbine` | Mugirl | Map (combat,content) [C26] |
| `PMC_Assault_Rifle` | `Mugirl_PMC_AssaultRifle` | Mugirl | Map (combat,content) |
| `Bullet_PMC_Assault_Rifle` | `Bullet_Mugirl_PMC_AssaultRifle` | Mugirl | Map (combat,content) [C26] |
| `PMC_DMR` | `Mugirl_PMC_DMR` | Mugirl | Map (combat,content) |
| `Bullet_PMC_DMR` | `Bullet_Mugirl_PMC_DMR` | Mugirl | Map (combat,content) [C26] |
| `PMC_LMG` | `Mugirl_PMC_LMG` | Mugirl | Map (combat,content) |
| `Bullet_PMC_LMG` | `Bullet_Mugirl_PMC_LMG` | Mugirl | Map (combat,content) [C26] |
| `OPC_Tranquilizer_Pistol` | `Mugirl_OPC_TranquilizerPistol` | Mugirl | Map (combat,content) |
| `Bullet_OPC_Tranquilizer_Pistol` | `Bullet_Mugirl_OPC_TranquilizerPistol` | Mugirl | Map (combat,content) [C26] |
| `OPC_Defense_Rifle` | `Mugirl_OPC_DefenseRifle` | Mugirl | Map (combat,content) |
| `Bullet_OPC_Defense_Rifle` | `Bullet_Mugirl_OPC_DefenseRifle` | Mugirl | Map (combat,content) [C26] |
| `OPC_Tactical_Shotgun` | `Mugirl_OPC_TacticalShotgun` | Mugirl | Map (combat,content) |
| `Bullet_OPC_Tactical_Shotgun` | `Bullet_Mugirl_OPC_TacticalShotgun` | Mugirl | Map (combat,content) [C26] |
| `OPC_Assault_Rifle` | `Mugirl_OPC_AssaultRifle` | Mugirl | Map (combat,content) |
| `Bullet_OPC_Assault_Rifle` | `Bullet_Mugirl_OPC_AssaultRifle` | Mugirl | Map (combat,content) [C26] |
| `MugirlMilkFilth` | `Mugirl_Filth_ShockMilk` | Mugirl | Map (content) |
| `MugirlWaterFilth` | `Mugirl_Filth_ShockFluid` | Mugirl | Map (content) |
| `Blueprint_WallRopeHitch` | `Blueprint_Mugirl_WallRopeHitch` | Mugirl (implied) | Map (restraints) |
| `Frame_WallRopeHitch` | `Frame_Mugirl_WallRopeHitch` | Mugirl (implied) | Map (restraints) |
| `Blueprint_Install_WallRopeHitch` | `Blueprint_Install_Mugirl_WallRopeHitch` | Mugirl (implied) | Map (restraints) |
| `Mugirl_NeuralCombatShoulders` | — (drop) | Mugirl | Drop |
| `MuGirl_Gun_Defense_Rifle` | `Mugirl_Gun_Defense_Rifle` | MooGirl | Map |
| `MuGirl_Gun_Flame_Thrower` | `Mugirl_Gun_Flame_Thrower` | MooGirl | Map |
| `MuGirl_Gun_Machine_Pistol` | `Mugirl_Gun_Machine_Pistol` | MooGirl | Map |
| `MuGirl_Gun_Support_Assault_Rifle` | `Mugirl_Gun_Support_Assault_Rifle` | MooGirl | Map |
| `Mugirl_MechanoidBulldog` | — (drop) | Mugirl (retired) | Drop |
| `Bullet_Mugirl_AT23` | `Bullet_Mugirl_AT23_Autocannon` | Mugirl (retired) | Map |
| `Bullet_MooGirl_AR_Standard` | `Bullet_Mugirl_AR_Standard` | MooGirl | Map |
| `Bullet_MooGirl_MP_Standard` | `Bullet_Mugirl_MP_Standard` | MooGirl | Map |
| `Bullet_MooGirl_Militia_Rifle` | `Bullet_Mugirl_Militia_Rifle` | MooGirl | Map |
| `MooGirl` | `Mugirl` | MooGirl | Map |
| `MooGirlMilkFilth` | `Mugirl_Filth_ShockMilk` | MooGirl | Map |
| `MooGirlWashMentalEyesInHead` | `Mugirl_Mote_BrainwashEyes` | MooGirl | Map |
| `MooGirlWaterFilth` | `Mugirl_Filth_ShockFluid` | MooGirl | Map |
| `MooGirl_AgedCheese` | `Mugirl_AgedCheese` | MooGirl | Map |
| `MooGirl_Armbinder` | `Mugirl_Armbinder` | MooGirl | Map |
| `MooGirl_ArmorHelmetRecon` | `Mugirl_ArmorHelmetRecon` | MooGirl | Map |
| `MooGirl_BaotaSugar` | `Mugirl_BaotaSugar` | MooGirl | Map |
| `MooGirl_Bar` | `Mugirl_Bra` | MooGirl | Map |
| `MooGirl_Bikini` | `Mugirl_Bikini` | MooGirl | Map |
| `MooGirl_Bikini_Stocking` | `Mugirl_Bikini_Stocking` | MooGirl | Map |
| `MooGirl_Blindfold` | `Mugirl_Blindfold` | MooGirl | Map |
| `MooGirl_Bodystocking` | `Mugirl_Bodystocking` | MooGirl | Map |
| `MooGirl_Boxbinder` | `Mugirl_Boxbinder` | MooGirl | Map |
| `MooGirl_BrainwashHelmet` | `Mugirl_BrainwashHelmet` | MooGirl | Map |
| `MooGirl_BunnyGirlHeaddress` | `Mugirl_BunnyGirlHeaddress` | MooGirl | Map |
| `MooGirl_BunnyGirlStocking` | `Mugirl_BunnyGirlStocking` | MooGirl | Map |
| `MooGirl_BunnyGirlTop` | `Mugirl_BunnyGirlTop` | MooGirl | Map |
| `MooGirl_Cheese` | `Mugirl_Cheese` | MooGirl | Map |
| `MooGirl_Cheongsam` | `Mugirl_Cheongsam` | MooGirl | Map |
| `MooGirl_ChildCheongsam` | `Mugirl_ChildCheongsam` | MooGirl | Map |
| `MooGirl_Coat` | `Mugirl_Coat` | MooGirl | Map |
| `MooGirl_Combatant_BulletproofVest` | `Mugirl_Combatant_BulletproofVest` | MooGirl | Map |
| `MooGirl_Combatant_Clothes` | `Mugirl_Combatant_Clothes` | MooGirl | Map |
| `MooGirl_Combatant_Helmet` | `Mugirl_Combatant_Helmet` | MooGirl | Map |
| `MooGirl_Combatant_Underwear` | `Mugirl_Combatant_Underwear` | MooGirl | Map |
| `MooGirl_CourierDiary` | `Mugirl_CourierDiary` | MooGirl | Map |
| `MooGirl_CowCake` | `Mugirl_CowCake` | MooGirl | Map |
| `MooGirl_Earplugs` | `Mugirl_Earplugs` | MooGirl | Map |
| `MooGirl_Eroticpanties` | `Mugirl_EroticPanties` | MooGirl | Map |
| `MooGirl_Gagstick` | `Mugirl_Gagstick` | MooGirl | Map |
| `MooGirl_Gambeson` | `Mugirl_Gambeson` | MooGirl | Map |
| `MooGirl_Handcuffs` | `Mugirl_Handcuffs` | MooGirl | Map |
| `MooGirl_Hipwrappingskirt` | `Mugirl_PencilSkirt` | MooGirl | Map |
| `MooGirl_ImmortalFairy` | `Mugirl_ImmortalFairy` | MooGirl | Map |
| `MooGirl_KnightHelmet` | `Mugirl_KnightHelmet` | MooGirl | Map |
| `MooGirl_KnightlyOrnament` | `Mugirl_KnightlyOrnament` | MooGirl | Map |
| `MooGirl_MagneticShackles` | `Mugirl_MagneticShackles` | MooGirl | Map |
| `MooGirl_Mask` | `Mugirl_Mask` | MooGirl | Map |
| `MooGirl_MilitaryDress` | `Mugirl_MilitaryDress` | MooGirl | Map |
| `MooGirl_MilitaryUniform` | `Mugirl_MilitaryUniform` | MooGirl | Map |
| `MooGirl_Milk` | `Mugirl_Milk` | MooGirl | Map |
| `MooGirl_MilkCandy` | `Mugirl_MilkCandy` | MooGirl | Map |
| `MooGirl_MilkCreamApple` | `Mugirl_MilkCreamApple` | MooGirl | Map |
| `MooGirl_MilkPowder` | `Mugirl_MilkPowder` | MooGirl | Map |
| `MooGirl_MilkPoweredGenerator` | `Mugirl_MilkPoweredGenerator` | MooGirl | Map |
| `MooGirl_MilkPudding` | `Mugirl_MilkPudding` | MooGirl | Map |
| `MooGirl_MilkTablet` | `Mugirl_MilkTablet` | MooGirl | Map |
| `MooGirl_MilkingDevice` | `Mugirl_MilkingDevice` | MooGirl | Map |
| `MooGirl_MilkingDeviceHidden` | `Mugirl_MilkingDeviceHidden` | MooGirl | Map |
| `MooGirl_OL` | `Mugirl_OL` | MooGirl | Map |
| `MooGirl_PlateArmor` | `Mugirl_PlateArmor` | MooGirl | Map |
| `MooGirl_PowerArmor` | `Mugirl_PowerArmor` | MooGirl | Map |
| `MooGirl_PowerArmorHelmet` | `Mugirl_PowerArmorHelmet` | MooGirl | Map |
| `MooGirl_RA_Carrying_Equipment` | `Mugirl_RA_Carrying_Equipment` | MooGirl | Map |
| `MooGirl_RA_MechanicBackpack` | `Mugirl_RA_MechanicBackpack` | MooGirl | Map |
| `MooGirl_RA_MechanicBackpack_B` | `Mugirl_RA_MechanicBackpack_B` | MooGirl | Map |
| `MooGirl_RA_MechanicBackpack_C` | `Mugirl_RA_MechanicBackpack_C` | MooGirl | Map |
| `MooGirl_RA_MechanicBackpack_D` | `Mugirl_RA_MechanicBackpack_D` | MooGirl | Map |
| `MooGirl_RA_MechanicRecon` | `Mugirl_RA_MechanicRecon` | MooGirl | Map |
| `MooGirl_RA_PowerArmor` | `Mugirl_RA_PowerArmor` | MooGirl | Map |
| `MooGirl_RA_Unionary` | `Mugirl_RA_Unionary` | MooGirl | Map |
| `MooGirl_ReconArmor` | `Mugirl_ReconArmor` | MooGirl | Map |
| `MooGirl_ReverseBunnySticker` | `Mugirl_ReverseBunnySticker` | MooGirl | Map |
| `MooGirl_ReverseBunnySuit` | `Mugirl_ReverseBunnySuit` | MooGirl | Map |
| `MooGirl_Shirt` | `Mugirl_Shirt` | MooGirl | Map |
| `MooGirl_ShockCollar` | `Mugirl_ShockCollar` | MooGirl | Map |
| `MooGirl_SlaveApparelKey_Advance` | `Mugirl_SlaveApparelKey_Advance` | MooGirl | Map |
| `MooGirl_SlaveApparelKey_Industrial` | `Mugirl_SlaveApparelKey_Industrial` | MooGirl | Map |
| `MooGirl_SlaveApparelKey_Medieval` | `Mugirl_SlaveApparelKey_Medieval` | MooGirl | Map |
| `MooGirl_SlaveApparelKey_Spacer` | `Mugirl_SlaveApparelKey_Spacer` | MooGirl | Map |
| `MooGirl_SlaveApperalKey_Advance` | `Mugirl_SlaveApparelKey_Advance` | MooGirl | Map |
| `MooGirl_SlaveApperalKey_Industrial` | `Mugirl_SlaveApparelKey_Industrial` | MooGirl | Map |
| `MooGirl_SlaveApperalKey_Medieval` | `Mugirl_SlaveApparelKey_Medieval` | MooGirl | Map |
| `MooGirl_SlaveApperalKey_Spacer` | `Mugirl_SlaveApparelKey_Spacer` | MooGirl | Map |
| `MooGirl_Slaveboots` | `Mugirl_SlaveBoots` | MooGirl | Map |
| `MooGirl_Slavegauntlet` | `Mugirl_SlaveGauntlet` | MooGirl | Map |
| `MooGirl_Slaveshackles` | `Mugirl_SlaveShackles` | MooGirl | Map |
| `MooGirl_Slavestockings` | `Mugirl_SlaveStockings` | MooGirl | Map |
| `MooGirl_Spacesuit` | `Mugirl_Spacesuit` | MooGirl | Map |
| `MooGirl_SpacesuitHeadgear` | `Mugirl_SpacesuitHeadgear` | MooGirl | Map |
| `MooGirl_SpeciallyStockings` | `Mugirl_SpecialStockings` | MooGirl | Map |
| `MooGirl_Straitjacket` | `Mugirl_Straitjacket` | MooGirl | Map |
| `MooGirl_Sunglasses` | `Mugirl_Sunglasses` | MooGirl | Map |
| `MooGirl_Suspenders` | `Mugirl_Suspenders` | MooGirl | Map |
| `MooGirl_Uniform` | `Mugirl_Uniform` | MooGirl | Map |
| `MooGirl_Veil` | `Mugirl_Veil` | MooGirl | Map |
| `MooGirl_WarriorCombatArmor` | `Mugirl_WarriorCombatArmor` | MooGirl | Map |
| `MooGirl_WarriorCombatHelmet` | `Mugirl_WarriorCombatHelmet` | MooGirl | Map |
| `MooGirl_WeddingDress` | `Mugirl_WeddingDress` | MooGirl | Map |
| `MooGirl_WhitePanties` | `Mugirl_WhitePanties` | MooGirl | Map |
| `MooGirl_Wool` | `Mugirl_Wool` | MooGirl | Map |

**ThinkTreeDef** (2)

| old | new | era | disposition / note |
|---|---|---|---|
| `MugirlPunisher` | `Mugirl_Punisher` | Mugirl | Map (incidents) [C13] |
| `MooGirlLike` | `MugirlLike` | MooGirl | Map |

**ThoughtDef** (23)

| old | new | era | disposition / note |
|---|---|---|---|
| `Consumed_MugirlMilk` | `Mugirl_ConsumedMilk` | Mugirl | Map (milk) |
| `Consumed_MugirlPudding` | `Mugirl_ConsumedPudding` | Mugirl | Map (milk) |
| `Consumed_MugirlCheese` | `Mugirl_ConsumedCheese` | Mugirl | Map (milk) |
| `Consumed_MugirlAgedCheese` | `Mugirl_ConsumedAgedCheese` | Mugirl | Map (milk) |
| `Consumed_MugirlRichMilkFlavor` | `Mugirl_ConsumedRichMilkFlavor` | Mugirl | Map (milk) |
| `Consumed_MugirlMilkCandy` | `Mugirl_ConsumedMilkCandy` | Mugirl | Map (milk) |
| `Mugirl_BrainWashing_End` | — (drop) | Mugirl (retired) | Drop |
| `Consumed_MooGirlAgedCheese` | `Mugirl_ConsumedAgedCheese` | MooGirl | Map |
| `Consumed_MooGirlCheese` | `Mugirl_ConsumedCheese` | MooGirl | Map |
| `Consumed_MooGirlMilk` | `Mugirl_ConsumedMilk` | MooGirl | Map |
| `Consumed_MooGirlMilkCandy` | `Mugirl_ConsumedMilkCandy` | MooGirl | Map |
| `Consumed_MooGirlPudding` | `Mugirl_ConsumedPudding` | MooGirl | Map |
| `Consumed_MooGirlRichMilkFlavor` | `Mugirl_ConsumedRichMilkFlavor` | MooGirl | Map |
| `MooGirl_AteHay` | `Mugirl_AteHay` | MooGirl | Map |
| `MooGirl_AteNutrient` | `Mugirl_AteNutrient` | MooGirl | Map |
| `MooGirl_BrainWashing_End` | — (drop) | MooGirl | Drop |
| `MooGirl_BrainwashHappy` | `Mugirl_BrainwashHappy` | MooGirl | Map |
| `MooGirl_DrankMilk` | `Mugirl_DrankMilk` | MooGirl | Map |
| `MooGirl_FedMilk` | `Mugirl_FedMilk` | MooGirl | Map |
| `MooGirl_Nuzzled` | `Mugirl_Nuzzled` | MooGirl | Map |
| `MooGirl_Roped` | `Mugirl_Roped` | MooGirl | Map |
| `MooGirl_Roper` | `Mugirl_Roper` | MooGirl | Map |
| `MooGirl_SingleshotWeaponAversionUnhappy` | `Mugirl_SingleshotWeaponAversionUnhappy` | MooGirl | Map |

**TraitDef** (6)

| old | new | era | disposition / note |
|---|---|---|---|
| `Mugirl_BrainWashPeace` | `Mugirl_BrainwashPeace` | Mugirl | Map (restraints,content) |
| `Mugirl_BrainWashObey` | `Mugirl_BrainwashObey` | Mugirl | Map (restraints,content) |
| `MooGirl_BrainWashObey` | `Mugirl_BrainwashObey` | MooGirl | Map |
| `MooGirl_BrainWashPeace` | `Mugirl_BrainwashPeace` | MooGirl | Map |
| `MooGirl_NurturedByMooGirl` | `Mugirl_NurturedByMugirl` | MooGirl | Map |
| `MooGirl_SingleshotWeaponAversion` | `Mugirl_SingleshotWeaponAversion` | MooGirl | Map |

**WorkGiverDef** (1)

| old | new | era | disposition / note |
|---|---|---|---|
| `Work_GatherMilk` | `Mugirl_GatherMilk` | Mugirl | Map (milk) [C13] |

**XenotypeDef** (1)

| old | new | era | disposition / note |
|---|---|---|---|
| `MooGirl_Xenotype` | `Mugirl_Xenotype` | MooGirl | Map |
