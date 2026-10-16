# A5 adversarial review (tag a5r, 2026-10-15)

Scope: `Source/Tests/MugirlRace.GameValidation/**`, `tools/Test-InGame.ps1`, `tools/New-SaveFixture.ps1`, `DevData/Evidence/SaveFixtures/**`.
I reviewed the actual files against decisions.md, design.md, critique-validation §4, validator-tooling §4 and K1-K5. I checked the engine claims against the rw17-game decompile, read the fixture manifests and archives, and read every boot report of the `-Fixture all` run.

## How I verified

- `agent-build.ps1 -Tag a5r -Project Tests/MugirlRace.GameValidation/MugirlRace.GameValidation.csproj -AllWarnings`: succeeded, 0 errors, 0 warnings.
- Builds in the a5r copy:
  - `-p:MugirlPlumbingOnly=true -p:TreatWarningsAsErrors=true`: 0/0.
  - The full ProjectReference build with `-p:TreatWarningsAsErrors=true --no-incremental`: 0/0.
  - `dotnet build MugirlRace.slnx`: 0/0, and no `1.7` folder was created.
- Default run: `TMP/InGameValidation/20261015-144507/summary.json` shows exitCode 0, ok 78, failed 0, and DLL 220E5D1A (the snapshot DLL, still deployed). The ok-check set equals the baseline except for pawn ids and the 13 new corporate-comms page checks.
- Fixture run: I read `TMP/InGameValidation/20261015-143755/report-P-*-boot{1,2}.txt` and the 5 manifests. I also scanned the 5 `.rws.gz` files (scratchpad `a5r/m.py`, `a5r/g.py`).
- Edits outside the owned files: none attributable to A5. The diff against `DevData/Backups/PreRefactor-1.7-20261015/tree.zip` and the mtimes show this.
- Leftovers: no lock file, no `Mods/__*` folders, and no A5 work folders remain.
- I did not run the game (the task does not allow it).

## Findings

### Major

**1. SaveNameMap flattens the K1 name spaces, so it mistranslates names.**

Evidence:
- `Core/SaveNameMap.cs:22,67-74` keys `defs` by the old name only and ignores `root`. The last row wins.
- `:89-97,135` keys labels by the old name only and ignores `owner`.
- `:108-115` ignores the `context` of values.
- The real rename spec already contains collisions:
  - `Consumed_MugirlMilk`: HediffDef → `Mugirl_MilkVigor`, ThoughtDef → `Mugirl_ConsumedMilk`.
  - `Mugirl_CourierRaid`: QuestScriptDef → `Mugirl_CourierContact`, IncidentDef → dropped.
  - `Mugirl_Slave`: ThingCategoryDef is renamed, PawnKindDef keeps its name.

  Sources: `spec/final/upgrade-rows.sample.json` and `spec/final/R/milk.json`. The 1.6 Defs have 18 such shared names (for example `Mugirl_Blindfold`, which is both a HediffDef and a ThingDef).
- Consequences:
  - Facts, `Map.Apply`, `TranslateValue` and `IsDroppedDef` give wrong answers.
  - `LegacyOnlyNames` turns names that are still current (`<kindDef>Mugirl_Slave</kindDef>`) into token hits.

Fix:
- Key defs by (root, old).
- Resolve the root from the XML context: a Thing `<def>` is a ThingDef, `hediffs/li/def` is a HediffDef, `kindDef` is a PawnKindDef, and so on. Facts keys should carry the root.
- A name counts as legacy-only only when no root keeps it.
- Key values by (context, old).

**2. Drops can never be explained in the boot-1 diff, and derived defs are not handled.**

Evidence:
- `Suites/Mod/UpgradeSuite.cs:472-490,717`: `IsDroppedItem` needs a `[...]` suffix. Thing items are keyed `thing:<id>`, and list items are `li[<id|def>]`, which `IsDroppedOrConsumedLabel` never matches.
- `SaveUpgradeManifest.cs:16` (A1) does not list implied defs (`Corpse_`, `Blueprint_`, `Frame_`, `Make_`, `Meat_`), and SaveNameMap does not derive them.
- In the actual P-0920 report:
  - `missing thing:… (was <li><def>Mugirl_NeuralCombatShoulders…)` is held by a pawn.
  - `missing thing:… <thing Class="Corpse"><def>Corpse_Mugirl_MechanoidBulldog…` is a derived def.
  - The fact `thing/Corpse_Mugirl_MechanoidBulldog: expected 1, missing`.
- Even with the K1 drop rows that A5 asks A1 to add, boot 1 stays red.

Fix:
- Parse the item and leaf identity (ThingID → def, `li[def]`, Class) and explain it through `IsDroppedDef`/`IsDroppedType`.
- Derive the implied names from the def rows, the same way decisions D2 does for load IDs.
- Unit-test this logic.

**3. The diff and the facts ignore K1 policies and declared consumption.**

