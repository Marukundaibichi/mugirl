# A1 report: save-upgrade framework (tag a1)

## Built
- `Source/MugirlRace/SaveUpgrade/` (88 files, ~5.6k lines, namespace `Mugirl.SaveUpgrade`):
  - Contracts: `SaveUpgradeManifest.ToJson` (K1), `SaveUpgradeDiagnostics.LastReportJson` (K2), `SaveUpgradeBootstrap.Initialize` (K4), `SaveSchema`, `SaveUpgradeRecord`, and `SaveUpgradeOffline.Run` with a public `IDefCatalog` for A2's `--save-corpus` mode.
  - `Engine/Tables`: the row model, `OwnerSelector`, `UpgradeModuleBuilder`/`UpgradeTableBuilder` (validates every problem in one pass), implied-def rules, and `[SaveUpgradeModule(name, order)]` discovery, which needs no edits to central files.
  - `Engine/Document`: a single-traversal `SaveIndex`, `EditPlan` with `DomEditor` (per-parent rebuild), `ContainerRules` (removable containers, dictionary pairs, parallel groups, otherwise null-out), `SaveDocument`.
  - `Engine/Steps`: plan→apply step bases. Framework steps run in this order: MooGirlEra, Types, Defs, Lords, LoadIds, Labels (enums, values, defaults).
  - `Engine/Runtime`, `Engine/Settings`: session runtime, backup, notices, in-flight job policy, installation check, `DefNameResolver`, settings upgrade.
  - `Hooks/`: 6 patch classes, each with `[MugirlPatch("SaveUpgrade", …)]`.
- `Source/MugirlRace/Core/GameComponent_Mugirl.cs` and `GameLifecycle{,Event,Hook,HookAttribute,Registry}.cs`.
- `Source/Tests/MugirlRace.Tests/SaveUpgrade/`: 17 files, 93 tests. The golden runner reads `Golden/<StepId>/{before,after}.xml`; Wave A has no fixtures.

## Verification
- `agent-build.ps1 -Tag a1 -Paths SaveUpgrade,Core/GameComponent_Mugirl.cs,Core/GameLifecycle`: build succeeded, 0 errors, 0 warnings (whole project: 0).
- `agent-build.ps1 -Tag a1t -Project Tests/MugirlRace.Tests/MugirlRace.Tests.csproj -AllWarnings -RunTests`: 0 warnings; 566 tests, 0 failed, 1 not run (the explicit benchmark).
- `MugirlRace.Tests.exe -explicit only -class Mugirl.Tests.SaveUpgrade.PerformanceBenchmarks`: a synthetic 60 MB save (129k things, 121k edits) upgrades in 1.06–1.13 s (index 0.53 s, load-ID pass 0.3 s).
  - The first version took 19 s, because `XmlNode.RemoveChild` and `ReplaceChild` are O(n) per call. Fixed by rebuilding each parent's child list once.
- Read-only offline run over the owner's 11 real saves, using a scratch harness in a temp mirror that has since been deleted:
  - No failures.
  - S1–S4 are detected as MooGirl era. The stripped items are 2 components plus 151 filter entries (S1), confirmed by an independent scan.
  - S5–S11 report Upgraded with 0 actions (the tables are empty).
  - With stress tables, no stale `Thing_Mugirl<n>` remains.
  - S7 (67 MB): the index pass takes 34 ms.
- Engine facts were checked against the decompiled 1.7.4977 game: the `LoadGame`/`FinalizeInit`/`InitNewGame` order, `BackCompatibleDefName`, the `GetSettingsFilename` formula, `IDNumberFromThingID`, the verb and bill ID formats, the implied-def generators, `Lord.ExposeData_StateGraph`, and the `JobQueue` API.

