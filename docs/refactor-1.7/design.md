# Mugirl 1.7 — no-legacy refactor + unified save upgrader: DESIGN (v1, 2026-10-15)

Status: working design for the orchestrated refactor. Inputs: the inventory reports in `understand/` next to this file
(module-*.md, content.md, engine-hooks.md, real-saves.md, validator-tooling.md). Where this file and a report disagree,
THIS FILE WINS; where this file is silent, follow the report and say so.

Repo root R = `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\mugirl`. Source = `R\Source\MugirlRace`.

---------------------------------------------------------------------------------------------------------------------
## 0. Owner directives (non-negotiable)

1. 1.7 is the only maintained version. `1.6/`, `Bio_1.6/`, `Odyssey_1.6/`, `Versions/1.6/` and the `v1.6` list of
   `LoadFolders.xml` stay frozen and are never edited.
2. Domain code carries NO backwards compatibility: no fallback Scribe labels, no LoadingVars conversions of old shapes,
   no stub/retired types or defs kept for saves, no "kept for old saves" comments, no names frozen because saves hold them.
   Rename freely where it makes the architecture cleaner.
3. ONE unified save-upgrade layer (`Source/MugirlRace/SaveUpgrade`, namespace `Mugirl.SaveUpgrade`) upgrades any older
   save once on load, stamps it, and play continues normally. All knowledge of old names/shapes lives ONLY there.
4. Production quality: anyone reading the result should find nothing to fault. Behaviour stays the same unless a change
   fixes a verified defect (record each such fix).
5. Valuable migration findings go into `..\1.7迁移文档` (done in the docs wave, not by refactor agents).

---------------------------------------------------------------------------------------------------------------------
## 1. Scope of legacy saves the upgrader supports

| Era | Builds | What the upgrader does |
|---|---|---|
| MugirlRace era | every published release 0.4.7 … 0.6.7 (git 324501c … 50f7979) + the frozen 1.6 working-tree build (`1.6/Assemblies/MugirlRace.dll`, sha256 86a6c191…) | FULL support: names, labels, structures, semantic post-load steps |
| MooGirl era | 0.1.0 … 0.4.4/0.4.6 (namespace `MooGirl`, defNames `MooGirl_*`, packageId `HAR.MuGirlRace`) | NAME-LEVEL support: explicit defName map (generated from git, no substring rules), MooGirl GameComponents/MapComponents dropped or merged into their successors where a successor label exists. No deeper semantics (real MooGirl-era saves contain no Mugirl race pawns). |
| Pre-refactor 1.7 tree | never released | NOT a source. (Snapshot kept in `DevData/Backups/PreRefactor-1.7-20261015` for parity tests.) |

Schema numbering: a save without the stamp but with `har.mugirlrace` (optionally `_steam`) in `meta/modIds` is schema 0
("legacy": the union of all eras above; steps detect their own applicability from XML shape). The first new schema is 1.
`SaveSchema.Current` is a single int constant. Future schema changes add steps `N → N+1`.
A stamp greater than Current = save from a newer Mugirl: do not touch, log one warning.

---------------------------------------------------------------------------------------------------------------------
## 2. Public API surface (frozen forever; documented in a new ADR)

Other mods reference these; they are NOT renamed (verified dependents in content.md §C and real-saves.md §9):
- packageId `HAR.MugirlRace`; Harmony id `MugirlMod.Mod`; the short name `MugirlMod` of the Mod subclass (settings file name).
- Race `Mugirl` (ThingDef_AlienRace), `MugirlBody` (+ its body-part order: part indices are saved), `MugirlLike` ThinkTreeDef,
  `Mugirl_Colony`, resource/item defs already prefixed `Mugirl_` that dependents use (`Mugirl_Milk`, `Mugirl_Wool`,
  `Mugirl_Hair`, `Mugirl_Horn`, `Mugirl_Tail`, `Mugirl_OnSkin`, `Mugirl_SlaveApparelKey_*`, `Mugirl_Shout`, `Mugirl_NobelPrize`,
  `Mugirl_Charge` (Ability/Gene), `Mugirl_Body_bondage` apparel layer), every abstract `Name` used as ParentName by dependents
  (content.md B.1 list), texture paths, `Job_Charge` (JobDef used by Kindseal.UFPC as `<jobDef>Job_Charge</jobDef>`).