Evidence:
- `jobPolicies` is used only in Facts (`UpgradeSuite.cs:242-262`), not in Diff. Every fixture has an in-flight mod job (`JobDriver_Nuzzle` or `JobDriver_FollowRoper`, from the archive scan). After Phase B ends it, `jobs/curJob/def` and `curDriver@Class` show up as unexplained differences.
- `lordPolicies`, `steps[].consumes` and `defaults` are not parsed at all (`SaveNameMap.cs:117-123` keeps only the step id and phase). The critique says a step's declared consumption must explain differences.
- `TranslateKey` never returns null for a dropped or consumed label (`SaveNameMap.cs:192-195`; `Label` never returns null). Facts of labels that a step moves (for example a component list moved into a nested IExposable) are therefore reported as missing.

Fix:
- Explain differences through consumes, policies and defaults.
- Skip the subtrees of jobs and lords ended by policy.
- Make `TranslateKey` return null for dropped or consumed label segments.

**4. The diff compares whole Mugirl pawns with uncalibrated vanilla and HAR noise.**

Evidence:
- `Core/SaveDiff.cs:141-146,232-256` compares the entire pawn subtree, including the vanilla and HAR trackers.
- Leaves are compared as raw strings with no numeric or RGBA tolerance (`:188-209`).
- Observed false differences:
  - P-1004: `AlienRaces_AlienComp/colorChannels/values/li#7/second: RGBA(0.824, 0.733,…) -> RGBA(0.824, 0.732,…)`.
  - `jobs/curJob/lastJobGiverKey`. The decompiled `Verse.AI.Job.ExposeData` writes -1 (not saved) when `TryGetThinkNodeWithSaveKey` fails, and only logs a warning.
- The reset list (lastLightTick, stock) comes from P fixtures only.

Fix:
- Restrict items to Mugirl-owned nodes: mod Class nodes, mod comp labels from `Baselines/legacy/legacy-def-comps.tsv`, and mod defs.
- Add a numeric and RGBA tolerance.
- Classify `lastJobGiverKey` as a load reset when "Could not find think node with key N" was logged.
- Calibrate with an identity control: boot 1 on a fixture written by the build under test must show 0 differences.

**5. The DefInjected assertion is vacuous.**

Evidence:
- `UpgradeSuite.cs:177-193` filters on `fileSource.Contains(modRoot)`.
- Vanilla sets `defInjection.fileSource = file.Name`, and `FilesystemFile.Name => fileInfo.Name` is the file name only (decompiled `DefInjectionPackage.TryAddInjection`). `loadErrors` carry file names only.
- All 10 boots report "0 DefInjected entries of English come from the mod" and then ok.
- Fixture boots run in English (`Test-InGame.ps1:137`), and English has 1 DefInjected file.

Fix:
- Enumerate the mod's DefInjected files from its load folders and match their keys to `package.injections`.
- Fail when 0 entries are found.
- Run the upgrade suite in ChineseSimplified (or in both languages).

**6. The token scan is not limited to Mugirl-owned positions, so it gives false failures.**

Evidence:
- Values are scanned everywhere (`UpgradeSuite.cs:515-521`).
- Labels are checked inside any element under a Mugirl pawn, including the vanilla trackers (`:503-513`).
- `LegacyOnlyNames(includeLabels:true)` adds generic labels. Examples are `color`, `quality` and `tickCounter`, exactly the losses A5 asks A1 to map. Once `color` is dropped, every vanilla `<color>` on apparel worn by a Mugirl becomes a hit.
- This contradicts critique §4.3(5).

Fix:
- Check labels only directly under the Mugirl Class/comp owners named by the K1 selectors.
- Check values only in def-reference, Class, ID, enum and saved-string contexts.

**7. Restraint facts break on the planned rename and on default changes.**

Evidence:
- `Core/SaveFacts.cs:70-71` detects restraints by the untranslated Class suffix `SlaveApparel`. The restraints plan renames these classes to Restraint*.
- `:74,82` store `(default)` sentinels. Every restraint in all 5 fixtures is `(default)`: 31-43 per fixture, and none was ever unlocked.
- `ContentSweepSuite.cs:226` wears restraints with `locked:false`, and nothing seeds lock counts or timers.

Fix:
- Record the restraint defs (their legacy `thingClass`) in the manifest at sweep time.
- Resolve sentinels to the legacy defaults (K1 `defaults` or the corpus).
- Seed varied lock states by name-reflection extras.

**8. The seed misses part of the task's coverage.**

Evidence:
- Corporate network: empty in P-1004 (`corporateOrders`, `Missions`, `Records` are empty; no representative arrived).
- Roping: facts only in P-1004. The other 4 fixtures record "roped ok", but the rope is gone in the save.
- Lords: no Mugirl LordJob in any fixture.
- Weapon wheel: at most 1 slot.
- The sweep records success at action time; it never checks persistence (`ContentSweepSuite.cs:353-366,449-471`).

Fix:
- Verify each category after saving: write a `coverage` block to the manifest and fail when a required category is missing.
- Re-rope just before saving and assert the rope.
- Drive the corporate introduction until the representative arrives.
- Have at least one lord in flight.

