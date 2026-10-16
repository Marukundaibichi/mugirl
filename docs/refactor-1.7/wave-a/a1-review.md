# A1 adversarial review (tag a1r): save-upgrade framework

Scope: every file under `Source/MugirlRace/SaveUpgrade/**`, `Core/GameComponent_Mugirl.cs`, `Core/GameLifecycle*.cs` and
`Tests/MugirlRace.Tests/SaveUpgrade/**`, checked against decisions.md §2/§5, design.md §5 and critique-upgrader.md.
Engine claims were checked in the `rw17-game` decompile context (1.7.4977) and, read-only, in a real save.

## How I verified it
- **Mod build:** `agent-build.ps1 -Tag a1r -Paths SaveUpgrade,Core/GameComponent_Mugirl.cs,Core/GameLifecycle -AllWarnings`
  - Result: build succeeded, 0 errors, 0 warnings across the whole project.
- **Tests:** `agent-build.ps1 -Tag a1rt -Project Tests/MugirlRace.Tests/MugirlRace.Tests.csproj -AllWarnings -RunTests`
  - Result: 566 total, 0 failed, 1 not run (the explicit benchmark).
- **Benchmark:** `MugirlRace.Tests.exe -explicit only -class …PerformanceBenchmarks`
  - The 60 MB synthetic save (129k things) upgraded in 1074 ms. The index pass took 518 ms, LoadIds 286 ms and Labels 171 ms.
  - This is inside the 2 s budget. The report's "S7 index pass = 34 ms" was not reproduced, because I did not run the owner's saves.
- **Probes P1–P9:** I wrote a temporary xunit file in the isolated tree `%TEMP%\mg17-agent\a1rt`, ran it, and deleted it. The repo was not touched. The findings below quote the outputs.
- **Decompile checks:**
  - `Hediff.ExposeData`, `Thing.ExposeData`
  - `Lord.ExposeData`, `Lord.ExposeData_StateGraph`, `Lord.SetJob`
  - `Game.LoadGame`, `Game.ExposeSmallComponents`
  - `SavedGameLoaderNow.LoadGameFromSaveFileNow`
  - `BackCompatibility.BackCompatibleDefName`
  - `LoadedModManager.GetSettingsFilename`, `Mod.GetSettings`
  - `GenDefDatabase.AllDefTypesWithDatabases`
  - `RecipeDefGenerator.CreateRecipeDefFromMaker`, `ThingDefGenerator_Buildings`, `DefGenerator.GenerateImpliedDefs_PreResolve`
  - `ScribeExtractor.ValueFromNode` / `DefFromNode`
  - `PostLoadIniter.DoAllPostLoadInits`
  - `LongEventHandler.ExecuteWhenFinished`
  - `GenTypes.IgnoredNamespaceNames`
  - `Bill.GetUniqueLoadID`
- **Real save (read-only grep):** I checked hediff, thing and lord element layout in the owner's `Saves/*.rws`.

## Confirmed correct
- **Deviation 1 (def root):** matches `AllDefTypesWithDatabases`, which picks the topmost non-abstract type.
- **Deviation 2 (file name):** `ScribeLoader` keeps no path, so capturing the file name is necessary.
- **Deviation 6 (lord reset):** the `curLordToilIdx` default is -1, which triggers an out-of-bounds error, so writing 0 is required.
- **Load order:** `Game.LoadGame` runs FinalizeLoading, then FinalizeInit, then `GameComponentUtility.LoadedGame`, all inside the method. So phases A, B and C, and the hub's `LoadedGame`, all see the session.
- **Formats and names:**
  - The settings file path formula is right.
  - The ignored-namespace list is identical to vanilla's.
  - `Bill_<recipe>_<n>` matches `Bill.GetUniqueLoadID`.
  - `Make_…Bulk`, `Administer_`, `Techprint_` and `Blueprint_Install_` all exist in 1.7.
- **Hooks:**
  - The `BackCompatibleDefName` postfix parameter names match the original.
  - The postfix is keyed on the `defName` argument, runs last and is not gated by the session.
  - The void finalizer does not swallow exceptions.
- **Stamp and settings:**
  - The stamp is written with forceSave, and the truthfulness rule is implemented.
  - The constructor is side-effect free.
  - The backup never overwrites an existing `.bak`.
  - The settings file is written through a temp file and then replaced.
- **Thread safety:** the statics use Volatile, Interlocked, Lazy and ConcurrentDictionary.
- **Edits outside ownership:** none attributable to A1.
  - `MugirlMod.cs` now calls `SaveUpgradeBootstrap.Initialize`; that edit was A4's (a4-report "#8 K4").
  - No leftover harness data and no `.bak` files were found in the owner's Saves folder.

## Findings