- XML-visible classes used by dependents: `Mugirl.StockGenerator_Mugirl_Slaves` (KindSeal.LOL), `Mugirl.CompProperties_MugirlMount`
  (MRK.WhiteFluff), `Mugirl.CompProperties_MooMilkable` (subspecies). These MAY be renamed: their old names resolve through the
  upgrader's type-alias table (one table serves saves AND XML type resolution, see §5.6). Everything else in this section is not renamed.
- HAR body-addon order of the race def (addonVariants indices are saved) and `bodyAddons` list order.

---------------------------------------------------------------------------------------------------------------------
## 3. Conventions (apply everywhere; the validator enforces what it can)

### 3.1 Namespaces and files
- Namespace = `Mugirl.<Area>[.<Feature>]`, flat per feature: subfolders organise files but do NOT extend the namespace
  (existing convention of WeaponWheel and Lances). Areas: `Mugirl.Core`, `Mugirl.Platform.*` (unchanged), `Mugirl.Mathematics`,
  `Mugirl.Rendering` (VFX/pawn effects), `Mugirl.Compatibility.<OtherMod>`, `Mugirl.SaveUpgrade`, `Mugirl.Features.<Feature>`.
- Nothing in the root namespace `Mugirl` (zero types). ADR-004 is superseded.
- Forbidden namespace segments (shadowing): `UI`, `Text`, `Log`, `Find`, `Gen`, `Widgets`, `Scribe`, `AlienRace`, `HarmonyLib`,
  and any vanilla "ignored namespace" (`RimWorld`, `Verse`, `LudeonTK`, `System`, …). HAR compatibility lives in
  `Mugirl.Compatibility.HumanoidAlienRaces`.
- One top-level type per file; file name = type name; partial parts `Type.Part.cs`. Small private nested types may stay nested.
- Feature map (target folders under `Source/MugirlRace/Features/`):
  `Corporate`, `Incidents` (story, events, quests, courier, punisher, migration, runaway farm, wild Mugirl), `Milk`
  (incl. milk recipe from Misc), `Abilities` (was Genes/{Charge,Dunk,Shout,Throw,Shared}), `Xenotype` (was Genes/Xenotype),
  `Appearance` (incl. hair, body accessory, outline), `Apparel` (incl. FixedColorApparel, pregnant variants), `AdvancedArmor`,
  `Restraints`, `Roping`, `Mounting`, `Lances`, `WeaponWheel`, `Weapons`, `Grazing` (from Misc), `Social` (Nuzzle, thoughts from
  Misc), `LifeStages` (was Newborn), `Trade` (StockGenerator from Incidents). `Misc` disappears. `Features/Incidents/Raids`
  (corporate raid point tiers) moves to Corporate; `Incidents/VanillaFixes` moves to `Core/VanillaFixes`;
  `RenderRefresh` moves to `Rendering`. Shared player-faction / ideo / generated-pawn helpers move to `Core`.
