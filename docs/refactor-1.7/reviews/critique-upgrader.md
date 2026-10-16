# Adversarial review: save-upgrader architecture (design.md §1, §2, §3.4, §5)

Reviewer role: find every way the unified upgrader can fail. This review is read-only. It writes nothing to the repo.

Evidence conventions:
- `rw17-game` is the DecompilerServer context for `RimWorld\Assembly-CSharp.dll`, 1.7.4977 rev1248. I checked it with `list_contexts`. Tokens are from that context.
- `design:N` is a line in `scratchpad/design.md`. `EH:N` is a line in `understand/engine-hooks.md`. `RS:N` is `understand/real-saves.md`. `VT:N` is `understand/validator-tooling.md`. `MC:N` is `understand/module-core.md`. `CT:N` is `understand/content.md`.
- `Mods/…` paths are under `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods`.

Severity scale:
- **blocker**: the design as written loses data or breaks dependent mods in a common case.
- **major**: silent corruption, or a whole class of saves or documents that is not handled.
- **minor**: robustness, diagnostics or hygiene.

---------------------------------------------------------------------------------------------------------------------
## Summary table

| # | Sev | Topic | One-line |
|---|---|---|---|
| 1 | blocker | GenTypes XML alias (§2, §5.2.4, §5.6) | In 1.7, an unresolved XML `Class` makes the new deserializer **throw**, and the dependent's whole Def is dropped. The alias table (all of LegacyTypes) also leaks into save loading with no assignability guard. Freeze the 3 classes instead |
| 2 | major | §2 public API incomplete | WhiteFluff has compile-time references to Mugirl types, runtime defName strings and an XPath on our names. Subspecies and KindSeal set XML **field** names. No alias can reach any of these |
| 3 | major | §1 eras / detection | The published v1.6-only build already runs on 1.7. Its saves carry gameVersion 1.7 with 1.6-era shapes, in the identical-environment case. No logic may branch on gameVersion |
| 4 | major | §1 MooGirl era | The "no race pawns" premise rests on the owner's 4 saves. Public MooGirl-era saves will contain `MooGirl` race pawns. The scope must be explicit and tested |
| 5 | major | §5.2.1 partial failure | "DOM left as is" is false after partial mutation. The Finalizer can swallow exceptions. The player is never told, and the first save destroys the original |
| 6 | major | §5.8 stamp truthfulness | The stamp says Current when the hooks never ran. It can be missing when the hub component failed, and then "materialise old default" steps corrupt a new-shape save |
| 7 | major | §5.3.2 / §5.5 def typing | `<def>` text is type-ambiguous (shared names across Def types). The postfix receives subclass types and must normalise to the database root. Let the postfix own all Scribe_Defs renames |
| 8 | major | §5.4 load IDs | `Bill_<recipe>_<n>` is missing (no number fallback). Verb IDs embed ManeuverDef and tool ids. Trailing-digit defNames change `thingIDNumber`, which can collide |
| 9 | major | §3.4 / §5.2.2 toil and lord indices | Lord graphs are rebound at PostLoadInit, which is before Phase A, so "end lords post-load" is too late. Jobs need Phase B, not A. DOM job removal causes reservation errors |
| 10 | major | §5.5 owner scoping | Comp owners are resolved through the *current* DefDatabase. Legacy def→comp relations are lost when the refactor moves comps |
| 11 | major | §5.3.4 defaults / enums | An absent label equals the old **default argument**, which the corpus does not record. Some defaults depend on Def data (`lockCount`) |
| 12 | major | §5.3 drop policy | Removing list items is unsafe beyond dictionaries: parallel lists (Punisher ×3) and positional lists |
| 13 | major | §5.8 lifecycle hub | Every GameComponent is constructed 2–3× per load (`new Game()` ×2). No event fires before deserialisation, so the previous game's statics leak into ExposeData |
| 14 | minor | DefMap / short hash | §3.3 file renames reorder `HairDef`/`ResearchTabDef` indices, which shifts `IdeoStyleTracker` DefMaps. ThingDef renames can shift short-hash probe chains |
| 15 | minor | §5.2.3 GetBackCompatibleType postfix | It has no consumer today (no Mugirl ScenPart/Precept). It costs one call per deep object and misses the `ParseType` path |
| 16 | minor | DefInjected | engine-hooks pitfall 20 is wrong: keys are converted at add time, so the BackCompatibleDefName postfix *does* serve third-party DefInjected |
| 17 | minor | §5.2 hook choice | `Game.LoadGame` Prefix is the right primary hook. Add installation verification and fix the Finalizer contract. Nothing Mugirl-specific is deserialised earlier |
| 18 | minor | §5.7 settings | The 1.6 frozen build shares the same settings file. Renames cause ping-pong and a red error at every 1.6 startup. Use an XML parse, an atomic write and a settings schema stamp |
| 19 | minor | §1 newer schema | Logging alone is not enough. Re-saving silently downgrades the save |
| 20 | minor | VEF / Harmony order | Key the postfix on the *original* defName and run it last. VEF's own `DefMigrationDef` could claim the same old names |
| 21 | minor | strings in saves | Type-valued labels, `TaleData_Def.defData`, quest signals and questTags need explicit context lists from the schema graph |
| 22 | minor | formatting / culture / perf / meta | Use Scribe's value formats, read meta from the DOM, and set a performance budget for 60+ MB saves |
| 23 | minor | §3.4 label rule | "label == field name" turns every future field rename into a schema migration. Freeze labels after this wave |