## Deviations
1. **Def database root.** `ThingDef`'s base type is the abstract `BuildableDef`, so I used the `AllDefTypesWithDatabases` rule (the topmost non-abstract type) instead of "walk until `BaseType == Def`".
2. **Save file name.** `Game.LoadGame` cannot see it, so an extra prefix on `SavedGameLoaderNow.LoadGameFromSaveFileNow` captures it for the `.bak`.
3. **Lifecycle registration.** It is declarative (`[GameLifecycleHook(event, Order=n)]`), sorted by (Order, method id). The files live in `Core/`, as the task's ownership states, not in core-plan's `Core/Lifecycle/`.
4. **Implied defs.** I added `Make_…Bulk`, `Administer_` and `Techprint_`. Implied rows are not written to the manifest `defs`.
5. **Settings.** The target schema is the highest row schema, so with no rows the settings file is not touched. `settings.fileClass` is `null` when no class rename is registered.
6. **Lord `ResetGraph`** writes `curLordToilIdx=0`. Removing the label makes vanilla log an out-of-bounds error.
7. **The Phase B job policy also runs after a DOM failure.** A legacy session counts as verified only if phases A and B ran.
8. **Thing recognition** also accepts ids that were already rewritten to the renamed def.
9. **`WasDefRemoved`/`RemovedDefs` is not patched.** Leftover references to dropped defs get vanilla's error.

## Requests
- **Integrator/A4:**
  - Call `SaveUpgradeBootstrap.Initialize(this)` in the `MugirlMod` constructor before `GetSettings`.
  - Add `SaveUpgrade` to the closed `PatchModule` set.
  - Accept the new `GameComponent` in the component-order baseline.
- **Language owner:** add EN and ZH Keyed entries for `Mugirl.SaveUpgrade.Failed`, `NewerSave`, `MooGirlEra` and `NotInstalled` (each takes `{0}`; English fallbacks are in `SaveUpgradeNotices`).
- **Core (Wave S):**
  - `MugirlSettings` should write `settingsSchema` (forceSave) equal to `UpgradeTables.Default.SettingsSchema`.
  - Register the settings rows.
  - Move the resets to `[GameLifecycleHook]`.
- **A2:**
  - Update the baselines for 6 new patch targets.
  - Add the save-visible `Mugirl.Core.GameComponent_Mugirl` (`mugirlSaveSchema`, `lastUpgrade/{fromSchema,toSchema,stepIds,gameVersion}`).
  - Consume K1 and use `SaveUpgradeOffline.Run`.
- **Wave S modules:**
  - Use `[SaveUpgradeModule("<Module>", order)]` in `SaveUpgrade/Modules/<Module>/`. Orders must be unique; I propose Core 100, Incidents 200, Corporate 300, Milk 400, Restraints 500, Combat 600, Content 700.
  - Declare removable containers for string lists; otherwise their items are nulled.

## Open risks
- **Nothing has run in game yet**, as the task required. Harmony application, the worker-thread dialogs, the Phase B job ending and the installation check are unverified at runtime.
- **Dictionary values are nulled, not removed.** A null-out in an undeclared string list writes the text `null`.
- **Dictionary and parallel-group removal is O(n) per item.**

---

# A1 fixer report (tag a1f / a1ft)

I fixed all 20 review findings in A1-owned files and rejected none outright. Two parts were deferred because they need a K1 contract change.