### 1. [blocker] Hediffs are never indexed on real saves
`SaveIndex.Visit` treats a `hediffSet/hediffs/li` as a hediff only when its first child is `<def>` (`Engine/Document/SaveIndex.cs:244`).

**Evidence:**
- 1.7 `Hediff.ExposeData` writes `loadID`, `ageTicks`, `tickAdded`, `visible`, `severity`, `recordedTale`, `causesNoPain`, `combatLogText` and `canBeThreateningToPart` *before* `def`.
- The real save `NavalDemo-…ab9b3f36.rws` confirms it: line 30308 is `<li Class="Hediff_Injury"><ageTicks>…<visible>…<severity>…<canBeThreateningToPart>…<def>Stab</def>`.
- Probe P1, with the vanilla field order: the `HediffComp` label rename did nothing, and the `RemoveHediffs` drop removed nothing.

**Impact:** on real saves, these all silently do nothing:
- `OwnerSelector.HediffComp`
- `DefDropPolicy.RemoveHediffs`
- `LegacyEvidence.LegacyDef` for hediffs

**Test quality:** the tests all put `<def>` first, so they encode the wrong layout:
- `LabelSelectorTests.cs:77`
- `DefDropTests.cs:54`
- `PerformanceBenchmarks.cs:375-376`

**Fix:**
- For `hediffSet/hediffs/li`, scan the direct children for the `def` element instead of using `FirstChild`.
- Rewrite the hediff fixtures in vanilla field order (`loadID`, `ageTicks`, …, `def`).
- Add a regression test whose layout is copied from `Hediff.ExposeData`.

### 2. [major] A dropped LordJob type leaves a lord with a null job (NRE at PostLoadInit), and RemoveLords cannot catch it
`TypeTableStep` handles `TypeDropPolicy.RemoveElement` through `RemoveItem`. For `lords/li/lordJob`, which is not a list item, that becomes `<lordJob IsNull="True"/>`. `TypeTableTests.cs:48` encodes this.

**Evidence:**
- 1.7 `Lord.ExposeData_StateGraph` dereferences `curJob.LostImportantReferenceDuringLoading` at PostLoadInit.
- `PostLoadIniter` only logs the exception. The lord stays with a null job and graph, and its ticks then throw NREs.
- `LordPolicyStep` (`Steps/Framework/LordPolicyStep.cs:29`) reads `OriginalClassOf(lord["lordJob"])` from the replacement element, which has no Class. `LegacyClasses` records renames only.
- So even `RemoveLords("<dropped type>")` is ignored. Probe P9 output: `<li><loadID>3</loadID><lordJob IsNull="True" /><curLordToilIdx>1</curLordToilIdx></li>`, and `Lord_3` is kept.

**Fix (any of these):**
- Run the lord policies before type drops, keyed on the original Class.
- Make `TypeTableStep` remove the whole `lords/li` and add `Lord_<id>` to `RemovedLoadIds` when a dropped type sits on `lordJob`.
- Add a builder rule: a dropped LordJob type must carry `RemoveLords`.

More generally, reject `RemoveElement` on non-list deep fields whose reader does not tolerate null.

### 3. [major] String leaves are "nulled" as the literal text `null`
`DomEditor.ApplyInPlace` NullOut sets `InnerText = "null"` (`Engine/Document/DomEditor.cs:159-166`).

**Evidence:**
- That text is read as null only by Scribe_Defs and reference leaves.
- `ScribeExtractor.ValueFromNode<string>` returns the string `"null"`; only `IsNull="True"` yields null.
- `DefDropStep` calls `RemoveItem` on `ValueContextKind.Def` contexts, which are string-typed by definition (`Steps/Framework/DefDropStep.cs:107-115`).
- So list items become `<li>null</li>`. Probe P2 shows it, and `DefDropTests.cs:81` encodes it.

**Fix:**
- Add a value-aware primitive (for example `NullOutValue`): an empty element with `IsNull="True"` and no text.
- Use it for Text and Def value contexts.
- Keep the `null` text only for references and Scribe_Defs leaves.

### 4. [major] Text-value drops bypass the container rules
For a list item that is not under `keys`, `LabelTableStep.PlanValues` calls `plan.Remove(leaf)` (`Steps/Framework/LabelTableStep.cs:122-130`).

**Evidence:**
- In a declared parallel group, only one list shrinks. Probe P3 output: `<signals><li>Live</li></signals><ticks><li>1</li><li>2</li></ticks>`.
- Positional lists shift.
- This contradicts decisions §2 "Lists (U#12)" and the report's own claim that dropped string items are nulled unless the container is declared removable.

**Fix:**
- Use `plan.RemoveItem(leaf)` for every `li`, together with the `IsNull` primitive from #3 for non-removable string lists.
- Keep `plan.Remove(label)` for the single-value case only.