---------------------------------------------------------------------------------------------------------------------
## Findings

### 1. [blocker] The GenTypes alias for XML-visible classes: the failure mode is "dependent Def dropped", and the alias leaks into saves

Design: design:47-49 says the three XML-visible classes MAY be renamed. Their old names resolve through the upgrader's type-alias table. design:184-185 and :207-208 add a Postfix on `GenTypes.GetTypeInAnyAssembly(string,string)`, acting "only for names in LegacyTypes".

Evidence:
- **1.7 parses Defs with the new IL-emitting deserializer by default.**
  - `LoadedModManager.ParseAndProcessXML` (0x06001921) sets `bool flag = !GenCommandLine.CommandLineArgPassed("legacy-xml-deserializer"); … Def def = (flag ? DirectXmlToObjectNew.DefFromNodeNew(item4, …) : DirectXmlLoader.DefFromNode(item4, …));`.
  - The old path, `DirectXmlToObject.ClassTypeOf` (0x06002653), logs "Could not find type named …" and **falls back** to `typeof(T)`.
  - The new path, `DirectXmlToObjectNew.ResolveTypeForNode(Type,XmlNode)` (0x0600267E), **throws**: `throw new ArgumentException("Could not find type named " + …)`.
  - The emitted IL in `EmitIlToCreateAndPopulateComplexType` (0x06002678) has no exception block.
  - `DefFromNodeNew` (0x0600265D) wraps the parser in `try { def = defParserForType(...) } catch { Log.Error("Exception loading def from file …") }` and returns `null`.
  - So one unresolved `<li Class="Mugirl.CompProperties_MugirlMount"/>` drops the **whole** WhiteFluff race ThingDef (`Mods/乳牛娘 白绒物语/1.6/Defs/WhiteFluff_Race.xml:103`). One unresolved `StockGenerator_Mugirl_Slaves` drops KindSeal's TraderKindDef. This is not a one-line red error.
