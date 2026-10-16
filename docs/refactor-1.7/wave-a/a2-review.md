# A2 adversarial review (tag a2r): validator legacy corpus and new checks

All paths are relative to `Source/Tests/MugirlRace.Validation/` (V) unless the path is spelled out in full.

## What I verified myself (isolated runs; no repo writes)

- **Validator build.** `agent-build.ps1 -Tag a2r -Project Tests/MugirlRace.Validation/MugirlRace.Validation.csproj -AllWarnings`, then a `--no-incremental` rebuild: 0 warnings, 0 errors.
- **Mod build.** `agent-build.ps1 -Tag a2r` built the mod.
- **Full validation.** I ran the validator directly with `--assume-active all` and `--artifacts %TEMP%\mg17-a2r\run1`. The result matches the report:
  - 38 of 45 checks pass;
  - C.save-upgrade: 4161 lines (unchanged 2387, dropped 974, n/a 4, undisposed 290).
- **Self-test.** I copied A2's `selftest.ps1` to `scratchpad/a2r/selftest.ps1` and pointed it at the a2r build: SELF-TEST PASSED, 154 rules, both runtimes agree.
- **Corpus reproduction.** `Export-LegacySchema.ps1 -Verify -WorkRoot %TEMP%\mg17-a2r\legacy -Validator <a2r exe>` passed: 13 files reproduced byte for byte in 62.6 s.
- **Parity negative controls.** I built `Controls/Parity/{A,B}` in `%TEMP%` and ran `--parity --map --expect`: 9 of 9 fired, and the mapped rename was not reported.
- **Parity calibration.** I rebuilt the mod in a second folder and compared it with the first build: 0 differences, 1060 types, 6077 identical bodies.
- **Save corpus.** `--save-corpus` on AllDlc6 reported Upgraded, 7 steps, 338 ms.
- **Engine facts.** I checked these by decompiling RimWorld 1.7.4977:
  - the "database root = topmost non-abstract type below Def" deviation is correct (`GenDefDatabase.AllDefTypesWithDatabases`). decisions §2 has the rule wrong: "walk BaseType until BaseType == Def" would give BuildableDef for ThingDef. The orchestrator should correct decisions §2.
- **Writes outside the owned files.** Nothing was written to `1.7/Assemblies`, and nothing outside the owned files changed except m14.

## Findings

### Blockers

**B1. `System.Object` is derived as a save root, so almost every class is treated as "save-visible".**

Evidence:
- `Legacy/SaveRoots.cs:122-141` (`AddRoot`) and `:64-91` (`RootOf`).
- The cause is the game method `RimWorld.CompStatue.PostExposeData`, which calls `Scribe_Collections.Look(ref additionalSavedPawnDataForMods /*Dictionary<string,object>*/, …, LookMode.Value, LookMode.Deep)`.
- `RootOf` walks base classes before interfaces and reaches `System.Object` first. Consequences:
  - `legacy-types.tsv` gives 933 rows the root `System.Object`.
  - 726 of the 819 required type rows are spurious, including static classes, Harmony patches, DefOfs, delegates and ThingComps. Examples: `Mugirl.MugirlLog` (static), `Harmony_*`, `Harmony_NeuralHelmetDarknessAccuracy+SetDarknessOffsetDelegate`.
  - IExposable types lose their real root.
- The engine cannot load these types anyway: `ScribeExtractor.SaveableFromNode` refuses abstract types and casts to `IExposable`.

Impact:
- Wave R would need about 700 type-map entries for classes that are never written to a save. These entries would pollute the runtime `GetBackCompatibleType` table.
- Legacy `Mugirl.MugirlLog → Mugirl.Core.MugirlLog` cannot be disposed: the target is itself a legacy (info-tier) name, so the rename trips the name-reuse rule.

Fix:
- Treat an `object` element type or an unconstrained element type as `Verse.IExposable`.
- `RootOf` returns null for static, abstract, interface, value and delegate types.
- The `IExposable` root applies only to types that implement `IExposable`. Match interface roots before `System.Object`.
- Re-extract the corpus (see M3).

**B2. The observed-label scan records the vanilla and comp labels of Mugirl Thing subclasses as required Mugirl labels.**

