# A3 report: spec finalisation, linter, tool

Root: `<scratchpad>` = `C:\Users\Fishiv\AppData\Local\Temp\claude\c--Program-Files--x86--Steam-steamapps-common-RimWorld-Mods-mugirl\de907362-7ceb-4fb9-ba6b-b0a761817e4f\scratchpad`. Nothing in the repo was written.

## Built
- `spec/final/R/*.json` (7 modules): 455 types, 24 fileMoves, 101 defs, 9 abstract Names, 2 DefAlias memberRenames, `PublicApi/XmlAliases.cs` (D4 aliases), a baseline append.
- `spec/final/R2/*.json`: DefOf dissolution (150 moves, 48 deletes, 15 new DefOfs, 14 dissolved, owner-of-the-Def rule), member moves (PlayerFactionUtility, TickUtility, GeneratedPawnUtility to Incidents, MountedPawnUtility to Core.PawnPredicates/MountUtility), closed PatchModule enum (27 members) plus attribute rewrites, XML/DefInjected splits (JobDefs, Thoughts_Traits, sounds, milking apparel, MugirlPatch.xml into 5 files with PatchGovernance headers), Misc_Gameplay Keyed split (357 keys), 5 date-named file renames, control updates.
- `spec/final/public-api.json` (146 frozen entries, usedBy file:line plus production/validation) and `owner-migration-list.md` (3 dependent call sites).
- `spec/final/moogirl-era-markers.json` (39 types, 9 components, 368 defs), `deferred-S.json`, `upgrade-hand-rows.json`, `upgrade-rows.sample.json`, `rename-applied-R/R2.sample.json`.
- `spec/final/_gen/` generators plus `lint_spec.py` (L01 to L15).
- `tools/RenameApply/` R2 sections, F5 gaps closed, applied log. `USAGE.md` is rewritten.
- Harness: `tooltest/final/{pipeline,run-tool,run-checks,selftest}.ps1`, `compare_validation.py`.

## Verification (fresh copy `tooltest/final/snapshot` to `postR` to `postR2`)
- `pipeline.ps1 -Stage all`: lint 0 errors (2 info). R and R2 applied with 0 new compile errors in all 8 projects. Binding check found 0 mismatches (R 110403 names, R2 110272).
- `run-checks.ps1 -Root postR2 -Out out-postR2`: mod, tests, validator, ShaderCheck and NegativeControl built with 0 errors. Warnings match the base. Unit tests passed 473/473, the same as the base.
- `selftest.ps1`: 114 rules, 0 problems. The ShaderCheck exe reported ALL PASSED.
- `compare_validation.py base postR2` (after name mapping): 0 findings lost. Only C.save-types and C.save-enums changed (60 findings: legacy 1.6-schema checks of renamed save types).
- `lint_spec.py` negative test (`tooltest/lint-negative`): 19 errors across L01 to L15.
- Tool build: 0 warnings, 0 errors.

## Deviations
- Keyed renames and non-date content renames moved to S (`deferred-S.json`). Wave R stays identity-preserving.
- SlaveApparelDef and MountedPawnUtility keep their names as public aliases rather than renames (D5).
- DefOf extraction rows stay in R. Recipes_Mugirl.xml is not split because every def in it belongs to restraints.
- TraitDefs, LifeStages and Items_Filth DefOf fields go to the Core MugirlDefOf by the owner rule.
- EquipmentTransferScope moved into R (C21). Mugirl_Dunk and Mugirl_Nuzzle use DefAlias (C05).
- 19 dead Misc_Gameplay keys go to prefix files rather than being deleted.
- The snapshot was taken mid-Wave-A.