## Fixes
1. **Hediffs (#1):** the index now finds the `def` child anywhere among the hediff's direct children, so real-layout hediffs are recognised. The fixtures now use the field order of `Hediff.ExposeData`.
2. **Dropped LordJob types (#2):** when a dropped type sits on `lords/li/lordJob`, the whole lord is removed and `Lord_<id>` references are nulled. The builder rejects a ResetGraph or Claimed policy on a dropped type.
3. **How values are nulled (#3):** a new `NullForm` decides the form. Value leaves get `IsNull="True"`; references and def references get the text `null`. A new `ValueContextKind.DefReference` marks Scribe_Defs sites, and the vanilla filters use it.
4. **Text-value drops (#4):** they now go through the container rules.
5. **Schema per row (#5):**
   - Every row and policy carries a schema, registered through `module.ForSchema(n)`; the builder requires it to be in `1..Current`.
   - The pipeline uses `UpgradeTables.ForSource(source)`, so a save only gets rows newer than its own schema.
   - Ending all in-flight Mugirl jobs by default now happens on schema-0 saves only (`InFlightJobSelection`).
6. **Enums (#6):** an enum rename with no declared site is now a table error. Sites and value contexts take a `ValuePart` (Items, Keys or Values).
7. **Removed defs (#7):** a new ungated `WasDefRemoved` postfix (`DefNameResolver.WasDropped`) stops leftover references from logging "Could not load reference".
8. **Optional hooks (#8):** a missing InitNewGame, file-name or WasDefRemoved hook now gives a warning only.
9. **`Lord_0` (#9):** fixed.
10. **Indirectly removed objects (#10):**
    - Every removal collects the IDs of things, bills and lords in the removed subtree.
    - The load-ID pass now runs last, so it also covers removals made by module steps.
    - Removed bill IDs are checked before the recipe rename.
11. **Lord duties (#11):** in Phase B, reset lords re-enter their starting toil.
12. **Chains and swaps (#12):** label chains and swaps, and enum chains, are rejected when the tables are built.
13. **Stale diagnostics (#13):** they are cleared at `BeginLoad`. A load that bails out before a session exists publishes a Failed report.
14. **Lifecycle timing (#14):** `LoadedGame` and `AddedToExistingGame` are raised by Phase C after `AcceptLoad`.
15. **Stamp messages (#15):** there is one message per state, logged once per game. A newer save logs a warning, not an error.
16. **Settings schema (#16):** `SaveSchema.SettingsCurrent` = 1. The settings file is rewritten only when a row changes it.
17. **Golden fixtures (#17):** every module DOM step must have a golden fixture.
18. **One type per file (#18):** the multi-type files are split.
19. **Implied defs (#19):** added rules for terrain `Blueprint_`/`Frame_`, `Neurotrainer_` and `Psytrainer_`. The trailing-digit rule now covers derived targets, and colliding implied defs are rejected.
20. **Action counts (#20):** edits inside removed subtrees are skipped and not counted.

## Deferred, with reasons
- Per-row `schema` (#5) and enum sites (#6) are not written to the K1 manifest. K1 is a frozen shared contract, and every row is schema 1 until `Current` becomes 2.
- Implied gene and thought sources (#19) are left to the validator. Mugirl defines none.

## Verification
- **Mod build:** `agent-build.ps1 -Tag a1f -Paths SaveUpgrade,Core/GameComponent_Mugirl.cs,Core/GameLifecycle -AllWarnings` succeeded with 0 errors and 0 warnings across the whole project.
- **Tests:** `agent-build.ps1 -Tag a1ft -Project Tests/MugirlRace.Tests/MugirlRace.Tests.csproj -RunTests -AllWarnings` ran 589 tests: 0 failed, 1 not run (the explicit benchmark).
- **Benchmark:** the 60 MB synthetic save upgraded in 1075 ms, with the index pass at 539 ms.
- **Real saves:** a temporary read-only probe ran outside the repo over the 11 owner saves that contain Mugirl, and was then deleted.
  - All hediffs were indexed, for example 3109/3109 in the 64 MB Quicksave1.
  - No save failed; 4 were detected as MooGirl era.
  - The pipeline took 37 ms on the 64 MB save.
  - No `.bak` files were created.

## Deviations
- **Pipeline order:** the load-ID pass moved from before the labels to the very end, after the module steps, so it can handle every removal (#10).
- **Settings manifest:** `settings.schema` is now 1 (it was 0).

## Requests
- **A2:**
  - Accept `settings.fileClass: null` and `settings.schema` = 1.
  - Add the `BackCompatibility.WasDefRemoved` patch target to the baselines.
  - Flag implied gene and thought sources in the validator.
- **Orchestrator:** K1 v2 should add a per-row `schema` before `Current` reaches 2.
- **Core (Wave S):** `MugirlSettings` should write `settingsSchema` = `SaveSchema.SettingsCurrent` with forceSave.
- **A5:** cover these in the in-game suite:
  - a hediff-comp label move;
  - a dropped LordJob;
  - no "Could not load reference" for dropped defs;
  - `LastReportJson` freshness;
  - a lord reset entering its starting toil;
  - a missing optional hook producing a warning only.
- **Wave S modules:**
  - Value contexts and enum sites name the *old* labels.
  - Scribe_Defs sites use `DefReference`.
  - Later schema changes register through `ForSchema`.

## Open risks
- Nothing has run in the game yet. This includes `GotoToil` in Phase B and the `WasDefRemoved` patch.
- Job IDs of removed pawns are not collected, so reservations that point at them may still log errors.
- Dictionary values are still nulled rather than removed.
