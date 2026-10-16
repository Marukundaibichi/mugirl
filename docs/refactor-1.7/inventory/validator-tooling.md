# Validator, tests and tooling: how the "frozen save names" rule is enforced today, and how to switch to the save-upgrade model

Scope: `Source/Tests/MugirlRace.Validation`, `Source/Tests/MugirlRace.Tests`, `Source/Tests/MugirlRace.GameValidation`, `tools/*.ps1`, plus the sibling mod `hk` (Black Science) cross-version tooling.
Method: everything below was read in the files cited (paths are relative to the mod root `Mods/mugirl` unless prefixed `hk/` or `docs17/` = `Mods/1.7迁移文档`). Facts about git history and DLL metadata were computed read-only (in memory, `git show` piped into `System.Reflection.Metadata`; nothing was written to the repo). Vanilla facts were checked with DecompilerServer on `rw17-game` (RimWorld 1.7 Assembly-CSharp).
Note on numbers taken from a validator report: the newest normal (non-self-test) report on disk is `%TEMP%\mg17-validation-dev\report-dev.json` (2026-10-14 15:07). It is a historical record, not proof that the current tree passes.

---

## 0. Key findings (short)

1. Seven validator checks and nine baseline files encode the old "keep every name the 1.6 build ever wrote" rule (section 1). All of them compare against exactly one artefact: the final frozen 1.6 DLL and the final 1.6 content folders.
2. That single reference is not the real legacy name space. The git history holds 31 earlier `1.6/Assemblies/MugirlRace.dll` builds (0.4.7 to 0.6.7) and 25 commits of an older `1.6/Assemblies/MooGirlRace.dll` (assembly `MooGirlRace`, namespace `MooGirl`, defNames `MooGirl_*`, releases 0.1.0 to 0.4.4, all published under the same workshop id 3578882219). About 277 defNames from committed 1.6-era trees are not in the current 1.7 content. No build since 0.4.7 has any compatibility code for the MooGirl era. The owner must decide whether "any 1.6-era release" includes ≤0.4.4 (section 2, decision D1).
3. The pre-refactor 1.7 build exists only as an untracked file (`1.7/` and `Source/` are untracked, see `git status`). The next build overwrites it. Snapshot it now, together with the 1.7 content, and produce the pre-refactor fixtures from it now (section 3.2, 4.3).
4. Proposed replacement: one check, `C.save-upgrade`. It proves that every name in an immutable legacy schema corpus is either unchanged, mapped by the upgrader's declarative tables to a target that exists and is read by the right owner, consumed by a named upgrade step, or explicitly dropped with a reason. The corpus is extracted once from every legacy build with the existing Cecil and string-flow code. The map lives in C# tables inside the mod and the validator reads it by reflection (section 3).
5. In-game: `SaveLoadSuite` only round-trips a fresh quick-test colony and compares counts. Real 1.6-game saves (gameVersion `1.6.4871 rev591`) already sit in `DevData/Evidence`. The hk `-CrossVersion` technique (frozen DLL installed as a temporary mod with another packageId and a `v1.7` load list that points at `1.6`) can produce further fixtures. That technique fits AGENTS.md if it reuses Test-InGame's marker-based temporary folders (section 4).
6. Defects found: the solution file contradicts its own project comments and fails to build in the live folder. Missing frozen DLL means skipped (not failed) save checks. `C.forbidden-patterns` and the key-shaped-literal rule would fail on legacy names inside an upgrade map. In-game log attribution only matches the substring "mugirl", which misses "MooGirl" names. Plus several precision gaps (section 5).

---

## 1. Inventory: what encodes the frozen-names rule today

### 1.1 Validator checks

| Check id | Code | What it enforces | How |
|---|---|---|---|
| `C.save-types` | `Checks/SaveContractChecks.cs:37,42-119` | (a) every type in `Baselines/save-types.txt` exists in the 1.7 build with the same full name and the same game base type (first ancestor outside the mod); mod-internal intermediate base changes are only Info (:65-76). (b) every non-abstract, non-compiler-generated type of the frozen DLL whose ancestry reaches one of 23 hard-coded "save-visible" vanilla roots must still exist (or be listed in `dropped-save-types.txt`) and keep that root (:82-107). (c) Info for public 1.6 types missing in 1.7 (:110-116). | Pure Cecil metadata of both DLLs (`MetadataWorld`, `Metadata/MetadataWorld.cs:14-20`). Roots list `SaveVisibleRoots` at `:16-23`. Ancestry via `CecilNames.BaseTypeNames` (resolves through game assemblies; `Metadata/CecilNames.cs:151-169`) and `ModAncestry` (`:257-273`). |
| `C.save-labels` | `SaveContractChecks.cs:38,121-147,187-210` | Every Scribe label used by the frozen 1.6 build is still used somewhere in the 1.7 build, unless it is listed in `dropped-scribe-labels.txt`. New labels are Info (:136-139). Labels that are not literals are only counted (:141-144). | String-flow over IL: every call to `Verse.Scribe_*.Look` or `Verse.Scribe.EnterNode` (`IsScribeCall`, :233-239). The first `string` parameter (`StringCall.FirstStringArgument`, `Metadata/StringCall.cs:14-28`) is traced back through module-internal helper parameters by `ModuleStringFlow.Expand` (`Metadata/ModuleStringFlow.cs:45-74`, depth ≤ 6). The comparison is one global set per build (:123-133), not per owner type. |
| `C.save-enums` | `SaveContractChecks.cs:39,149-184,212-231` | For every enum of the frozen module that appears as a generic argument (or component of one) of a Scribe call, plus `save-enums.txt`: the enum exists in 1.7 under the same full name, and every 1.6 member name still exists. | Cecil scan of `GenericInstanceMethod` operands (:218-227). In the last dev report this covered 7 enums: ThrowState, DunkState, CorporateOrderSpeed/State, CorporateMissionKind/State, CorporateResearchChoice (`save-enums.txt:1-2`). |
| `C.save-defs` | `Checks/FrozenContentChecks.cs:23-24,29-76` | Every non-abstract `<defName>` of the 1.6 content still exists in the 1.7 content with the same "database family" (direct child of `Verse.Def`), unless listed in `dropped-defs.txt`. | XML only. The 1.6 load folders are resolved with `LoadFolderResolver.Resolve(mod, 1.6.9999, …)` (:18,22). The family is computed with `DatabaseRoot` (:139-152). |
| `C.frozen-keys` | `FrozenContentChecks.cs:25-26,78-120` | Every Keyed key of the 1.6 content (English and Simplified Chinese) still exists in 1.7, unless listed in `dropped-keys.txt`. | XML only. The 2026-10-14 dev report covered 1600 keys. |
| `B.scribe-defaults` | `Checks/SourceConventionChecks.cs:40,132-172` | Every `Scribe_Values.Look(ref field, …)` of a field listed in `scribe-defaults.txt` passes an explicit default ("the value an old save without this node should load as", :159-160). | Source-text regex (:171), because IL cannot tell an omitted optional argument from an explicit one (:14-15). |
| `B.member-rebinds` | `Checks/MemberRebindCheck.cs:21-61` | A Verse/RimWorld static method that the frozen build called and the 1.7 build no longer calls must not have been silently replaced by a same-named method on another type (for example `GenCollection.MaxBy` replaced by `Enumerable.MaxBy`), unless listed in `rebinds.allow.txt`. | Member-reference sets of both DLLs (`Metadata/ExternalMemberReferences.cs`). Not about saves, but it is another "equivalent to frozen 1.6" check. |