### 5. [major] Upgrade rows have no schema version
None of these carry an introduced-in schema: TypeRow, DefRow, LabelRow, EnumRow, ValueRow, DefaultRow, and the lord and job policies.

**Evidence:**
- `UpgradePipeline.cs:38` filters only module steps by `SourceSchema < step.Schema`. The framework steps apply every row on any `SourceSchema < Current`.
- `InFlightJobPolicy` ends every Mugirl-assembly job on any legacy load (`Engine/Runtime/SaveUpgradeRuntime.cs:59-63`). Decisions §2 say "Default policy on schema-0 loads".

**Impact:** this is latent while `Current == 1`. At the first bump to schema 2, 0→1 rows re-run on schema-1 saves:
- `ResetGraph` for an un-renamed LordJob type resets live lords.
- All in-flight jobs are ended.
- Label rows re-fire if a legacy label name was ever reused, and the builder has no "never reuse a legacy label" rule.

**Fix:**
- Add `int Schema` (default 1, validated to 1..Current) to every row and policy.
- Filter on it in each framework step and in the job policy.
- Validate that label renames never target a legacy label of the same owner.

### 6. [major] Enum renames can be registered with no effect
**Evidence:**
- `UpgradeTableBuilder` accepts an `EnumRow` whose enum has no `EnumSite` (`Engine/Tables/UpgradeTableBuilder.cs:283-290`).
- Probe P5: `<mode>Old</mode>` is unchanged, yet the K1 manifest lists the row. So the validator's ratchet sees coverage that the DOM never applies.
- Dictionary *values* never receive enum renames: `ValueLeaves.cs:21` yields keys only, and `ValueTableTests.cs:26` encodes this. A `Dictionary<_,Enum>` therefore cannot be declared.

**Fix:**
- Add a builder problem: "enum X has renames but no site".
- Give `EnumSite` a part (Self/Items/Keys/Values).
- Optionally emit the sites in the manifest.

### 7. [major] Dropped defs read through Scribe_Defs log a red error at every occurrence
**Evidence:**
- No `BackCompatibility.WasDefRemoved` postfix exists (deviation 9).
- `ScribeExtractor.DefFromNode` logs "Could not load reference to … named …" unless `WasDefRemoved(defName, typeof(T))` is true.
- `RemoveReferences` covers only declared string contexts and ThingFilters. A dropped ThoughtDef in memories, HediffDef `source`, or JobDef in queued jobs therefore errors on every legacy load.
- That breaks the in-game upgrade suite's "no load errors" assertion.

**Fix:**
- Add an ungated postfix on `WasDefRemoved(string defName, Type type)` that returns true for drop rows (including derived ones) of `DefRoots.DatabaseRoot(type)`.
- Use the same rules as `DefNameResolver`: refuse when the root is Def, and do not apply when the name exists in the current DB.
- Unit-test it through a pure resolver.

### 8. [minor] The installation check requires optional hooks
`Verify` also requires the `InitNewGame` and `LoadGameFromSaveFileNow` patches (`Engine/Runtime/SaveUpgradeInstallation.cs:30,32`). Decisions §2 name only LoadGame, FinalizeLoading, FinalizeInit and BackCompatibleDefName.

If the backup or lifecycle patch fails:
- the status becomes Broken;
- a startup error dialog appears;
- no loaded game is ever stamped Current.

**Fix:** split the hooks into required and optional. A missing optional hook gives a warning only.

### 9. [minor] RemoveLord misses `Lord_0`
Scribe omits a lord's `loadID` when it is 0, the default. The real save `Autosave-1.rws` has lords without `<loadID>`.

`LordPolicyStep.cs:58` therefore never adds `Lord_0`, and that reference stays (probe P4).

**Fix:** use `lord["loadID"]?.InnerText ?? "0"`.

### 10. [minor] References to indirectly removed objects are left dangling
These removals do not feed `RemovedLoadIds`:
- things (or bills) removed by a type drop: P7 keeps `Thing_Mugirl_Bed1`;
- things nested inside a removed thing: P6 keeps `Thing_Steel2`.

Vanilla then warns "Could not resolve reference".

**Fix:** on every structural removal, collect the IDs of indexed things, bills and lords in the removed subtree.

### 11. [minor] ResetGraph keeps the legacy duties
`Lord.SetJob(loading: true)` neither calls `GotoToil` nor updates duties (decompile). After the reset, pawns keep the duties of the old toil until a transition.

**Fix:**
- Record the IDs of reset lords in `SessionData`.
- Add a framework Phase B action that calls `lord.GotoToil(lord.Graph.StartingToil)` for them.