Evidence:
- `Legacy/SaveTokenScanner.cs:84-87` adds every child element of a node with a Mugirl `Class`.
- `NewBuild.cs:93-121` (`LabelsOf`) only knows labels from types in the mod.
- 41 observed labels are undisposed. Examples: `def`, `id`, `health`, `stackCount`, `pos`, `map`, `questTags`, `abilities` on `Mugirl.SlaveApparel`, `Mugirl.AdvancedSlaveApparel` and `Mugirl.BrainWashSlaveApparel`. 38 of the 58 lines in `Baselines/save-upgrade-ratchet.restraints.txt` are these.
- `SaveUpgradeCheck.cs:202` tells the reader to "rename, consume or drop it". Dropping `def` or `id` would destroy those things on load.

Fix:
- Labels that game base classes write are not Mugirl-owned. Derive them with the same `ScribeSchema` over Assembly-CSharp, or skip any label that no legacy Mugirl owner in the chain writes.
- Attribute labels inside a thing node to the def's comps using `legacy-def-comps.tsv`.
- Purge the affected rows (M3).

**B3. Contract mismatch with A1's manifest (K1 consumer side).**

What A1 writes:
- Owner selector text uses new, mapped type names, compared case-insensitively. The forms are `Class(a|b)`, `GameComponent(X)`, `ThingComp(X)`, `HediffComp(X)`, `Path(<root>, a/li)` (no quotes) and `Document(a/b)` (`Source/MugirlRace/SaveUpgrade/Engine/Tables/OwnerSelector.cs:142-212`).
- Consumed-label tokens are `label:<selector text>/<label>`. A1 enforces this form (`UpgradeTableBuilder.cs:268`, `ManifestTests.cs:41`).
- `EnumRow.Enum` is the new enum name.
- `ValueRow.Context` is a declared context name, not `Owner::field`.

What A2 does:
- Keys labels, defaults and enums by the legacy owner or enum name (`SaveUpgrade/DispositionEngine.cs:173-203, 448, 517`; `DispositionEngine.Rows.cs:23`).
- Only recognises `PathFrom(inner, "path")` (`DispositionEngine.cs:557, 584`). A1's `Path(...)` falls through to the root's type, and `Document(...)` entries vanish silently.
- Checks `label:` tokens against raw legacy owners (`DispositionEngine.cs:322, 480`).
- Keys values by `Owner::field` (`DispositionEngine.Rows.cs:113`).

Result:
- Every correct A1 label consumption fails as "Stale consumption", in both ratchet and strict modes.
- Any label, enum or default entry on a renamed owner fails as "Stale entry" and leaves the legacy line undisposed.

The control manifest uses an invented `ClassIs(...)` form with owners that are never renamed (`Controls/NegativeControl/SaveUpgrade/ControlSaveUpgrade.cs`), so the self-test cannot see any of this.

Fix:
- Parse A1's selector grammar.
- Map legacy owners and enums through the type map before lookup and in the stale checks.
- Resolve `Path` and `Document` through the schema graph.
- Parse `label:<selector>/<label>`.
- The orchestrator pins the selector, token and value-context formats in K1. Manifest values should carry the owner and label of their context.
- Add controls in A1's format with renamed owners.

### Major

**M1. Vanilla and foreign def names are treated as Mugirl-owned.**

Evidence:
- `ThinkTreeDef/HumanlikeConstant`, which is vanilla, is a required undisposed def. It is also an observed token (`def HumanlikeConstant curJob/jobGiverThinkTree`).
- `NewBuild.cs:28, 127-130` only consults defs where `IsOurs` is true. `SaveTokenScanner` (`knownDefs`) includes legacy overrides of vanilla defs.

Impact:
- Satisfying the check with a `drop` entry would make the DefDrop step strip vanilla references.
- `legacy-token-set.txt` would flag vanilla names in re-saves.

Fix: a legacy def that exists in the new DefDatabase (any pack) is Unchanged. Exclude defNames that vanilla, the DLCs or dependencies define from observed tokens.

**M2. Append-only merge and `-Verify` are broken for inputs that change.**

