# A2 report: validator legacy corpus and new checks (tag a2)

## What I built
All under `Source/Tests/MugirlRace.Validation/` (V) unless noted.
- **Corpus extraction.** `--extract-legacy-schema [--verify]`: `V/Legacy/*` (TypeUniverse, SaveRoots, ScribeSchema, AssemblySchema, ContentSchema, SaveTokenScanner, CorpusTable, LegacyExtraction). The driver is `tools/Export-LegacySchema.ps1`. It reads git read-only (rev-list, ls-tree, cat-file, archive), checks the ZIP sidecars, and deletes only its own marked temp folders.
- **Corpus.** `V/Baselines/legacy/` holds 13 TSV files with 152 inputs: 71 published, 25 observed, 39 dev, 1 info (the pre-refactor snapshot) and 16 ref. Row counts: types 1113, labels 523, defs 1121, def-comps 221, observed 826, jobs/lords 51, enums 40, saved values 38, settings 15.
- **C.save-upgrade** (ratchet by default, `--save-upgrade strict` available): `V/SaveUpgrade/*` and `V/Checks/SaveUpgradeCheck.cs`. Writes `save-upgrade-coverage.tsv` and `legacy-token-set.txt` to `--artifacts`. Ratchet files are `Baselines/save-upgrade-ratchet.<module>.txt`, with modules from `save-upgrade-modules.tsv`.
- **New checks:**
  - B.namespaces, B.component-ctors and B.save-confinement in `StructureChecks.cs`, with debt list `structure-debt.allow.txt` (402 entries).
  - B.scribe-defaults as an IL rule, with debt list `scribe-defaults.allow.txt` (27).
  - B.patch-order, with golden `patch-order.txt` seeded from the pre-refactor snapshot.
  - C.content-shadowing, C.definjected-paths and C.public-api.
  - Initial `public-api.json`: 107 names covering decisions §1–§5.
  - `save-internals` rule in `architecture-rules.json`.
  - SaveUpgrade exemptions in C.forbidden-patterns and in the key-literal rule.