- DefOf: one `internal` (or `public` only if XML/reflection needs it) `[DefOf]` class per feature named `<Feature>DefOf`,
  with `static <Feature>DefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(<Feature>DefOf));`, `[MayRequire]` for DLC,
  no unused fields (existence checks belong in the validator's content contracts). `Mugirl_DefOf`, `MugirlContentDefOf`,
  `MugirlMilkDefOf` dissolve into feature DefOfs; the race-core defs (`Mugirl`, `MugirlBody`, core backstories/pawnkinds)
  go to `Mugirl.Core.MugirlDefOf`.

### 3.2 Type names
- Vanilla role shapes: `CompX` + `CompProperties_X` (ThingComp); `HediffComp_X` + `HediffCompProperties_X`; `CompAbilityEffect_X`,
  `CompUseEffect_X`, `CompTargetEffect_X`, `CompTargetable_X`, `CompUsable_X`; `JobDriver_X`, `JobGiver_X`, `WorkGiver_X`,
  `ThinkNode_X`, `ThoughtWorker_X`, `Thought_X`, `IncidentWorker_X`, `QuestNode_X`/`QuestNode_Root_X`, `QuestPart_X`,
  `LordJob_X`, `LordToil_X`, `PawnFlyer_X`, `PawnRenderNode*_X`, `StockGenerator_X`, `DamageWorker_X`, `Dialog_X`,
  `FloatMenuOptionProvider_X`, `GameComponent_X`, `MapComponent_X`, `XUtility` for static helpers, `<X>Extension` for DefModExtension.
- No `Mugirl`/`Mugirl_` in type names inside `Mugirl.*` namespaces UNLESS (a) it carries meaning ("only for Mugirls",
  e.g. `Thought_MugirlOnly`, `JobDriver_MountMugirl`) or (b) the short name would equal a vanilla type of the same role
  (`JobDriver_MugirlNuzzle`, `JobGiver_MugirlFollowRoper`, `QuestNode_Root_MugirlRefugeePodCrash`). Exceptions that collide with
  Verse statics: `MugirlLog`, `MugirlText`, `MugirlMod`, `MugirlSettings`.
- No "Moo" anywhere (MooGirl-era leftover). Terminology: Restraint (not SlaveApparel/BondageGear/SlaveApparelGear) in code;
  Mission (not Quest/Contract) for corporate missions except vanilla-derived QuestPart_/QuestNode_ types.
- Harmony patch classes: `internal static class Harmony_<TargetType>_<TargetMember>[_<Purpose>]`, carrying `[MugirlPatch]`
  metadata whose module is a member of a closed set (enum or constants), never a free string. Patch order between
  patches on the same target is expressed only with `[HarmonyPriority]`/`[HarmonyBefore]`/`[HarmonyAfter]`, never by class-name sort.
- Visibility: `internal` by default; `public` only for types the engine instantiates by reflection from XML or saves
  (comps, props, workers, drivers, GameComponents, DefModExtensions, Def subclasses, Mod/ModSettings) or that a
  documented public API needs. `sealed` on leaves.

### 3.3 Def names
- `Mugirl_<PascalName>` for every Def the mod defines, EXCEPT: the public-API names of §2; Facial Animation defs that follow the
  FA ecosystem convention (`<faName>_Mugirl`, shared shape names); vanilla-implied derived names (`Blueprint_*`, `Frame_*`,
  `Corpse_*`, `Meat_*`, `Make_*`).
- Rename a defName only when it is wrong or inconsistent: missing/other prefix (`Job_*`, `JobDriver_*`, unprefixed, `AI_GC_*`,
  `Hediff_Mugirl*`, `Work_*`, `PMC_*`/`OPC_*` when not public), typos (`Mugirl_Bar`→`Mugirl_Bra`), "Moo", misleading meaning.
  Do not rename for taste. Every rename lands in the upgrader map; ThingDef renames also trigger thing-ID rewriting (§5.4).
- Def XML file names: by Def type and topic, no dates (`Apparel_0920.xml` → topic name). Abstract `Name`s: `Mugirl_` prefix,
  no dates (`Mugirl_0920CasualBase` → topic name) unless public (§2).
- No dead content: defs never generated or referenced, fields without a C# reader, retired-only defs → delete (with DefInjected).

### 3.4 Scribe (save schema rules for the NEW code)
- Labels: lowerCamel, equal to the field name; no redundant owner prefix INSIDE an owner element; but comp labels that land in
  a SHARED element (pawn/thing/hediff node shared with vanilla and other comps) carry a comp prefix (`milk…`, `mount…`,
  `weaponWheel…`) and must not collide with vanilla labels (`def`, `id`, `health`, `story`, …).
- Related state goes into a nested `IExposable` (`Scribe_Deep`), not parallel lists or 40 flat labels.
- Defaults: every `Scribe_Values.Look` passes a default equal to the field initializer (the validator checks it in IL).
  Values whose default depends on Def data use `forceSave: true`.
- No transient caches or tick counters in saves when `IsHashIntervalTick`/absolute ticks do the job.
- No translation keys or translated text stored as data: store an enum/ID and translate at display time.
- Enums saved by name: member names are part of the schema; renames go through the map.
- `GetUniqueLoadID()` formats, JobDriver toil order, LordJob graph order, DefMap'd def lists and short-hashed defs are schema too:
  a change needs an upgrade step (or ending in-flight jobs/lords in a post-load step).
- New GameComponents/MapComponents need the public `(Game)`/`(Map)` ctor and must treat "constructed but never exposed" (added to an
  old save) as a valid state.

### 3.5 Code
- Per-game transient state registers with the lifecycle hub (§5.8); no feature reaches into another feature to reset it.
- Core must not depend on Features (settings constants live in Core; features read settings).
- Compatibility code never Harmony-patches Mugirl's own methods: features expose explicit extension points (hooks/registries) that
  compatibility modules register into.
- Logging only via `MugirlLog`; no `Verse.Log` in features. No `DefDatabase.GetNamed*` at runtime except where the validator's
  dynamic-lookup allowlist justifies it. Static caches carry `StaticCacheLifecycle:` comments.
- Comments in Chinese (technical names stay English), describe the current reason, never the history ("since 9.20…", "1.6 did…")
  — history belongs to git and docs/records.

---------------------------------------------------------------------------------------------------------------------
## 4. Execution model (waves; file ownership prevents concurrent edits of one file)

- Wave A (parallel, disjoint files): A1 SaveUpgrade framework + lifecycle hub + settings upgrade + unit tests;
  A2 validator legacy-schema corpus extractor + corpus generation; A3 rename SPEC per module (read-only) + rename-apply tool;
  A4 build/solution fix and explicit patch ordering.
- Wave R (single central pass): apply the merged rename spec with the deterministic tool (C# via Roslyn, XML/DefInjected/Patches via
  XML rewriter, file moves). The type/defName maps of the upgrader are GENERATED from the same spec, so code and map cannot diverge.
  Build, unit tests and validator must be green afterwards (save-contract checks aside, replaced in Wave V).
- Wave S (parallel by module, disjoint folders): structural refactors, legacy-code removal, label/structure changes, each with
  its upgrade steps (`SaveUpgrade/Steps/<Module>/…`) and step unit tests; then adversarial review + fix per module.
  Shared files (e.g. `1.7/Defs/JobDefs/JobDefs.xml`, `Misc_Gameplay.xml`, DefInjected shared files) have ONE owner per wave;
  others request edits in their report.
- Wave V: `C.save-upgrade` completeness green; offline real-save corpus run; headless smoke; in-game suites incl. the new
  upgrade suite; fix loop.
- Wave D: docs (README, ADRs, runbook, records), migration-doc findings, About.xml `1.7`.

---------------------------------------------------------------------------------------------------------------------
## 5. Save upgrader architecture (Mugirl.SaveUpgrade)

Engine facts: `understand/engine-hooks.md` (all verified on 1.7.4977 rev1248). Key ones: vanilla converters are SKIPPED when game build
and ordered packageId list are unchanged (the common "only Mugirl updated" case); `PreLoadSavegame/PostLoadSavegame` are not gated;
the whole save DOM exists before any ExposeData; comps save inline in their parent element; default values are not written;
`GetBackCompatibleType` runs for every deep object and its result is cast without an assignability check.

### 5.1 Components
```
SaveUpgrade/
  SaveSchema.cs                 // const int Current; stamp label "mugirlSaveSchema" (frozen forever)
  LegacyNames/                  // DECLARATIVE TABLES (pure data, no Verse state): the only place old names live
    LegacyTypes.cs              //   old full type name -> new Type (typeof) | Drop(policy, reason)
    LegacyDefs.cs               //   (def root type, old defName) -> new defName | Drop(policy, reason)  [generated part + hand part]
    LegacyLabels.cs             //   owner selector + old label -> new label | Drop
    LegacyEnums.cs, LegacyValues.cs
  Document/                     // DOM model: typed-owner resolution, safe element rename/move, ID rewriting, report
  Steps/<Module>/*.cs           // ordered, idempotent DOM steps, each declares the legacy names it consumes + a unit test
  PostLoad/<Module>/*.cs        // semantic steps in phases A/B/C, each gated by the session
  Hooks/*.cs                    // the Harmony patches below (only this folder may touch Scribe internals / BackCompatibility)
  SaveUpgradeSession.cs         // captured meta, detected schema, per-load report; cleared by the LoadGame finalizer
  SettingsUpgrade.cs            // mod-settings file migration (runs in the Mod ctor before GetSettings)
```
### 5.2 Hooks (all applied from the Mod constructor via HarmonyBootstrap)
1. `Verse.Game.LoadGame()` Prefix `[HarmonyPriority(Priority.First)]` + Finalizer: verify `Scribe.mode==LoadingVars`,
   `curXmlParent.Name=="game"`, root `savegame`; snapshot `ScribeMetaHeaderUtility` (modIds, gameVersion); detect Mugirl and schema
   (by the stamp LABEL under `game/components/li`, not by Class); run the DOM pipeline; never throw (catch → one error with step id,
   DOM left as is); Finalizer clears the session.
2. Phase A AfterResolve: Postfix `ScribeLoader.FinalizeLoading` (gate on session). Phase B AfterSpawn: Prefix `Game.FinalizeInit`
   (gate on session). Phase C AfterLoaded: Postfix `Game.LoadGame`. Player-facing messages via `LongEventHandler.ExecuteWhenFinished`.
3. Runtime fallbacks for documents the DOM pass never sees (xenotype/ideo/scenario files, DefInjected): Postfix
   `BackCompatibility.GetBackCompatibleType` (first statement `if (__result != null) return;`, then map lookup, assignability check),
   Postfix `BackCompatibility.BackCompatibleDefName` (type-aware map; never reuse an old name for a different new def).
4. XML type aliases for third-party Defs (§2): Postfix `GenTypes.GetTypeInAnyAssembly(string,string)` acting only on `null` results and
   only for names in LegacyTypes. Applied in the Mod ctor (before LoadModXML). Must be verified in game (doc claim was UNVERIFIED).

### 5.3 DOM pipeline order (legacy session)
1. Type renames/drops on every `Class` attribute (incl. generic args) and Type-valued Scribe_Values text.
2. Def renames/drops: `<def>`-style references in known contexts AND via the BackCompatibleDefName postfix; drop policies remove
   dependents (things, bills, dictionary key+value pairs together, list items) and null out references.
3. Thing-ID rewriting for renamed ThingDefs (§5.4).
4. Owner-scoped label renames, enum value renames, value maps, materialisation of OLD defaults where a default changed.
5. Module structural steps in a fixed global order (each idempotent; detect applicability from shape).
Rules: never insert comments/text nodes; rename elements by create+move+ReplaceChild; collect edits then apply; single traversal
where possible; dictionaries (`keys`/`values`) are edited pairwise.

### 5.4 Thing IDs
`Thing.ThingID = def.defName + thingIDNumber`; references are `Thing_<ThingID>`; verb/comp load IDs embed ThingIDs. For each thing whose
def is renamed, map oldID→newID and rewrite exact tokens (`<id>`, `Thing_<id>`, and `<id>` embedded in load IDs where bounded by
start/end/`_` and not followed by a digit). Also `Blueprint_`, `Blueprint_Install_`, `Frame_`, `Corpse_`, `Meat_`, `Make_`-derived defNames.

### 5.5 Owner selectors (for labels)
`ClassIs(T)` (element's Class after remap is T or listed subclass); `ThingWithComp(T)` (element has `<def>` whose remapped ThingDef has
comp class T); `HediffWithComp(T)`; `PathFrom(ownerSelector, "a/li/b")` for nested deep objects without Class; `GameComponent(T)`,
`MapComponent(T)`. Comp scoping resolves defs through `DefDatabase` (ready at load time) after applying the def map.

### 5.6 One table, three consumers
LegacyTypes/LegacyDefs feed: the DOM pass, the BackCompatibility runtime fallbacks, and (types only) XML alias resolution. Domain code
never mentions a legacy name. The validator (C.save-upgrade) proves the tables are complete against the legacy corpus and that every
target exists with the right base type / Def root / reading owner.

### 5.7 Mod settings
`Mod_<folder>_MugirlMod.xml` (short Mod name + folder name — the namespace does not matter). In the Mod ctor before `GetSettings`:
transform the file textually with the same table style (settings class name, renamed keys, e.g. `enableStructuralCrashEvent` →
`enableOpeningStoryline`), then `GetSettings` and `Write()` once if it changed.

### 5.8 Lifecycle hub and stamp
`Mugirl.Core.GameComponent_Mugirl` (type name frozen forever; public `(Game)` ctor): writes `mugirlSaveSchema = SaveSchema.Current` on
every save (plus diagnostics: source schema, applied step ids); raises `StartedNewGame` / `LoadedGame` / `FinalizeInit` to a registry of
per-game reset callbacks that features register (replaces MugirlStoryState as the de-facto hub). Field default for the stamp = 0
("unknown"), never "current".

### 5.9 Logging/report
One summary line per upgraded load (source era guess, steps run, counts). DevMode: full per-step report. No player-facing letter.

---------------------------------------------------------------------------------------------------------------------
## 6. Validator & tests (Wave A2/V)
- Replace C.save-types/labels/enums/defs, C.frozen-keys, B.member-rebinds, B.scribe-defaults(list) by `C.save-upgrade` over an
  append-only legacy corpus `Source/Tests/MugirlRace.Validation/Baselines/legacy/` extracted once from all legacy builds (git
  history DLLs 0.4.7…0.6.7 + frozen DLL + MooGirl-era DLLs + their Def trees) + `legacy-extra.txt` for names known only from real
  saves (e.g. `Mugirl.CorporateRuntimeValidation`). Dispositions: Unchanged | Renamed | Consumed(step) | Dropped(reason).
- New rules: confinement (Scribe internals/BackCompatibility/`curXmlParent` only in `SaveUpgrade/`), namespace-shadow ban, zero types in
  root namespace, Scribe default == initializer (IL), forbidden-patterns/key-literal rules exempt `SaveUpgrade/LegacyNames`.
- Unit tests: every step has golden before/after XML (trimmed from real fixtures); idempotence (upgrade twice = once); a corpus test
  that runs the DOM pipeline over local real saves when present (skipped in CI, reported).
- In-game: `upgrade` suite loads legacy fixtures (archived 1.6 saves in DevData/Evidence; optionally a scripted save produced by the frozen
  1.6 DLL as a marked temporary mod), asserts no load errors, stamp present, no legacy names in the re-save, key state preserved,
  round trip in a fresh process with the upgrader inert. Fixtures stay out of git if they are the owner's personal saves.
- `dotnet build Source/MugirlRace.slnx` must succeed; test builds never write into load folders.