Evidence:
- The merge keeps the fresh aggregate for an existing key (`Legacy/LegacyExtraction.cs:242-251`). When an input disappears, the row silently loses its tier, so required can drop to dev.
- `builds.tsv` contains rotating inputs: `saves:Autosave-3.rws`, `saves:Quicksave1.rws`, and the Workshop item, which keeps a fixed id while its sha256 changes.
- `--verify` compares an extraction without merge (`:123`) and fails on any missing or changed input (`:296-305`). It therefore fails as soon as the owner plays, and can never pass again after a retaining merge.

Fix:
- Union aggregates with the retained line.
- Verify against the same merge.
- Snapshot observed token sets per save id instead of re-reading live files, or exclude autosave and quicksave files.
- Use timestamps that do not depend on the time zone. ZIP entry times use the local zone: `tools/Export-LegacySchema.ps1:138`.

**M3. Extractor bugs can never be removed from the corpus.**

The append-only key includes derived columns (saveRoot, flags, default, …). After B1, B2 and M1 are fixed, the wrong rows stay in the corpus, and therefore in the ratchet, forever.

Fix: add an extractor schema version. A change of version rewrites the corpus and requires a reviewed diff. Append-only applies to inputs, not to derived facts.

**M4. Parity ignores member renames.**

Evidence:
- `RenameMap.cs:38-44` reads `defOfFields` but not `members`.
- RenameApply's applied log contains `members`: 2 entries in R and 237 renames and moves in R2 (`spec/final/rename-applied-R*.sample.json`). They would show up as unexplained differences.
- A member that moves between types cannot be represented, so R2's own parity check is unusable.
- `defs` is keyed by defName only and ignores defType.

Fix: read `members`, including moves, match moved members in their target type, and key defs by root.

**M5. Content parity is far weaker than proposal (a) §2.4.**

`Parity/ContentParity.cs` compares raw XML per file. It does not have:
- xpath literal mapping, so patches whose xpath names a renamed def report unexplained differences;
- resolved defs, def order per type, patch results, texture and sound reference sets, or superset and minimal configurations.

DefInjected handles are mapped only when the type was in the `Mugirl.` root namespace (`:114`).

Fix: compare `ContentModel` resolved defs and patch results, and map xpath predicates.

**M6. `--save-corpus` passes too easily.**

Evidence:
- A report with `state` Failed or Newer, or with `failedStep` set, still counts as a pass (`SaveCorpusRun.cs:55-70`: only exceptions count).
- Zero saves counts as a pass.
- `*.rws.gz` files are ignored, which includes A5's P-0927 and P-1004 fixtures.
- There is no idempotence check (a second run should do 0 actions) and no check of the ≤ 2 s budget from decisions §2.

**M7. Validator rules from decisions §2 and validator-tooling §3.4.7 are missing.**
- Rename targets of ThingDef or RecipeDef must not end in a digit.
- ManeuverDef, ToolCapacityDef and tool ids stay frozen unless a verb-ID step exists. Coverage currently asks for dispositions of `ManeuverDef` and `ToolCapacityDef` rows.
- The Removable-container rule.
- Every step has a unit test.
- The D4 rule: a renamed type under a side-document root requires the `GetBackCompatibleType` postfix. This rule currently has no owner.

**M8. The key-literal exemption is too narrow.**

It covers only legacy type names (`Checks/LookupChecks.cs:176-178`). Old Keyed keys stored as saved values (`legacy-saved-values` rows of kind `keyed`) and legacy defNames in SaveUpgrade value and def tables will fail C.translation-keys once Wave S renames Keyed keys (F16).

Fix: also exempt legacy saved values, legacy Keyed keys and legacy defNames inside `Mugirl.SaveUpgrade`.

**M9. Test quality.**

- **The extractor has no tests.** SaveRoots, ScribeSchema, SaveTokenScanner and ContentSchema are untested. `--verify` proves determinism, not correctness, which is how B1, B2 and M1 got through.
- **A positive control proves nothing.** `absent: C.forbidden-patterns | SaveUpgrade/` passes vacuously:
  - `ControlSaveUpgrade.cs` contains no forbidden pattern;
  - `SaveUpgrade/` is not copied into `control-mod/Source/MugirlRace`, as `%TEMP%\mg17-a2r\st` shows.
- **No positive controls for C.save-upgrade.** Nothing checks that a correct rename, consumption or materialisation produces no failure.
- **`--expect` has no `absent` form.**