## REQUESTS
- A2: accept the alias types via the baseline append. Treat save-types/save-enums as legacy until C.save-upgrade. Take `public-api.json` as the frozen input. R2 textEdits update DataViolations/expected-failures. PatchMetadataCheck must use the PatchModule enum.
- A1: build the manifest from `moogirl-era-markers.json`, `applied_to_manifest.py` rows and `upgrade-hand-rows.json`.
- Combat: add NoInlining to IsMugirl in S. Restraints: rename SlaveApparelDef through its alias in S.
- Integrator: run R, then R2, on the integrated tree with no concurrent builds. Note that GameValidation does not compile at present (4 baseline errors from A5's edits).
- All owners: review `deferred-S.json`.

## Open risks
- The snapshot was taken mid-edit, so the specs need re-linting and a dry-run on the integrated tree.
- GameValidation compile is unverified.
- Design-time builds write `obj/`.
- DefInjected field-segment renames are not checked against their owner. XML field renames were not exercised.
- C.save-* failures are expected until C.save-upgrade exists.
- Nothing was tested in game.

---

# A3 fixer report (tag a3)

`<S>` = scratchpad. Nothing in the repo was written. Every number below comes from one fresh run: `tooltest/final/pipeline.ps1 -Stage all` (fresh copies; log `pipeline-final.log`), followed by `-Stage checks` with clean output folders (`pipeline-checks.log`). Both runs used the final tool build in `tools/RenameApply/bin`.

## Fixes (review numbers)
1. **Manifest rows.** `applied_to_manifest.py` is now wave-aware. Each legacy name maps to what the post-R2 DLL or Defs actually contain:
   - BrainWashSlaveApparel → BrainwashHelmetApparel.
   - The Wave S dispositions of 5 types and 11 defs move to `pendingWaveS`.
   - Every row has a module.
   - Roots are runtime roots (`legacyRoot` is kept when it differs).
   - It emits the K1 `settings` block.
   - Targets are checked against the DLL and the Defs: 55 types, 127 defs, 0 errors.
2. **Order.** New `order_diff.py` covers reflection families, def load order per root, and patch operations on shared targets. The brainwash patch is now named `Restraints_TraderStock_Patch.xml`, so 0 swapped patch pairs remain. The rest is for sign-off below.
3. **Ownership.** New `ownership-postR2.json` and linter rule L16: 2185 files, each with exactly one owner.
4. **C12.** The mote stays `saveVisible=true`, and its row is present.
5. **Settings.** Added a hand settings row and the K1 block, and corrected the false `MugirlSettings` comment (R textEdit).
6. **Conformance check.** New `Conformance.cs` checks "applied == spec" with exact spelling: R 987 rows, R2 1073 rows. The case-only rename to `…ReduceWillOrEnslave.cs` now lands. The harness now copies fresh, because robocopy keeps the old case.
7. **Linter.** L07, L08, L14 and L15 (C04 placement) are now real checks. New coverage: patterns, xmlFields, ManeuverDef/ToolCapacityDef, createTypes, and Unity, FA and VEF types. Negative test: 30 of 30 expected findings fire.
8. **Derived names.** They now follow each def's shape: 22 rows.
9. **Frozen names.** The tool reads `public-api.json`.
10. **Transactional writes.** Writes go through a backup with rollback and `--restore`. Tested: an injected IO failure exits 3 and leaves the tree identical; a restore after success leaves it identical.
11. **Migration list.** Adds the `"CompMooMilkable"`/`"Comp_WeaponWheel"` literals and the CompMount members, including HasMountedPawn.
12. **public-api.json.** Adds the Harmony-bound parameter `pawn`, 4 xpathStructure entries, and the DLLs in dependents' load folders (RavenRace).
13. **Comments and modifiers.** Fixed the headers and comments, made moved members `internal`, and kept the overloads together (`order: spec`).
14. **Split files.** TraitDefs.xml and Items_Filth.xml are split, so their DefOf fields move to the Corporate, Restraints and Milk DefOfs.
15. **Tool location.** The tool now builds into its own folder; the old `tools/bin` is removed. All evidence comes from the final build.
16. **Markers.** The patterns are anchored.

Also new: R and R2 remove 391 and 8 paid-off `structure-debt.allow.txt` root lines (`baselineRemovals`).

## Rejected
- **14, partly:** `Mugirl_Teenager` stays in `Core.MugirlDefOf`. LifeStages.xml is part of the race definition (C11, content), so under C04 Core is the correct owner.

## Verification (base = snapshot of the current repo)
- **Lint:** 0 errors.
- **R and R2:** exit 0, 0 new compile errors in 10 projects, and the binding check found 0 mismatches (R 143603 names, R2 143472).
- **Builds:** 0 warnings. NegativeControl has 14 warnings in all three trees.
- **Tests:** 589 passed in base, postR and postR2.
- **ShaderCheck:** all passed. **Selftest:** 179 rules, 0 problems.
- **Validator:** 0 findings were lost. Two checks changed:
  - B.namespaces: 3 new findings, for the alias types.
  - C.save-upgrade: 775 new failures, because no K1 rows are wired in yet.
- **Order changes for sign-off:**
  - GameComponent, MapComponent, FloatMenuOptionProvider and StaticConstructorOnStartup are ordered differently. They are independent of each other; only the right-click menu order is visibly different.
  - Def order changed for JobDef, ThingDef, ThoughtDef, SoundDef, TraitDef and FleckDef. None of these roots is saved through a DefMap, and short hashes are assigned in defName order.

## Requests
- **Integrator + A1:** land R together with the K1 rows from `applied_to_manifest.py`, including `RenameSettingsClass("Mugirl.MugirlSettings","Mugirl.Core.MugirlSettings",1)`.
- **A2:**
  - Find the source of types that have no methods (the alias types), for example through the PDB TypeDefinitionDocuments record.
  - Accept the new public-api kinds and `legacyRoot`.
- **A5:** replace the `"Comp_WeaponWheel"` and `"SlaveApparel"` name literals in `ContentSweepSuite.cs` (around lines 550 and 608).

## Open risks
- The specs must be regenerated and linted again on the integrated tree.
- Nothing was tested in game.
