# Critique: design.md §4 (execution model), §6 (validator and tests), Wave R rename tool

Role: adversarial reviewer. Inputs: `design.md` v1 and the `understand/` reports (validator-tooling, engine-hooks, real-saves, content).
Every fact below was re-checked read-only in the repository, the game (DecompilerServer `rw17-game` = RimWorld 1.7.4977), Harmony
(`0Harmony.dll` from workshop 2009463077/1.7, context `harmony17-review`) or the user's data folders.
Paths are relative to the repo root R unless they are absolute. Helper scripts used: `spec/work/dllinfo.ps1` and `spec/work/zipdlls.ps1`.

Severity levels:
- **Blocker**: the wave cannot meet its exit criterion, or data or behaviour is lost silently.
- **High**: a likely silent regression.
- **Medium**: a gap or rework risk.
- **Low**: hygiene.

---------------------------------------------------------------------------------------------------------------------
## 0. Findings at a glance

| # | Sev | One line |
|---|---|---|
| F1 | Blocker | The pre-refactor source and content cannot be restored. Only the DLL and PDB are snapshotted. `Source/`, `1.7/` and `tools/` are untracked, and `DevData/` is gitignored. |
| F2 | Blocker | §1 equates "published releases" with git. In fact 10 of the 14 distinct release-ZIP DLLs, including the current Workshop build, exist in no git object. |
| F3 | High | The parity baseline for Wave R is wrong. Wave A changes the DLL, so R must be compared with the integrated post-A build, not with the 2026-10-15 snapshot. |
| F4 | High | "Validator green after R" is unreachable or meaningless. About 320 baseline lines and 5 validator constants are keyed by mod names, and `--update-baselines` would hide real losses. |
| F5 | High | The Roslyn and XML rename misses: DefInjected class-name handles (one set in C#), XML field names, XPath predicates, untyped def references, test-mod string reflection, linked files, name-sensitive logic, generated defNames and doc links. |
| F6 | High | Renames reorder Harmony patches on at least 7 targets. `HarmonyBefore`/`HarmonyAfter` cannot order Mugirl's own patches. |
| F7 | High | Renames and file moves change metadata order. This changes GameComponent tick and save order, FloatMenu provider order and auto-take, and `[StaticConstructorOnStartup]` order. |
| F8 | High | XML file renames change def and patch load order and can silently shadow files. The validator drops shadowed Def/Patch files without a report. |
| F9 | High | Parallel agents share one untracked working tree and fixed `%TEMP%` build dirs. In-game runs share `1.7/Assemblies`. |
| F10 | High | C.save-upgrade misses default drift, value-type and LookMode changes, self-declared "Consumed" claims, name reuse, map composition and index state. |
| F11 | Medium | The settings file stores the settings class's full name. MooGirl-era settings files have a different file name. |
| F12 | Medium | Save-schema enforcement is switched off from Wave R to Wave V, while Wave S changes the schema. |
| F13 | Medium | Shared hotspot files in Wave A and Wave S (LegacyLabels.cs, the step registry, baselines, Core hooks, scripts). |
| F14 | Medium | The offline real-save corpus test has no Def catalog, because the selectors need `DefDatabase`. |
| F15 | Medium | Golden step fixtures "trimmed from real fixtures" contradict the rule that personal saves stay out of git. |
| F16 | Medium | Keyed keys that look like namespaces, key renames, and the key-literal rule. |
| F17 | Medium | No SDK pin, so codegen can drift between the A and B builds. |
| F18 | Low | Legacy inputs in DevData (release ZIPs, backups) could be cleaned away before extraction. |
| F19 | Low | In-game attribution of load errors for real saves that carry 13 to 1045 foreign mods. |
| F20 | Low | The `GenTypes.GetTypeInAnyAssembly` alias in §5.2(4) contradicts the engine-hooks advice and needs an in-game proof fixture. |
| F21 | Low | It is unproven that 1.6 builds (Harmony 2 era) boot on 1.7. Each replay fixture needs a feasibility gate. |

---------------------------------------------------------------------------------------------------------------------
## 1. Findings

### F1 (Blocker): no restorable pre-refactor baseline

**Evidence**
- `git status --short` lists `?? 1.7/`, `?? Source/` and `?? tools/`. `.gitignore` excludes `DevData/`.
- `DevData/Backups/PreRefactor-1.7-20261015/` holds only `MugirlRace.dll`, `.pdb`, `SHA256SUMS.txt` and `SNAPSHOT-NOTE.txt`.
- The DLL's sha256 `220e5d1a…` equals the live `1.7/Assemblies/MugirlRace.dll`. No `.cs` or `1.7/**` file is newer than it, so today the DLL matches the tree. After Wave R edits in place, that pairing is lost.
- The DLL is a Release build: `AssemblyConfiguration` is "Release" and `InformationalVersion` is `1.7.0+50f79794…`. That commit hash belongs to a commit that does not contain this source.

**Consequences**
- There is no source or XML to diff, and no `git diff -M` rename review.
- The A build cannot be rebuilt with a pinned compiler.
- No content-parity baseline exists for the 1.7 XML.
- Wave S agents cannot be isolated with `git worktree`, because untracked directories are not part of a worktree.

**Amendment: a new Wave 0 that runs before Wave A**
1. Owner-approved commit of the current tree on a branch or tag `refactor/base-1.7` (preferred; commits need the owner's go-ahead).
2. Always archive `Source/` (without `bin/` and `obj/`), `1.7/`, `tools/`, `LoadFolders.xml` and `About/` as `DevData/Backups/PreRefactor-1.7-20261015/tree.zip`, with a per-file SHA256 manifest.
3. Record a same-day baseline run and archive all reports:
   - `Validate.ps1` (both runtimes) and `-SelfTest`;
   - `MugirlRace.Tests` and ShaderCheck;
   - `Test-InGame -Suite default` and `SmokeTest.ps1`.

   AGENTS.md says historical records do not prove the current state. Pre-existing failures are listed, never "fixed" by restoring user-deleted resources.

### F2 (Blocker): legacy builds are not "git 324501c … 50f7979"

**Evidence**
- `spec/work/zipdlls.ps1` computed the git-blob hash of the DLL in every `DevData/Releases/*.zip` and compared it with all 55 DLL blobs in `git rev-list --all`.

| Release ZIP | In git? |
|---|---|
| 0920 | yes (`a172c15`) |
| 0920.2 | yes (`37545af`) |
| 0924 | yes (`131ee89`) |
| 1001 | yes (`50f7979`) |
| 0920.1, 0921 (same DLL as 0921.1), 0921.2, 0926, 0927, 0929, 0930, 0930.1, 1004 | **no** |

- The Workshop copy `workshop/content/294100/3578882219/1.6/Assemblies/MugirlRace.dll` (2026-09-26 21:12) has blob `35889b2a` (= 0927.zip). It is not in any git object: `git rev-list --all --objects | grep` returns 0.
- The frozen working-tree DLL `1.6/Assemblies/MugirlRace.dll` (blob `1edc8178`, sha256 `86a6c191…`) matches no release. 1004.zip is `7a404a87`.
- real-saves.md: S10 and S11 were written by uncommitted trees.
- More legacy blobs that §6 ignores:
  - `refs/stash` (e4ec89c: `1.6/Source/WRace/obj/Debug/MooGirlRace.dll`, `1.6/Assemblies/AlienRace.dll`);
  - `1.6/Source/WRace/obj/Debug/MooGirlRace.dll` (14 revisions);
  - two `refs/codex/turn-diffs/checkpoints/*` tree refs (they hold blob `1edc8178`);
  - 60 `MugirlRace*/MooGirl*` DLLs under `DevData/Backups`.

**Amendment**
- Rewrite §1 and §6 to identify builds by sha256 and provenance, in three tiers:
  - **published** (required): release ZIPs, the Workshop folder, and git release commits;
  - **observed** (required): every name found in a real save;
  - **dev** (Info only): the other git blobs (including stash, WRace/obj and codex trees), `DevData/Backups/**`, and the frozen working-tree DLL with its modified content.
- Fixture replay (§4 of this document) uses published ZIPs: 0927 has the widest install base and 1004 is the latest. The unreleased frozen DLL is not the main source.

### F3 (High): wrong parity baseline for Wave R

**Evidence:** §4 Wave A changes code before R:
- A1 adds `SaveUpgrade/`, a hub and Mod-ctor hooks;
- A4 adds priorities and changes the bootstrap and patch metadata;
- A2 adds exemptions.

Comparing the post-R DLL with `PreRefactor-1.7-20261015/MugirlRace.dll` would mix A's intended changes with R's renames.

**Amendment**
- R's parity baseline is the integrated end-of-A tree (tag `refactor/pre-R`), built by the same pipeline.
- Add an "A-parity" run (snapshot → pre-R). It may only show:
  - additions in declared namespaces (`Mugirl.SaveUpgrade`, the hub);
  - attribute changes on patch classes;
  - body changes in a declared method list.
- The snapshot stays the anchor for end-to-end behaviour review. Every Wave S body change must appear in a "deliberate change / defect fix" list (directive 4).

### F4 (High): name-keyed baselines and validator constants

**Evidence:** count of lines that contain a mod type or defName, per baseline (`Source/Tests/MugirlRace.Validation/Baselines`).

| Baseline | Lines | Notes |
|---|---|---|
| `patch-targets.txt` | 137 | |
| `unity-boundary.json` | 82 | |
| `dynamic-lookups.allow.txt` | 45 | `Type::Method` keys |
| `banned-symbol-exemptions.txt` | 20 | |
| `content-contracts.json` | 11 | Includes file paths such as `1.7/Defs/Bodies/Mugirl_MechanoidBodies.xml` and `AbilityDef/Mugirl_Throw` |
| `save-types.txt` | 11 | |
| `architecture-rules.json` | 9 | `Mugirl.Mugirl_DefOf::MugirlBody`, `Mugirl.ExternalBreastHediffDefs`, `allowedSources ["Compatibility/"]` |
| `empty-types.allow.txt` | 2 | |
| `transpiler-hooks.txt` | 2 | |

- `GoldenList.Compare` (`Infrastructure/GoldenList.cs`) fails on both missing and added lines. `--update-baselines` rewrites the golden lists wholesale and appends `REVIEW:` entries (`tools/Validate.ps1:17-23`). After a mass rename, a lost patch target looks like a rename.
- The validator hard-codes mod type names:
  - `Checks/HarmonyApplyCheck.cs:19` (`Mugirl.Core.HarmonyBootstrap`);
  - `Checks/PatchMetadataCheck.cs:13` and `Checks/LogCheck.cs:22` (`Mugirl.Core.MugirlPatchAttribute`);
  - `Checks/ShaderCheck.cs:27`;
  - `Checks/BannedSymbolsCheck.cs:31` (`Mugirl.Core.MugirlLog`).
- `allowedSources` rules key on source folders. A file moved into `Compatibility/` silently gains permissions.

**Amendment**
1. The rename tool translates every baseline column-aware: type, `Type::Method`, DocID, `Def/defName`, file path and source folder. The validator constants move into one `ModContract.cs` that is translated as well.
2. Gate: the validator passes **without** `--update-baselines`. A second run **with** `--update-baselines` must leave a zero git diff, which proves the translation is complete.
3. `Controls/**` (deliberately mimicked names) is excluded from translation, but `-SelfTest` must pass.

### F5 (High): coverage gaps of a Roslyn plus XML rename

**a. DefInjected list handles are short type names**
- `[TranslationHandle]` sits on these fields (rw17-game search):
  - `CompProperties.compClass`, `HediffCompProperties.compClass`, `AbilityCompProperties.compClass`, `WorldObjectCompProperties.compClass`, `StorytellerCompProperties.compClass`;
  - `VerbProperties.verbClass`;
  - `ReadingOutcomeProperties.doerClass`;
  - `QuestNode.myTypeShort`.
- 68 Simplified Chinese keys use Mugirl handles (content.md D.2):
  - `HediffComp_BrainWashingStar` ×39. Its `compClass` is set in a C# constructor (`HediffCompProperties_PerformanceEffect.cs:22`), so no XML rewriter can see it.
  - `Comp_MagneticShackles` ×10. §3.2 renames `Comp_X` to `CompX`.
  - `CompMooMilkable` ×1 (the "no Moo" rule).
- Broken injections are **not logged**. They exist only in `DefInjectionPackage.loadErrors` and the translation report (`LanguageReportGenerator`). `SmokeTest.ps1` and the in-game log policy cannot see them.
- `C.language-files` checks only the defName segment (`Checks/LanguageFileChecks.cs:85`).
- Amendment:
  - The rewriter computes handles from runtime-evaluated `compClass`/`verbClass`/`myTypeShort`, by instantiating Props in the validator's phase 2.
  - New check `C.definjected-paths` resolves every key path, including handles and list indices, against the resolved def.
  - The in-game assertion requires every injection whose file lies under the mod to have `injected == true` and empty `loadErrors`, in the active language.

**b. XML-bound field names**
- Renaming a field of a Def, CompProperties, DefModExtension or other XML-loaded class renames XML elements, DefInjected path segments, XPath steps and third-party XML. Examples:
  - `slaveCountRange` of `StockGenerator_Mugirl_Slaves`, used by KindSeal.LOL 1.7;
  - `displayString`, `milkDef`, `milkIntervalDays` and `milkAmount`, used by Subspecies (content.md §C).
- `C.xml-types` only covers our own Defs.
- Amendment:
  - The spec linter forbids field renames on §2 classes. `Verse.LoadAliasAttribute` exists, but it would be legacy code in the domain (directive 2), so it is an owner decision.
  - The rewriter covers XPath steps and DefInjected segments.

**c. Patches XPath predicates**
- Examples:
  - `li[compClass="Mugirl.CompRopeToBuild"]` and `li[@Class="Mugirl.StockGenerator_Mugirl_Slaves"]`;
  - `li[thingDef="Mugirl_BrainwashHelmet"]` and `recipes/li[text()="Mugirl_SmeltBondageApparel"]` (`1.7/Patches/MugirlPatch.xml`).
- Inside `PatchOperationConditional`, a stale predicate is a silent no-op.
- Amendment: an XPath AST rewrite of literals compared against `@Class`, `*Class`, `defName`, `text()` and def-typed children. Content parity compares PatchSimulator results.

**d. Untyped references**
- FA `targetJobs/li` holds JobDef names (`1.7/Mods/FacialAnimation/**`). These are typed only when the FA assembly resolves.
- `tradeTag` and `tag` values look like defs (`Mugirl_MilkFood`) but are strings and must **not** be renamed.
- Amendment: the residual-token scan described at the end of F5.

**e. Test code outside the mod assembly**
- `GameValidation/Suites/Mod/UiSmokeSuite.cs:81-83` uses the strings `"RequestPage"`, `"selectedPage"` and `"PageKeys"`.
- `B.reflection-names` scans only `MugirlRace.dll`.
- GameValidation never builds in the live folder (GuardLiveModFolder).
- Amendment:
  - Load Tests and GameValidation (non-plumbing) into the Roslyn workspace.
  - Run ReflectionNameCheck on `MugirlRace.GameValidation.dll`.
  - The R gate builds GameValidation both with `-p:MugirlRaceDll` and with `-p:MugirlPlumbingOnly=true`. The plumbing build removes `Suites/Mod/**` (`csproj:44-45`), so moving a suite across that line breaks one of the two modes.

**f. Linked files**
- `Controls/NegativeControl/NegativeControl.csproj:23-25` links `Core/HarmonyBootstrap.cs`, `Core/MugirlPatchAttribute.cs` and `Core/MugirlLog.cs` by path.
- Moving them breaks only `Validate.ps1 -SelfTest`.
- Amendment: the tool rewrites `Link` and `Include` paths, and `-SelfTest` is part of the gate.

**g. Name-sensitive runtime logic with identical IL**
- `Features/Corporate/Pricing/CorporateSupplyAnalysis.cs:109` uses `generator.GetType().Name.StartsWith("StockGenerator_Buy")`.
- Record `ToString` prints the type name.
- The settings file name uses the Mod short name.
- `GenTypes` falls back to the short name.
- IL parity cannot see any of these. Amendment: the parity tool lists every site that reads `Type.Name`, `Type.FullName` or `MemberInfo.Name` and then compares the value or uses it as a key. Each site needs explicit sign-off.

**h. Generated defNames**
- `Features/Misc/MilkRecipe/Patch_DrugAdministerDefs.cs:28` builds `"Administer_" + milkDef.defName`, and the result is saved in bills.
- Amendment: the corpus records generated-name patterns (string flow into a `Verse.Def::defName` store). The derived-name list in §5.4 gains `Administer_`.

**i. Docs**
- Moving files breaks relative links, which `tools/Test-Docs.ps1` checks.
- Amendment: either the tool rewrites links to moved files, or the R gate explicitly defers Test-Docs to Wave D. State which one applies.

**j. Paths the tool must refuse to touch**
- `1.6/`, `Bio_1.6/`, `Odyssey_1.6/`, `Versions/1.6/` (frozen);
- `DevData/`, `TMP/`;
- `docs/tools/` (1.6 probes; 27 files reference Mugirl names);
- `Controls/**` (except for the Link paths above).

**Residual-token scan (mandatory last step of R)**
- Scan `Source/`, `1.7/`, `tools/` and `LoadFolders.xml` for every old token in the applied map: whole identifier, case-sensitive.
- Every hit must be classified as one of:
  - a `SaveUpgrade/LegacyNames` entry;
  - a §2 public-API exemption;
  - a Keyed key that only shares a prefix with a namespace. For example, Keyed keys `Mugirl.WeaponWheel.*` share the text of namespace `Mugirl.WeaponWheel`, so a textual namespace replace would corrupt them.
- Unclassified hits fail.

### F6 (High): Harmony application order

**Evidence**
- `HarmonyBootstrap` sorts patch classes by ordinal full name (`Core/HarmonyBootstrap.cs:207`), and that order is the registration order.
- Harmony sorts by priority, then by `index` (`PatchSorter.PatchSortingWrapper.CompareTo` → `PriorityComparer(index, priority)`).
- `HarmonyBefore` and `HarmonyAfter` match the **owner** (the Harmony id) in the `PatchSorter` constructor. All Mugirl patches share the id `MugirlMod.Mod`, so they can be ordered among themselves **only by priority**.
- Targets with more than one same-kind Mugirl patch, from `patch-targets.txt`:

| Target | Kind | Count | Patch classes |
|---|---|---|---|
| `PawnGenerator.GeneratePawn` | postfix | 4 | Harmony_CorporateUniforms, Harmony_PawnGenerator_NewbornVisuals, Harmony_WeaponWheel_EnemyLoadout, PawnGenerator_GeneratePawn_Patch |
| `Pawn.DynamicDrawPhaseAt` | postfix | 3 | |
| `Verb.TryStartCastOn` | prefix | 2 | |
| `Verb.get_WarmupTime` | postfix | 2 | |
| `Pawn_EquipmentTracker.Notify_EquipmentRemoved` | prefix | 2 | |
| `ChildcareUtility.SuckleFromLactatingPawn` | prefix | 2 | |
| `Game.ExposeSmallComponents` | prefix | 2 | |

- Only 7 `[HarmonyPriority]` attributes exist in the source.
- Renaming `PawnGenerator_GeneratePawn_Patch` to `Harmony_…` moves it in the order.

**Amendment**
- Correct §3.2: "order between Mugirl patches only via distinct `[HarmonyPriority]`; Before/After only against other Harmony ids".
- A4 assigns priorities that reproduce today's effective order, **before** R.
- New check `B.patch-order`: on every target, same-kind Mugirl patches have pairwise distinct priorities.
- New golden `patch-order.txt`: the effective sorted order per target, from `Harmony.GetPatchInfo` after `Apply` in `HarmonyApplyCheck`. It must be identical modulo the map.

### F7 (High): metadata (reflection) order is behaviour

**Evidence**
- TypeDef order follows compile (path) order. The snapshot starts with `Harmony_StylingStationRefresh`, then `Compatibility/*`, and so on.
- Game consumers of reflection order:
  - `Game.FillComponents` and `Map.FillComponents` use `AllSubclassesNonAbstract` (engine-hooks.md:281). The result fixes the component list, which sets tick order and `<components>` element order.
  - `FloatMenuMakerMap.Init` builds `providers` from `typeof(FloatMenuOptionProvider).AllSubclassesNonAbstract()`. `GetAutoTakeOption` keeps the first option among equal `autoTakeablePriority`, so provider order decides what a click auto-takes.
  - `[StaticConstructorOnStartup]` call order.
- The snapshot has 6 GameComponents, 7 concrete FloatMenuOptionProviders and at least 6 `[StaticConstructorOnStartup]` types.

**Amendment**
- The parity tool reports order per family.
- GameComponent, MapComponent and FloatMenuOptionProvider order must be identical, or accepted explicitly with a reason.
- Code must never depend on reflection order. The lifecycle hub sorts its registrations explicitly.

### F8 (High): XML load order and silent shadowing

**Evidence**
- `Verse.DirectXmlLoader.XmlAssetsInModFolder` (rw17-game) walks the load folders in descending priority. For each it calls `GetFiles("*.xml", AllDirectories)` and then `dictionary.TryAdd(relativePath)`, so a lower-priority file with the same relative path is dropped silently.
- The validator mirrors this without a report (`Content/LoadFolderResolver.cs:92-117`). Only language files report shadowing (`LanguageFileChecks.cs`).
- The validator orders files ordinally, which is not the game's enumeration order.
- Patch order and DefDatabase order follow file order. They show in bill menus, category children and any `AllDefs` iteration.
- §3.3 wants XML file renames.

**Amendment**
- No XML file renames in R; do them per module in S.
- New failing check `C.content-shadowing` for Defs and Patches across the mod's load folders.
- Content parity compares per-type def order and patch-operation order using the game's enumeration rule.

### F9 (High): parallel execution hazards

**Evidence**
- `tools/Validate.ps1` uses fixed directories and `Reset-TempDirectory` clears them:
  - `%TEMP%\mg17-validation` (:94);
  - `mg17build\validation` (:184);
  - `mg17-control-mod` (:153) and `mg17-control-baselines` (:168);
  - `mg17-clr` (:201).

  Two concurrent agents destroy each other's builds.
- `tools/Test-InGame.ps1` refuses to start while RimWorld runs (:331). Its `-Deploy` overwrites the shared `1.7/Assemblies/MugirlRace.dll` (:382).
- Untracked trees cannot be put in worktrees (F1).

**Amendment**
- `Validate.ps1 -WorkRoot <unique>`.
- Each agent works in its own worktree (after the F1 commit) or a temp copy, and the integrator merges.
- A single in-game runner holds a lock file `%TEMP%\mugirl-ingame.lock`, and all in-game work is serialized.
- No build writes into `1.7/Assemblies`, which needs the validator-tooling F1 `OutputPath` fix. §6's "`dotnet build Source/MugirlRace.slnx` must succeed" currently contradicts `MugirlRace.csproj` (`OutputPath` = `$(ModDir)1.7\Assemblies\`).

### F10 (High): semantic holes in C.save-upgrade (§6 and validator-tooling §3.4)

| | Hole | Fix |
|---|---|---|
| a | **Default drift.** Defaults are not written. If an initializer or a Scribe default changed between a legacy build and now, an old save silently loads the new value. §5.3(4) has the step, but §6 has no check. | The corpus stores the default constant per (owner, label, build). "Unchanged" requires an equal default, or a `ValueMapping.MaterializeOldDefault`. |
| b | **Value type and kind changes.** A label that changes value type (int→float, enum type), Scribe kind (Values→Deep) or LookMode (Reference→Deep) is not Unchanged. | Compare these columns. |
| c | **"Consumed" is self-declared.** | The validator reads `Tests/MugirlRace.Tests/SaveUpgrade/Golden/<StepId>/{before,after}.xml`. Every consumed name must occur in `before` and in no `after`. |
| d | **Name reuse.** A new name equal to the legacy name of a different entity (type, def, or label in the same owner) defeats the null-gated runtime fallbacks of §5.2(3). | Hygiene fail. The spec linter checks this too. |
| e | **Map composition.** The map generated in Wave R is keyed by pre-refactor names, but the corpus is 1.6-era. | The generator composes legacy→pre→new and collapses chains, because hygiene forbids A→B→C. |
| f | **Index state.** JobDriver toil order and Lord graph (engine-hooks §9) cannot be compared across the 1.6 and 1.7 compilers. | Default policy: on a schema-0 load, end in-flight jobs with Mugirl drivers and Mugirl lords, unless a step claims the type. The check requires a policy or a claim for every legacy JobDriver and LordJob type. |
| g | **Saved strings.** `GetUniqueLoadID` prefixes, quest tags and signals, generated defNames (`Administer_`) and `CorporateRecord` keys. | Saved-value contracts. |
| h | **Dynamic defaults.** Fields set in `Initialize`, `PostMake` or a constructor from Props. | The IL rule requires `forceSave: true`. The "default == initializer" rule only covers constant initializers. |

### F11 (Medium): settings

**Evidence**
- `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\Mod_mugirl_MugirlMod.xml` contains `<ModSettings Class="Mugirl.MugirlSettings">`.
- `LoadedModManager.ReadModSettings` runs `Scribe_Deep.Look(ref target, "ModSettings")` (rw17-game), so the full class name is schema. validator-tooling.md §1.2 calls the full-name check "stricter than needed"; that is wrong.
- `MugirlSettings` is in the root namespace today (`Core/MugirlSettings.cs:1`), and §3.1 moves it.
- MooGirl-era settings files: `Mod_3578882219_MooGirlMod.xml` (`Class="MooGirl.MechanoidWorkControlSettings"`), plus `Mod_mugirl_MooGirlMod.xml`, `Mod_mugirl0519_MooGirlMod.xml` and others.

**Amendment**
- The corpus gets `legacy-settings.tsv` (class, label, default, per build), covered by C.save-upgrade through the SettingsUpgrade consumer.
- The owner decides whether `*_MooGirlMod.xml` files are migrated.
- Add settings fixtures (§4 of this document).

### F12 (Medium): enforcement gap from R to V

**Evidence:** §4 sets the save checks aside until V, but Wave S changes labels and structure.

**Amendment**
- C.save-upgrade and the corpus land in A2 and run from R onward in ratchet mode: the per-module count of undisposed required lines never grows.
- A module's S review closes only when its corpus lines are disposed. Ownership comes from the spec's module column.

### F13 (Medium): shared hotspots

**Evidence**
- In Wave S, every module writes `SaveUpgrade/LegacyNames/LegacyLabels.cs`, the hand-written part of LegacyDefs, the step registry and the validator baselines.
- In Wave A, `Core/MugirlMod.cs` is touched by A1 (SettingsUpgrade before `GetSettings`, early hooks) and by A4 (bootstrap).
- `tools/Validate.ps1` is touched by A2 (extraction) and A4 (`OutputPath`).

**Amendment:** the ownership table in §5 below, plus per-module table files.

### F14 (Medium): offline corpus test without a Def catalog

**Evidence:** `ThingWithComp(T)` and `HediffWithComp(T)` resolve through `DefDatabase` (§5.5). `MugirlRace.Tests` has no DefDatabase.

**Amendment**
- The pipeline takes an `IDefCatalog` interface. At runtime it is backed by DefDatabase; offline it is backed by the validator's ContentModel.
- The real-save corpus run becomes a validator mode `--save-corpus <dir>`, not an xunit test.

### F15 (Medium): fixture privacy

**Evidence:** §6 wants golden XML "trimmed from real fixtures" and also wants personal saves kept out of git.

**Amendment:** golden snippets contain only Mugirl-owned nodes, with neutral ids and names, synthesized or anonymized. They are committed after owner review. Raw saves never are.

### F16 (Medium): keys

**Evidence**
- The key-literal rule (`LookupChecks.cs:142-172`) exempts strings equal to current type names. Renames shift these exemptions.
- Keyed keys saved as data (content.md A.6.3) and third-party translations break when keys are renamed.

**Amendment:** Keyed-key renames are out of scope for R. In S they need a saved-value map entry and a release-notes entry.

### F17 (Medium): no SDK pin

**Evidence:** `Source/global.json` pins only the test runner.

**Amendment:** pin `sdk.version` in Wave 0. The parity report records `dotnet --version` and the Roslyn version. A calibration run (proposal a) proves zero noise.

### F18 to F21 (Low)

- **F18:** extract the corpus before any DevData cleanup. `builds.tsv` stores the sha256 of every input.
- **F19:** real saves list 13 to 1045 mods. Load errors that name only foreign names are classified "foreign", not "pass" and not "fail" (proposal c).
- **F20:** §5.2(4) patches `GetTypeInAnyAssembly`, against engine-hooks.md:452 (82 IL bytes, so an inlining risk). The X-alias fixture must prove it in game.
- **F21:** 1.6 builds were compiled against 1.6 Assembly-CSharp and Harmony 2, while 1.7 ships Harmony 3. Each replay fixture first does a plumbing boot. Failed patches are recorded in its manifest.

---------------------------------------------------------------------------------------------------------------------
## 2. Proposal (a): the behaviour-preservation check after Wave R

### 2.1 Definition of Wave R

Wave R contains **only identity-preserving renames and moves**:
- namespaces, types and members;
- C# file moves, including all cross-module moves (Raids→Corporate, VanillaFixes→Core, RenderRefresh→Rendering, shared helpers→Core), so that Wave S folder ownership is clean;
- defNames, Def class roots and DefInjected keys and handles that follow from those renames.

These are **not** in R; they go to S:
- DefOf dissolution;
- visibility and `sealed` changes;
- deletions;
- Scribe label changes;
- Keyed key renames;
- XML file renames.

With this scope the criterion stays crisp: **zero unexplained differences**.

### 2.2 Rename spec and tool contract (A3)

**Spec format:** `spec/rename-spec.tsv` with the columns `kind`, `module`, `oldId`, `newId`, `reason`, `publicApi`.
- `kind` is one of: namespace, type, member, enumMember, defName, xmlField, file.
- `oldId` and `newId` are a DocumentationCommentId for code, `DefRoot/defName` for defs, and a path for files.
- `reason` is one of: prefix, typo, moo, terminology, namespace, convention.

**Spec linter, run before R:**
- the map is injective per kind and scope;
- no new name equals a legacy-corpus name of another entity (F10d);
- no new name equals a vanilla type of the same role (§3.2b);
- no forbidden or ignored namespace segments;
- no `publicApi` entry, and no field rename on §2 classes;
- every `Comp_X → CompX` rename lists the affected DefInjected handles;
- no rename of an override of an external member, or of a Harmony-convention method (`Prepare`, `TargetMethod(s)`, `Prefix`, `Postfix`, `Transpiler`, `Finalizer`, `Cleanup`);
- no rename of a parameter of a Harmony patch method (`__instance`, `__result`, `___field`, or a parameter that matches the target's parameter name);
- no `[TweakValue]` or `[DebugAction]` member.

**Tool:** `Source/Tools/MugirlRace.Rename` (a new folder outside the mod's Compile glob).
- Roslyn `MSBuildWorkspace` over MugirlRace, Tests, GameValidation (full) and NegativeControl (linked docs). `Renamer` runs with `RenameInStrings=false` and `RenameInComments=false`. Comments are handled by the residual scan, because `forbidden-patterns` catches "Moo" in comments.
- A schema-aware XML rewriter (using the validator's ContentModel and TypeResolver):
  - Class attributes, `*Class`/`*Type` fields and Def root elements;
  - def-typed fields and lists;
  - DefInjected key paths (defName, field segments, runtime-evaluated handles);
  - XPath literal predicates and steps.
- Baseline translation (F4), Link/Include paths, and optionally markdown links.
- **Output:** `rename-applied.json`, the log of what was actually changed (old and new DocIDs and paths, with file:line). It must equal the spec (each entry applied exactly once).
- The upgrader's generated `LegacyTypes`/`LegacyDefs` parts are produced from the applied log composed with the legacy corpus (F10e).

### 2.3 IL parity: `MugirlRace.Validation --parity` (driver `tools/Test-RenameParity.ps1`)

It reuses `MetadataWorld` (Cecil), `ModuleStringFlow` and `ExternalMemberReferences`.

**Inputs**
- A = `refactor/pre-R` Release DLL and PDB, built in isolation.
- B = the post-R Release DLL and PDB, same SDK.
- M = `rename-applied.json`.
- The A content tree and the B content tree.

**Calibration (positive control):**
- Run with A against A′, where A′ is the same source rebuilt in another directory with the pinned SDK. The result must be zero differences.
- Also run with the snapshot DLL against a rebuild of the archived pre-refactor tree.

**Ignored (build noise):**
- MVID, debug directory and PDB checksum, `AssemblyInformationalVersion` (`+commit` suffix);
- `Nullable`, `NullableContext`, `NullablePublicOnly`, `RefSafetyRules` and `CompilerFeatureRequired` attributes;
- `.maxstack`;
- metadata row order (reported separately, see "Order families").

**Canonicalization of compiler-generated names:**
- `<M>b__N_K`, `<>c__DisplayClassN_K`, `<M>d__N` and `<M>g__L|N_K` are keyed by the mapped owner method plus the ordinal K. N is a method ordinal and changes when files are split.
- `<>c`, `<>O`, `<>z__ReadOnlyArray` and `<>y__InlineArrayN` are matched by owner and usage.
- `<>f__AnonymousTypeN` is matched by property names and types. The numbering is global, by compile order.
- `<PrivateImplementationDetails>` fields are matched by data content hash.

**Comparisons (all through M; A's names are mapped to B's):**

| Area | What must match |
|---|---|
| Types | 1:1 mapping, no unmatched types on either side. Same base type, interfaces, generic parameters and constraints, flags, layout and custom attributes. `typeof` arguments are mapped. String arguments must be equal or explained. |
| Fields | Same type, flags, constant value, RVA data and attributes. |
| Methods | Same signature (mapped) and flags. Parameter names must be equal or explained, because Harmony binds parameters by name. |
| Method bodies | Instruction by instruction: opcodes (short and long branch forms normalized), branch targets as instruction indices, operands as canonical mapped tokens, locals (mapped types), exception-handler table, `initlocals`. Any changed call target into Verse, RimWorld, System or Harmony fails. That catches silent rebinding, such as a different extension method or a nearer namespace winning, which retires the need for `B.member-rebinds`. |
| Order families | Metadata order of GameComponent, MapComponent, WorldComponent, FloatMenuOptionProvider, Alert, `[StaticConstructorOnStartup]`, `[DefOf]` and Harmony patch classes. Effective per-target Harmony order is a **hard** fail. The other families fail unless listed in `parity-accept.tsv` with a reason. |
| Name-sensitive sites | List of every read of `Type.Name`, `Type.FullName` or `MemberInfo.Name` that is compared or used as a key (F5g). Sign-off needed when a renamed type can reach the site. |

**String literals (`ldstr` and attribute strings):** a changed literal is accepted only if (old, new) is a projection of M:
- the simple or full name of a renamed symbol (`nameof`, log text);
- a defName;
- an XML field name.

Each accepted change is classified by the string-flow sink it reaches:

| Sink | Effect |
|---|---|
| Scribe label | **SCHEMA**: requires a LegacyLabels entry, otherwise fail. |
| `Translate`/`CanTranslate` key | The key must exist. |
| `DefDatabase` lookup or DefOf | The def must exist. |
| `ContentFinder`/`ShaderDatabase` path | The asset must exist. |
| `AccessTools`/`Type.GetX` | The member must exist. |
| Log or exception text | Benign. |

**Output:** `parity-report.json` plus a text summary:
- counts of matched types, members and identical bodies;
- explained differences per category;
- unexplained differences, which fail;
- order differences and name-sensitive sites.

**Negative controls (`Controls/Parity/{A,B}` projects):**
- `GenCollection.MaxBy` rebound to `Enumerable.MaxBy`;
- a changed constant;
- a Scribe label renamed through `nameof`;
- an unmapped type rename;
- a dropped `[StaticConstructorOnStartup]`;
- a Harmony priority tie that reorders patches;
- a changed key literal;
- a changed FloatMenu provider order.

### 2.4 Content parity (same mode)

The ContentModel for A and B is built with all optional packs active (superset) and also minimal. Through M the following must be equal:
- every resolved def (after inheritance and patches) as canonical XML, with Class and def references mapped;
- per DefDatabase type, the def order;
- patch-operation results (success and nodes touched) in game order;
- DefInjected `(language, defType, path)` → text, where every path also resolves in B;
- Keyed `(language, key)` → text;
- texture and sound reference sets;
- no shadowed Def or Patch file (F8).

### 2.5 Wave R gate ("green")

1. The slnx builds after the A4 fix. Both GameValidation modes build. `-SelfTest` builds and passes.
2. `MugirlRace.Tests`: the same test set (names through M), all passing.
3. Validator: translated baselines, pass without `--update-baselines`, zero diff with it, both runtimes agree.
4. IL parity has zero unexplained differences. Content parity has zero.
5. The residual-token scan has zero unclassified hits. The applied log equals the spec.
6. C.save-upgrade in ratchet mode is not worse than at `refactor/pre-R`.
7. In game, serialized: `Test-InGame -Suite default` and `SmokeTest.ps1` on the post-R DLL show no new failures against the archived Wave 0 reports.

---------------------------------------------------------------------------------------------------------------------
## 3. Proposal (b): legacy corpus extraction and the C.save-upgrade check

### 3.1 Extraction (A2, run in Wave 0; inputs immutable)

**Driver:** `tools/Export-LegacySchema.ps1`, a new file, so there is no conflict with A4's ownership of `Validate.ps1`. It calls `MugirlRace.Validation --extract-legacy-schema --out Baselines/legacy [--verify]`.

It is strictly read-only on the repo. Allowed: `git rev-list`, `git ls-tree`, `git cat-file`, `git archive | tar -x -C %TEMP%\mugirl-legacy\<id>`. Forbidden: checkout, stash apply, worktree on old revisions, and restoring user-deleted files. Temp folders carry a marker and are cleaned with the link-aware rule.

1. **Build inventory → `builds.tsv`** with the columns: id, tier, kind, source, sha256, gitBlob, assemblyName, era, packageId, firstSeen, lastSeen, contentRef.

   | Tier | Sources |
   |---|---|
   | published | `DevData/Releases/*.zip` (15 ZIPs, 14 distinct DLLs; verify `.sha256` sidecars), each with its own `LoadFolders.xml` and content; Workshop `3578882219`; git release commits (31 MugirlRace.dll revisions from 324501c to 50f7979; MooGirl 0.1.0 to 0.4.4 under D1) |
   | dev | every other DLL blob reachable from `git rev-list --all` (66 commits, including `refs/stash` e4ec89c and its untracked parent) and the `refs/codex/*` tree refs, identified by assembly metadata name, not path (includes `1.6/Source/WRace/obj/Debug/MooGirlRace.dll` ×14); `DevData/Backups/**/(MugirlRace|MooGirl)*.dll*`; the working-tree frozen DLL and the modified 1.6 content |
   | prerefactor | the snapshot. Informational only (§1). |

2. **Reference resolution**
   - Use a 1.6 `Assembly-CSharp` reference passed with `--ref16 <dir>` (for example the `Krafs.Rimworld.Ref` 1.6.x package or a recorded local copy; its sha256 goes in `builds.tsv`).
   - Add the commit's own `1.6/Assemblies/AlienRace.dll` (the MooGirl era bundled it), otherwise HAR 1.6 from the Workshop. Add Harmony 2.x.
   - An unresolved base type is an extraction error (exit 2), never "not save-visible".

3. **Per-DLL facts.** All TypeDefs with their raw base chain and SaveRoot (derived roots, validator-tooling §3.5.4) and the resolver used. Then:
   - per-owner Scribe labels: kind, value type, LookMode and default constant; string flow, a virtual-getter resolver, and `saveKey` evaluated from constructor IL plus the build's XML;
   - the schema graph (owner → label → element type);
   - saved enums with members;
   - `GetUniqueLoadID` prefixes;
   - literals stored into saved string fields (quest tags, signals, record keys, DunkHead shader and paths);
   - generated defName patterns;
   - the ModSettings class with its labels and defaults;
   - JobDriver and LordJob types;
   - XML-visible types (for the §2 alias table).

4. **Per-content facts**, resolved with that tree's `LoadFolders.xml` with every `IfModActive` set true:
   - `DefRoot/defName` (abstract flag, file);
   - types referenced from XML (Class, `*Class`, root elements, patch values);
   - Keyed keys that hold saved values.

5. **Observed names.** Stream-parse every real save into `legacy-observed.tsv`: the user's `Saves/*.rws` (S1–S11) and `DevData/Evidence/**/Saves/*.rws`. Only Mugirl-owned tokens are kept, by exact token, never by substring "Mugirl" (real-saves.md warns that third-party mods use it). This replaces hand entries. `legacy-extra.tsv` is only for names justified with an evidence path.

6. **Writing.**
   - Files: `builds.tsv`, `legacy-types.tsv`, `legacy-labels.tsv`, `legacy-schema-graph.tsv`, `legacy-enums.tsv`, `legacy-defs.tsv`, `legacy-xml-types.tsv`, `legacy-saved-values.tsv`, `legacy-settings.tsv`, `legacy-jobs-lords.tsv`, `legacy-observed.tsv`, `legacy-extra.tsv`.
   - Format: ordinal sort, LF, UTF-8 without BOM; each line carries build ids and a first..last range.
   - Append-only: a merge never removes a line.
   - `--verify` re-extracts from `builds.tsv` (checking sha256) and must reproduce the files byte for byte.
   - The corpus is committed under `Source/Tests/MugirlRace.Validation/Baselines/legacy/` and never shipped.

### 3.2 C.save-upgrade: inputs, algorithm, outputs

**Inputs**
- the corpus;
- the new build: Cecil, `ModuleStringFlow`, and reflection on the loaded mod;
- the `Mugirl.SaveUpgrade` tables: Types, Defs, Labels, Enums, Values, Settings, and Steps (Id, Consumes, Policy);
- ContentModel (superset and minimal);
- the step golden directory;
- `frozen-forever.txt`;
- the tier threshold: published and observed are required, dev is Info.

**Algorithm**
- **Types, labels, enums, defs, saved values and frozen-forever:** as in validator-tooling §3.4.1-6, plus the F10 rules:
  - default-drift check;
  - value-type, kind and LookMode check;
  - verified "Consumed" claims;
  - name reuse;
  - composed map (no chains);
  - a job/lord policy or claim for each legacy JobDriver and LordJob type;
  - settings coverage.
- **Map hygiene:** validator-tooling §3.4.7.
- **Leftover readers:** a legacy-only name used outside `SaveUpgrade/` fails. The ldstr scan exempts only `Mugirl.SaveUpgrade.*` (F7/F8 exemptions).

**Outputs**
- Report findings:
  - **Fail**: each undisposed required line, each hygiene or drift violation;
  - **Warn**: undisposed dev-tier lines;
  - **Info**: counts per kind and tier, and def renames that affect third-party mods (D5).
- `save-upgrade-coverage.tsv`: every corpus line → disposition, target, map entry or step id. It is diffed between runs and is the reviewer's view.
- `legacy-token-set.txt`: exact legacy-only tokens per kind. The re-save scan of the in-game suite and the R residual scan consume it.
- Ratchet mode (R through S): it fails only when a module's undisposed count rises compared with the recorded previous value.

**Negative controls:** validator-tooling §3.7, plus:
- default drift without a ValueMapping;
- a Deep→Reference kind change on an "Unchanged" label;
- a step that declares a name its golden `before.xml` lacks;
- a new name that reuses a legacy name;
- an uncomposed chain;
- a legacy JobDriver without a policy;
- a renamed settings class without a map entry.

---------------------------------------------------------------------------------------------------------------------
## 4. Proposal (c): the in-game upgrade suite and fixture production

### 4.1 Fixture catalogue (manifest per fixture, sha256-addressed)

| Id | Source | Converter path | Stored |
|---|---|---|---|
| R-S1…S11 | Read-only copies of the owner's saves (`%USERPROFILE%\AppData\LocalLow\…\Saves`). All of them go through the offline `--save-corpus` run. In-game candidates: S3 (MooGirl era, 18 mods, 3.3 MB), S6 (13 mods, 9 MB), S9 (44 mods, corporate), S10 (37 mods). | runs (1.6.4633 or 4871 game) | never in git; manifest in `DevData/Evidence/SaveFixtures/manifests/` |
| E-* | `DevData/Evidence/{BugReportFixes-20261011,CorporateValidation-AllDlc6,…}/Saves` (validation builds; contain `CorporateRuntimeValidation`) | runs | DevData (owner's evidence) |
| P-<build> | **Replay** of published builds on the 1.7 game: 1004.zip, 0927.zip (Workshop), 0920.zip; git `46d2190` (0.5.0) and `324501c` (0.4.7); MooGirl `cacbc58` (0.4.4) if D1 = yes | skipped (identical environment after meta rewrite): the worst case for type renames | `DevData/Evidence/SaveFixtures/<id>/`, gzip; small ones in git only if D2 = yes |
| X-alias | A tiny temporary dependent mod whose Defs use old XML class names (`Mugirl.CompProperties_MooMilkable`, `li Class="Mugirl.StockGenerator_Mugirl_Slaves"`), loaded alongside | n/a | generated per run |
| X-newer | An upgraded re-save with the stamp set to Current+1 | n/a | generated per run |
| X-settings | Copies of `Mod_mugirl_MugirlMod.xml` and `Mod_3578882219_MooGirlMod.xml`, placed into the temporary `Config/` | n/a | DevData |

### 4.2 Producing P fixtures: `tools/New-SaveFixture.ps1 -Build <zip|git:rev> [-Config AllDlc|Core]`

The procedure follows validator-tooling §4.3, with these changes and the AGENTS.md rules:
1. Refuse if RimWorld is running, or if the runner lock is held. Verify the build's sha256 against `builds.tsv`.
2. Extract the build into `<RimWorld>/Mods/__MugirlLegacy_<id>_<rand>/` with the marker `.mugirl-gamevalidation-tmp`:
   - from the ZIP, or with `git archive` for git builds;
   - only `MugirlRace.dll` or `MooGirlRace.dll` from `Assemblies` (never `NewbornBackstoryProbe.dll`);
   - **copies, never junctions**, because the game can write `Assets.zip` and `*.spv-cache` into load folders (`.gitignore:21-23`), and a junction would let it write into frozen `1.6/`;
   - `About.xml`: packageId becomes `mugirl.legacy.<id>`, and `supportedVersions` gains 1.7;
   - `LoadFolders.xml`: `v1.7` is set equal to the build's own `v1.6` list.
3. Build GameValidation with `-p:MugirlPlumbingOnly=true`.
4. Seed suite `Suites/Fixture/ContentSweepSuite.cs`. It uses only vanilla API and is build-agnostic: it selects defs by `modContentPack.PackageId == targetPackageId`. It runs a quicktest colony with Mugirl pawns (`Mugirl_Colony`), then:
   - spawns every item and building ThingDef (minified when needed);
   - equips every apparel def, including restraints (locks and timers);
   - adds every applicable HediffDef;
   - applies genes and the xenotype;
   - fires every mod IncidentDef and QuestScriptDef;
   - ticks 2 × 60000 with the AI running, so milk, corporate, migration and roping state accumulate;
   - saves while mod jobs are in flight.

   Each action records success. Failed patches or classes of an old build go into the manifest (F21 feasibility gate). Build-specific reflection seeds are optional extras.
5. Post-process a copy outside the game:
   - rewrite `meta/modIds` and `modNames` (`mugirl.legacy.<id>` → `har.mugirlrace` / `Mugirl Race`), keeping order, so the vanilla identical-environment check applies;
   - gzip;
   - write the manifest: source build, game version, mod list, DLC config, seed actions with results, facts, size, sha256.

   **Facts** are extracted from the **original** XML with independent legacy-name queries (never from the upgrader's output, which would be circular). Examples:
   - credits, turnover, orders, missions and records;
   - lock counts and restraint timers;
   - milk fullness;
   - weapon wheel slots;
   - roping links;
   - story flags;
   - Mugirl things per def;
   - current job driver per pawn.
6. In `finally`, remove only directories that carry the marker and contain no links (the `Remove-MarkedDirectory` rule, `Test-InGame.ps1:186-204`). Never touch the user's `Saves/` or `Config/`, `1.7/Assemblies` or the 1.6 folders. Keep the temporary save-data folder only with `-KeepWorkDir`.

### 4.3 The upgrade suite (Wave V)

**Suite and runner:** `Suites/Mod/UpgradeSuite.cs`, run as `Test-InGame -Suite upgrade -Fixture <id|all>`. The run is serialized and boots twice per fixture.

**Boot 1** (load the legacy fixture from the main menu, `entry:"load:<id>"`)
1. **Error policy.** Any ERROR during load fails when its text contains a token from `legacy-token-set.txt`, a Mugirl type or def, or "mugirl"/"moogirl" (case-insensitive).
   - Errors whose only tokens belong to mods that are not loaded are marked "foreign" and listed. That classification needs proof that the token is in no Mugirl corpus.
   - Vanilla noise needs a reviewed allowlist.
   - Explicitly fatal: "Could not find class", "SaveableFromNode exception", "Could not resolve cross-reference", "Cleaning up invalid job state", "Exception ticking".
2. **Stamp and diagnostics.** The stamp equals `SaveSchema.Current`. The diagnostics report one run, and its per-step action counts equal those of the **offline** `--save-corpus` run on the same file. That proves the runtime hooks equal the offline pipeline.
3. **DefInjected:** no injection whose file lies under the mod is left un-injected, and `loadErrors` is empty (F5a).
4. **Facts:** the manifest's facts equal the live state read through the new API, after mapping (floats within an epsilon).
5. **Re-save scan.** Re-save immediately. Scan the file for the tokens of `legacy-token-set.txt` in Mugirl-owned positions: Class values, def references and `Thing_<def><id>`, labels under Mugirl owners, enum values and saved strings. The result must be empty.
6. **Map-explained diff.** `SaveDiff(fixture, resave)` over Mugirl-owned items, with a port of hk `SaveDiff`. Every difference must be explained by a map entry or a step's declared consumption.
7. **Ticks.** 2500 or more (rare and long ticks): no mod errors, and the seeded pawns are alive.
8. **Settings.** For X-settings: the upgraded `Mod_mugirl_MugirlMod.xml` in the temporary `Config/` has the new class and keys, it is written once, and no "Could not find class" appears.
9. **XML alias.** For X-alias: the dependent defs resolve to the new types, with no "Could not find type" (§5.2(4) proof, F20).

**Boot 2** (fresh process, loads the re-save)
1. The upgrader stays inert: 0 actions, stamp unchanged, no ERROR.
2. Re-save again. `SaveDiff(resave1, resave2)` over Mugirl-owned items must be empty, except hk's vanilla resets.
3. X-newer: the save is untouched and exactly one warning is logged.

**Framework changes** (validator-tooling §4.5):
- `MugirlTarget` gets a configurable packageId, or detection by assembly name (`Core/MugirlTarget.cs:11`);
- the options gain fixtures, the entry mode and the token set;
- a per-suite mode in which any error fails;
- `SaveDiff` and `SaveFacts` readers (vanilla XML only, so plumbing builds can use them);
- `Test-InGame` gains `-Fixture` and boot sequencing.

### 4.4 AGENTS.md compliance

- Uses the Steam install as is: no copied launcher, UnityPlayer or game tree, and no game-resource junctions.
- Fixture production is a one-time, archived step, not a per-round copy.
- Configuration and saves are isolated in a temporary `-savedatafolder`.
- The user's saves and config are only read, never written.
- Cleanup removes only marked temporary directories, does not follow links, and checks absolute paths.
- Fixtures with personal content stay in DevData.
- No README or AGENTS file in any fixture folder. The runbook goes into `docs/validation-runbook.md` (Wave D).

---------------------------------------------------------------------------------------------------------------------
## 5. Amended wave order and file ownership

| Wave | Who | Owns (exclusive write) | Notes |
|---|---|---|---|
| 0 | 1 agent, serialized, owner-gated | baseline commit or archive, `global.json` SDK pin, archived baseline reports | F1, F17. Run corpus extraction (A2a) here, because its inputs are immutable and DevData is at risk (F18). Also build the calibration A′. |
| A1 | upgrader owner | `Source/MugirlRace/SaveUpgrade/**` (new), `Tests/MugirlRace.Tests/SaveUpgrade/**` | Exposes `SaveUpgradeBootstrap.Initialize(Mod)`. A4 adds the single call in `Core/MugirlMod.cs`. The hub `Core/GameComponent_Mugirl.cs` is a new file owned by A1. |
| A2 | validator owner | `Source/Tests/MugirlRace.Validation/**` (not `Controls/NegativeControl` Link lines), `Baselines/legacy/**`, `tools/Export-LegacySchema.ps1` (new) | Corpus, C.save-upgrade (ratchet), parity mode, `C.definjected-paths`, `C.content-shadowing`, `B.patch-order`, F7/F8 exemptions, `ModContract.cs`. |
| A3 | spec | read-only on the repo; new `Source/Tools/MugirlRace.Rename/**` | Spec plus linter plus tool. Tool tests on a temporary copy only. |
| A4 | build and bootstrap | `Source/Directory.*.props`, `*.csproj`, `*.slnx`, `global.json`, `Core/HarmonyBootstrap.cs`, `Core/MugirlPatchAttribute.cs`, `Core/MugirlMod.cs`, the `[HarmonyPriority]` lines of patch classes, `tools/Validate.ps1` (`-WorkRoot`, `OutputPath`), `tools/SmokeTest.ps1` | Patch priorities that reproduce today's order (F6). Nobody else touches feature files in Wave A. |
| A5 (new) | fixtures and in-game | `Tests/MugirlRace.GameValidation/**`, `tools/Test-InGame.ps1` (`-Fixture`, lock, boots), `tools/New-SaveFixture.ps1` (new) | Produces P fixtures early (needs only old builds). In-game runs are serialized by the lock. A4's `OutputPath` change to Test-InGame is applied by A5. |
| A-int | integrator | merge, gate, baseline regeneration and review | Tags `refactor/pre-R` (the F3 baseline). |
| R | 1 agent; the tree is frozen for everyone else | everything in the applied log | Gate §2.5. All cross-module moves happen here. |
| S | 1 agent per module | `Features/<Module>/**`, `SaveUpgrade/Steps/<Module>/**`, `SaveUpgrade/PostLoad/<Module>/**`, `SaveUpgrade/LegacyNames/Modules/<Module>.cs` (partial-class contribution; no central list edits), `Tests/.../<Module>/**`, module-only XML and language files (list in the spec) | Shared files have one owner each: `Core/**`, `Rendering/**`, `Compatibility/**`, `1.7/Defs/JobDefs/JobDefs.xml`, `Keyed/Misc_Gameplay.xml`, shared DefInjected files, and `SaveUpgrade/{Document,Hooks}/**` plus the central tables (upgrader owner). Validator baselines belong to the integrator. Each module works in its own worktree with its own `-WorkRoot`, and ratchets C.save-upgrade (F12). |
| V | integrator plus the single in-game runner | — | C.save-upgrade with no ratchet; offline `--save-corpus` over R-*, E-* and P-*; SmokeTest; default suites; the upgrade suite (§4.3). |
| D | docs | README, ADRs, runbook, records | Supersede `README.md:63` and ADR-005. Retire 1.6-only `docs/tools` references. |

**Owner decisions this critique adds** to D1–D5 of validator-tooling:
- **D6**: commit the baseline (F1).
- **D7**: field renames on public XML classes, given that `[LoadAlias]` would conflict with directive 2 (F5b).
- **D8**: migrate the `*_MooGirlMod.xml` settings files (F11).
- **D9**: accept the order changes listed in `parity-accept.tsv` (F7).