Fix:
- Add a legacy-control DLL and a control save that contains vanilla labels, vanilla defs and static classes.
- Put `Gloden` into the SaveUpgrade control file.
- Add `absent:` controls.

### Minor

- **m1.** `legacy-token-set.txt` includes undisposed rows whose names still exist in the new build, for example after a shape change (`SaveUpgradeCheck.cs:310`). This gives false hits in the re-save scan.
- **m2.** The name-reuse rule:
  - The type rule (`DispositionEngine.cs:430`) fires when the target is a legacy name of the same entity (info or dev tier).
  - The def rule (`DispositionEngine.Rows.cs:59`) checks a different hazard than F10d.
  - There is no chain check for labels or enums.
- **m3.** B.save-confinement:
  - It reads only `[HarmonyPatch]` attributes, and the last attribute wins (`Checks/StructureChecks.cs:216-245`). It misses `TargetMethod(s)` and manual `Patch` calls.
  - It should use the targets B.harmony-apply actually applied.
  - The confined list lacks `Verse.Scribe`, `CrossRefHandler` and `PostLoadIniter`.
- **m4.** The B.component-ctors side-effect scan ignores `newobj` of mod types, `callvirt`, and static-constructor triggers through `ldsfld` (`StructureChecks.cs:150-183`).
- **m5.** C.save-upgrade invokes mod code (`ToJson`) in the data phase. This breaks the invariant documented in `ValidationRun.cs:12-14` (call at `:99`). Move it to phase 4.
- **m6.** `CompareShape`:
  - flags default drift on legacy rows with `forceSave=True`;
  - treats `Undefined=>Deep` and `Deep` as different;
  - does not check default drift of settings.
- **m7.** The `save-upgrade-modules.tsv` rows `def FacialAnimation.*Def/...` never match, because the corpus uses the short root. These lines become "unassigned". In total 75 required lines are unassigned.
- **m8.** `legacy-saved-values.tsv` has duplicate `keyed` and `literal` rows for the same value: d.bak inputs have no content, so their keys are recorded as `literal`.
- **m9.** `Content/DefRoots.Root` implements the wrong engine rule. It is still used by `DefReferenceCheck.cs:202` and documented as "the DefDatabase". Keep only `Database`.
- **m10.** Production code `Metadata/SourceMap.cs:89` knows the path `/Controls/NegativeControl/`.
- **m11.** `--frozen` is accepted but ignored: a dead compatibility option.
- **m12.** `builds.tsv` commits the owner's personal save file names. Consider writing `saves:<sha8>` instead.
- **m13.** The 1.6 reference (`ExtraWeapon2\bin\Release`) is a personal project's output, so reproducibility depends on it.
- **m14.** A build artefact inside the repo: `Controls/NegativeControl/obj/Debug/net10.0/NegativeControl.csproj.AssemblyReference.cache` was rewritten on 2026-10-15 at 13:06, by an in-repo build or the IDE.
- **m15.** IlParity:
  - the Harmony-order key has no parameter list, so overloads merge (`Parity/IlParity.cs:522`);
  - it builds `X::` when no method name is given;
  - numbering of anonymous types depends on compile order.
- **m16.** PublicApiCheck:
  - the `harmonyId` check does not prove that the id is passed to `new Harmony` (`Checks/PublicApiCheck.cs:159-173`);
  - nothing checks that mounting code still calls `IsMugirl`;
  - the initial `public-api.json` lacks xmlField entries for `CompProperties_MugirlMount` (D4).
- **m17.** `value:` and `label:` tokens are split at the last `/`, which breaks values that are paths (`SaveUpgrade/GoldenSteps.cs:63`, `DispositionEngine.cs:316`).

## REQUESTS

- **Orchestrator:**
  - Correct the database-root wording in decisions §2.
  - Pin in K1 the selector grammar and name space (legacy or new), the token format, and value contexts that carry the owner and label (B3).
  - Assign the D4 side-document rule.
- **A1:** Export each value context's owner and label in the manifest. Confirm whether enum and consumes tokens use legacy or new names.
- **A3:** Have the rename tool translate the name-keyed `structure-debt.allow.txt`, `scribe-defaults.allow.txt` and `patch-order.txt`.
- **A4:** Copy `NegativeControl` recursively, as A2 already requested.