- **Other modes:** `--parity A B` (`V/Parity/*`, IL and content) and `--save-corpus <dir>` (calls A1's `SaveUpgradeOffline.Run` through a DispatchProxy `IDefCatalog`).
- **Removed:** C.save-types, C.save-labels, C.save-enums, C.save-defs, C.frozen-keys, B.member-rebinds, the list-based B.scribe-defaults, their eight baselines, `Controls/ModRoot/1.6/**` and the retired control types. `--frozen` is still accepted but ignored.
- **Controls:** about 50 new self-test rules. Parity controls are in `Controls/Parity/{A,B}` with `map.tsv` and `expected.txt`.

## Verification
- `agent-build.ps1 -Tag a2 -Project Tests/MugirlRace.Validation/...`: builds with 0 warnings and 0 errors.
- `Export-LegacySchema.ps1`: about 60 s. `-Verify`: **byte-identical**.
- My self-test driver, which copies `Validate.ps1 -SelfTest`: **SELF-TEST PASSED, 154 rules on 2 runtimes, both runtimes agree.**
- Parity:
  - Calibration (the same source built in two folders, IL and content): **0 differences**, 1059 types, 6070 identical bodies.
  - Negative controls: **9 of 9 fired**. A mapped rename is not reported.
- `--save-corpus` on `DevData/Evidence/CorporateValidation-AllDlc6/Saves`: Upgraded, 7 steps, 337 ms.
- **Ratchet baseline** (after `--ratchet-reset` on the current tree and A1's current manifest): 4161 corpus lines, of which 2387 are unchanged, 974 dropped (A1's MooGirl patterns), 4 n/a and 290 undisposed. The 290 split into 190 required lines and 100 dev-only lines (warnings only).
  - Required lines per module: unassigned 75, restraints 58, milk 12, content 11, core 9, corporate 9, moogirl 6, combat 5, incidents 5.
  - By kind: types 74, observed 51, jobs/lords 26, defs 20, labels 19.
- `Validate.ps1 -WorkRoot %TEMP%\mg17-a2\vroot`, run once: 38 of 45 checks pass, both runtimes identical (90 keys), unit tests and shader check pass. None of the 7 failures comes from a defect in my checks:
  - A.about: already failing in the Wave 0 baseline (Wave D).
  - C.public-api: `IsMugirl` has no NoInlining. This is a real gap against decision §5.
  - B.architecture, B.base-calls, C.def-lookups, C.translation-keys and B.patch-targets: all caused by A1's new SaveUpgrade code (see REQUESTS).

## Deviations
- The database root is the topmost **non-abstract** type below `Def` (the game's `AllDefTypesWithDatabases` rule, which A1 also uses), not "BaseType == Def" as written in decisions §2.
- The control corpus lives in `Controls/ModRoot/Source/Tests/.../legacy`, because `Validate.ps1` copies `Controls/Baselines` without subfolders. The corpus lookup falls back to that path.
- The key-literal exemption covers only legacy type names in `Mugirl.SaveUpgrade`, so real keys there are still checked.
- `--map` reads the RenameApply report (`maps.*`) or TSV.

## REQUESTS
- **A1:**
  - Add Keyed `Mugirl.SaveUpgrade.{Failed,MooGirlEra,NewerSave,NotInstalled}`.
  - Add allow entries for the dynamic lookups and translation in DefDatabaseCatalog, BackCompatibleDefName and SaveUpgradeNotices.
  - Call `base.FinalizeInit` in `GameComponent_Mugirl`.
  - Route Find/Current through the core helpers, or the integrator allows `SaveUpgrade/Engine/Runtime/` for those rules.
  - Add `jobPolicies`/`lordPolicies` for the 26 legacy drivers.
- **A4 (Validate.ps1):**
  - Stop passing `--frozen`.
  - Build `Controls/Parity/{A,B}` and run `--parity A B --map Controls/Parity/map.tsv --expect Controls/Parity/expected.txt` in `-SelfTest`.
  - Copy `NegativeControl` recursively.
  - Update the header text (dropped-*/scribe-defaults lists are gone).
- **Integrator:** regenerate `patch-targets.txt`.
- **Combat owner:** add `[MethodImpl(NoInlining)]` to `MountedPawnUtility.IsMugirl`.
- **A3:** replace `public-api.json` with the full scan. The format is unchanged; method entries may carry `noInlining`.

## Open risks
- Labels supplied through generic helpers record `T` and `dynamic`. The comparison treats these as wildcards.
- Observed labels cover only owners that carry a `Class` attribute.
- C.definjected-paths skips list paths that are populated at runtime.
- Content parity compares raw XML after mapping, not the patched def database.
- Module attribution is seeded from pre-R paths. 75 required lines are "unassigned".

---

# A2 fixer report (tag a2): review fixes

All paths are under `Source/Tests/MugirlRace.Validation/` unless spelled out. Scripts: `scratchpad/a2f/`.

## Fixes
- **B1:** `Legacy/SaveRoots.cs` maps object or unconstrained element types to `Verse.IExposable`. Abstract, static, interface, value and delegate types get no root. System.Object roots dropped from 933 to 0.
- **B2:** `Legacy/ObservedLabelClassifier.cs` assigns each observed label to the legacy class that writes it. Labels that game or dependency code writes as literals (6,912 found) are excluded. The rest are kept only for comp owners or owners with dynamic labels; anything else becomes `label?`, which is review-only.
- **B3:** `SaveUpgrade/OwnerResolver.cs` reads A1's selector syntax with new class names and resolves `Path` through the schema graph. Label tokens split at the last `/`, enum rows use the new enum name, value rows match by value, defaults are resolved.
- **M1:** defs from any loaded pack count as existing. 11,849 vanilla, DLC and dependency defNames are left out of observed tokens.
- **M2/M3:**
  - Merging now unions rows; the `builds` count column is gone.
  - `--verify` checks only recorded inputs and keeps rows of inputs that disappeared.
  - The Workshop id now includes a hash, ZIP times use a fixed `-ZipTimeZone`, and saves are recorded as `saves:<sha8>.rws` (m12).
  - Extractor version 2 is written to `builds.tsv`; a version change needs `-Rewrite`. The corpus was rewritten.
- **M4:** the rename map reads member rename, move, copy, delete and dissolve entries and keys defs by root. IL parity compares moved members in their new types.
- **M5 (partial):** content parity now compares each def, def order per type, the patch operation sequence (with xpath mapping), language entries by key, and texture/sound sets.
- **M6:** `--save-corpus` fails on a Failed or Newer state, a failed step or an empty folder. It reads `.rws.gz`, runs twice to check idempotence, and enforces the 2000 ms budget.
- **M7:** new rules: no trailing digit on ThingDef/RecipeDef targets; ManeuverDef/ToolCapacityDef frozen; tool ids (`legacy-tools.tsv`, 39 rows); every step tested; the D4 side-document rule.
- **M8:** the key-literal exemption covers every corpus name.
- **M9:** added extractor controls (`Controls/Legacy`, 13), positive controls, `Gloden` in the SaveUpgrade control file, and parity `absent:` rules.
- **Minors fixed:** m1–m10, m12, m15, m16, m17. C.save-upgrade now runs in phase 4 (m5).

## Rejected
- **m11:** `Validate.ps1` still passes `--frozen`.
- **m13:** the Krafs reference assembly has no method bodies, so it cannot replace the full DLL; the DLL's sha256 is recorded and `-Verify` checks it.
- **m14:** the `bin`/`obj` folders date from 2026-10-14 and were not created by A2.
- **m16, xmlField part:** WhiteFluff sets no fields on `CompProperties_MugirlMount`.
- **Removable-container rule:** K1 does not export container data.
- **Not done (M5):** comparing defs after patches would need game type resolution.

## Verification
- Validator builds with 0 warnings and 0 errors.
- Rewrite: 152 inputs (71 published, 25 observed, 39 dev, 1 info, 16 ref). `-Verify` reproduced the corpus byte for byte, twice.
- Self-test: **179 rules pass, both runtimes agree**; parity controls 17/17; extractor controls 13/13.
- Calibration: 1072 types, 6212 identical bodies, **0 differences**.
- `--save-corpus`: 5 fixtures, the AllDlc6 save and the owner's 82 saves all pass. The MooGirl saves are idempotent; the slowest save took 341 ms.
- **Ratchet:**
  - 4169 lines: 1875 unchanged, 737 dropped, 7 n/a, 108 undisposed.
  - 63 of the 108 are required: combat 5, content 13, core 6, corporate 4, incidents 5, milk 6, moogirl 2, restraints 11, unassigned 11.
- `Validate.ps1 -WorkRoot %TEMP%\mg17-a2\vroot`, run once:
  - 38/45 per runtime, identical results; 589 unit tests and the shader check pass.
  - C.save-upgrade passes.
  - The 7 failures are A.about plus 6 that other owners must fix (see REQUESTS).

## REQUESTS
- **A4, `-SelfTest`:**
  - Copy NegativeControl recursively.
  - Run the parity and extractor controls (see `a2f/selftest.ps1`).
  - Stop passing `--frozen`.
- **A1/orchestrator:**
  - Add value contexts (owner and label) and removable containers to K1.
  - Add job/lord policies.
  - Add the Keyed keys and allow entries, and call `base.FinalizeInit`.
- **Integrator:** regenerate `patch-targets.txt`.
- **Combat:** add `NoInlining` to `IsMugirl`.
- **A3:** deliver `public-api.json` as an array.

## Open risks
- `Document(...)` selectors are resolved approximately.
- `label?` rows could hide a dynamic Mugirl label.
- `-Verify` assumes build inputs never disappear.
