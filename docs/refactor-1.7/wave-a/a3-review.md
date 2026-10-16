# A3 adversarial review (tag a3r): spec finalisation, linter, RenameApply

In this review, `<S>` means the scratchpad, and `<W>` means `%TEMP%\mg17-agent\a3r`, which holds my private copies. I wrote nothing to the repo or to A3's files.

## What I re-ran (independently)
- I built the tool from the current sources with `dotnet build <S>\tools\RenameApply\RenameApply.csproj -o <W>\tool --artifacts-path <W>\tool-art`. It reported 0 warnings and 0 errors.
- I ran R on a fresh mirror of `tooltest/final/snapshot`, then R2 on that result (`<W>\workR`, `<W>\workR2`, `--applied`).
  - Both trees came out byte-identical to A3's `postR`/`postR2` (`diff -rq -x obj -x bin`: 0 differences). The output is deterministic.
  - The binding check covered 110403 names in R and 110272 names in R2, with 0 mismatches.
- I ran `run-checks.ps1 -Root <W>\workR2 -Out <W>\out-postR2`:
  - The mod built with 0 errors and 0 warnings.
  - Tests passed 473/473.
  - NegativeControl had 14 warnings, the same as the base.
  - GameValidation had 4 errors that were already in the snapshot.
- I ran ShaderCheck on `<W>\workR2`: ALL CHECKS PASSED.
- I copied the CURRENT repo (with the A1/A2/A4/A5 edits) to `<W>\repoNow`.
  - The R dry run had 0 errors. All 10 projects had 0 new compile errors; GameValidation now compiles. The binding check covered 140672 names with 0 mismatches.
  - A real R run followed by an R2 dry run had 0 errors and 0 new compile errors (140541 names).
  - `lint_spec.py <repo> <W>\repoNow` (with `LINT_SPEC_ROOT` pointing at a private copy) found 0 errors.
- Running R again on the post-R tree is refused: 850 errors, nothing written. This is safe, but it is not a no-op.
- `agent-build.ps1 -Tag a3r` on the repo: the mod has 0 warnings; the tests build and pass (589, 0 failed).
- Decompile of the 1.7 code (`DefOfHelper.BindDefsFor`) confirms that `[DefAlias]` is honoured (C05 is OK). It also confirms these engine behaviours:
  - `Game.FillComponents` and `FloatMenuMakerMap.Init` follow the metadata order of `AllSubclassesNonAbstract`.
  - `XmlAssetsInModFolder` applies patches in file order.
  - `ScribeExtractor.SaveableFromNode` logs "Could not find class" for unknown `ModSettings` `Class=` values.

## Findings

1. **major: the manifest generator applies Wave S dispositions to Wave R types that still exist.**
   - Evidence: `_gen/applied_to_manifest.py:26-33` (transitional rows) and `:42-46,63` (hand rows override generated rows unconditionally). In `upgrade-rows.sample.json` the following old names point at the wrong target:
     - `Mugirl.BrainWashSlaveApparel` → `AdvancedRestraintApparel`. R renames it to `BrainwashHelmetApparel`, which is still the helmet's `thingClass`.
     - `ChoiceLetter_MugirlFusionInvestment` → `Verse.StandardLetter`. The type still exists, so open investment letters lose their choices.
     - `Mugirl_XenotypeFix_GameComp` → drop ("consumed" by a step that does not exist yet).
     - `MapRopingIndex` and `JobDriver_UseItemOn` → drop. Both types still exist.
   - Also, 46 rows have `module:""` (K1 expects an owner).
   - Fix: make the generator wave-aware. Emit the R target while the new type exists in the assembly. Apply hand and transitional dispositions only once the type is gone, and assert this against the built DLL. Fill `module` from the spec or the hand row.

2. **major: Wave R and R2 change reflection, def and patch order, and A3 neither checked nor reported it (F7, §2.4).**
   - My metadata scan of `out-base` against `out-postR2/MugirlRace.dll` found changed order in four families:
     - GameComponent: new games get a different component order, which changes tick and save order.
     - MapComponent.
     - FloatMenuOptionProvider: the right-click option order changes.
     - StaticConstructorOnStartup: `ExternalBreastHediffDefs` moves from first to last.
   - The def load order changes for JobDef (89 inversions), ThingDef (1584, from the date renames and the milking split), ThoughtDef (22) and SoundDef (10).
   - Patch order: `BrainwashHelmet_TraderStock_Patch.xml` now applies before `MilkFood_TraderStock_Patch.xml`. This reorders `stockGenerators` of `Caravan_Outlander_Exotic` and `Orbital_Exotic`, which shifts the stock RNG.
   - Fix: add an order-family and def/patch-order diff to the harness. Then either keep the order (for example, prefix the split file names to keep their sort position) or list each change for parity-accept sign-off in the report.