### 12. [minor] Label chains and swaps fail the whole load instead of the table build
Rows `a→b` and `b→c` under the same owner make every legacy load fail with "owner already has <b>" (probe P8, `LabelTableStep.cs:63-66`).

**Fix (either):**
- Reject such chains in `ValidateLabels`.
- Plan the renames simultaneously, ignoring targets that are themselves renamed away.

### 13. [minor] Diagnostics can be stale
When `BeginLoad` bails out before a session exists (`SaveUpgradeRuntime.cs:113-118`), or throws before `Volatile.Write`, `LastReportJson` keeps the previous load's report.

**Fix:** clear it, or publish a Failed/Skipped report, at `BeginLoad`.

### 14. [minor] Lifecycle `LoadedGame` and `AddedToExistingGame` fire before Phase C
**Evidence:** these events fire before `AcceptLoad` and before `Publish` (`GameComponent_Mugirl.cs:80-87`). Vanilla calls `GameComponentUtility.LoadedGame` before the LoadGame postfix.

**Impact:** handlers see the pre-upgrade `SourceSchema` and `LastUpgradeReportJson`, and the previous load's diagnostics.

**Fix:** document this in `GameLifecycleEvent`, or raise both events from Phase C.

### 15. [minor] The stamp error message is misleading
`GameComponent_Mugirl.cs:50-55` logs "the upgrade did not complete, next load upgrades it again". It does so even for Newer saves, and for Current saves loaded with broken hooks.

The `ErrorOnce` key is process-wide, so a second affected game logs nothing.

**Fix:** use a message per state, and key it per session.

### 16. [minor] The settings target schema comes from the maximum row schema
`UpgradeTables.cs:44` derives the target as the highest row schema.

**Impact:** the requested Core change (MugirlSettings writes `UpgradeTables.Default.SettingsSchema`) would make `MugirlSettings.ExposeData` throw whenever the tables are invalid, because `Default` throws.

**Fix:**
- Add a constant `SaveSchema.SettingsCurrent`.
- Have the builder check that every row is at most that constant.

The K1 `settings.fileClass` is `null` when no row exists (`SaveUpgradeManifest.cs:198-208`). A2 must accept that.

### 17. [minor] The golden runner passes with no fixtures
`GoldenFixtureTests.cs:20` returns early when the folder is missing, so it never checks that registered steps have fixtures. Design §6 wants a fixture for every step.

**Fix:** assert that every registered module `DomUpgradeStep` has `Golden/<id>/{before,after}.xml`. Activate this in Wave S.

### 18. [minor] Code style: more than one top-level type per file
- `SaveIndex.cs` declares `IndexedThing`, `IndexedHediff` and `SaveIndex`.
- The test file `SaveXml.cs` declares `SaveXml` and `TestCatalog`.

**Fix:** move each type into its own file.

### 19. [minor] Implied-def coverage and checks are partial
`DerivedDefNames` (`Engine/Tables/DerivedDefNames.cs:11-22`) covers only ThingDef and ResearchProjectDef sources. Not covered:
- terrain `Blueprint_` and `Frame_`;
- `Neurotrainer_`;
- implied GeneDef and ThoughtDef names.

Mugirl has no such sources today. Derived ThingDef targets such as `Techprint_<new>` also skip the trailing-digit check (`UpgradeTableBuilder.cs:201`).

**Fix:** have the validator flag these sources. Apply the digit rule to derived ThingDef targets too.

### 20. [minor] Action counts are inflated
In-place edits on nodes inside removed subtrees, and rebuilds of parents that were nulled in the same plan, are counted as actions (`DomEditor.cs:47-59`).

Only diagnostics are affected.

## Test quality summary
The tests exercise the code paths well:
- table validation
- the primitives
- the load-ID scanner
- plan and apply semantics
- the manifest shape

However, three of them encode engine-incorrect behaviour:
- the hediff layout (#1);
- the literal `null` in string lists (#3);
- the nulled `lordJob` (#2).

No unit test covers the runtime glue: `SaveUpgradeRuntime`, the hooks, the installation check and the `InFlightJobPolicy`. These depend on the A5 in-game suite, which must cover:
- failure injection;
- a missing hook;
- an in-flight job;
- a lord reset or removal;
- a newer save.

## REQUESTS
- **A1 fixes:**
  - #1–#7 before Wave S. #1 is a blocker for every hediff-comp label move.
  - Then #8–#12.
- **A2:**
  - Treat `settings.fileClass: null` as valid.
  - After #6 lands, accept enum sites in the manifest, if they are emitted.
- **A5:** cover these in the in-game upgrade suite:
  - a hediff-comp label move on a real-layout save;
  - a dropped LordJob type;
  - no "Could not load reference" for dropped defs;
  - `LastReportJson` freshness.