Related checks that mention 1.6 but are about shipping the frozen folder, not about save compatibility (keep them while 1.6 still ships):
- `A.about` (`Checks/ArtifactChecks.cs:70-119`): `packageId` must be `HAR.MugirlRace`. Saves store it in the mod list, and `BackCompatibility` compares packageIds (docs17/资料来源与核验记录.md:990-993). It also requires `supportedVersions` to list 1.6 and 1.7 (`Baselines/about.json:2-5`).
- `A.layout` (`ArtifactChecks.cs:136-178`): golden `load-folders.txt`, which includes the frozen v1.6 line (`load-folders.txt:3`). In the 1.6 view, extra DLLs only warn (:166-175, :209, :228-229).
- `C.language-files` (`Checks/LanguageFileChecks.cs:20,34,103-114`): shadowing in the frozen 1.6 view only warns. These warnings concern unmaintained content, so they are noise. Recommend dropping the 1.6 view.
- `C.forbidden-patterns` (`ContentContractChecks.cs:31,309-368`) bans legacy names and typos in the 1.7 content and in **all of `Source/MugirlRace`** (:331-337). This conflicts with an upgrade map, see defect F7.
- `C.translation-keys`, key-shaped literal rule (`Checks/LookupChecks.cs:22,142-172`): every `ldstr` that matches `^Mugirl(\.\w+){2,}$` must be a Keyed key unless it is a type name of the current module. Legacy namespaced type names inside a map would trip it (defect F8).

### 1.2 Baseline files

| File | Content today | Role under the old rule | Fate under the new regime |
|---|---|---|---|
| `Baselines/save-types.txt` | 11 types: 8 GameComponents/MapComponents, `Mugirl.MugirlMod` ("part of the settings file name"), `Mugirl.MugirlSettings` (`:4-14`) | Manual "must keep full name + game base" list | Delete. Replace with the derived corpus plus a small "frozen-forever" list (packageId, schema-stamp type/label, short name of the Mod class). Note: the settings file name uses `GetType().Name` (short name) and the folder name (`Verse.Mod.GetSettings` → `LoadedModManager.ReadModSettings` → `GetSettingsFilename`, verified on rw17-game), so the full-name check at `:12` is stricter than needed. |
| `Baselines/dropped-save-types.txt` | `Mugirl.Verb_MeleeAttack_SegmentedTailAssist` (dormant feature) (`:3`) | Exceptions to `C.save-types` | Becomes a `TypeDrop` entry in the upgrade map |
| `Baselines/dropped-scribe-labels.txt` | `currentTickCount`, `remainingCycles`, `joinedAlready`, `tickCounter` (`:2-5`) | Exceptions to `C.save-labels` | Becomes `LabelDrop` entries (owner-scoped: HediffComp_EleShock, HediffComp_JoinWhenRescued, Comp_MagneticShackles) |
| `Baselines/save-enums.txt` | No entries (auto-detected list in the comment, `:1-3`) | Extra saved enums | Delete. Saved enums come from the corpus extractor. |
| `Baselines/dropped-defs.txt` | Empty (`:1-2`) | Exceptions to `C.save-defs` | Becomes `DefDrop` entries |
| `Baselines/dropped-keys.txt` | Empty | Exceptions to `C.frozen-keys` | Delete with the check. Only Keyed keys that are stored in saves need a value map (see 3.4.5) |
| `Baselines/scribe-defaults.txt` | 6 fields, every reason is "old saves" (`:3-8`) | Explicit defaults for old saves | Generalise to an IL rule (default argument == field initializer) and delete the list (see 3.6) |
| `Baselines/rebinds.allow.txt` | Empty | `B.member-rebinds` | Retire the check after the refactor; turn the known hazard into a banned-API rule (see F10) |
| `Baselines/dynamic-lookups.allow.txt` | 4 entries justify saved names: DunkHead saved shader name and texture paths (`:7,12,23`); `CorporateHistoryPage::RecordTitle` "record.key is saved … the keys of 1.6 saves stay defined (C.frozen-keys)" (`:42`) | Rely on the frozen rule | Rewrite the reasons to point at the saved-value map (3.4.5) |
| `Baselines/about.json` | `packageId`, `supportedVersions [1.6,1.7]` (`:2-5`) | Shipping identity | Keep. The packageId is frozen forever by design. |
| `Baselines/forbidden-patterns.txt` | e.g. `Apperal` (`:3`), `MugirlSkinApplied` (`:13`), `MechanoidWorkControlSettings` (`:7`) | Anti-regression | Keep, but exempt the upgrade-map source (F7) |

### 1.3 Negative controls (self-test)

- `Controls/expected-failures.txt:41-46` (`B.scribe-defaults`, `C.save-types` ×2, `C.save-labels`, `C.save-enums`), `:25` (`B.member-rebinds`), `:78-80` (`C.save-defs` ×2, `C.frozen-keys`).
- Control baselines `Controls/Baselines/save-types.txt:2-3`, `scribe-defaults.txt:2-3`, and the empty `dropped-*`/`save-enums`/`rebinds` files.
- Control "1.6 content": `Controls/ModRoot/1.6/Defs/Control16Defs.xml:4-15` and `Controls/ModRoot/1.6/Languages/English/Keyed/Control16.xml`.
- Control code: `Controls/NegativeControl/EngineViolations.cs:35-50` (`StaticRebind`, `MapRopingIndex : GameComponent`) and `:81-94` (`ControlScribeHolder`, `lockCount` without a default).
- **Coupling:** the self-test uses the real frozen DLL as the control's "1.6 build": `tools/Validate.ps1:161` copies `1.6/Assemblies/MugirlRace.dll` into the control mod and `:176` passes `--frozen` with it. The `C.save-labels` control (`expected-failures.txt:45`, empty subject) passes only because the tiny control DLL lacks hundreds of real labels.

### 1.4 Unit tests (`MugirlRace.Tests`) that touch legacy saves

| Test | What it pins | Under the new regime |
|---|---|---|
| `Compatibility/CorporateValidationSaveMigrationTests.cs:9-41` | `CorporateValidationSaveMigration.RemoveLegacyComponents` removes `Mugirl.CorporateRuntimeValidation`/`CorporateVisualValidation` `<li>` from `game/components` | Becomes a test of an upgrader `TypeDrop` step. The XML shape is still needed: real archived saves contain these classes (4.2). |
| `Compatibility/NeuralShoulderSaveMigrationTests.cs:16-77` (1.6 XPath reference copy at `:84-139`, 3000 random saves) | Removal of the `Mugirl_NeuralCombatShoulders` def, its recipe, bills and references, byte-identical to 1.6 | Becomes the `DefDrop(..., policy: remove things + null refs)` step test. Keep the randomized differential idea and drop the 1.6 copy once the step is the reference. |
| `Milk/MilkProductionMathTests.cs:43-52` (`LegacyPendingTicks`) | Conversion of the old `_lastResourceUpdateTick` into pending ticks (used at `Features/Milk/Production/CompMooHasBodyResource.cs:59-62,76-79`) | Moves into a computed upgrade step. Domain code drops `legacyLastResourceUpdateTick`. |
| `Corporate/CorporateMoneyTests.cs:81-99` (`LegacySalesTarget`) | Missions without `salesGoal` (old saves) compute the target (`Features/Corporate/Missions/CorporateMission.cs:63`) | The upgrader fills `salesGoal` once. Delete the fallback and its test, or move it into the step test. |
| `Incidents/MugirlEventMarkersTests.cs:10-15` | questTags strings are "存档数据，字符串不能改" | A saved-value contract. Keep as "values written today", plus a value map if the tags ever change |
| Many `*Tests` that compare against "1.6 code" (`Lances/LanceChargeGridTraversalTests.cs:102`, `WeaponWheel/*`, `Rendering/Legacy16PawnEffects.cs`, `Restraints/ManualCooldownTests.cs:30`, …) | Behaviour parity with 1.6 algorithms | Not save compatibility. They are behaviour regressions and can stay, or be re-baselined when the refactor deliberately changes behaviour. |

