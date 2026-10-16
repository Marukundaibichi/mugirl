# RenameApply — Wave R / R2 rename tool

Applies the final rename specs (`spec/final/R/*.json`, then `spec/final/R2/*.json`) to a mod tree in one deterministic pass
per wave: C# (semantic, every project under `Source/`), XML under `1.7/**`, validator baselines, project files. It verifies
the C# result (compile + binding equivalence) and that every spec row landed (conformance) before writing, writes as one
transaction with a backup, and emits a report and an applied log; the applied log is the input of the upgrader's generated
type/def tables (`spec/final/_gen/applied_to_manifest.py`).

## Build

```
dotnet build tools/RenameApply/RenameApply.csproj -c Release -o tools/RenameApply/bin      (paths relative to the scratchpad)
```
.NET SDK 10.0.401; references the active SDK's Roslyn (`sdk/<ver>/Roslyn/bincore`, the compiler of the mod build).
Override with `-p:RoslynDir=<dir>`. The build copies `frozen-names.json` (= `spec/final/public-api.json`, written by
`gen_public_api.py`) next to the tool as the default frozen-name guard.

## Run

```
cd <tree>\Source      (global.json pins the SDK of the design-time builds)
dotnet RenameApply.dll --repo <mod root> --spec <file|dir> [--spec ...] --report <report.json> [--applied <applied.json>]
       [--dry-run] [--msbuild-property Name=Value]... [--dump <dir>] [--literal-exclude <glob>]... [--report-only-baseline <glob>]...
       [--no-default-excludes] [--frozen-names <public-api.json>] [--game <RimWorld dir>] [--backup <dir>] [--keep-backup]
       [--force] [--quiet]
dotnet RenameApply.dll --restore <backup dir>        (puts a tree back after an interrupted run)
```
- Wave R: `--spec spec/final/R`. Wave R2 (on the post-R tree): `--spec spec/final/R2`. Never mix the two in one run.
- Always pass `--frozen-names spec/final/public-api.json` (decisions 5: the frozen public API). A spec row that renames a
  frozen defName (exact or `defNamePattern` glob), abstract Name, the Mod short name or a frozen type (PublicApi types may
  move files but keep their names) is an error; aliased XML classes (D4) only warn `public-alias-needed`.
- `--dry-run` verifies everything and writes only the report. A real run writes only when there are no errors (`--force`
  overrides, debugging only). `--dump <dir>` mirrors every would-be-written file. `--applied` writes the applied log.
- A copy outside `Mods/` needs `--msbuild-property "RimWorldDir=..." --msbuild-property "WorkshopDir=..."`. `RimWorldDir`
  (or `--game`) also locates `Data/*/Defs` for the Def-shape analysis of derived names (see Outputs).
- Exit codes: 0 ok · 1 errors (nothing written) · 2 usage/spec errors · 3 environment/internal error (incl. a rolled-back write).
- Duration on the current tree: ~70 s per wave (10 design-time builds, 2–4 compilations of all projects).
- Side effect: design-time `Compile` per project writes `obj/` of the tree being renamed. Run it on the integrated tree
  with no concurrent builds (or on a copy).

### Writing is a transaction

Before the first write every file the run overwrites or deletes is copied to the backup folder (default
`%TEMP%/RenameApply/backup-<time>`, must be outside the tree) with `backup-manifest.json` (files to restore, files the run
creates). An IO failure while writing restores the tree from it and exits 3; an interrupted process leaves the folder for
`--restore <dir>`. After success the folder is deleted unless `--keep-backup`. Source/, 1.7/ and tools/ are untracked by git:
take a fresh tree archive (like `DevData/Backups/PreRefactor-1.7-20261015/tree.zip`) right before the real run anyway.

### Procedure per wave (integrator)

1. `spec/final/_gen/lint_spec.py <tree> [<post-R tree>]` → 0 errors (re-run the generators first when the tree changed:
   `gen_R.py <tree>`; for R2 `defof_facts.py <post-R tree>`, `gen_R2.py <post-R tree> <R report>`, `gen_deferred.py`).
2. `--dry-run` → review warnings (`literal-*`, `unedited-mention`, `public-alias-needed`, `root-namespace-remaining`).
3. Archive the tree, real run with `--applied` (and `--backup`).
4. After R2: `gen_ownership.py <post-R2 tree> <R2 applied>` and `lint_spec.py <tree> <post-R tree> --post-r2 <post-R2 tree>
   --r2-applied <R2 applied>` (L16: one Wave S owner per file).