3. **major: after R2 not every file has exactly one Wave S owner (decisions §3, C09–C11).**
   - Matching `ownedSourceFolders` and `ownedContentFiles` against `postR2` leaves 41 production files unowned. Examples:
     - The split targets `Thoughts_{Mugirl,Restraints,Roping}.xml`, `BrainwashHelmet_TraderStock_Patch.xml`, `Restraints_ElectricSmelter_Patch.xml` and `Roping_CaravanPackingSpot_Patch.xml`.
     - `Recipes_Mugirl.xml`, which the report assigns to restraints but `restraints.json` does not list.
     - `Mugirl_ThinkTreeDefs.xml`, which C11 assigns to content.
     - All `Defs/Misc/Corporate*.xml` files.
   - Meanwhile, 36 owned paths name files that do not exist after R2 (Wave S target names such as `Defs/Corporate/*.xml`, `MugirlPatch.xml`, `JobDefs.xml` (zh)).
   - Fix: publish `spec/final/ownership-postR2.json` (each file mapped to one module) and add a linter rule that every Source/1.7 file of the post-R2 tree matches exactly once.

4. **major: binding decision C12 is violated.** `_gen/gen_R.py:300` forces `saveVisible=False` for `MugirlWashMentalEyesInHead`, and `R/restraints.json:1248` follows. decisions.md C12 says motes are `saveVisible=true`. This drops the row from the upgrade map. Fix: delete the override and regenerate.

5. **major: R renames the settings class, but nothing produces the settings map.**
   - `Mugirl.MugirlSettings` → `Mugirl.Core.MugirlSettings` (`R/core.json`) has `saveVisible:false`. `applied_to_manifest.py` emits no K1 `settings` block, and `upgrade-hand-rows.json` has no row for it.
   - From the moment R lands, every existing `Config/Mod_mugirl_MugirlMod.xml` logs "Could not find class Mugirl.MugirlSettings" on each startup (verified in `SaveableFromNode`). The R in-game and smoke gate on a real profile would fail.
   - The class comment "类名…不能改名" (the class name must not be renamed) becomes false.
   - Fix: add `settings.fileClass {old,new}` to the hand rows and emit it. Ask the integrator to land A1's settings upgrade with R, or defer this namespace move to S.

6. **major: the tool does not verify "applied log == spec" (gate §2.5 item 5), and one file rename is silently lost.**
   - The spec gives `newFile …/HediffCompProperties_ReduceWillOrEnslave.cs`. The file stays `…ReduceWillorEnslave.cs` in both postR2 trees and in the applied log, with no finding.
   - Cause: `CSharp/Phase0.cs:64` (`SamePath` uses `OrdinalIgnoreCase`) together with lines 119/150 (the primary type does not match the file stem). The case-only target is treated as the same file.
   - Fix: honour case-only renames, and add a final pass that checks every spec `newFile`, fileMove, def, type and split against the result with exact case. Any mismatch should be an error.

7. **minor: the linter claims rules it does not implement** (`lint_spec.py:12-19` against lines 223-247):
   - L14 "no DefOf type rows" is not checked, and R keeps 5 DefOf rows.
   - L15 C04 placement is not checked.
   - L07 does not check patch parameters, and it misses `[HarmonyPrefix]`/`[HarmonyPostfix]`-attributed methods. For example, `Harmony_Building_Door_PawnCanOpen.AllowFollowRoperJob` could be moved unflagged. The negative test fires only because the member is missing.
   - L08 ignores attributes on the declaration line, and `member_source` can match a call site.
   - Nothing checks `defNamePattern`/`xmlField` frozen entries, ManeuverDef/ToolCapacityDef freezes (decisions §2 Load IDs), or R2 `createTypes`.
   - `foreign-types.tsv` lacks the UnityEngine/FA/VEF types.
   - Fix: implement these checks or remove the claims from the docstring and `lint-report.json`, and add negative cases for L08 and L07 attributes.

8. **minor: derived def names are over-generated.** `RenameModel.cs:66-77` emits all 7 prefixes for every ThingDef rename, giving 245 rows such as `Corpse_/Meat_/Administer_/Blueprint_Mugirl_Bullet_*`, none of which exist. These rows go into the manifest and will fail a "target exists" check. Fix: derive a prefix only from the def's shape (recipeMaker, race, buildable, drug).