### 1.5 In-game and tools

- `Suites/SaveLoadSuite.cs` saves and reloads in the same process (details in 4.1). It has no legacy coverage.
- `tools/New-ReleasePackage.ps1:107-113` and `Test-ReleasePackage.ps1:264-269` require the frozen 1.6 DLL hash (`tools/release-baseline.json:4-9`). This is about shipping 1.6, not save compatibility. Keep it.
- Documents that state the old rule and must be superseded by the owner: `README.md:63` ("XML 类名、Scribe 字段、根命名空间及补丁 XPath 不能随意改名"), `docs/architecture/ADR-005-version-layout.md:14,21`, and the checklist line `docs17/迁移与性能验证清单.md:47-48`.

### 1.6 Mod code the frozen checks currently protect (input for the upgrader inventory)

Only three places read the loader XML directly, and all three are compatibility code:
- `Compatibility/Savegames/NeuralShoulderSaveMigration.cs:7-20` and `CorporateValidationSaveMigration.cs:8-21` (Prefix on `Game.ExposeSmallComponents`, `LoadingVars`).
- `Features/Restraints/HediffComps/HediffCompScribe.cs:9-24`, which reads the 1.6 shared labels `age`/`triggered` when the new node is missing. Called from `HediffComp_DelayedTrigger.cs:44-45` and `HediffCompProperties_SuppressionEnhancer.cs:54`.

There are also read-only legacy labels in domain code, for example `CompMooHasBodyResource.cs:59-62` (`saveKey + "_lastResourceUpdateTick"` only in `LoadingVars`).

---

## 2. Measured facts about the legacy name space

| Source | Facts |
|---|---|
| Final frozen 1.6 DLL (`1.6/Assemblies/MugirlRace.dll`, sha256 `86A6C191…650DD`, `release-baseline.json:7`) | 56 save-visible types (11 listed + 45 derived; matches the dev report "11 listed save types and 45 save-visible"), 298 literal Scribe labels and 5 non-literal calls (dev report). The 5 non-literal calls are `1.6/Source/Features/Milk/CompMooHasBodyResource.cs:44-48`, `SaveKey + "_…"`. |
| 31 committed revisions of `1.6/Assemblies/MugirlRace.dll` (324501c 0.4.7, 2026-07-03 … 50f7979, 2026-10-03) | Union of save-visible types = the same 56 names. No type was lost during the MugirlRace era. Labels and enums across these revisions were **not** computed (that needs the string-flow extractor proposed in 3.2). |
| 25 commits of `1.6/Assemblies/MooGirlRace.dll` (d9b9bb9 2026-05-19 … cacbc58 0.4.4, 2026-06-29; deleted in 324501c) | Assembly `MooGirlRace`. 271 top-level types, 265 in namespace `MooGirl`, plus `MooGirlSkinFix`, `BondageToys`, `MooGirlMod`. GameComponents include `MooGirl.MooGirlStoryState`, `MooGirl.MooGirl_GameComp`, `MooGirl.MooGirl_ConvertComp`, `MooGirl.AdultContentCleanupGameComponent`, `MooGirlSkinFix.GameComponent_MooGirlSkinOnce`, `MooGirl.MooGirl_XenotypeFix_GameComp`. MapComponent `MooGirl.MapRopingIndex`. packageId was `HAR.MuGirlRace` (only the case differs). Workshop id is the same, 3578882219. `git grep MooGirl 324501c -- '*.cs'` is empty, so 0.4.7 shipped **no** compatibility for these saves. |
| defNames (grep over `<defName>` in 46 Def-touching commits; this approximation also counts commented-out defs) | 696 distinct historical names. 277 are absent from the current 1.7 content. 253 of those have a mechanical counterpart (`MooGirl→Mugirl`, `MuGirl→Mugirl`, `Apperal→Apparel`). Examples without one: `HumanlikeConstant`, `Mugirl_NeuralCombatShoulders`, `MooGirl_Hai`, `MooGirl_Settings`. |
| Current (pre-refactor) 1.7 DLL (`1.7/Assemblies/MugirlRace.dll`, 2026-10-14 16:10, untracked) | 55 save-visible types. The only difference from the final 1.6 DLL is the dropped `Mugirl.Verb_MeleeAttack_SegmentedTailAssist`. It adds new labels, for example `mugirlReduceWillAge` (`HediffCompProperties_ReduceWillorEnslave.cs:26`) and `milkFullness_pendingResourceTicks` (`CompMooHasBodyResource.cs:58`), so pre-refactor 1.7 saves have their own schema. |
| Real 1.6-game saves already archived | `DevData/Evidence/{CorporateValidation-*,CorporatePricing-20261001,HuntWheelFix-20261004,BugReportFixes-20261011}/…/Saves/*.rws` (3.4–16.8 MB). Meta `<gameVersion>1.6.4871 rev591</gameVersion>`, mod list includes `har.mugirlrace`. `CorporateValidation-AllDlc6` contains `Class="Mugirl.CorporateRuntimeValidation"` and `Mugirl.CorporateVisualValidation` (validation-only components), plus every save-type GameComponent. ADR-005:14 says no 1.6 game is installed any more, so these are irreplaceable. |

Consequence: today's checks guard only the "final 1.6" slice. The new corpus must merge (i) the MugirlRace-era 1.6 builds, (ii) optionally the MooGirl era (decision D1), (iii) the pre-refactor 1.7 build and content, and (iv) names that only ever appeared in dev/validation builds but are known to exist in real saves (the two corporate validation components).

---

## 3. Redesign: the validator proves the upgrade map is complete

### 3.1 Principles

- Domain code knows only the new schema. All knowledge of old names lives in one namespace (proposed `Mugirl.SaveUpgrade`, folder `Source/MugirlRace/SaveUpgrade/`).
- Only a few names are frozen forever: the packageId, the type name and labels of the schema-stamp component, and the short name of the `Mod` class (it is part of the settings file name). Everything else may change, provided the map covers it.
- The legacy name space is immutable data (old builds never change). Extract it once into committed baselines. Do not read it from DLLs at every run: CI may lack git history, and the pre-refactor DLL is about to disappear.
- Every legacy name ends in exactly one disposition: **Unchanged**, **Renamed** (declarative map entry), **Consumed** (a named, unit-tested upgrade step that declares which legacy names it reads), or **Dropped** (with a reason and a node policy). Map entries must be used (stale entries fail, as in `AllowList.ReportProblems`, `Infrastructure/AllowList.cs:147-158`), and targets must exist in the new build.

### 3.2 The legacy schema corpus