5. Build, tests, validator, ShaderCheck, `applied_to_manifest.py` (K1 rows; exit 0) and the order report (see Harness).

## Spec format

A spec file is one object (or an array, or `{"specs":[...]}`; a directory is the union of its `*.json`). Identical rows
merge; conflicting ones are errors. Unknown properties are ignored (reports only: `saveVisible`, `xmlVisible`, `reason`,
`owned*`, `alsoListedBy`, `wave`, `mote`, `feature`).

Wave R sections (identity-preserving):
- `types[]`: `old`/`new` CLR names (`Ns.Type`, nested `Outer+Inner`, generic `Type`1`), `oldFile`/`newFile`.
  `old == new` + `newFile` = extraction into its own file; a `newFile` that differs only in case renames the file.
  `transitional` = C23 marker `{"waveS","upgrade"}`; `publicAlias` = D4 marker (the old name stays as an alias type);
  `definjectedHandles` = DefInjected keys whose class-name handle segment follows the short name (linter L10). All are
  copied into the applied log.
- `fileMoves[]` (C# files incl. partial parts), `defs[]` (`defType` = database root short name, `old`, `new`,
  `sharedNameWithOtherDefTypes` = the OLD name exists in another def type, `explicitRefs` = `path:LINE` sites of a shared
  name, `notRefs` = sites that carry the old text but mean something else, `newNameSharedWithOtherDefTypes`), `abstractNames[]`.
- `memberRenames[]`: `member` (`Ns.Type.Member`, overloads `Ns.Type.M(T1,T2)`), `new`, `addAttributes` (e.g.
  `DefAlias("Mugirl_Nuzzle")`, used where two DefOf fields would collide, C05). Instance fields that XML loads rename their XML
  elements (schema-typed) and DefInjected path segments; patches that mention them are reported.
- `createFiles[]`: `path`, `content` (verbatim; e.g. `PublicApi/XmlAliases.cs`). `baselineAppends[]`: `file`, `lines`.
- `textEdits[]` (also R): comment corrections such as the `MugirlSettings` header.

Wave R2 sections:
- `createTypes[]`: `fullName`, `file`, `kind` (`defOf` | `staticClass` | `enum`), `accessibility`, `header` (comment lines),
  `members` (enum), `linkWith` (projects that link one of these files by path link the new file too, e.g. NegativeControl).
- `memberMoves[]`: `to` (`Ns.Type`, or `Ns.Type@path` to pick a partial part), `members[]`, `accessibility` (rewrites the
  modifier), `order` (`source` = declaration order, default; `spec` = the order of `members`, e.g. overloads together),
  `referenceRules[]` (`pathPrefix`→`to`: the declaration is copied into every rule target and each reference uses the target
  of its file's FINAL path — vanilla defs declared per feature, C04). Static members only.
- `memberDeletes[]` (`members[]`), `dissolveTypes[]` (`type`: must be empty after the moves except a static ctor; deleted
  with its file when nothing else remains).
- `attributeArgumentRewrites[]`: `attribute` (metadata name), `argument` (positional index), `valueFormat` (`"PatchModule.{0}"`),
  `rules[]` (`pathPrefix`, `value`, `{folder}` = segment after the prefix), `requireNamespace`.
- `textEdits[]`: `file`, `old` (must occur exactly once; LF in specs, CRLF files handled), `new`. C# files are edited inside
  the verified compilation; other files (csproj, txt, xml) after the other passes.
- `xmlSplits[]`: `source` (Defs, Patch or DefInjected file), `targets[]` (`file`, `owner`, `feature`, `defs` = `defName` |
  `Type/defName` | `Name=<abstract>`, or `operations` = 1-based top-level indices + `operationFingerprints`, `header` for
  created files such as `PatchGovernance` comments). Blocks keep their leading comments/blank lines; existing targets get them
  appended before the closing root tag; created targets copy the XML declaration and root tag of the source. DefInjected
  entries of moved defs follow into `DefInjected/<RootType>/<target file name>` of every language. Emptied sources are deleted.
  Patch files apply in file-name order: name split targets so that operations on a shared target keep their order (the
  brainwash-helmet stock patch is `Restraints_TraderStock_Patch.xml`, after `MilkFood_TraderStock_Patch.xml`).
- `keyedSplits[]`: `source` (file name under `Languages/<lang>/Keyed/`), `contentRoot`, `keys` (key → target file), `deleteKeys`.
- `contentFileMoves[]` (whole-file XML renames; same-named DefInjected files follow).

## What it does

C# (all projects; types of the mod and of MugirlRace.Tests are renamed, negative controls never):
- Projects from the exact csc command line of a design-time `Compile`; source generators run; baseline errors compared, not fixed.
- Phase 0 file layout and type extraction (one type per file; a file whose only type moves is moved, a file emptied by
  extraction is deleted; projects that link a source file get links for the extracted/created files; case-only file renames).
- Every simple name is a site bound with the semantic model: type renames (declarations, references, attributes, crefs,
  `nameof`/`typeof`, `using` alias/static), namespace moves with using repair and deterministic qualification fallback,
  [DefOf] fields coupled to renamed defNames (and `[DefAlias("x")]` arguments), member renames, member moves (owner rewritten,
  unqualified uses qualified, `Old.M` inside the target type becomes `M`, private helpers of the old type qualified),
  attribute argument rewrites, text edits. Harmony-bound members (convention names in `[HarmonyPatch]` classes),
  `[TweakValue]`/`[DebugAction]` members and instance members are refused as move sources.
- Verification loop: compile all projects; fixes ambiguities by qualification; binding-equivalence check of every name (moved
  declarations are checked at their new position). Usings that became unused are removed (also in created types).
- String literals: exact old type full names, unshared defNames, keys are replaced; shared names only at `explicitRefs`;
  `notRefs` and Keyed keys that merely share a type-name prefix are left alone; literals equal to an old short type name or a
  renamed/moved member name are reported (`literal-short-type`, `literal-member-name`: reflection by name).
- Comments: full type names and distinctive short names are rewritten; defNames/keys reported.

XML (`1.7/**`), span edits on the original text:
- `Class`, type-valued text, Def subclass element names, XPath tokens (`@Class=`, `compClass=`, `defName=`, `text()=`).
- Def references typed by a schema built from the compilation (`XmlSchema`, private engine fields included): a field of a Def
  type is a reference of that database; `ThingFilter.categories` etc. are typed string refs; other engine string fields
  (backstory categories, tags) are not references; unknown shapes (dictionaries, FA/VEF types) fall back to name rules.
- `<defName>`, element-name dictionary keys, DefInjected keys and class-name handle segments (also handles set only in C#),
  derived names (`Make_`, `Administer_`, `Blueprint_`, `Blueprint_Install_`, `Frame_`, `Corpse_`, `Meat_`), abstract Names,
  Keyed keys, unique defNames/abstract Names inside Defs/Patches comments (commented-out XML follows).

Baselines (`Source/Tests/MugirlRace.Validation/Baselines/*.txt|json`, `tools/*.json`): type names, `Type::Member` and member
names of moves/renames, unshared defNames, keys, abstract Names, moved paths. Report-only (named on purpose):
`forbidden-patterns.txt`, `dropped-*.txt`, `save-types.txt`, `save-enums.txt` (legacy 1.6 schema). `legacy/` is never touched.

Conformance ("applied == spec", critique-validation 2.5 item 5): after all passes and before writing, every spec row is
checked against the final tree with exact path spelling — `newFile`/`fileMoves`/`contentFileMoves`/created files exist as
spelled, renamed defs exist under their (possibly renamed) Def root and the old name is gone, abstract Names, split defs and
operations are in their targets and gone from the source, Keyed split keys are in their target file in every language,
member moves/renames/deletes and text edits are in the applied log, dissolved types are gone, baseline lines are present.
A miss is an error `spec-not-applied` (nothing is written); the info finding `conformance` counts the checked rows.

Never edited: `1.6/`, `Bio_1.6/`, `Odyssey_1.6/`, `Versions/1.6/`, `DevData/`, `docs/`, `.git`, `LoadFolders.xml`, `bin/obj`.
Other mentions (docs, scripts, controls' expected-failures) are reported (`docs-mention`, `unedited-mention`).

## Outputs

- Report (`--report`): `maps`, `fileOps`, `edits` (`file` = path before the run; extracted code reports its source line),
  `findings`, `compile`.
- Applied log (`--applied`, after a real run): `waves`, `types[]` (old/new/saveVisible/xmlVisible/module/implicit/
  transitional/publicAlias), `defs[]` (defType/rootDefType/old/new/derived/derivedBy/saveVisible/newNameSharedWithOtherDefTypes),
  `defOfFields[]`, `members[]` (rename/move/copy/delete/dissolve), `keyed`, `abstractNames`, `files`, `createdFiles`,
  `splits[]` (def/operation/definjected/keyed with from/to/language), `attributeArguments`, `fileOps`.
  Derived def names are logged only when the def's shape implies them (`derivedBy: shape`: `Make_` ← recipeMaker,
  `Administer_` ← ingestible.drugCategory, `Corpse_`/`Meat_` ← race, `Blueprint_`/`Frame_` ← designationCategory,
  `Blueprint_Install_` ← minifiedDef, resolved through ParentName over the mod's and the game's Defs), when the run met the
  name (`referenced`), or when the shape is unknown (`unknown shape`, info finding).
- `spec/final/_gen/applied_to_manifest.py <R log> <R2 log> --dll-types <post-R2 dll-types.tsv> --tree <post-R2 tree>
  [--ownership spec/final/ownership-postR2.json]` turns them (+ `upgrade-hand-rows.json`) into the K1 manifest's
  `types`/`defs`/`settings` rows. WAVE-AWARE: rows map legacy names to what the post-R2 build contains; transitional types and
  hand rows whose legacy type/def still exists keep their R name now and are listed in `pendingWaveS` (the owning module
  applies them in Wave S together with the deletion); every target is checked against the post-R2 assembly/Defs and every
  row has a module (exit 1 otherwise). Samples from the test run: `spec/final/rename-applied-R.sample.json`,
  `-R2.sample.json`, `upgrade-rows.sample.json`.

## Spec generators and linter (`spec/final/_gen`)

`scan_dependents.py` (dependents' Defs, Patches, Languages, C# incl. literals and Harmony-bound parameter names, XPath
structure, load-folder assemblies) → `gen_public_api.py <R log> <R2 log>` (public-api.json, owner-migration-list.md, the
tool's frozen-names.json) · `gen_R.py <tree>` · `defof_facts.py <post-R tree>` + `gen_R2.py <post-R tree> <R report>` ·
`gen_deferred.py` (deferred-S.json, upgrade-hand-rows.json) · `gen_markers.py` (moogirl-era-markers.json) ·
`gen_ownership.py <post-R2 tree> <R2 applied>` (ownership-postR2.json; rules in `ownership.py`) ·
`lint_spec.py <tree> [<post-R tree>] [--post-r2 <post-R2 tree> --r2-applied <json>]` (rules L01–L16, `lint-report.json`;
`LINT_SPEC_ROOT=<dir>` lints another spec set; negative test `tooltest/lint-negative/run.ps1`) ·
`export-foreign-types.ps1` (engine, Unity compat, HAR, Harmony, FA, VEF type names for L03/L04) · `dll_types.ps1`
(metadata-order type dump of a built assembly).

## Harness (`tooltest/final`)

`pipeline.ps1 -Stage all|snapshot|specs|R|R2|post|checks` (copy the repo → `snapshot`; generate + lint; apply R
`snapshot`→`postR`; apply R2 `postR`→`postR2`; ownership/public API/markers + L16; isolated builds, tests, validator,
ShaderCheck, selftest, K1 rows, order report, validation diff), `run-tool.ps1`, `run-checks.ps1 -Root -Out`,
`selftest.ps1`, `compare_validation.py` (validator diff after name mapping), `order_diff.py` (reflection-family order,
per-root def load order with DefMap-saved roots flagged, patch-operation order on shared targets).

## Limitations

- Analyzers are not run by the tool; `dotnet build` afterwards is the check (0 warnings on the test run).
- Member moves: static members only; references through derived types or aliases are reported, not rewritten; nested types
  used by moved code are not re-qualified (the compile check would catch it).
- XML field renames: DefInjected path segments are renamed by name (not owner-checked, reported as info); patches only reported.
- `nameof`/`typeof(...).Name` of renamed symbols change value: reported. Keyed keys composed at runtime: reported.
- Nested namespace declarations around moved types, global-namespace types and moves across project directories are refused.
- No support for moving whole content folders (`1.7/Mods/<X>`).
- Renames and file moves change reflection order (metadata order) and def load order; the tool does not preserve them.
  `order_diff.py` lists every change for sign-off.