- **Postfix visibility (the task's question).**
  - `GetTypeInAnyAssembly` caches `null` (`typeCache.Add(key, value)` before `return value`; `GenTypes` decompile). The cache lives *inside* the original body, and a Harmony postfix runs on every call, cached or not. So caching null does **not** hide later calls from a postfix.
  - The real blind spots are:
    - callers that inline the 82-byte method;
    - code that bypasses GenTypes.
  - The new deserializer emits `call GenTypes.GetTypeInAnyAssembly` into DynamicMethods (field `GenTypesGetTypeInAnyAssemblyMethod`). CoreCLR compiles LCG methods fully optimised and never tiers them. Whether the Waylith/Harmony 3 detour prevents later compilations from inlining the original IL is **unverified**.
  - Compile-time TypeRefs never touch GenTypes. Example: WhiteFluff's `[HarmonyPatch(typeof(Mugirl.MountedPawnUtility), nameof(Mugirl.MountedPawnUtility.IsMugirl))]` at `Mods/乳牛娘 白绒物语/Source/Core/Identity.cs:99`. `AccessTools.TypeByName` also bypasses GenTypes.
- **The alias leaks into save loading.**
  - `BackCompatibility.GetBackCompatibleType` returns `GenTypes.GetTypeInAnyAssembly(providedClassName)` directly when the environment is identical, and also as its final fallback (`BackCompatibility` decompile, hash e25b4bf7).
  - With a LegacyTypes-wide GenTypes alias, any legacy `Class` that the DOM missed resolves to the new type **without** the assignability check.
  - The GetBackCompatibleType postfix then sees `__result != null` and returns early (design:182), so its guard is dead.
  - `SaveableFromNode` casts `(T)exposable` unchecked (`ScribeExtractor` decompile). A mismatch raises InvalidCastException, and the object is lost.
- **An alias cannot fix XML field names.**
  - Subspecies sets `displayString`, `milkDef`, `milkIntervalDays` and `milkAmount` on `Mugirl.CompProperties_MooMilkable` (`Mods/MooGirl_Subspecies/1.6/Defs/AlienRaceDefs/Races.xml:322-327`).
  - KindSeal sets `slaveCountRange` (CT:213).

Amendment:
- §2: freeze the full names **and the XML field names** of `Mugirl.StockGenerator_Mugirl_Slaves`, `Mugirl.CompProperties_MugirlMount` and `Mugirl.CompProperties_MooMilkable`. They are public API like everything else in §2.
- Delete §5.2.4 and the "(types only) XML alias resolution" consumer in §5.6. The namespace rule in §3.1 then needs one explicit exception, recorded in the ADR.
- If the owner still wants renames:
  - Use a separate 3-entry `XmlTypeAliases` table, never LegacyTypes.
  - The postfix acts only when `__result == null`.
  - Test in game with the dependents loaded, under both the default deserializer and `-legacy-xml-deserializer`.

### 2. [major] §2 omits dependents that no upgrader table can reach

Evidence. String literals decompiled from `Mods/乳牛娘 白绒物语/1.6/Assemblies/MRK.WhiteFluff.dll`:
- `"Mugirl_Lactation"` (WF + CompWhiteFluffHealth);
- `"MugirlMilkFilth"` (JobDriver_WhiteFluffGather);
- `"Defs/BodyDef[defName='MugirlBody']"` (PatchOperation_CopyBody);
- `"Mugirl_Charge"`, `"Mugirl_Shout"`, `"Mugirl_NobelPrize"`.

Its source has a compile-time reference to `Mugirl.MountedPawnUtility` and `Mugirl.Comp_MugirlMount` (CT:218). KindSeal finds the mod by its display name `Mugirl Race` (CT:213). WhiteFluff FA uses `Job_MugirlMilkingTargetWait` (CT:186).

CT:173 proposes renaming `MugirlMilkFilth` to `Mugirl_Filth_ShockMilk`, and §3.3 (design:101) would rename every unprefixed `Mugirl*` def. `DefDatabase.GetNamed("MugirlMilkFilth")` in WhiteFluff never goes through `BackCompatibleDefName`.

Amendment:
- Add to §2:
  - `Mugirl_Lactation`, `MugirlMilkFilth` and `Job_MugirlMilkingTargetWait`;
  - the type `Mugirl.MountedPawnUtility` with the member `IsMugirl`, and `Mugirl.Comp_MugirlMount`;
  - the display name `Mugirl Race`;
  - the field names from #1.
- Alternatively, record an explicit owner decision to ship a coordinated WhiteFluff release; the same author owns it.
- Have the validator read the dependents list (CT §C) as a "frozen-forever" input. Then a rename spec entry for a listed name fails Wave R.

### 3. [major] The published 1.6 build already runs on 1.7, which is the identical-environment case with 1.6-era shapes

Evidence:
- The workshop copy `steamapps/workshop/content/294100/3578882219/LoadFolders.xml` defines only `<v1.6>`, and `About.xml` declares `supportedVersions` 1.6.
- `ModContentPack.InitLoadFolders` (0x0600196D) falls back to the highest defined version ≤ current: `where … VersionFromString(x) <= VersionControl.CurrentVersion … orderby x descending`. So on 1.7.4977 the 1.6 DLL loads.
- Saves written that way have `<gameVersion>1.7.4977 …` but MugirlRace-era shapes.
- When such players install the refactored build, the build and the ordered mod list are unchanged. `CheckSaveIdenticalToCurrentEnvironment` is then true, and every converter is skipped (EH:212-222).
- EH:432 suggests "use the meta `gameVersion` (1.6 against 1.7) as a sub-discriminator". That would misclassify these saves.

Amendment:
- §1: add the row "MugirlRace-era build running on the 1.7 game (published workshop build)" with FULL support.
- State: *no step may branch on gameVersion; applicability is by XML shape only.*
- §6: promote fixture B ("frozen 1.6 build on the 1.7 game", VT:258) from a test technique to the primary real-player fixture. Run it with the mod list unchanged.

### 4. [major] The MooGirl-era scope rests on 4 personal saves

Evidence:
- design:29 says "real MooGirl-era saves contain no Mugirl race pawns". RS:65 shows this only for the owner's S1–S4. RS:301 itself says "Workshop users may have saves from builds not seen here".
- The MooGirl builds were published under the same workshop id (VT:12).
- A public MooGirl-era colony will contain `<def>MooGirl</def>`, `Thing_MooGirl<n>`, MooGirl comp labels and MooGirl hediffs.
- "Name-level, no deeper semantics" turns `MooGirl` into `Mugirl`. Those pawns would then load with legacy comp labels nobody reads. If the MooGirl-era body def differed in part order, they would also get wrong body-part indices, because `BodyPartFromNode` resolves `bodyDef.GetPartAtIndex(index)` (`ScribeExtractor` decompile).

Amendment. §1 must pick one of two options explicitly:
- (a) "MooGirl era: race pawns upgraded at name level plus ID rewrite. Every MooGirl-era label is Dropped with a reason. Body and addon order is verified equal to the legacy `MooGirlBody`, or the step remaps part indices."
- (b) "MooGirl era: Mugirl content dropped deliberately, with the player notified once."

Either way, add a MooGirl-era fixture **with race pawns** produced from a git `MooGirlRace.dll` build (VT:98), and add MooGirl-era body/addon order to the corpus.

### 5. [major] Partial failure: neither atomic nor visible, and the original file is overwritten

Evidence:
- design:177-178: "run the DOM pipeline; never throw (catch → one error with step id, DOM left as is)".
- Steps 1–5 mutate sequentially (design:187-195). A throw in step 4 leaves steps 1–3 applied, so the DOM is not "as is".
- A Harmony Finalizer that returns `null` swallows the exception of the original. `Game.LoadGame` failures (`FinalizeInit` is not wrapped; `Game.LoadGame` decompile 0x06000DD5) would then be hidden and the game would continue half-initialised. This is general Harmony semantics.
- design:224 rules out any player-facing notice.
- RimWorld overwrites the same `.rws` on save, and commitment mode saves to the same file automatically. So the legacy original is gone at the first save after a failed upgrade.

Amendment:
- §5.3: each step runs **plan → apply**. The plan is pure, collects edits and may throw. The apply phase uses only primitive DOM operations that cannot throw on valid plans.
- The first failing plan aborts the pipeline. Already-applied steps stay, and that is consistent because every step is idempotent and shape-detecting.
- Set `session.State = Failed(stepId)`.
- The Finalizer is `void`, or returns `__exception` unchanged. It only clears the session.
- §5.9: on `Failed`, show one main-thread dialog through `ExecuteWhenFinished`: "the save upgrade failed at step X; do not overwrite this save; send the log".
- §5.1: on the first legacy load of a file, copy the original once to `<name>.rws.mugirl-schema0.bak` (it does not appear in the load list) before any edit. Record it in the report.

### 6. [major] The stamp can lie, or can be missing

Evidence:
- design:218-221: the hub "writes `mugirlSaveSchema = SaveSchema.Current` on every save". The hooks are applied per class with failure isolation: `HarmonyBootstrap.TryPatch` catches and only warns (`Source/MugirlRace/Core/HarmonyBootstrap.cs:124-136`).
- If the `Game.LoadGame` patch fails (Harmony conflict, target changed), a legacy save loads unupgraded, and the first save stamps it as Current. That is permanent and silent.
- Conversely, if `GameComponent_Mugirl` fails to construct, `Game.FillComponents` (0x06000DD3) only logs "Could not instantiate a GameComponent", and nothing is stamped. The next load sees "Mugirl in modIds, no stamp", which means schema 0.
- On schema 0, "materialisation of OLD defaults" (design:192) **writes old defaults into labels that new code omitted because they equalled the new default**. That is silent corruption.
- `Scribe_Values` omits values equal to the default (EH:487). A stamp written without `forceSave` disappears if someone ever makes the default equal Current.

Amendment:
- §5.8: the hub writes the stamp with `forceSave: true`.
- The hub constructor is trivial and cannot throw (validator rule: no DefDatabase/Find/static access in GameComponent ctors under `Mugirl.Core`).
- The hub writes `Current` only if (new game) or (the session for this game completed, or found the save already at Current). Otherwise it writes the source schema and logs an error.
- At startup, `SaveUpgrade.Hooks` verifies with `Harmony.GetPatchInfo` that its LoadGame/FinalizeLoading/FinalizeInit patches are owned by `MugirlMod.Mod`. If not, raise a startup error dialog and set `Installed=false`. The hub then never writes Current for loaded games.
- Steps that materialise old defaults require positive legacy evidence on the **owner element**: a legacy-only sibling label, a legacy Class, or a legacy def. "No stamp" alone is not enough.

### 7. [major] Def renames: `<def>` text is type-ambiguous, and the postfix receives subclass types

Evidence:
- RS:193 lists names shared across Def types:
  - `Mugirl_Blindfold` (HediffDef + SlaveApparelDef, which is a ThingDef);
  - `Mugirl_Charge` (Ability + Gene + Hediff);
  - `Mugirl_Slave`, `Consumed_MugirlMilk` and others.
- The same element name `<def>` carries the def of Things, Hediffs, Genes, Abilities, Thoughts and Jobs. So a DOM rename "in known contexts" (design:189) or the selector `ThingWithComp(T)` "element has `<def>` whose remapped ThingDef has comp class T" (design:203-204) misfires on a hediff `li` whose `<def>` is `Mugirl_Blindfold`.
- `ScribeExtractor.DefFromNode<T>` passes `typeof(T)`, the **declared** field type, which can be a subclass.
- `TaleData_Def.ExposeData` passes `def.GetType()`, the **runtime** subclass (`tmpDefType = def.GetType(); … BackCompatibleDefName(tmpDefType, tmpDefName)`). Example: `Mugirl.SlaveApparelDef` or `AlienRace.ThingDef_AlienRace`.
- `BackCompatibleDefName` returns early only when the name exists (`GetDefSilentFail … != null`). A postfix runs in every environment, identical or not.

Amendment:
- §5.3.2: Scribe_Defs-backed references (def fields, def lists, dictionary keys, FA/WorkTab/HAR fields, xenotype/ideo/scenario files) are renamed **only** by the BackCompatibleDefName postfix, which is type-aware by construction.
- The postfix:
  - normalises `defType` to its database root (walk `BaseType` until `BaseType == typeof(Def)`);
  - refuses ambiguous lookups when the root is `Def`;
  - maps only when `GetDefSilentFail(root, originalName) == null`, so third-party redefinitions win.
- The DOM rewrites def names only:
  - in string-typed contexts (an explicit list, see #21);
  - in load-ID tokens (#8);
  - for drops.
- The DOM resolves def names *virtually* for owner selection.
- §5.5: recognise a Thing element structurally. It has `<def>` **and** an `<id>` equal to defName plus digits, or it is in the ID map. Hediffs are recognised only under `hediffSet/hediffs/li`.
- Validator: a legacy defName that is shared across roots must carry a per-root disposition.

### 8. [major] Load-ID rewriting misses Bill IDs, verb components and number preservation

Evidence:
- `RimWorld.Bill.GetUniqueLoadID`: `"Bill_" + recipe.defName + "_" + loadID`.
- `LoadedObjectDirectory.ObjectWithLoadID<T>` falls back to the thing number **only** `if (typeof(Thing).IsAssignableFrom(typeof(T)) …)`.
- So a renamed RecipeDef, including the implicit `Make_<thingDef>` of every renamed craftable ThingDef, breaks `Job.bill`, `UnfinishedThing` bound bills and every other `Bill_…` reference, with a warning and `null`. design:197-200 covers only ThingIDs.
- `Verb.CalculateUniqueLoadID(owner, tool, maneuver)`: `$"{owner.UniqueVerbOwnerID()}_{tool.id}_{maneuver.defName}"`. Mugirl defines 1 ManeuverDef and 1 ToolCapacityDef (defs.json). Renaming either, or reordering weapon `tools` (which have implicit ids), makes VerbTracker "Replace verb" (RS:256).
- `Thing.IDNumberFromThingID` uses `Regex.Match(thingID, "\\d+$")`, and `ThingID` is `def.defName + thingIDNumber`.
  - If the old or new defName ends in a digit, a naive suffix swap changes the parsed number.
  - `LoadedObjectDirectory.RegisterLoaded` then hits `allThingsByThingID.Add(thing.thingIDNumber, …)` with a duplicate key, giving the error "Exception registering … unique thing ID".
  - Numbers stored as ints, such as `TaleData_Thing.thingID` and `Thought_MemoryObservation.targetThingID`, stop matching.
  - The legacy corpus has `Bullet_Mugirl_AT23` (git only).
- Motes have no ID number (`ThingDef.HasThingIDNumber => category != ThingCategory.Mote`), so their ThingID is just the defName and is not unique.

Amendment:
- §5.4 becomes a table of load-ID templates generated from the def map:
  - `Thing_<ThingID>`;
  - `<id>`;
  - `Bill_<RecipeDef>_<n>`;
  - verb IDs `<ownerID>_<toolId>_<ManeuverDef>`, `<ownerID>_<index>` and `CompEquippable_<ThingID>_<i>`;
  - Corpse/Blueprint/Blueprint_Install/Frame/Meat/Make derived defs.
- New ID = `newDefName + vanillaParsedNumber(oldId)`.
- Validator:
  - rename targets of ThingDef/RecipeDef must not end in a digit;
  - an old name ending in a digit gets a computed number;
  - ManeuverDef, ToolCapacityDef and weapon tool lists or ids are frozen, or carry a verb-ID step.
- Keep the token-boundary rule. It is sound only because `thingIDNumber` is globally unique; state that as the invariant. Exclude motes.

### 9. [major] Toil and lord indices: §3.4's "end in a post-load step" is too late for lords and needs Phase B for jobs

Evidence:
- `Lord.ExposeData_StateGraph` at **PostLoadInit**, which runs inside `ScribeLoader.FinalizeLoading` and so before Phase A:
  - it calls `SetJob(lordJob, loading: true)`;
  - it applies `lordToilData` and `triggerData` by index, with the error "Could not find lord toil for lord toil data … index is out of bounds";
  - `RemoveLord` happens only if `LostImportantReferenceDuringLoading`.
- `JobDriver.ExposeData` at PostLoadInit runs `SetupToils()` with the saved `curToilIndex` (forceSave).
- Ending jobs in Phase A runs driver cleanup on **unspawned** pawns, where `Map` is null.
- Removing jobs in the DOM leaves `ReservationManager` entries. Its `ExposeData` logs "Loaded reservation with null job: … Deleting it…" as a `Log.Error`.

Amendment:
- §3.4 / §5: LordJob graph changes are DOM steps. Remove `lordToilData`, `triggerData` and `curLordToilIdx` for the affected `lordJob Class`, or remove the lord `li` together with its quest/lord references.
- JobDriver toil changes are Phase B steps (`Game.FinalizeInit` prefix, maps spawned): `pawn.jobs.EndCurrentJob(JobCondition.InterruptForced)` for the declared driver types, plus queued jobs of those JobDefs.
- Each such step declares the legacy driver or lord types it covers. The validator requires a step whenever the IL of `MakeNewToils`/`CreateGraph` changed against the corpus. The corpus records a toil-count or graph signature per legacy build.

### 10. [major] Owner scoping uses the current DefDatabase, but legacy owners are a legacy fact

Evidence:
- design:205: "Comp scoping resolves defs through `DefDatabase` (ready at load time) after applying the def map."
- `ThingWithComps.InitializeComps` builds comps from `def.comps` (EH:256), and labels are inline (EH:255).
- If the refactor moves a comp between defs, removes it from a def, or merges two comps, the current DB no longer says which comp wrote a legacy label on that def. The label is silently dropped, and nothing errors.

Amendment:
- The corpus records `def → comps[]` and `hediffDef → comps[]` per legacy build. VT:125 `legacy-defs.txt` gains a comps column.
- Owner selectors for Mugirl defs evaluate the **legacy** relation.
- DefDatabase is used only for third-party defs that carry Mugirl comps (WhiteFluff, Subspecies).

### 11. [major] Defaults and enums: the corpus cannot tell what "absent" meant

Evidence:
- `ScribeExtractor.ValueFromNode<T>`: `if (subNode == null) return defaultValue;`. The parse-error path is `Log.Error(…); return default(T);`.
- So an absent label means the old **default argument**, not the old initializer.
- The proposed `legacy-labels.txt` format (VT:123) has no default column.
- Some defaults depend on Def data: `SlaveApparel.lockCount` defaults to the def's `needkeynumber` (RS:272). If that XML value or field changes, the old meaning cannot be recomputed from the current DefDatabase.

Amendment:
- The corpus extractor records `default` (an IL constant or a "def-dependent" marker) and `forceSave` per (owner, label).
- For def-dependent defaults, it records the legacy def-field values per def.
- Materialisation steps are generated from the corpus diff. The validator fails when a default changed with no materialisation entry.
- Enum maps cover dictionary keys and list items, not just labels. Flags enums use split/map/join with `", "`.

### 12. [major] Drop policy: pairwise editing beyond dictionaries

Evidence:
- design:193 says "dictionary key+value pairs together, list items". MC:242 shows the dictionary hazard.
- `MapComponent_MugirlPunisher` saves three index-parallel lists: `punishers`, `punisherSpawnedAtTicks` and `punisherLeavingFlags` (RS:115).
- `DefMap.vals` is positional (`DefMap<D,V>.ExposeData`: `Scribe_Collections.Look(ref values, "vals", …)`, indexed by `def.index`).

Amendment:
- §5.3: list items may be removed only in containers that the schema graph marks `Removable`: `HashSet`/filter lists, `ThingOwner.innerList`, and things lists.
- Dictionaries are edited pairwise. Declared parallel-list groups are edited together.
- Everywhere else, null out (`IsNull="True"` or the text `null`) and never remove.
- Validator: every Mugirl `Scribe_Collections` pair or parallel group is declared.

### 13. [major] Lifecycle hub: constructed 2–3 times per load, with no "before load" event

Evidence:
- `SavedGameLoaderNow.LoadGameFromSaveFileNow` (0x0600165C) runs `Current.Game = new Game();` **twice**.
- `Game..ctor` (0x06000DCD) is `FillComponents();`, which constructs every non-abstract GameComponent with `Activator.CreateInstance(type, this)` (0x06000DD3).
- If the save contains the hub, `SaveableFromNode` constructs it a third time.
- design:219-220 raises only `StartedNewGame`/`LoadedGame`/`FinalizeInit`. All of them fire after ExposeData, so static per-game state from the previous game is visible to every ExposeData of the new load.

Amendment:
- §5.8: the hub constructor has no side effects (no static self-registration, no registry reset). The hub is found through `Current.Game.GetComponent`.
- Add a `BeforeLoad` callback raised from the `Game.LoadGame` prefix before the DOM pass, and a `BeforeNewGame` callback from a `Game.InitNewGame` prefix. Feature resets register there.
- Phase C copies the session diagnostics into the hub instance. The session itself is cleared by the Finalizer.

### 14. [minor] DefMap indices and short hashes shift through file moves and renames

Evidence:
- Saved DefMaps include `IdeoStyleTracker.hairFrequencies` (HairDef; Mugirl defines 10) and `ResearchManager.tabInfoVisibility` (ResearchTabDef; Mugirl defines 1). See `IdeoStyleTracker.ExposeData` (`Scribe_Deep.Look(ref hairFrequencies, …)`) and the search results for `DefMap` fields.
- `def.index` follows load order.
- `DirectXmlLoader.XmlAssetsInModFolder` (0x0600263A) enumerates `GetFiles("*.xml", SearchOption.AllDirectories)`. So the file renames and moves in §3.3 (design:104) reorder Mugirl HairDefs, and ideo hair weights shift silently.
- `ShortHashGiver.GiveShortHash`: `StableStringHash(defName) % 65535`, linear probing in defName order per type. A renamed ThingDef can displace a colliding compressible or deep-resource def.

Amendment:
- Validator: the ordered list of Mugirl defs of every DefMap'd type equals the legacy order. The types are HairDef, BeardDef, TattooDef, ResearchTabDef, WorkTypeDef, RecordDef, TrainableDef, JoyKindDef and HistoryEventDef.
- Simulate short hashes for vanilla+DLC+Mugirl before and after the rename. Fail if any `saveCompressible` def or deep-resource def changes hash.

### 15. [minor] The `GetBackCompatibleType` postfix has no consumer today

Evidence:
- Saves are handled by the DOM.
- The settings file is read before Harmony (`Source/MugirlRace/Core/MugirlMod.cs:16-18`).
- grep finds no Mugirl `ScenPart`, `Precept` or other side-document type.
- Type-valued fields go through `ParseHelper.ParseType` (0x06000626), which uses `GetBackCompatibleTypeDirect`, not the patched method.
- The postfix fires for every deep object (EH:450).

Amendment: drop §5.2.3a. Add a validator rule: if a Mugirl type derives from a side-document root (ScenPart, Precept, ritual types, …) and is renamed, the alias postfix becomes mandatory and needs a test.

### 16. [minor] DefInjected: the postfix works, and engine-hooks pitfall 20 is wrong

Evidence:
- `DefInjectionPackage.TryAddInjection` (0x06000F3C) runs `key = BackCompatibleKey(key); … defInjection.path = key;`.
- `BackCompatibleKey` (0x06000F3E) converts `array[0]` through `BackCompatibleDefName(defType, …, forDefInjections: true)`.
- So `SetDefFieldAtPath` (0x06000F41) receives the already-converted path, and `list2[0]` is the new name. This contradicts EH:550-551.

Amendment:
- Keep the BackCompatibleDefName postfix as the DefInjected consumer, not gated by session.
- Correct the engine-hooks claim in the migration docs.
- Test with a third-party translation that uses old keys. Expected: no "Found no … named"; at most one "Duplicate def-injected translation key" when a pack carries both keys.

### 17. [minor] Hook choice: keep the `Game.LoadGame` Prefix, but verify it

Comparison:

| Hook | Timing | Risks |
|---|---|---|
| `Game.LoadGame` Prefix | Sole caller is `SavedGameLoaderNow` (`find_callers`). Runs before `ClearCaches` and `PreLoadSavegame`. The DOM is complete and `curXmlParent` is `<game>` | Harmony conflicts. It is not an inlining risk: the method is large and compiled at the first load |
| `ExposeSmallComponents` Prefix | Same DOM state, one step later | Also runs while Saving (needs a mode gate). It is private |
| Reflected converter `PreLoadSavegame` | Not gated (only `AppliesToLoadedGameVersion(allowInactiveScribe:true)`). Vanilla wraps it in try/catch and logs | Writes a private static field. It adds a virtual call per deep object in non-identical loads |

What runs before `LoadGame`:
- the header-only load (`PreLoadUtility`);
- `LoadGameDataHeader`, which reads only meta;
- the two `new Game()` constructions (#13).

The scenario, world, maps and components are all deserialised inside `LoadGame`. So no Mugirl data is read earlier; the GameComponent constructors are the only Mugirl code that runs before it.

`ScribeLoader.FinalizeLoading` is 165 IL bytes with exception handling (`get_il` 0x060026F8). It is a poor inline candidate, so it is acceptable for Phase A.

Amendment:
- Keep the `LoadGame` Prefix (`Priority.First`).
- Read meta from `curXmlParent.OwnerDocument.DocumentElement["meta"]` rather than from `ScribeMetaHeaderUtility` statics.
- Add the installation check from #6.
- Gate Phase C on "Phase A ran". `LoadGame`'s "Could not find world XML node" path skips `FinalizeLoading`.
- `ExecuteWhenFinished` closures capture the report object, never the session.

### 18. [minor] Mod settings

Evidence:
- `GetSettingsFilename` uses the folder name and the short Mod name (EH:306-308). The frozen 1.6 build uses the same `MugirlMod` and the same folder, and the config folder is per user, not per game version.
- After a 1.7 rewrite, a 1.6 install sees a renamed Class (one red error at every startup) and renamed labels (old defaults). Its next write restores the old labels, so the two versions ping-pong.

Amendment. §5.7:
- Parse with `XmlDocument`, not text replacement.
- Add `settingsSchema` (forceSave) and run the upgrade by version, not by shape sniffing.
- Write to a temp file, then replace.
- Keep the `Class` attribute value used by the frozen 1.6 build (`Mugirl.MugirlSettings`) **or** accept and document the 1.6 red error.
- Never call `GetSettings` or `InitLoading` from load hooks (EH:331).

### 19. [minor] Saves from a newer schema

design:35 says "do not touch, log one warning". The domain then drops the newer labels, and the next save stamps `Current`, which is older. That is a silent downgrade.

Amendment:
- Show a main-thread dialog: "this save was written by a newer Mugirl; saving it here loses data".
- The runtime postfixes stay active; they are safe under the no-reuse rule.

### 20. [minor] VEF and Harmony order on `BackCompatibleDefName`

Evidence:
- VEF's postfix (applied in `ExecuteWhenFinished`) overrides `__result` only in the identical environment (EH:383).
- In the non-identical case, VEF's converter runs inside the original before every postfix. Another mod's `DefMigrationDef` could claim a Mugirl legacy name.

Amendment:
- The Mugirl postfix uses `[HarmonyPriority(Priority.Last)]` and keys on the **`defName` argument**, not on `__result`.
- It overrides only when its root lookup of `__result` fails.
- Name the postfix owner in the report. The validator warns when a loaded `DefMigrationDef` contains a Mugirl legacy name. This is a dev check only.

### 21. [minor] String contexts need an explicit list

Evidence:
- `TaleData_Def` saves `defData/defName` and `defType`, and `defType` is a Type-valued value of the runtime subclass.
- Quest signals such as `Quest<N>.CourierRaid_Spawn` are strings built from code constants (RS:275).
- `questTags` (VT:73) and `CorporateRecord.key` (RS:268) are strings.
- design:188 ("Type-valued Scribe_Values text") gives no way to locate them.

Amendment:
- The validator's schema graph (VT:197) emits a list of string-valued and Type-valued contexts, for vanilla and Mugirl.
- DOM value maps act only on those paths.
- Signal names emitted by Mugirl code are recorded as saved values (`legacy-saved-values.txt`).

### 22. [minor] Format, culture and performance

Evidence:
- The worker thread forces English culture (`LongEventHandler.RunEventFromAnotherThread`, EH:25). Unit tests do not.
- Saves reach 67 MB (RS:37), and the DOM pass runs on the loading thread.

Amendment:
- DOM writes use Scribe's formats: invariant floats, `True`/`False`, enum `ToString()`.
- Never insert text or comment nodes (EH:531).
- Set a budget of at most one full traversal plus one ID pass, and at most 2 s on S7, measured in the corpus test.
- `MugirlLog` must be thread-safe for worker-thread calls.

### 23. [minor] §3.4 "label equals field name" couples refactoring to the schema

Evidence: design:109. After this wave, any field rename silently changes the written label, and `C.save-upgrade` then demands a migration for a cosmetic change.

Amendment: "Labels are chosen equal to the field name *when introduced*. They are then frozen strings, and the validator compares labels with the corpus, not with field names." Shared-element comp labels use the `mugirl` prefix, because third-party races carry Mugirl comps next to their own `milk…` comps (WhiteFluff `CompWhiteFluffMilk`).

---------------------------------------------------------------------------------------------------------------------
## Must be tested in game

1. **Dependents with renamed XML classes (only if #1 is not adopted).**
   - Run WhiteFluff, Subspecies and KindSeal.LOL with the default deserializer and with `-legacy-xml-deserializer`.
   - Assert that `WhiteFluff_Mugirl` and the KT trader defs exist, there is no "Could not find type named", and the comps are of the correct type.
2. **Identical environment.**
   - Fixture B: a save written by the frozen 1.6 DLL on 1.7.4977, mod list unchanged, loaded with the new build.
   - Assert that the converters were skipped (dev log) and the upgrade is complete.
3. A real 1.6-game save (gameVersion 1.6.4871, non-identical) produces the same upgrade report as #2 for equal content.
4. A MooGirl-era fixture **with race pawns**, matching the scope decision of #4.
5. **Failure injection.**
   - A dev switch throws in step k. Assert the dialog, the backup file, the session state Failed, no exception swallowed, and the stamp policy from #6.
   - Run again with the LoadGame patch disabled. Assert the startup error and no Current stamp.
6. Load an upgraded save twice in a fresh process: the upgrader is inert, and the re-save diff is empty.
7. A new game saved by the new build reloads with the upgrader inert. The hub is present, and the stamp is written with `forceSave`.
8. **In-flight state.**
   - A legacy save taken during a Mugirl job whose toils changed (Phase B end): no "index out of bounds" and no "Cleaning up invalid job state" for unaffected pawns.
   - A save during an active Mugirl lord (DOM step): no "Could not find lord toil".
9. **ID rewriting.**
   - A save with a renamed craftable ThingDef: an unfinished thing bound to a bill, an active bill job, an equipped renamed weapon with a burst in progress, and a corpse/blueprint/frame of renamed defs.
   - Expect zero "Could not resolve reference", zero "Replaced verb" and zero "Exception registering".
10. **Side documents.**
    - A `.xtp` containing renamed GeneDefs.
    - An ideo with a relic or apparel precept of a renamed ThingDef.
    - A third-party DefInjected pack using old keys.
11. **Settings.**
    - A legacy settings file, both the local-folder and the workshop-folder name.
    - Then start the frozen 1.6 build against the same Config to observe the documented behaviour.
12. A newer-schema save produces the dialog and nothing else.
13. Per-game statics: load save A, then save B in the same session. Assert that no state leaks across, using the `BeforeLoad` callback.
14. With VEF loaded: VEF and Mugirl postfix order, no double mapping, no "Could not load reference".
15. **Performance.** The DOM pass time on the largest real saves (S7 67 MB, S11 63 MB), logged in the summary line.
16. An Ideology save with ideo hair frequencies, before and after the refactor's file moves. Frequencies attach to the same HairDefs.