**Extractor:** a new validator mode, for example `--extract-legacy-schema --legacy <id>=<dll>[;<content-root>] …`. It writes golden files that are append-only (lines are never removed; each line carries the first and last legacy build id). Proposed files under `Baselines/legacy/`:

| File | Line format | Extracted with |
|---|---|---|
| `legacy-types.txt` | `fullName<TAB>saveRoot<TAB>firstBuild..lastBuild` | `CecilNames.AllTypes`, a `SaveRoot` derived automatically (3.5.4), `ModAncestry` |
| `legacy-labels.txt` | `ownerType<TAB>label<TAB>scribeKind[<TAB>elementType]<TAB>builds` | Per-owner string flow (3.5.1). `scribeKind` is Values/Defs/References/Deep/Collections/TargetInfo/EnterNode. For Deep/Collections the element type comes from the generic arguments (same scan as `SavedEnums`, `SaveContractChecks.cs:212-231`). |
| `legacy-enums.txt` | `enumType<TAB>member<TAB>builds` (plus owner/label where it is used) | generalised `SavedEnums` + `EnumMembers` (:241) |
| `legacy-defs.txt` | `DefRoot/defName<TAB>builds` | XML via `LoadFolderResolver` + `DatabaseType` (`FrozenContentChecks.cs:35-52,139-152`), run over content trees exported from git by the extractor's driver script (`git archive <rev> 1.6 Bio_1.6 …` into %TEMP%) |
| `legacy-saved-values.txt` | `contract<TAB>value<TAB>builds` | Literal values that reach known saved string fields (CorporateRecord `key` `Features/Corporate/Network/CorporateRecord.cs:16`, questTag constants, `protectedTargetTag` `QuestPart_SpawnRunawayFarm.cs:237`, DunkHead `headShaderName/headGraphicPath/headMaskPath` `Thing_MugirlDunkHead.cs:208-210`), found with `MethodStringFlow.Stores` (`FieldStore`) into those fields |

**Inputs (legacy builds):**
- 31 × `git show <rev>:1.6/Assemblies/MugirlRace.dll` (0.4.7–0.6.7, 50f7979) plus the working-tree final frozen DLL. Content: the matching `1.6/Defs`, `Bio_1.6`, `Odyssey_1.6`, `Versions/1.6` trees.
- Optional (D1): 24 readable × `MooGirlRace.dll` and their `1.6/Defs` trees.
- **Pre-refactor 1.7** DLL and PDB, plus `1.7/` content. They are untracked today: snapshot them first (commit/tag the current tree, or archive DLL+PDB+content with their sha256 in `DevData`), then extract.
- Extra hand-maintained names known from real saves but absent from any shipped build: `legacy-extra.txt` (for example `Mugirl.CorporateRuntimeValidation`, `Mugirl.CorporateVisualValidation`, with the evidence path).