9. **minor: the tool's built-in guard is stale.** `RenameApply/frozen-names.json` (01:53) is the design §2 list only. It lacks the §5 names (`Mugirl_Lactation`, `MugirlMilkFilth`, `Job_MugirlMilkingTargetWait`, FA `Mugirl_*Normal*`, `Mugirl_cry`/`lovin`, `Ears`, `Mugirl_Settings`, XML fields and others). Fix: generate it from `public-api.json`, or read that file directly.

10. **minor: `Apply` is not transactional.** In `Program.cs:201-233` writes, deletes and rmdirs happen in place, and Source/, 1.7/ and tools/ are untracked. An IO failure leaves a half-renamed tree. Fix: stage the result plus a backup manifest and restore on failure. At minimum, USAGE should require a fresh tree archive right before the real run.

11. **minor: owner-migration-list.md is incomplete.** WhiteFluff `Source/Validation/RuntimeChecks.cs:206` compares `GetType().Name` with `"CompMooMilkable"`/`"Comp_WeaponWheel"`, which become `CompMilkProduction`/`CompWeaponWheel`, so the check silently passes. `HasMountedPawn` is named in decisions §5 but appears in neither list.

12. **minor: gaps in public-api.json.**
    - The Harmony-bound parameter name `pawn` of `IsMugirl` (WhiteFluff `Postfix(Pawn pawn, ref bool __result)`) is not recorded.
    - XPath structural dependencies are not recorded: KindSeal `alienRace/raceRestriction/{apparelList,whiteApparelList}`, Subspecies `Mugirl_Settings/pawnKindSettings/startingColonists/li/pawnKindEntries` and `Mugirl_Shout/comps`.
    - The scanner skips `Assemblies/`: the RavenRace 1.7 DLL uses `Mugirl_Charge` and `Mugirl_Milk`. Both are already frozen.

13. **minor: generated code carries false comments.**
    - The `IncidentsDefOf.cs:3` header says "全部来自原版 Core" (all from vanilla Core), but it now holds Mugirl defs.
    - The `PublicApi/MountedPawnUtility.cs:6-7,10` header describes the moved queries and adds a "历史入口" (historical entry) comment.
    - In `MugirlSettings.cs:5`, see 5.
    - Moved members keep mixed `public` modifiers inside `internal` classes (`PawnPredicates`, `MountUtility`).
    - The overloads of `IsHostileToPlayer` are split.
    - Fix: add R2 `textEdits` for the headers, or put them on the deferred-S list.

14. **minor: Core holds feature defs.** Applying C04 to the content-owned `TraitDefs.xml`, `LifeStages.xml` and `Items_Filth.xml` puts `Mugirl_CorporateDiehard`, `Mugirl_BrainwashObey`, `Mugirl_NurturedByMugirl`, `Mugirl_Teenager` and `Mugirl_Filth_MilkStain` into `Core.MugirlDefOf`. Fix: split these files by feature in R2, as was done for `Thoughts_Traits`.

15. **minor: evidence provenance.**
    - The tool changed after the final end-to-end run: `MemberOps.cs` 13:29:48 and the DLL 13:29:52, against the R2 apply at 13:27:34.
    - `out-postR2/shadercheck.log` (13:23) comes from an earlier R2 iteration.
    - I re-ran both and the results are unchanged, but the report should say this.
    - The build output goes to `<S>\tools\bin\RenameApply`, which is outside the owned `tools\RenameApply\**`.

16. **minor: moogirl-era markers.** `defPatterns` contains the unanchored `"MooGirl"`, which is broader than D3's prefix rule. `defsNotMatchingPatterns` lists `AttackMelee_MooGirl`, which that pattern does match. Fix: anchor the patterns and recompute.

17. **minor (REQUEST A5):** on the current tree the tool flags new GameValidation short-name literals `"Comp_WeaponWheel"` and `"SlaveApparel"` (`Suites/Fixture/ContentSweepSuite.cs`, `Core/SaveFacts.cs`). A5 must confirm that these name legacy types deliberately; otherwise they break silently after R.

## Not found
- No A3 edits in the repo: there is no `PublicApi/` folder and the baselines are not appended.
- C26, C21, C18, C25 and D4 aliases are correct.
- The root namespace after R2 contains only PublicApi.
- No R row renames a frozen name.
- The DefInjected handles (`HediffComp_BrainwashPerformance` ×78 and others) are renamed correctly.