**9. The K5 lock takeover race is still open, and there are three divergent lock copies.**

Evidence:
- `Test-InGame.ps1:275-282` and `New-SaveFixture.ps1:163-170` do Remove-Item and then CreateNew. Two waiters can both take a stale lock, or one can delete a lock the other just created.
- A4 asked A5 to adopt the exclusive re-check takeover (a4-report, REQUESTS). The scripts were last changed at 12:44, before A4's 13:01 fix.
- `New-SaveFixture` ignores an inherited token.
- A different implementation also lives in `SmokeTest.ps1`.

Fix: put one shared lock implementation in a tools module, using A4's FileShare.None re-check (integrator).

### Minor

**10. The X-newer "untouched" check is vacuous.** `UpgradeSuite.cs:419,429` hashes the file before and after loading, but loading never writes the save file. Fix: assert diagnostics actions = 0, no backup made, and an unchanged stamp after a re-save. The `newer` regex is also too broad.

**11. The backup is never asserted** (decisions §5 BACKUP=YES; diagnostics.backup is only logged at `:133`). Fix:
- Boot 1: `<save>.rws.mugirl-schema0.bak` exists and equals the fixture's sha256.
- Boot 2 and X-newer: no backup is created.

**12. "Seeded Mugirls stay alive" is flaky** (`:359-378`). Fixtures are saved mid-combat: forced `Mugirl_CourierRaid` and other incidents, plus every HediffDef applied (`ContentSweepSuite.cs:267-394`). One death was already observed. Fix: settle the map before saving (no hostiles), or assert only on deaths caused by exceptions or missing defs.

**13. Every current defName of 3+ characters becomes a failure token, including for warnings** (`UpgradeSuite.cs:110`, `LogPolicy.cs:66-83,117-121`). Examples are `Ears`, and the FA shapes `hot`, `cold`, `half` and `heart` when FA is active. Fix: use only legacy-only names or Mugirl-marked names, matched in def-reference contexts.

**14. The X-settings and X-alias checks are dead code.** Test-InGame can neither stage a dependent temporary mod nor seed `Config/`. `CheckSettings` reads class names from suite options instead of K1 `settings.fileClass`. `foreignTokens` and `logAllowlist` are never written (`Test-InGame.ps1:539-556`).

**15. Offline parity skips silently** (`:539-543`) and ignores in-game steps that are absent offline. Add a required mode for Wave V.

**16. K3 deviation:** `-ModDll -Deploy` copies the files by hand (`Test-InGame.ps1:709-716`) instead of using the project's deploy mechanism.

**17. Duplication and hard-coding:**
- `New-SaveFixture.ps1:119-146` copies the Test-InGame helpers instead of sharing them.
- The build hashes are hard-coded (`:82-88`) instead of being read from A2's `Baselines/legacy/builds.tsv` (critique §4.2 step 1). The values do currently match.
- Fixture kinds are free strings (`FixtureRef.Kind`).

**18. Performance:** each step re-parses the 5-20 MB saves (the entry save about 3 times, the re-save about 4 times). Cache the XDocuments in `UpgradeState`, and clone before `Map.Apply`.

**19. No offline unit tests** for the pure logic (SaveNameMap, SaveDiff explanations, SaveFacts, `LogPolicy.FirstToken`). Findings 1-7 would have been caught. Link these files into a test project and test them with golden XML trimmed from the fixtures.

**20. Facts record empty lists as ""** (`SaveFacts.cs:232-235`). A build that writes null or omits the list causes a false mismatch, although the diff treats this case as info. Normalize.

**21. The converter-skip premise depends on the game build.** The decompiled `BackCompatibility.CheckSaveIdenticalToCurrentEnvironment` compares `VersionControl.CurrentBuild`. A Steam update would silently move the fixtures onto the converter path. Report which path ran by comparing manifest `game.version` with the running version.

**22. Facts compare re-save XML, not live state through the new API** (critique §4.3 boot-1 item 4). This proves persistence, not interpretation. Document it, or add a live-state probe in Wave S.

**23. Screenshot analysis reads `$OutDir/Screenshots`** (`Test-InGame.ps1:893`), but multi-boot runs store screenshots under `$OutDir/<boot>/`. This is harmless today.

## Verified correct

- **DrawProbe fix:** Harmony rejects non-declared members, as the baseline exception text shows. Patching the declaring type is correct.
- **UI fix:** the close-wait is sound.
- **Fixture provenance:**
  - DLL hashes and blobs equal `builds.tsv`.
  - Content is copied, not linked, and only `MugirlRace.dll` is included.
  - The meta rewrite leaves 0 residual occurrences, keeps the mod order and adds no BOM.
- **Labels and contract names:**
  - SaveFactQueries' legacy labels match the frozen 1.6 source.
  - K1/K2 member names match A1's source.
- **Csproj:** the guard removal is correct under K3.