`MetadataWorld.Read(path)` (`Metadata/MetadataWorld.cs:45-55`) already reads any DLL in memory with the Unity redirect resolver (`Metadata/GameCecilResolver.cs:24-35`). The same `ModuleStringFlow`/`StringFlowAnalysis` works on any module, `MooGirlRace` included (the self-assembly test uses the module's own name, `ModuleStringFlow.cs:20-24`).

### 3.3 Where the upgrade map lives

Recommended: **C# tables in the mod, read by the validator through reflection.**
```csharp
namespace Mugirl.SaveUpgrade;
internal static class UpgradeMap            // pure data: no Verse state, no DefDatabase, no Find
{
    public static IReadOnlyList<TypeMapping>  Types  { get; } = [ TypeMapping.Rename("Mugirl.CorporateNetwork", typeof(CorporateNetworkComponent)),
                                                                  TypeMapping.Drop("Mugirl.CorporateRuntimeValidation", "validation driver; never shipped", NodePolicy.RemoveNode) ];
    public static IReadOnlyList<DefMapping>   Defs   { get; } = [ DefMapping.Rename<ThingDef>("Mugirl_X", "Mugirl_Y"), DefMapping.Drop<ThingDef>("Mugirl_NeuralCombatShoulders", "...", RefPolicy.RemoveThingsNullRefs) ];
    public static IReadOnlyList<LabelMapping> Labels { get; } = [ LabelMapping.Rename(typeof(HediffComp_AddThought), legacyOwner: "Mugirl.HediffComp_AddThought", "age", "mugirlAddThoughtAge"), ... ];
    public static IReadOnlyList<EnumMapping>  Enums  { get; } = [ ... ];
    public static IReadOnlyList<ValueMapping> Values { get; } = [ ... ];        // saved strings (keys, tags, shader names)
    public static IReadOnlyList<IUpgradeStep> Steps  { get; } = [ new MilkPendingTicksStep() /* Consumes: (CompMooHasBodyResource, "milkFullness_lastResourceUpdateTick") */ ];
}
```
- The new side is compile-checked (`typeof`, `nameof`). The legacy side is strings, checked against the corpus by the validator.
- The validator already loads the mod assembly into its process in phase 2 (`Runtime/GameRuntime.cs`, `ValidationRun.cs:54`). Reading these static properties in phase 3 is safe if the static initialisers are pure. Enforce that with an architecture rule: `Verse.DefDatabase`1::*`, `Verse.Find::*`, `Verse.Current::*` are not allowed in `Mugirl.SaveUpgrade.UpgradeMap` (an `architecture-rules.json` entry with `allowedTypes` excluding it; the format is described at `Baselines/architecture-rules.json:2-7`).
- Alternative: an embedded resource (`upgrade-map.json` as `<EmbeddedResource>`). The mod reads it with `GetManifestResourceStream`, and the validator reads it in phase 1 through `ModuleDefinition.Resources` without executing code. You lose compile-time checks of targets. Use it only if executing mod statics in the validator is unacceptable.
- The corpus belongs to the validator (`Baselines/legacy/`). The map belongs to the mod. Do not ship the corpus.

### 3.4 Check `C.save-upgrade` (replaces C.save-types/labels/enums/defs and C.frozen-keys)

Runs in phase 3, after `C.content`, because it needs `ContentModel` for defNames and `runtime.Mod` for the map. Inputs: corpus, new-build Cecil module, `ModuleStringFlow` of the new build, `ContentModel`, and the map (reflection).

1. **Types.** For each legacy type `N` (root `R`):
   - Unchanged: the new build defines `N` with `SaveRoot == R`.
   - Renamed: the map gives `T`. `T` exists, is not abstract, has root `R` (assignable to the vanilla declared type), and has the constructor the game uses (`(Game)` for GameComponent, `(Map)` for MapComponent, `(World)` for WorldComponent). `T` is not itself a legacy-only name (no chains). If `N`'s root is `GameComponent`/`MapComponent`, the old-named class must not also exist (docs17/兼容迁移指南.md:801: `Game.FillComponents` instantiates every subclass).
   - Dropped: the node policy is declared.
   - Otherwise: fail.
   Also flag ldstr occurrences of a legacy-only type name outside `SaveUpgrade/` (legacy readers in domain code).
2. **Labels (owner-scoped).** For each legacy `(Owner, L, kind)`:
   - `Owner' = TypeMap(Owner) ?? Owner`.
   - `NewLabels(Owner')` = labels from Scribe calls reachable from `Owner'`'s expose methods, plus those of its mod base classes. Comps write into their parent's node, so for `ThingComp`/`HediffComp` owners the set stays per comp type, and the runtime resolves comps through the def.
   - Unchanged if `L ∈ NewLabels(Owner')`. Otherwise one of: Renamed `L→L'` with `L' ∈ NewLabels(Owner')`; Consumed by a step that declares `(Owner, L)`; Dropped with a reason.
   - Fan-out must be allowed: 1.6 hediff comps all wrote `age`/`triggered` into the same hediff node (`HediffCompScribe.cs:3-6`), so one legacy label maps to several new owners.
   - Fail if a label marked Renamed or Consumed is still used by domain code outside `SaveUpgrade/`. That is how leftovers like `HediffCompScribe.Look(…, "age")` or `CompMooHasBodyResource.cs:59-62` get caught.
3. **Enums.** Only member names are written; the enum type name is not. Scribe writes `value.ToString()`. So check `(enum via TypeMap or same name, member)`: the member exists, or `EnumMapping` maps it to an existing member of the target enum, or it is dropped with a fallback value. Renaming or moving an enum type then needs only a type-map entry.
4. **Defs.** For each legacy `Root/D`:
   - Unchanged: `D` exists in the new content in family `Root` (`DefIndex` + `FrozenContentChecks.DatabaseRoot`).
   - Renamed: `D'` exists in family `Root`.
   - Dropped: with a reference policy (remove things/bills, null refs; the semantics of today's `NeuralShoulderSaveMigration`).
   - Info: renamed or dropped defs affect third-party patches and translations (an owner decision, not a save issue).
5. **Saved values.** Each contract in `legacy-saved-values.txt` must be valid now (Keyed key exists, ShaderDef exists, texture exists) or be mapped. This replaces the `C.frozen-keys` dependency in `dynamic-lookups.allow.txt:42`.
6. **Frozen-forever.** The packageId (`about.json`), the stamp component type and its version label, and the short `Mod` class name: unchanged. A tiny explicit list.
7. **Map hygiene.**
   - Every map key exists in the corpus (otherwise stale, fail).
   - No duplicate keys.
   - No `A→B` where `B` is also a key (no chains or cycles).
   - Every `Drop`/`Step` has a reason or id.
   - Every `IUpgradeStep` is covered by a unit test (convention: `MugirlRace.Tests/SaveUpgrade/<StepId>Tests.cs`, checked by name).

### 3.5 Reusing the existing Cecil and string-flow infrastructure

1. **Per-owner labels.**
   - Keep `IsScribeCall` (`SaveContractChecks.cs:233-239`) and `Expand` (`ModuleStringFlow.cs:45-74`).
   - Record `(ownerType, label)`, where `ownerType = CecilNames.UserType(expanded.Method.DeclaringType)` for the method that supplied the literal.
   - For literals inside shared helpers, walk the reverse call graph to the expose roots: methods named `ExposeData`, `PostExposeData`, `CompExposeData`, and `IExposable.ExposeData` implementations. This needs the private `callers` map (`ModuleStringFlow.cs:13`) exposed read-only.
2. **Virtual constant labels.** `HediffComp_DelayedTrigger.cs:14-16,44-45` passes abstract `AgeScribeLabel`/`TriggeredScribeLabel` getters. Concrete owners return literals (for example `HediffCompProperties_AddTrait.cs:34,36`). Add a resolver: for owner `T`, if the label value is a `callvirt` of a getter, find `T`'s override and, if its body is `ldstr; ret`, use that literal. Today these are "dynamic" and invisible.
3. **Dynamic labels from fields.** `CompMooHasBodyResource.cs:54-64` builds labels from `SaveKey`, which comes from `CompProperties_MooMilkable.saveKey = "milkFullness"` (`CompProperties_MooMilkable.cs:10`). No XML in `1.7/` sets `saveKey` (grep: no matches). Recommend making the labels literal during the refactor. Otherwise the validator must evaluate `saveKey` from the content model.
4. **Automatic save-visible roots.** Replace the 23 hard-coded roots (`SaveContractChecks.cs:16-23`) with a derivation: every non-sealed type `T` used as the generic argument of `Scribe_Deep.Look<T>`, or of `Scribe_Collections.Look<T>` with `LookMode.Deep`, inside Assembly-CSharp (scan its IL with the same `GenericInstanceMethod` walk), plus `Verse.ModSettings`. Verified gaps in today's list: `Verse.AreaManager.ExposeData` saves `List<Area>` Deep, `Verse.PlayLog.ExposeData` saves `List<LogEntry>` Deep, and `RimWorld.Scenario.ExposeData` saves `List<ScenPart>` Deep (rw17-game). Mugirl has no such subclass today (grep), so the gap is latent.
5. **Owner discovery at runtime vs. validator.** The validator can also emit a "schema graph": owner → labels → child element types for Deep collections, for example `CorporateNetwork/orders/li → CorporateOrder`. The runtime upgrader can then locate nodes that carry no `Class=` attribute. A label rename inside such an IExposable item is keyed by the graph path. The validator checks that the path exists in both the legacy and the new graph.
6. **Content side.** Reuse `FrozenContentChecks` XML walking and `DatabaseRoot`, `LoadFolderResolver`, `DefIndex`, and `AllowList` (reasons, staleness, `REVIEW:` generation). Use `GoldenList.Compare` (`Infrastructure/GoldenList.cs:11-42`) for the corpus files, with an "append-only" variant: missing lines fail, new lines only with `--update-baselines`.

### 3.6 Supporting rules and baseline changes

- **Confinement (architecture rule):** members `Verse.ScribeLoader::curXmlParent`, `Verse.BackCompatibility::*`, `Verse.ScribeExtractor::*`, `Verse.ScribeMetaHeaderUtility::*` and Harmony targets on `Game.ExposeSmallComponents`/`ScribeLoader` are allowed only in `allowedSources: ["SaveUpgrade/"]`. Today exactly three files would violate it (1.6 above), and all three are migration code. This is cheap and expressible in the existing JSON rules (`ArchitectureCheck.cs`).
- **Exemptions:** `C.forbidden-patterns` and the `C.translation-keys` key-shaped literal rule must skip the map source and the `Mugirl.SaveUpgrade.*` types. Legacy defNames such as `UnlockSlaveApperal`, `GiveSlaveApperalGear`, `MooGirl_SlaveApperal*` hit `forbidden-patterns.txt:3`.
- **`B.scribe-defaults` → IL rule:** for every `Scribe_Values.Look` whose target field has a non-default constant initializer (a `stfld` with a constant in the constructors), the `defaultValue` argument constant must equal it. This catches the "new field loads as 0" class without a hand list and works on IL. Then delete `scribe-defaults.txt`.
- **Retire `B.member-rebinds`** after the refactor: a deliberate rewrite makes the 1.6 diff meaningless. Keep the specific hazard as an explicit banned-API entry (for example `Enumerable.MaxBy`/`MinBy` on reference types) in `BannedSymbols.txt` or an architecture rule.
- **Missing inputs must fail in release mode:** the new check reads committed baselines, so there is nothing to skip. If `--legacy` re-verification is requested and a DLL is missing, that is an environment error (exit 2), not a Skip.

### 3.7 Negative controls for the new check

Add `Controls/LegacyControl/` (a tiny "legacy" DLL project) and a control corpus or map that each violate one rule. Seed list:
- a legacy type that is neither present nor mapped;
- a map target of the wrong root;
- a GameComponent target without a `(Game)` constructor;
- a label renamed to a label the owner never reads;
- a legacy label still read by domain code after its rename;
- a def renamed to a missing def;
- an enum member mapped to a non-member;
- a stale map key;
- a chain `A→B→C`;
- a step without a test;
- a forbidden-pattern hit inside `SaveUpgrade/` (absent: positive control).

This removes the self-test's dependency on the real frozen DLL (`Validate.ps1:161,176`).

### 3.8 Baseline migration table

| Delete | Replace with |
|---|---|
| `save-types.txt`, `dropped-save-types.txt`, `save-enums.txt`, `dropped-scribe-labels.txt`, `dropped-defs.txt`, `dropped-keys.txt`, `scribe-defaults.txt`, `rebinds.allow.txt`, `Controls/ModRoot/1.6/**`, `Controls/Baselines/{save-*,dropped-*,scribe-defaults,rebinds}.txt` | `Baselines/legacy/*.txt` (corpus), `frozen-forever.txt` (3–4 lines), map in `Source/MugirlRace/SaveUpgrade/`, `Controls/LegacyControl/*` |
| `FrozenContentChecks.cs`, `SaveContractChecks.cs`, `MemberRebindCheck.cs`, `SourceConventionChecks.CheckScribeDefaults` | `SaveUpgradeCheck.cs` (3.4), `LegacySchemaExtractor.cs` (3.2), `ScribeDefaultsIlRule` (3.6) |
| `--frozen` option (`Infrastructure/ValidationOptions.cs:16,94,125`) | `--legacy`, used only by `--extract-legacy-schema` and optional re-verification |

---

## 4. In-game tests

### 4.1 What `SaveLoadSuite` does today

`Source/Tests/MugirlRace.GameValidation/Suites/SaveLoadSuite.cs`:
- **save** (`:28-56`): makes sure 3 Mugirl colonists exist (`GameFixtures.EnsureMugirlColonists`, `Core/GameFixtures.cs:50-73`, kind `Mugirl_Colony`). It snapshots each pawn as counts (`PawnSnapshot`, `:129-146`: id, name, def, kind, faction, position, age, number of hediffs/apparel/equipment/inventory/comps), records the mod's GameComponent/MapComponent type names and the tick, and calls `GameDataSaveLoader.SaveGame` (`Core/Wait.cs:101-129`, which deletes any older file first). It asserts the file exists and that no `Mugirl.GameValidation.` class leaked into it (`:52-55`).
- **load** (`:58-98`): `LoadWait` calls `GameDataSaveLoader.LoadGame` in the same process. A test-only Postfix on `Game.LoadGame` pauses the new game (`Wait.cs:137-185`). The step asserts the same tick, equal pawn snapshots, and equal component lists, then runs 300 ticks and checks the pawns are alive.
- **Log policy:** per step, warnings and errors whose text or stack contains "mugirl" fail (`Core/ValidationSession.cs:506-537`, `Core/LogCapture.cs:89-91`). Other errors are listed as "not attributed".
- **Gaps:**
  - It never loads a save written by another build.
  - It covers no mod feature state (corporate network, restraints, milk, wheel, roping, story state).
  - It compares counts, not content, and has no re-save diff.
  - It runs only in-process (static state survives).
  - Attribution misses names without "mugirl", such as `MooGirl_*`, `UnlockSlaveApperal`, `HumanlikeConstant`.
  - The suite only exists for the HAR.MugirlRace packageId (`Core/MugirlTarget.cs:11,32`).

### 4.2 Fixture sources and the BackCompatibility matrix

Vanilla skips all `BackCompatibility` converters when the save's game build and packageId list equal the running ones (`CheckSaveIdenticalToCurrentEnvironment`, docs17/资料来源与核验记录.md:990-993; 兼容迁移指南.md:799-800). Postfixes on `GetBackCompatibleType`/`BackCompatibleDefName` still run. The upgrader must therefore work in both situations, and the fixtures must cover both:

| Fixture | Produced how | gameVersion / mod list | Converter path | Notes |
|---|---|---|---|---|
| A. real 1.6 saves | already archived: `DevData/Evidence/**/Saves/*.rws` (3.4–16.8 MB each) | `1.6.4871 rev591`, `har.mugirlrace` | converters **run** | Real 1.6 vanilla data. Several were written by validation builds (contain `CorporateRuntimeValidation`). Copy (never move) the chosen files after owner approval. Pick at least one Core-only (`BugReportFixes…/Runtime-Core-EN-Complete`) and one AllDlc. |
| B. frozen 1.6 build on the 1.7 game | hk method (4.3) | 1.7 build. Mod list rewritten from `mugirl.frozen16` to `har.mugirlrace` | converters **skipped** | The worst case for type renames. Fully scripted and reproducible. Scenario coverage is under our control. |
| C. pre-refactor 1.7 build | Test-InGame with today's deployed DLL, **now**, before the refactor overwrites `1.7/Assemblies` | 1.7 build, `har.mugirlrace` | skipped | This is the "players updated only the mod" case |

Store each fixture as `Source/Tests/Fixtures/Saves/<id>.rws.gz` (or keep large ones in DevData and reference them by sha256). Add `<id>.json` with: source build sha256, game version, mod list/DLC config, scenario, expected upgrade actions per kind, and "facts" (4.4).

### 4.3 Producing fixtures with the frozen 1.6 DLL on the 1.7 game (adapted from hk)

What hk does (`hk/tools/SmokeTest.ps1`):
- `Install-FrozenBuild` (`:351-377`) copies About (packageId changed to `blackscience.frozen16`), Common, AssetBundles and `1.6/` minus `Source` into `<Mods>/__BlackScience16_<rand>`.
- Its `LoadFolders.xml` makes `v1.7` load `/`, `Common`, `1.6`.
- Runs use `-Headless -savedatafolder=<temp> -mod=<deps>;blackscience.frozen16;blackscience.smoketest` (`:461-477`, `:672-700`).
- The test mod has no compile-time reference to the mod under test and reads members by name through reflection (docs17/资料来源与核验记录.md:1153). It is passed the other build's DLL (`-BlackScienceOtherAssembly`, `:661-662`) so that label differences explained by the builds' string literals are excused (`hk/…/Exercises/CrossVersionAllowance.cs:22-77`).
- Cross-version log noise is filtered (`:203-213`).
- The frozen build's own errors are baselined by its own exercise (`:588-592`, `:499-523`).
- hk's driver is a `GameComponent` (`hk/…/Core/ExerciseGameComponent.cs:5`), so it is written into saves. Mugirl's session deliberately avoids that (`Core/ValidationSession.cs:12-15`), which is better for fixtures.
- hk's cleanup is a plain `Remove-Item -Recurse -Force` (`:313`, `:740`, `:748`). Mugirl's marker-and-link-aware removal (`tools/Test-InGame.ps1:186-204`) is the one to reuse.

Proposed `tools/New-SaveFixture.ps1` (or `Test-InGame.ps1 -ProduceFixture`), compliant with AGENTS.md (no copied game tree, temp mods and temp `-savedatafolder` only, clean only marked test data, never follow links):
1. Refuse if RimWorld is running (Test-InGame `:330-331`). Verify the frozen DLL sha256 against `release-baseline.json:7`.
2. Create `<Mods>/__MugirlRace16_<rand>/` with the marker `.mugirl-gamevalidation-tmp`. Copy into it:
   - `About/` (rewrite packageId to `mugirl.frozen16`, the name to "frozen 1.6 build (test only)");
   - `1.6/` without `Source`, and only `MugirlRace.dll` from `1.6/Assemblies`. Note the untracked `1.6/Assemblies/NewbornBackstoryProbe.dll` must not come along. Keep `1.6/Resources`, because the frozen DLL loads its AssetBundles from `ContentRoot/1.6/Resources` (`1.6/Source/Core/MugirlMod.cs:13`, `Features/Appearance/MugirlExtraOutline.cs:213`);
   - `Bio_1.6`, `Odyssey_1.6`, `Versions/1.6`, root `Textures` and `Sounds` (about 45 MB, copies not junctions);
   - a `LoadFolders.xml` whose `v1.7` list equals today's `v1.6` list (`LoadFolders.xml:7-15`).
3. Build GameValidation with `-p:MugirlPlumbingOnly=true` (no MugirlRace reference; `MugirlRace.GameValidation.csproj:44-46`; Test-InGame `:398-399`). The seed suite therefore lives in `Suites/` (vanilla API, defNames, reflection by name), not `Suites/Mod/`.
4. Write the options file (`Core/ValidationOptions.cs:43-107`) with new fields `targetPackageId: "mugirl.frozen16"`, `saveName`, `scenario`. Launch `RimWorld.exe -Headless -NoFileLog -NoCrashDialog -quicktest -mugirlvalidate -savedatafolder=%TEMP%\… -mod=brrainz.harmony;erdelf.HumanoidAlienRaces;mugirl.frozen16;MugirlRace.GameValidation`. Use `ModsConfig.xml` for DLC subsets, as Test-InGame does at `:483-511`.
5. The seed scenario creates colonists, restraint hediffs with timers and locks, milk state, the weapon wheel, a roping link, corporate network state (orders, records, missions), story flags, and an active job of a mod JobDriver. Run ticks, save, and extract facts from the written XML.
6. Post-process a copy outside the game: rewrite `meta/modIds` and `modNames` `mugirl.frozen16→har.mugirlrace` so the loading run sees an identical mod list. Gzip it and write the manifest.
7. Cleanup in `finally`: remove the marked temp mod and test-mod folders. Keep the temp save-data folder only with `-KeepWorkDir`. Never touch `Mods/mugirl/1.7/Assemblies`, the user's saves, or the user's config.

Unverified: whether Mugirl's frozen DLL boots and plays on 1.7.4977. hk's did (E50, docs17/资料来源与核验记录.md:1150-1165). Mugirl's DLL references HAR 1.6 and Unity modules, which `WaylithClr` redirects. Make a plumbing boot of the frozen copy (quicktest, save, quit) the first step.

### 4.4 New `upgrade` suite: assertions

The suite lives in `Suites/Mod/` (it reads upgrader diagnostics). The options file gets `fixtures:[{id,file,manifest}]` and an entry mode `entry:"load:<id>"`, which loads from the main menu as hk's `-BlackScienceLoad` does. Today the session only waits for a quicktest game (`ValidationSession.cs:34,190-210`). Test-InGame gets `-Fixture <id|all>`: it copies the fixture into `<savedata>/Saves/` and chooses `-Config` from the manifest.

(a) **Load a legacy fixture and assert a full upgrade:**
1. During the load step, **any** ERROR fails, not only mod-mentioning ones. The exceptions are a reviewed vanilla allowlist (hk's `loadNoise`, the version-mismatch line). Name the dangerous ones explicitly: "Could not find class", "SaveableFromNode exception", "Could not load reference", "Could not resolve cross-reference", "Some verbs were null", "Cleaning up invalid job state", "Exception ticking" (examples from docs17/资料来源与核验记录.md:1141-1145, 兼容迁移指南.md:793-796).
2. The stamp component exists and its version equals `SaveSchema.Current`. Upgrader diagnostics (for example `SaveUpgrader.LastRun`) report "ran once" and per-kind action counts equal to the manifest's expectation.
3. **No legacy names left:** re-save immediately (`SaveWait`). Scan the re-save for every legacy-only name in the corpus: `Class=` values, def references (`<def>`, `<li>`, `Thing_<def><id>`, `Bill_<recipe>_<id>`), owner-scoped labels under mod-owned nodes, enum members, and saved values. The list must be empty.
4. **Key state preserved:** the manifest holds facts extracted from the fixture XML using legacy names. Compare them with live state read through the new API after mapping. Examples:
   - mod things by mapped def;
   - per-pawn mod hediffs and their timers or triggered flags;
   - apparel lock counts;
   - milk fullness (within an epsilon);
   - corporate credits, turnover, orders, records, missions;
   - story flags;
   - weapon wheel slots;
   - roping links;
   - current job class.
5. **Map-explained diff:** `SaveDiff(fixture, resave)` over mod-owned items, adapted from hk's `Core/SaveDiff.cs` (it compares items by id, `:73`; documented vanilla recomputations `:27,36`). Every difference must be explained by a map entry or by a step's declared consumption. This replaces hk's literal-based `CrossVersionAllowance` and proves nothing was silently lost.
6. Run 2500+ ticks (rare and long ticks): no mod errors, pawns alive.

(b) **Round trip with the new build:**
1. Load the re-save from (a) **in a fresh process** (a second boot, as hk does for `load16-with17`). Also load it in-process for speed.
2. Assert the upgrader did not run (diagnostics: 0 actions; stamp unchanged) and that no ERROR appears.
3. Re-save again and require `SaveDiff(resave1, resave2)` over mod-owned items to be empty, with hk's vanilla-reset exclusions only.
4. Unit level (MugirlRace.Tests): applying the upgrader to an already-upgraded document changes nothing (idempotence), and every step has golden before/after XML taken from real fixtures.

### 4.5 Framework changes needed

- `MugirlTarget.Detect` must accept a configured packageId (`Core/MugirlTarget.cs:11,32`). Better, follow hk and detect by assembly name, failing if several mods load it (`hk/…/Core/BlackScienceBuild.cs:36-54`).
- `ValidationOptions` needs fixtures, entry mode, targetPackageId and expected-log allowlists.
- The log policy needs a per-suite "any error fails" mode (`ValidationSession.cs:523-530`).
- Port `SaveDiff` and a `SaveFacts` reader (vanilla XML only, no mod types, so plumbing builds can use them).
- Test-InGame needs `-Fixture`, `-ProduceFixture`, and multi-boot sequencing (load, then reload in a fresh process).

---

## 5. Defects and production-quality issues

| # | Severity | Where | Issue | Fix |
|---|---|---|---|---|
| F1 | High (DX/CI) | `Source/MugirlRace.slnx:6,9`; `Tests/MugirlRace.Tests/MugirlRace.Tests.csproj:8`; `Tests/MugirlRace.GameValidation/MugirlRace.GameValidation.csproj:4,63-66`; `MugirlRace/MugirlRace.csproj:12`; `tools/Validate.ps1:8-9` | Both test projects say "not in MugirlRace.slnx" but the slnx contains them. `dotnet build Source/MugirlRace.slnx` fails by design in the live folder (GuardLiveModFolder), and the same build still writes `MugirlRace.dll` into the live `1.7/Assemblies` through the main project's `OutputPath`. The guard protects one path while the solution violates the same policy through another. A solution that cannot build is a poor default: IDE "Build Solution", `dotnet test` and CI all fail. | "Build never deploys": default `OutputPath` of MugirlRace to an artifacts folder (or `UseArtifactsOutput`), plus an explicit deploy target (`-t:DeployToMod`) used by Test-InGame `-Deploy`. Then GameValidation's ProjectReference is harmless, the guard can go, and the slnx builds anywhere. Alternatively remove GameValidation from the slnx, as its comment claims. Fix the two stale comments either way. |
| F2 | High | `SaveContractChecks.cs:27-35`, `MemberRebindCheck.cs:26-30`, `Reporting/Report.cs:22,78-80` | A missing frozen DLL turns the save checks into Skipped, and Skipped does not fail the run, so a release gate can pass without any save verification. | Baselined corpus (no input to miss). In release mode, treat Skipped save checks as failures. |
| F3 | High (coverage) | `ValidationOptions.cs:125`, `FrozenContentChecks.cs:18-22` | The reference is only the final 1.6 build and folders. Older releases (31 + 25 revisions, MooGirl era) and the pre-refactor 1.7 schema are invisible. | Corpus over all legacy builds (3.2). |
| F4 | Medium | `SaveContractChecks.cs:123-139` | Labels are compared as one global set. A label moved between owners, or a rename that collides with another owner's label, passes. `EnterNode` nesting is flattened. | Per-owner, path-aware labels (3.5.1, 3.5.5). |
| F5 | Medium | `SaveContractChecks.cs:141-144`; `CompMooHasBodyResource.cs:54-64`; `HediffComp_DelayedTrigger.cs:14-16,44-45` | Non-literal labels are not compared. Today these are 6 `saveKey`-based labels (`saveKey` is never set in XML) and 2 abstract-getter labels. | Literal labels plus a virtual-getter resolver (3.5.2-3). |
| F6 | Low | `SaveContractChecks.cs:170-173` | Enum *type* renames fail although saves only contain member names. | Check member names through the type map (3.4.3). |
| F7 | Medium (blocks the redesign) | `ContentContractChecks.cs:331-337`, `forbidden-patterns.txt:3` | The forbidden patterns scan every `.cs` in `Source/MugirlRace`. Legacy names in an upgrade map (`…Apperal…`) would fail. | Exempt `SaveUpgrade/`. |
| F8 | Medium | `LookupChecks.cs:22,142-172` | Key-shaped literals (`Mugirl.A.B`) that are not current type names must be Keyed keys. Legacy namespaced type names in the map would fail. | Exempt `Mugirl.SaveUpgrade.*`. |
| F9 | Low (latent) | `SaveContractChecks.cs:16-23` | Hard-coded save-visible roots miss `Verse.Area`, `Verse.LogEntry`, `RimWorld.ScenPart` (all saved Deep, verified in rw17-game `AreaManager/PlayLog/Scenario.ExposeData`). | Derive roots from Assembly-CSharp (3.5.4). |
| F10 | Low | `MemberRebindCheck.cs` | After a deliberate rewrite, the diff against 1.6 is noise. | Retire it. Ban `Enumerable.MaxBy/MinBy` on reference types explicitly. |
| F11 | Low | `SourceConventionChecks.cs:132-171`, `scribe-defaults.txt` | A hand list kept for old saves, and a source regex that misses multi-line calls. | IL rule: default argument == initializer (3.6). |
| F12 | Medium (in-game) | `Core/LogCapture.cs:89-91`, `ValidationSession.cs:523-530` | Attribution is the substring "mugirl"; load errors about `MooGirl_*`, `…Apperal…` or `HumanlikeConstant` would only be "not attributed" info. | The upgrade suite fails on any load ERROR outside an allowlist. |
| F13 | Medium (in-game) | `Core/MugirlTarget.cs:11,32`, `ValidationSession.cs:34,190-210` | Hard-coded packageId; quicktest-only entry. Cannot drive frozen or pre-refactor temp copies or main-menu loads. | 4.5. |
| F14 | Low | `SaveLoadSuite.cs:129-146` | Count-only snapshots; no content diff; Mugirl colonists only. | 4.4. |
| F15 | Low (hygiene) | `1.6/Assemblies/NewbornBackstoryProbe.dll` (untracked) | A probe DLL sits inside the frozen 1.6 load folder. A 1.6 game would load it from the dev folder. `A.layout` only warns (`ArtifactChecks.cs:209,228-229`) and the release skips it (`release-baseline.json:11`). A naive frozen16 copy would carry it. | The owner removes or archives it. Fixture tooling copies only `MugirlRace.dll`. |
| F16 | Low (hygiene) | `Source/MugirlRace/obj/{Debug,Release}`, `Tests/MugirlRace.{Validation,GameValidation}/{bin,obj}` exist | In-tree build outputs contradict Validate.ps1's isolation claim (`:6-9`). They are harmless for release (`Source` is excluded) but show F1 in practice. | F1. |
| F17 | Low | `Source/global.json:1-5` | No SDK pin (only the test runner). | Pin `sdk.version` with `rollForward: latestFeature`. |
| F18 | Info | `dynamic-lookups.allow.txt:42` | The reason depends on `C.frozen-keys`. | Point it at the saved-value contract. |
| F19 | Info | `README.md:63`, `docs/architecture/ADR-005-version-layout.md:14,21` | The documents encode the old rule (bidirectional 1.6↔1.7 saves). | The owner writes a superseding ADR (the save-upgrade layer), linked from the root README. |

---

## 6. Decisions the owner must make

- **D1** – Scope of "any 1.6-era release". The MugirlRace era (0.4.7 to final, 2026-07-03 →) is clearly in scope. The MooGirl era (0.1.0 to 0.4.4, 2026-05-19 → 06-29) was already unsupported by every build since 0.4.7. Including it means about 260 def renames (253 mechanical) plus a `MooGirl.* → Mugirl.*` type map and their labels.
- **D2** – Fixtures in git (gzip, roughly 0.3–2 MB each) or in DevData referenced by sha256.
- **D3** – Upgrade map as C# tables read by reflection (recommended) or as an embedded JSON resource.
- **D4** – Downgrade: once renamed, saves are no longer readable by the frozen 1.6 build. docs17 E49 only described 1.6↔1.7 bidirectionality as a goal. State the one-way choice in the release notes.
- **D5** – Third-party impact of def renames (patches, translations, `MayRequire` users). This is not a save issue, but the validator can list public defName changes as Info.

## Appendix: read-only commands used

- `git log --format=%h -- 1.6/Assemblies/MugirlRace.dll` (31 revisions), `-- 1.6/Assemblies/MooGirlRace.dll` (25), `-- 1.6/Defs Bio_1.6 Odyssey_1.6 Versions Defs` (46). The `git show <rev>:<dll>` bytes were piped into `System.Reflection.Metadata` in memory.
- `git grep -h -o '<defName>…' <rev>` unions compared with `grep` over `1.6/…` and `1.7/` (approximate: commented-out defs counted).
- `git show <rev>:About/About.xml` for the packageId history (`HAR.MuGirlRace` → `HAR.MugirlRace` at 324501c).
- DecompilerServer (rw17-game): `Verse.AreaManager.ExposeData`, `RimWorld.Scenario.ExposeData`, `Verse.PlayLog.ExposeData`, `Verse.LoadedModManager.GetSettingsFilename/ReadModSettings`, `Verse.Mod.GetSettings`.
- Head of archived saves read for `<gameVersion>`/mod list; `Class="Mugirl.*"` counts from `DevData/Evidence/CorporateValidation-AllDlc6/Saves/CorporateRoundtrip.rws`.
