# RimWorld 1.7 save-loading machinery: hooks for a unified Mugirl save upgrader

Scope: engine-side only (Assembly-CSharp 1.7 and the VEF 1.7 build). This report inventories no Mugirl names. Everything below was read from decompiled code. Claims I could not verify are marked **[UNVERIFIED]**.

## 0. Evidence base and citation conventions

| Alias (DecompilerServer) | File | Version |
|---|---|---|
| `rw17-game` | `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Assembly-CSharp.dll` | AssemblyVersion 1.7.9782.37458. `VersionControl` subtracts 4805 from the build, giving **1.7.4977 rev1248** (matches `Version.txt`) |
| `rw16-9676-erebus` | `C:\Users\Fishiv\source\repos\ExtraWeapon2\ExtraWeapon2\bin\Release\Assembly-CSharp.dll` | FileVersion **1.6.9676.17735**, a full-body 1.6 build (15.7 MB). It is used only for the 1.6/1.7 comparison. The Steam library has no 1.6 game copy, and `steamapps\common` contains only the current RimWorld |
| `vef17` | `...\workshop\content\294100\2023507013\1.7\Assemblies\VEF.dll` | About.xml packageId `OskarPotocki.VanillaFactionsExpanded.Core` |

- **Type-level line numbers** ("L123") are lines of the DecompilerServer type decompile with the header included. I verified them with `get_source_slice includeLineNumbers`. Method-level citations give the metadata token and quote the statement.
- All `0x06xxxxxx` tokens are from `rw17-game`.

---

## 1. Full load path of a savegame (1.7.4977)

### 1.1 Entry and threading

- `Root_Play.Start()` queues `SavedGameLoaderNow.LoadGameFromSaveFileNow(...)` through `LongEventHandler.QueueLongEvent(..., doAsynchronously: true, GameAndMapInitExceptionHandlers.ErrorWhileLoadingGame)`. This covers both the autostart path and the `Find.GameInitData.gameToLoad` path.
  - `GameDataSaveLoader.LoadGame(string)` only sets `GameInitData.gameToLoad` (`GameDataSaveLoader` L153-L166).
  - `CheckVersionAndLoadGame` first runs `PreLoadUtility.CheckVersionAndLoad`. That is a **header-only** read: `InitLoadingMetaHeaderOnly`, then `LoadGameDataHeader(mode, logVersionConflictWarning:false)`, then `FinalizeLoading`.
- The whole load runs on the long-event **worker thread**. `LongEventHandler.RunEventFromAnotherThread` (0x060006AC) calls `CultureInfoUtility.EnsureEnglish()`, then the action, and logs any exception as "Exception from asynchronous event".
- **Consequence:** every upgrader hook runs off the main thread. Do not call Unity APIs or open UI from a hook; queue that work with `LongEventHandler.ExecuteWhenFinished`.

### 1.2 `SavedGameLoaderNow.LoadGameFromSaveFileNow` (0x0600165C)

```
Current.Game = new Game();                       // throw-away instance
Scribe.loader.InitLoading(GenFilePaths.FilePathForSavedGame(fileName));
ScribeMetaHeaderUtility.LoadGameDataHeader(ScribeHeaderMode.Map, logVersionConflictWarning: true);
if (!Scribe.EnterNode("game")) { Log.Error("Could not find game XML node."); Scribe.ForceStop(); return; }
Current.Game = new Game();                       // the real instance
Current.Game.LoadGame();
catch { Scribe.ForceStop(); throw; }
```

### 1.3 `ScribeLoader.InitLoading(string)` (0x060026F6, `ScribeLoader` L19-L53)

- L38-L43: `new StreamReader(filePath)` → `new XmlTextReader(input)` → `new XmlDocument()` → `xmlDocument.Load(reader)` → `curXmlParent = xmlDocument.DocumentElement`.
- L45: `Scribe.mode = LoadingVars`.
- The `XmlDocument` is not stored anywhere else. It is reachable only through `Scribe.loader.curXmlParent` (field L15) and through `OwnerDocument` of any node. `FinalizeLoading` L98 sets `curXmlParent = null`, after which the document can be collected.
- The DOM is complete in memory before any `ExposeData` runs. Nothing is streamed, so the whole document can be rewritten before Scribe reads it.

### 1.4 Meta header: `ScribeMetaHeaderUtility.LoadGameDataHeader` (L76-L117)

- **Reset** (L78-L85): `loadedGameVersion="Unknown"`, Major/Minor/Build = 0, `loadedModIdsList = loadedModNamesList = null`, `modListChanged=false`.
- **Fields read** (L90-L92): `gameVersion`, `modIds`, `modNames`.
  - `modSteamIds` is **written** (`WriteMetaHeader` L65-L66) but **never read**. The field `loadedModSteamIdsList` (L39) has no usages anywhere in the assembly.
  - **There is no per-mod version.** The meta contains only `gameVersion` (e.g. `1.7.4977 rev1248`), `modIds`, `modSteamIds` and `modNames`.
  - `modIds` holds `ModContentPack.PackageId`, which is `ModMetaData.PackageId` = `packageIdLowerCase` plus `"_steam"` when `appendPackageIdSteamPostfix` is set. Mugirl therefore appears as `har.mugirlrace` or `har.mugirlrace_steam`. Git history shows the packageId was `HAR.MuGirlRace` before commit 324501c. It lowercases to the same string.
- **`modListChanged`** = `!LoadedModsMatchesActiveModsNoInfo()` (L112).
  - The comparison (L171-L200) matches by packageId list, or else by folder-name list.
  - It is case-insensitive per element and **order-sensitive**, with an exact count.
  - Updating Mugirl in place leaves it `false`.
- **Static state is clobbered.** These statics are reset by every header load, including ones that happen in-game:
  - scenario/ideo/xenotype/modlist/camera-config loads in `GameDataSaveLoader`;
  - `Dialog_ModList_Load`;
  - the header-only pre-check.

  Capture whatever you need into your own session object during the load. Never read these statics later.

### 1.5 `Game.LoadGame()` (0x06000DD5): the exact order

The body is identical in 1.6.9676 and 1.7.4977.

1. `ClearCaches(); MemoryUtility.UnloadUnusedUnityAssets();`
2. **`BackCompatibility.PreLoadSavegame(ScribeMetaHeaderUtility.loadedGameVersion)`**. Its only caller is this method. At this point:
   - `Scribe.loader.curXmlParent` is the `<game>` element;
   - the meta statics are populated;
   - `Current.Game` is the real (empty) game.
3. `Current.ProgramState = MapInitializing;`
4. `ExposeSmallComponents()` (0x06000DD2) in LoadingVars:
   - info, rules, scenario, tickManager, … , questManager, …;
   - `Scribe_Collections.Look(ref components, "components", LookMode.Deep, this)`;
   - then `FillComponents()` (LoadingVars only), the null-replacement block, and `BackCompatibility.PostExposeData(this)`.
5. `Scribe.EnterNode("world")`:
   - `World = new World(); World.ExposeData();`. In LoadingVars this calls `WorldGenerator.GenerateFromScribe`, which calls `GameSetupStep_Components.GenerateFromScribe` (0x0600DBAC) = `ConstructComponents(); ExposeComponents();`.
   - `RegisterForCrossRefResolve(World)`. World is **not** registered for PostLoadInit.
6. `World.FinalizeInit(fromLoad: true)` → `WorldComponentUtility.FinalizeInit`. **This runs before cross-references are resolved.**
7. `Scribe_Collections.Look(ref maps, "maps", LookMode.Deep)`. Each map is loaded through `SaveableFromNode` and registered for cross-refs and PostLoadInit. Then `currentMapIndex` and `Find.CameraDriver.Expose()`.
8. `Scribe.loader.FinalizeLoading()` (0x060026F8, `ScribeLoader` L88-L112):
   - `Scribe.ExitNode()` leaves `<game>`; `curXmlParent=null`; `Scribe.mode=Inactive`;
   - `crossRefs.ResolveAllCrossReferences()` (0x060026CE) sets mode ResolvingCrossRefs, then registers every `ILoadReferenceable` in `crossReferencingExposables`, then calls `ExposeData()` on every registered exposable;
   - `initer.DoAllPostLoadInits()` (`PostLoadIniter` L35-L54) sets mode PostLoadInit and iterates a **`HashSet<IExposable>`** (L8), so the order is unspecified. It finishes with mode Inactive.
   - From here on, `curXmlParent` is `null`. XML is no longer readable in ResolvingCrossRefs or PostLoadInit.
9. For each map: `maps[i].FinalizeLoading()` (0x06001459). This spawns things (`BackCompatibility.Pre/Post/CheckSpawnBackCompatibleThingAfterLoading`), then `Map.FinalizeInit()` (0x0600145A) → `MapComponentUtility.FinalizeInit(this)`. Then `maps[i].Parent?.FinalizeLoading()`.
10. `FinalizeInit()` (0x06000DDA):
    - `researchManager.ReapplyAllMods()`;
    - **`GameComponentUtility.FinalizeInit()`**;
    - `ProgramState = Playing`; ideoManager notification.
11. `if (Prefs.PauseOnLoad)`: an `ExecuteWhenFinished` single tick.
12. **`GameComponentUtility.LoadedGame()`**.
13. **`BackCompatibility.PostLoadSavegame(loadedGameVersion)`**. This is the very last statement.

**When is DefDatabase ready?** During the Mod constructor, Defs do not exist yet. `LoadedModManager.LoadAllActiveMods` (0x06001919) runs `InitializeMods → LoadModContent → CreateModClasses → LoadModXML → ... → ApplyPatches → ParseAndProcessXML`. By the time any save is loaded, `PlayDataLoader` has finished: all Defs, short hashes and DefOfs exist. So a pre-read hook may use `DefDatabase` freely.

### 1.6 Other `InitLoading` callers (and how to recognise a savegame document)

`find_callers(ScribeLoader.InitLoading)` returns eight callers:

| Caller | Root element | Header mode | Notes |
|---|---|---|---|
| `SavedGameLoaderNow.LoadGameFromSaveFileNow` | `savegame` | `Map` with `logVersionConflictWarning: true` | The only path to `Game.LoadGame`; then `EnterNode("game")` |
| `GameDataSaveLoader.TryLoadScenario` | `savedscenario` | `Scenario` | `Scribe_Deep(scen,"scenario")`. Player `.rsc` files can contain ScenParts that reference mod Defs |
| `GameDataSaveLoader.TryLoadIdeo` | `savedideo` | `Ideo` | Precept classes and Def names |
| `GameDataSaveLoader.TryLoadXenotype` | `savedXenotype` | `Xenotype` | **Gene defNames.** Player `.xtp` files will reference Mugirl GeneDefs |
| `GameDataSaveLoader.TryLoadModList` | `savedModList` | `ModList` | |
| `GameDataSaveLoader.TryLoadCameraConfig` | `cameraConfig` | `CameraConfig` | |
| `ExternalHistoryUtility.Load` (0x06006830) | (`externalHistory` node) | none | |
| `LoadedModManager.ReadModSettings<T>` (0x06001927) | `SettingsBlock` | none | No meta at all; see §6 |

- No world-only load path exists in 1.7. The const `SavedWorldParentNodeName="savedworld"` has no `ldstr` usage.
- `InitLoadingMetaHeaderOnly` (callers: `PreLoadUtility`, `Dialog_ModList_Load`) builds a synthetic `<root>` element that contains only `<meta>` (`ScribeLoader` L71-L76). It never reaches `InitLoading`.
- **How to recognise a savegame document unambiguously:**
  1. Be inside `Game.LoadGame` or `BackCompatibility.PreLoadSavegame`. Both are reachable only from `SavedGameLoaderNow`.
  2. Or, from `InitLoading`/`LoadGameDataHeader`: `Scribe.loader.curXmlParent.OwnerDocument.DocumentElement.Name == "savegame"`.

  `LoadGameDataHeader(Map, ...)` is called **twice** per load, once header-only (curXmlParent is `<root>`, `logVersionConflictWarning:false`) and once for real. Do not key a one-time action on "header loaded".

---

## 2. `ScribeExtractor.SaveableFromNode<T>` (0x060026EA, `ScribeExtractor` L84-L153)

- **L95** `subNode.Attributes["IsNull"]` sits **outside** the try. A non-element child (`XmlComment`, text) gets `Attributes == null` → NRE → propagates to the nearest enclosing `SaveableFromNode` catch, which then loses the **enclosing** object. See the pitfalls in §11.
- **L105-L106:** `string text = Class attribute ?? typeof(T).FullName;`. Every deep-loaded object goes through type resolution, including objects that have **no** `Class` attribute.
- **L107:** `Type type = BackCompatibility.GetBackCompatibleType(typeof(T), text, subNode);`. **Always called**, once per deep object; this is the only call site (`find_usages`). `baseType` is the **declared** generic `T`.
- **L108-L113:** if the result is `null`: `GetBestFallbackType<T>(subNode)` followed by **`Log.Error("Could not find class … Trying to use … instead. Full node: …")`**. That is one error per instance, with no `ErrorOnce`.
- **`GetBestFallbackType<T>`** (L155-L190): the fallback depends on the declared type `T`.
  - `T : Thing` → `def.thingClass`
  - `T : Hediff` → `hediffClass`
  - `T : Ability` → `abilityClass`
  - `T : Thought` → `thoughtClass`
  - In these four cases the def lookup goes through `TryFindDef`, which applies `BackCompatibleDefName` (L192-L200).
  - Otherwise → `typeof(T)`.
- **L114-L116:** if `type.IsAbstract` → `throw new ArgumentException("Can't load abstract class …")`.
- **L118:** `Activator.CreateInstance(type, Instance|Public|NonPublic, ctorArgs)`. The constructor signature must match `ctorArgs`, e.g. `(Game)`, `(Map)` or `(World)` for components.
- **L119-L123:** register for cross-refs unless `T` is a value type or a `Name`.
- **L124-L139:** swap `curXmlParent = subNode`, `curParent = exposable`, `curPathRelToParent = null`; call `ExposeData()`; restore.
- **L140-L142:** `RegisterForPostLoadInit`.
- **L144:** `return (T)exposable;`. **There is no assignability check before this cast.** A remap to a type that does not derive from `T` throws `InvalidCastException`, which the catch at L146-L149 turns into `"SaveableFromNode exception"`. Result: `null`, and the object is lost.

**Type-name resolution behind it:** `GenTypes.GetTypeInAnyAssembly(string, string ns = null)` (0x0600059B, `GenTypes` L272-L290; IL 82 bytes).

1. If `ns == null` or `ns` is an ignored namespace → `TryGetTypeInIgnoredNamespace(typeName)` (L252-L270).
   - This is a dictionary built once from **every** type whose namespace is `null` or one of `RimWorld, Verse, LudeonTK, Verse.AI, Verse.AI.Group, Verse.Sound, Verse.Grammar, RimWorld.Planet, RimWorld.BaseGen, RimWorld.QuestGen, RimWorld.SketchGen, System` (L53-L57).
   - It is keyed by **short name**, with the last writer winning.
2. Otherwise → `typeCache` lookup. **`null` results are cached too.** `GetTypeInAnyAssemblyInt` → `GetTypeInAnyAssemblyRaw` (L506-L527) does:
   - `Assembly-CSharp.GetType(name, false, ignoreCase:true)`;
   - then every running mod assembly in load order;
   - then `Type.GetType(name, false, true)`;
   - then `ns + "." + name` and each ignored namespace + `"." + name`;
   - then a mixed-assembly generic parse.

   **Lookups are case-insensitive.**
- **Writer side:** `Scribe_Deep.Look` writes `Class=GenTypes.GetTypeNameWithoutIgnoredNamespaces(runtimeType)` only when `target.GetType() != typeof(T)`. That value is:
  - the short name for vanilla-namespace types;
  - `FullName` (`Ns.Outer+Inner`) for mod types;
  - `type.ToString()` for generics.

### Failure behaviour per base type

This applies when the Class cannot be resolved and nothing remaps it:

| Declared `T` | What happens |
|---|---|
| `GameComponent` / `MapComponent` / `WorldComponent` (abstract) | "Could not find class" error, then "Can't load abstract class" (caught → "SaveableFromNode exception" error); `null` goes into the list. `FillComponents` (`RemoveAll(null)`, then creates every missing non-abstract subclass) builds a **fresh default instance**. **All saved data lost.** Two red errors per component |
| `Thing`/`Pawn`, `Hediff`, `Ability`, `Thought` | One error per instance. Constructed as the def's class; data is kept if labels match |
| Any other non-abstract declared `T` | Error; constructed as `T`; the subclass's extra labels are ignored |
| Abstract declared `T` (e.g. `QuestPart`, `LordJob`, `JobDriver`) | Same as the components row: abstract → exception → `null`. Each owner then handles `null` its own way **[UNVERIFIED per owner]** |

---

## 3. `BackCompatibility` (type hash identical in 1.6.9676 and 1.7.4977)

| Member | Location | Behaviour |
|---|---|---|
| `conversionChain` (private static `List<BackCompatibilityConverter>`, field 0x04002264) | L18-L29 | `0_17_AndLower, 0_18, 0_19, 1_0, 1_2, 1_3, 1_4, 1_5, Universal`. **There is no `_1_6` converter in 1.7.** `BackCompatibilityConverter_1_5.AppliesToVersion` is `major==1 && minor<=5 \|\| major==0`. For a 1.6 save, only `Universal` applies |
| `RemovedDefs` (private static readonly) | L31-L51 | (defName, defType) pairs whose absence `DefFromNode` does not report |
| `IsSaveCompatibleWith(string)` | L56-L79 | Same major and minor ≤ current → compatible. Only used for the version-mismatch dialog |
| `PreLoadSavegame(string)` | L81-L97 | Loops converters with `AppliesToLoadedGameVersion(allowInactiveScribe:true)`. **Not gated by `CheckSaveIdenticalToCurrentEnvironment`.** Sole caller: `Game.LoadGame` |
| `PostLoadSavegame(string)` | L99-L115 | Same gating. Sole caller: end of `Game.LoadGame` |
| `BackCompatibleDefName(Type defType, string defName, bool forDefInjections=false, XmlNode node=null)` | L117-L148 | See the detail below |
| `BackCompatibleEnum(Type, string)` | L150-L164 | Hard-coded `QualityCategory` values. **Not gated; not converter-based.** Caller: `ParseHelper.FromString(string, Type)` (0x06000639), used for every enum parse (`Scribe_Values` → `ValueFromNode` → `FromString<T>` → this) |
| `GetBackCompatibleType(Type baseType, string providedClassName, XmlNode node)` | L166-L192 | See the detail below |
| `GetBackCompatibleTypeDirect(Type, string)` | L194-L216 | Converters with `AppliesToVersion(CurrentMajor, CurrentMinor)`, i.e. **not** save-version-based and **not** gated. Then GenTypes. Caller: `ParseHelper.ParseType` (0x06000626), but only after `GetTypeInAnyAssembly` returned `null`. This path covers every `Type`-valued `Scribe_Values` field (e.g. `TaleData_Def.defType`, 0x060097F5) and every `Type` field in Def XML |
| `PostExposeData(object)` | L253-L273 | Returns if Saving **or** identical. Otherwise calls `converter.PostExposeData(obj)` for converters applying to the loaded version |
| `PostCouldntLoadDef(string)` | L275-… | Gated by identical. Called from `DefFromNode` L62 |
| `GetBackCompatibleBodyPartIndex` | | Gated by identical |
| `CheckSpawnBackCompatibleThingAfterLoading`, `Pre…`, `Post…` | | Hard-coded for 0.18 and earlier. Not useful as hooks |
| `CheckSaveIdenticalToCurrentEnvironment()` (private) | **L479-L498** | See the skip condition below |

**`BackCompatibleDefName` detail.**
- L119: `if (CheckSaveIdenticalToCurrentEnvironment()) return defName;`
- L123: `if (GenDefDatabase.GetDefSilentFail(defType, defName, false) != null) return defName;`. **An old name that still exists is never converted.**
- L128-L146: it then **chains** through every applicable converter; each converter receives the previous result.

**`GetBackCompatibleType` detail.**
- L168-L170: when identical, returns `GenTypes.GetTypeInAnyAssembly(providedClassName)` directly.
- Otherwise the **first non-null** converter result wins (L172-L190), and the vanilla lookup is the fallback (L191).
- **The converters run before the vanilla lookup**, so a converter can re-point a name that still resolves.

**`BackCompatibleDefName` callers** (`find_usages`, six in total):
- `ScribeExtractor.DefFromNode<T>` (0x060026E7, L50; `defType = typeof(T)`, `node = subNode`). This covers `Scribe_Defs` and Def lists and dictionaries via `DefFromNodeUnsafe`.
- `ScribeExtractor.TryFindDef` (0x060026EC, L199; used by the fallback type logic).
- `DefInjectionPackage.BackCompatibleKey` (0x06000F3E) and `DefInjectionPackage.SetDefFieldAtPath` (0x06000F41), both with `forDefInjections:true`.
- `TaleData_Def.ExposeData` (0x060097F5).
- `GenText.AdjustedForKeys` (0x060005B5; KeyBindingDef, error text only).

**The skip condition, exactly** (L479-L498):

```csharp
if (Scribe.mode == LoadSaveMode.Inactive) return false;
if (ScribeMetaHeaderUtility.modListChanged) return false;
if (VersionControl.CurrentBuild != ScribeMetaHeaderUtility.loadedGameVersionBuild) return false;
if (Application.isEditor) return false;
return true;
```

So with the same game build and the same ordered packageId list, the converter calls are skipped in `GetBackCompatibleType`, `BackCompatibleDefName`, `PostExposeData`, `PostCouldntLoadDef` and `GetBackCompatibleBodyPartIndex`. That is exactly the situation when a player updates only Mugirl. **`PreLoadSavegame` and `PostLoadSavegame` are not skipped.**
- A 1.6 save (build 4871) loaded in 1.7 (build 4977) is "not identical".
- A pre-refactor 1.7 save on the same build is "identical".
- The outcome also flips with every RimWorld hotfix and every mod-order change, so it is non-deterministic from the mod's point of view.

**`BackCompatibilityConverter` base** (abstract, hash identical across versions):
- abstract: `AppliesToVersion(int,int)`, `BackCompatibleDefName(...)`, `GetBackCompatibleType(...)`, `PostExposeData(object)`;
- virtual: `GetBackCompatibleBodyPartIndex`, `PostCouldntLoadDef`, `PreLoadSavegame(string)`, `PostLoadSavegame(string)`;
- non-virtual `AppliesToLoadedGameVersion(bool allowInactiveScribe=false)`: false if `loadedGameVersion` is empty, false if Scribe is inactive and the flag is not set, otherwise `AppliesToVersion(loadedMajor, loadedMinor)`;
- a protected `Scribe_TypeToType` helper.

**Where `BackCompatibility.PostExposeData(obj)` is called** (39 callers). Among them:
- end of `Thing.ExposeData`. This is **before** `ThingWithComps` loads its comps, because `base.ExposeData()` runs first;
- `Hediff.ExposeData`;
- `Pawn.ExposeData`;
- `WorldComponent.ExposeData` (base implementation);
- `World.ExposeComponents`;
- `Map.ExposeData` and `Map.ExposeComponents`;
- `Game.ExposeSmallComponents`;
- `Quest`, `QuestManager`, `Faction`, `Pawn_NeedsTracker`, `Pawn_GuestTracker`, `Building_Bed`, `Caravan`, `Site`, `WorldPawns`, `Zone`, `GameCondition`, ….

During LoadingVars, `Scribe.loader.curXmlParent` is the object's own element at that point. `Universal.SaveLampColor` reads `curXmlParent["def"]`, so a converter can read legacy XML at this point. All of this is gated by the identical-environment check.

**Is there a public way to register a converter?** **No.** `conversionChain` is `private static`, and `BackCompatibility` exposes no add method (full member list checked). The only route is reflection (`AccessTools.Field(typeof(BackCompatibility), "conversionChain")`), which is what VEF does (§8).

---

## 4. How comps and trackers are saved (confirming that labels are inline)

- **`Thing.ExposeData`** (0x06002A98):
  - first `Scribe_Defs.Look(ref def, "def")`, then `if (def.HasThingIDNumber)`, which throws an NRE if the def failed to resolve; `SaveableFromNode` catches it and the **thing is lost**;
  - then `id`, `map`, `pos`, `rot`, `health`, `stackCount`, `stuff`, `faction`, `questTags`, …;
  - ends with `BackCompatibility.PostExposeData(this)`.
- **`ThingWithComps.ExposeData`** (0x06002B7E): `base.ExposeData(); if (LoadingVars) InitializeComps(); foreach comp: comps[i].PostExposeData();`. **No `EnterNode`**: every comp reads and writes its labels directly in the thing's element, alongside the Thing's and Pawn's own labels.
- **`InitializeComps()`** (0x06002B7C) creates comps **from the def**: `Activator.CreateInstance(def.comps[i].compClass)`, `parent=this`, `Initialize(def.comps[i])`. Comps are never type-named in the save.
- **`HediffWithComps.ExposeData`** (0x060020AD) has the same shape: `base.ExposeData(); if LoadingVars InitializeComps(); comps[i].CompExposeData();`. HediffComp labels are inline in the hediff's `<li>`.
- **`Pawn.ExposeData`** (0x06002357): `base.ExposeData()` (Thing + comps inline), then `kindDef`, `gender`, …, then trackers as child elements: `Scribe_Deep.Look(ref mindState,"mindState",this)`, `jobs`, `stances`, `verbTracker`, `natives`, `meleeVerbs`, `pather`, `carryTracker`, `apparel`, `story`, `equipment`, `drafter`, `ageTracker`, **`health` → label `"healthTracker"`**, `records`, `inventory`, …. Ends with `BackCompatibility.PostExposeData(this)`.
- Both comp methods are identical in 1.6 and 1.7 (`compare_symbols` body: unchanged).

**Consequences for an XML-level rename:**
- A comp label lives in a flat namespace shared by the Thing, the Pawn and every other comp on the same def.
- A rename such as `<oldLabel>` → `<newLabel>` must be **scoped to elements whose (remapped) `def` actually has that comp**. Find the elements by their `<def>` child and check `DefDatabase<ThingDef>.GetNamedSilentFail(newDefName).comps.Any(c => c.compClass == typeof(NewComp))` (likewise `HediffDef.comps`).
- Never rename by label name across the whole document. Scoping by element name is not enough either: `<thing>`, `<li>` and `<pawn>` all carry comps.
- Things in compressed map storage (`MapFileCompressor`, short-hash based) carry no comp data.

**Index-based state that a refactor can silently corrupt.** No label changes; ordering alone breaks these:
- **`JobDriver.ExposeData`** (0x060041F0) saves `curToilIndex` (forceSave) and `nextToilIndex`. Changing `MakeNewToils()` resumes in-progress jobs at the wrong toil.
- **`Lord.ExposeData_StateGraph`** (0x06004825) saves `lordToilData` keyed by toil index, `triggerData` keyed by the flattened trigger index, and `curLordToilIdx`. Changing a `LordJob.CreateGraph()` misassigns state or logs "index out of bounds" errors.
- **`DefMap<D,V>`** saves `vals` as a **positional list indexed by `def.index`**, then pads or truncates to `DefCount`. Example: `Pawn_WorkSettings.priorities` (0x06005E4E). Adding, removing or reordering Defs of a DefMap'd type (e.g. `WorkTypeDef`, `RecordDef`, `TrainableDef`) shifts values for every pawn.
- **Short hashes**: `ShortHashGiver.GiveShortHash` = `StableStringHash(defName) % 65535` plus collision probing in defName-sorted order. Terrain and roof grids and compressed things are stored by short hash. Renaming such a Def loses map data. `BackCompatibleDefName` is not involved; only the hard-coded `BackCompatibleTerrain/ThingDefWithShortHash` handle this. Mugirl 1.7 has no `TerrainDef`/`RoofDef`, and its only explicit `saveCompressible` entries are `false` (`1.7/Mods/Biotech/Defs/ThingDefs_Misc/Throwing.xml` L15, L30). Keep checking this.
- **Load IDs**: `LoadedObjectDirectory.RegisterLoaded` (0x060026D6) keys objects by `GetUniqueLoadID()` computed **at load**. References are stored as the old string. Changing a `GetUniqueLoadID()` format breaks every saved reference to those objects.

---

## 5. Game, Map and World components: lifecycle and where to stamp

| Aspect | GameComponent | MapComponent | WorldComponent |
|---|---|---|---|
| Saved | `game/components/li[@Class]` via `ExposeSmallComponents`, ctorArgs `(Game)` | `maps/li/components/li[@Class]` via `Map.ExposeComponents` (0x0600145B), ctorArgs `(Map)` | `game/world/components/li[@Class]` via `World.ExposeComponents` (0x0600DEB8), ctorArgs `(World)` |
| FillComponents | `Game.FillComponents` (0x06000DD3), **LoadingVars only**. `RemoveAll(null)`, then every `typeof(GameComponent).AllSubclassesNonAbstract()` that `GetComponent(Type)` (`IsInstanceOfType`) lacks is created with `Activator.CreateInstance(type, this)` (public ctor required) | `Map.FillComponents` (0x06001458): same, but skips `CustomMapComponent` subclasses and adds `generatorDef.customMapComponents`. Called on **every** pass (end of `ExposeComponents`) | `World.FillComponents` (0x0600DEBA): already run once by `ConstructComponents` (0x0600DEB9) before the list is loaded, then again after loading |
| base `ExposeData` | empty | (base) | `BackCompatibility.PostExposeData(this)` |
| FinalizeInit | `GameComponentUtility.FinalizeInit` inside `Game.FinalizeInit`, i.e. **after** maps are spawned and after MapComponent FinalizeInit | `MapComponentUtility.FinalizeInit` inside `Map.FinalizeInit`, i.e. after cross-refs and spawning | `WorldComponentUtility.FinalizeInit(world, fromLoad:true)` inside `World.FinalizeInit`, i.e. **before cross-refs are resolved** (LoadGame step 6) |
| New-game hook | `StartedNewGame()` at the end of `Game.InitNewGame` (0x06000DD4) | `MapGenerated` | `FinalizeInit(false)` |
| Load hook | `LoadedGame()` after `Game.FinalizeInit`, before `PostLoadSavegame` | none | none |

**A newly added component on an old save:**
- It is constructed by `FillComponents` (constructor only).
- Its `ExposeData` is **never called during that load**, not even with a missing node. It was not created by `SaveableFromNode`, so it is not registered for cross-refs or PostLoadInit. `Game.ExposeSmallComponents` runs only once (`Game` is not registered). In the Map and World ResolvingCrossRefs and PostLoadInit passes, the `Deep` branch of `Scribe_Collections.Look` (0x0600271F) does nothing to items.
- So its fields keep their **constructor and initializer defaults**. A schema field must therefore default to "unknown" (0), not "current".
- A component that exists in the save gets `ExposeData` in all three passes (LoadingVars, ResolvingCrossRefs, PostLoadInit).

**Unknown `Class=` on a component** gives two errors, the element is dropped, and a fresh instance is created. That confirms the migration doc. Keeping a legacy-named subclass is wrong, because `FillComponents` would add one stray instance of it to **every** game.

**Where to stamp, and where to run steps:**
- **Stamp:** a dedicated GameComponent that **always writes the current schema constant on Saving**. Whatever the domain code writes is, by construction, the new schema.
- **Read the stamp:** read it from the DOM in the pre-read hook. Its `ExposeData` value arrives too late, and it never arrives for a component missing from the save.
- **Phase timing** is in §10(v).

---

## 6. Mod settings

- `Mod.GetSettings<T>()` (`Mod` L18-L32): `modSettings = LoadedModManager.ReadModSettings<T>(intContent.FolderName, GetType().Name)` (L29). The type has two fields, so a second settings type triggers an error.
- `ModSettings.Write()` (L13) → `WriteModSettings(Mod.Content.FolderName, Mod.GetType().Name, this)`.
- `GetSettingsFilename` (0x06001926) = `Path.Combine(GenFilePaths.ConfigFolderPath, GenText.SanitizeFilename($"Mod_{modIdentifier}_{modHandleName}.xml"))`.
  - The file name uses the **mod folder name**, which differs between a local folder (`mugirl`) and a workshop install (numeric ID).
  - It uses the **short name** of the `Mod` subclass. The namespace does not matter.
- **File content.** `WriteModSettings` → `InitSaving(path, "SettingsBlock")`, then `Scribe_Deep.Look(ref settings, "ModSettings")`. The declared type is the abstract `ModSettings`, so `<ModSettings Class="<settings full type name>">` with the settings labels inline. There is no meta.
- **Reading.** `ReadModSettings<T>` (0x06001927):
  - `InitLoading`, then `Scribe_Deep.Look(ref target,"ModSettings")` with `T` = the concrete settings class → `SaveableFromNode<T>` → `GetBackCompatibleType(typeof(T), oldName, node)`.
  - At startup `loadedGameVersion` is null, so no converter applies. The fallback is `GenTypes`, then `null`, then `GetBestFallbackType<T>` = **`typeof(T)`**.
  - Then `FinalizeLoading`. Any exception → warning, then fresh defaults.

**Effects of renames:**

| Rename | Effect |
|---|---|
| `Mod` subclass short name | A different file is read. **Settings silently reset**; the old file is orphaned |
| Settings class namespace or name | **Values kept** (labels still match). One "Could not find class X while resolving node ModSettings" red error at **every startup**, until the file is rewritten. Vanilla rewrites it only when the settings window closes or `Write()` is called |
| Settings label or enum value | Old values ignored, so defaults apply (enum: parse error → `default(T)`) |

- The current comment at `Source\MugirlRace\Core\MugirlMod.cs` L5 says "类名决定设置文件名（Mod_<文件夹>_MugirlMod.xml），不能改名或改命名空间". The **namespace part of that comment is wrong** for the file name. The namespace only affects the Class attribute: data is kept, at the cost of one error per startup.
- Mugirl reads settings **before** applying Harmony (`MugirlMod.cs` L16 `GetSettings`, L18 `HarmonyBootstrap.ApplyAtStartup`). A `GetBackCompatibleType` postfix would therefore not be active for the settings read anyway.

**Recommended migration.** In the `MugirlMod` constructor, **before `GetSettings`**:
1. Compute the old and new paths with the same formula. `GetSettingsFilename` is private, so replicate it, using `Content.FolderName`.
2. If the new file is missing and the old one exists, or the new file still has the old `Class`/labels, transform the XML with the same rename tables the save upgrader uses and write the new file.
3. Call `GetSettings`, then `Write()` once so the file is canonical.

Never call `GetSettings` or any other `InitLoading`-based API from inside a load hook. `InitLoading` with `Scribe.mode != Inactive` logs an error and **`ForceStop`s the in-progress save load** (`ScribeLoader` L21-L25).

---

## 7. 1.7 against 1.6

A 1.6 assembly **was** found: `rw16-9676-erebus`, 1.6.9676.17735, a full build outside the Steam tree. Bodies compared with `compare_symbols … body`:

**Unchanged:**
- `ScribeLoader.InitLoading` and `FinalizeLoading`
- `SavedGameLoaderNow.LoadGameFromSaveFileNow`
- `ScribeMetaHeaderUtility.LoadGameDataHeader`
- `ScribeExtractor.SaveableFromNode` and `DefFromNode`
- `GenTypes.GetTypeInAnyAssembly`, `GetTypeInAnyAssemblyInt` and `GetTypeInAnyAssemblyRaw`
- `ParseHelper.ParseType` and `FromString<T>`
- `Game.LoadGame`, `ExposeSmallComponents`, `FillComponents` and `FinalizeInit`
- `Map.FillComponents`, `Map.ExposeComponents` and `Map.FinalizeLoading`
- `World.ExposeComponents`
- `Thing.ExposeData`, `ThingWithComps.ExposeData` and `HediffWithComps.ExposeData`
- `Mod.GetSettings`, `LoadedModManager.ReadModSettings` and `GetSettingsFilename`
- `PostLoadIniter.DoAllPostLoadInits`, `CrossRefHandler.ResolveAllCrossReferences` and `Root_Play.Start`
- `BackCompatibility` (whole type: same source hash `e25b4bf7…`)
- `BackCompatibilityConverter` (same hash `7a8d2049…`)
- `BackCompatibilityConverter_Universal.PostExposeData` and `BackCompatibleDefName`

**Cosmetic only:**
- `BackCompatibilityConverter_Universal.GetBackCompatibleType`: a compiler pattern-match rewrite of the `GravshipLaunch` check, with the same logic.
- `Map.ExposeData`: a local variable rename.

**Changed:** `LoadedModManager.LoadAllActiveMods` adds `ShaderDefRegistry.RegisterShaderDirectories()` after `InitializeMods`. This does not affect saves.

**Conclusion:**
- The save-loading logic is the same in 1.6.9676 and 1.7.4977. Vanilla adds **no 1.6→1.7 save conversion**: there is no `_1_6` converter, and for 1.6 saves only `Universal` runs.
- The 1.7 differences that matter are runtime ones:
  - CoreCLR on .NET 10 with tiered JIT and dynamic PGO, with Harmony 3;
  - the inlining caveat from migration-doc E39 (small patched methods can be invisible to callers that reached Tier 1 before the patch). The IL sizes of the candidate hook targets are in §10.
- The `Assembly-CSharp` build number also changes with each RimWorld patch, which toggles `CheckSaveIdenticalToCurrentEnvironment` for every existing save.

---

## 8. How Vanilla Expanded Framework 1.7 does back-compatibility (`vef17`)

1. **`VEF.BackwardsCompatibilityMigrationUtility`**, a `[StaticConstructorOnStartup]` class, i.e. Defs are loaded. Its static constructor:
   - reflects `AccessToolsExtensions.Field(typeof(BackCompatibility), "conversionChain")` and **appends** `new BackCompatabilityConverter_VEF()` (`AppliesToVersion` → always `true`). Appended means it comes after `Universal`; for types, the first non-null result wins, so Universal has priority.
   - reflects `"RemovedDefs"` and appends `(original, type)` for every migration whose `replacement == null`. This suppresses the "Could not load reference" error for intentionally deleted Defs.
   - builds `abilityClasses` from `AbilityExtension_ClassMigration.oldClass` (on `AbilityDef.modExtensions`), mapped to `AbilityDef.abilityClass`.
   - builds `defNameConverters: Dictionary<oldName, Dictionary<Type, newName>>` from the data-driven **`DefMigrationDef`** (XML: `migratedDefs` → per Def type → `original`/`replacement`/`removeIfMissingReplacement`, with `priority` ordering). `ResolveReferences` validates that every replacement exists.
2. **Converter behaviour.**
   - `BackCompatibleDefName`: looks up `(defName → defType)`, which makes it type-aware.
   - `GetBackCompatibleType`: only `if (baseType == typeof(Ability) && abilityClasses.TryGetValue(...))`.
   - `PostExposeData`: empty.
3. **Two Harmony patches**, category `"LateHarmonyPatch"`. `VEF_HarmonyCategories.TryPatchAll`, called from `VEF_Mod`, applies them inside **`LongEventHandler.ExecuteWhenFinished`**, i.e. after startup loading and *not* in the Mod constructor.
   - `VanillaExpandedFramework_BackCompatibility_BackCompatibleDefName_Patch`: a **Postfix**. Only when the private `CheckSaveIdenticalToCurrentEnvironment()` returns **true** (invoked through a delegate built with `NonPublicMethods.MakeDelegate`), it runs the VEF converter and overrides `__result` when that returns non-null.
   - `VanillaExpandedFramework_BackCompatibility_GetBackCompatibleType_Patch`: a **Prefix**, not a Postfix. When identical, it runs the converter and, on a hit, sets `__result` and skips the original.
   - `Prepare()` disables both patches when the converter maps are empty.

   In other words, the patches only re-enable VEF's own converter in exactly the case where vanilla skipped the chain.
4. **What VEF does not do:**
   - XML label or enum migration;
   - post-load semantic steps;
   - schema stamping;
   - anything for names that still exist (vanilla `BackCompatibleDefName` L123 returns early);
   - any type remap beyond `Ability`.

   VEF is a name-alias layer, not a schema upgrader.

---

## 9. Verification of the migration doc's "存档中的类型名" claims

| Claim | Verdict |
|---|---|
| `Scribe_Deep` writes `Class` only when the runtime type differs from the declared type | **Confirmed.** `Scribe_Deep.Look` writes when `target.GetType() != typeof(T) \|\| typeof(T).IsGenericTypeDefinition`; the value comes from `GetTypeNameWithoutIgnoredNamespaces` |
| Comps, Properties, extensions and Workers are not type-named in saves | **Confirmed** for comps (`InitializeComps` uses `def.comps`) |
| Unknown GameComponent/MapComponent class → two errors, element dropped, fresh instance | **Confirmed** (§2, §5) |
| Thing, Hediff, Ability, Thought → one error per instance, built from the Def's class, data kept when labels are unchanged | **Confirmed** (`GetBestFallbackType` L155-L190) |
| `conversionChain` is private and has no registration API | **Confirmed** |
| `GetBackCompatibleType` and `BackCompatibleDefName` skip all converters when the build and the packageId list are identical | **Confirmed, with additions.** The list comparison is **order-sensitive** and also accepts a folder-name match. `PostExposeData`, `PostCouldntLoadDef` and the body-part index are also skipped. **`PreLoadSavegame` and `PostLoadSavegame` are not skipped**, and neither is `GetBackCompatibleTypeDirect` |
| `GetBackCompatibleType`'s only caller is `SaveableFromNode`; the mapped type must be assignable to `baseType` | **Confirmed.** There is no check; a mismatch causes an `InvalidCastException`, which is caught and loses the object |
| XML loads before `[StaticConstructorOnStartup]`, so a `GetTypeInAnyAssembly` patch must be applied in the Mod constructor | **Confirmed** (`LoadAllActiveMods`: `CreateModClasses` before `LoadModXML`/`ParseAndProcessXML`) |
| VEF 1.7 = "reflection to add converters + the two patches" | **Confirmed, refined:** one converter, plus `RemovedDefs` injection, a Prefix on `GetBackCompatibleType`, a Postfix on `BackCompatibleDefName`, both active only when the environment is identical, applied late through `ExecuteWhenFinished` |
| Doc's untested remedy: "Postfix the two methods when the vanilla result is null" (UNVERIFIED in the doc) | **Mechanically sound, but incomplete.** It misses label and enum renames, defNames that still exist or are reused, string-stored defNames, index-based state and the settings file. See §10 |

---

## 10. RECOMMENDED design: the Mugirl unified save upgrader

Principle: **one deterministic DOM transform before Scribe reads anything**. This does not depend on the identical-environment flag or on JIT visibility, and it can be unit-tested on real save files. It is complemented by thin, always-safe runtime fallbacks for documents the DOM pass never sees, plus explicit post-load phases. Domain code only knows the new schema.

### (i) Pre-read XML upgrade of the save document

- **Hook.** Harmony **Prefix on `Verse.Game.LoadGame()`** with `[HarmonyPriority(Priority.First)]` and a **Finalizer** that always clears the session.
  - The single caller is `SavedGameLoaderNow`, so this hook fires only for savegames.
  - At this point the DOM is complete and unread: `Scribe.loader.curXmlParent` is `<game>`, and `curXmlParent.OwnerDocument.DocumentElement` is `<savegame>`.
  - The meta has been parsed, `DefDatabase` is ready, and `Current.Game` is the real game.
  - `Game.LoadGame` is large and is compiled only on the first load, so it is never inlined before the patch.
  - *Patch-free alternative with identical timing:* append a converter to `conversionChain` by reflection (as VEF does) and override `PreLoadSavegame`. It is not gated by the identical check (`BackCompatibility` L81-L97), but it relies on a private field.
- **Sanity checks:** `Scribe.mode == LoadingVars`, `curXmlParent.Name == "game"`, and `OwnerDocument.DocumentElement.Name == "savegame"`.
- **Mugirl detection:** `ScribeMetaHeaderUtility.loadedModIdsList` contains `har.mugirlrace` or `har.mugirlrace_steam` (ordinal, case-insensitive). Copy the list, the game version and its major/minor/build into a session object **now**.
- **Schema detection:** `game/components/li` whose child element is the stamp label (e.g. `<mugirlSaveSchema>N</mugirlSaveSchema>`).
  - Search **by the child label, not by the `Class` value**. That way, renaming the stamp component later does not break detection.
  - Absent stamp with Mugirl present → schema 0 ("legacy"). Schema 0 is the union of 1.6-era and pre-refactor-1.7 names; the old tree kept all save names frozen. If the two eras ever differ, use the meta `gameVersion` (1.6 against 1.7) as a sub-discriminator.
  - Stamp greater than the current schema → a save from a newer Mugirl. Do not touch it; log one warning.
- **Transform:** run the ordered migration steps `N → N+1 → … → Current`. Each step is a pure `XmlDocument` → report function, written to be idempotent.
- **Guarantees:**
  - Never insert comments or text nodes.
  - Never set `InnerText` on non-leaf elements.
  - Rename an element by creating a new element, moving its attributes and children, then `ReplaceChild`.
  - Traverse once with `FirstChild`/`NextSibling` (avoid XPath and `ChildNodes[i]`); collect edits first, then apply them.
- **Error handling:** catch everything inside the hook. On failure, log a single error with the step ID and leave the DOM as it is. Never let an exception escape into `SavedGameLoaderNow`, because that aborts the load to the main menu.

### (ii) Type remap

- **Primary (DOM):** rewrite every `Class` attribute whose value equals an old full name (case-insensitive, matching `GenTypes`). Also rewrite old names that appear:
  - inside generic `Class` values (`Ns.Gen`1[Ns.Old]`, from `type.ToString()`), using a token-wise replace;
  - in `Type`-valued `Scribe_Values` text, e.g. `TaleData_Def/defType` for a Mugirl Def subclass, and any Mugirl field of type `Type`.
- **Merges and splits** of components or QuestParts are expressed as DOM moves, because the DOM pass knows both sides.
- **Fallback (runtime, all documents):** a Postfix on `BackCompatibility.GetBackCompatibleType`.
  - Act only `if (__result == null)` and the name is in the old→new map, and only `if (baseType.IsAssignableFrom(newType))`.
  - It fires for **every** deep object (`ScribeExtractor` L106-L107), so the first statement must be the `__result != null` return: no allocation, a single dictionary lookup.
  - This covers ScenPart, Precept and other `Class` values inside `.rsc`/`.rid` files, which the save pass never sees.
- **Do not patch `GenTypes.GetTypeInAnyAssembly`** unless third-party XML must keep resolving old Mugirl class names. It is only 82 IL bytes, so it can be inlined into callers that reached Tier 1 before the patch, and it sits on the hot path of all Def loading.
- New namespaces must **never** be `null`/global or one of the ignored namespaces (`GenTypes` L53-L57). Such types resolve by short name with "last writer wins" (L258-L260).

### (iii) defName remap (type-aware)

- **Runtime, all documents:** a Postfix on `BackCompatibility.BackCompatibleDefName(Type defType, string defName, bool forDefInjections, XmlNode node)`.
  - Key: `(DefDatabase root of defType, oldName)` → newName.
  - Override when **either** the save session is legacy (schema < the step that introduced the rename), **or** `GenDefDatabase.GetDefSilentFail(defType, __result, false) == null`. The legacy-session condition is required because vanilla returns the old name unconverted at L123 whenever a Def with that name exists, e.g. when old names are reused for different new Defs. Simpler: forbid reusing old names.
  - This covers:
    - `Scribe_Defs`;
    - Def lists and dictionaries;
    - fallback-type lookups;
    - DefInjected keys (`forDefInjections`);
    - **player xenotype files (gene defNames)**, ideo files and scenario files.
  - IL size is 168 bytes with a loop and exception handling, so it is not an inlining risk. Patch it in the Mod constructor anyway.
- **Deleted Defs:** map them to a replacement. Alternatively, add `(name, type)` to `RemovedDefs` by reflection (VEF's trick) so `DefFromNode` stays quiet.
  - Then remove dependents in the DOM pass: a Thing whose def is gone NREs in `Thing.ExposeData` and is lost noisily.
  - Remember any ID references to removed objects.
- **The DOM pass must also handle** what `BackCompatibleDefName` never sees:
  - defNames stored as plain strings (`Scribe_Values<string>`, string-keyed dictionaries);
  - `DefMap` positional data;
  - short-hash grids.

  The DOM pass also needs the defName map itself to resolve `<def>` text when scoping comp labels (§4).

### (iv) Scribe label and enum renames inside typed owners

- **DOM only**; domain `ExposeData` never reads legacy labels. Rules are declared as `(owner selector) → {oldLabel → newLabel, label → enum value map, label → materialize old default}`. Owner selectors:
  - `Class` attribute (after remap) equals type X. This matches subclasses too only if listed explicitly; inherited labels share the element.
  - **Thing comp:** the element has a `<def>` that, after remap, names a ThingDef whose `comps` contain `compClass == X`.
  - **Hediff comp:** a `HediffDef` whose `comps` contain X; the elements live under `healthTracker/hediffSet/hediffs/li`.
  - **Path from a typed owner** for nested `Scribe_Deep` objects saved without `Class`, because the declared type equals the runtime type.
- **Enum values:** replace the text of the targeted label. `[Flags]` enums are saved as `"A, B"`; split them, map each member and re-join.
  - A Postfix on `BackCompatibility.BackCompatibleEnum` can serve as a fallback for settings and side documents only. It is 60 IL bytes, so it is an inlining candidate in callers that reached Tier 1 early.
  - A failed enum parse makes `ValueFromNode` log an error and return **`default(T)`**, not the declared default.
- **Defaults:** `Scribe_Values.Look` omits values equal to the default (0x06002742). A label that is absent therefore means the *old* default. If the refactor changes a default, the step must **materialize the old default** explicitly.
- **Reference labels** (`Scribe_References`) are path-keyed at LoadingVars and resolved by the same label later. A DOM rename handles them as well.

### (v) Post-load semantic steps and schema stamping

Phases are gated by the static session object set in (i) and cleared by the Finalizer on `Game.LoadGame`:

| Phase | Hook | Guaranteed state |
|---|---|---|
| A. AfterResolve | Postfix on `ScribeLoader.FinalizeLoading` (instance; also used by settings/ideo/… loads, so gate on the session) | Every object constructed, cross-refs resolved, PostLoadInit done. Nothing spawned. Map, MapComponent and GameComponent FinalizeInit have **not** run. WorldComponent `FinalizeInit` **has** run, before cross-refs. **Use for object-graph fixes** that FinalizeInit caches depend on, such as ending in-progress Mugirl jobs or lords whose toil indices changed. Vanilla precedent: `EndJobWith(Incompletable)` in `BiosculpterReworkBackCompat` |
| B. AfterSpawn | Prefix on `Game.FinalizeInit` (gate on the session; it is also called by `InitNewGame`) | Maps spawned, `Map.FinalizeInit` and MapComponent `FinalizeInit` have run. GameComponent `FinalizeInit` has not |
| C. AfterLoaded | Postfix on `Game.LoadGame`, or `BackCompatibility.PostLoadSavegame`, or the reflected converter's `PostLoadSavegame` override | After `GameComponent.LoadedGame`. For player-facing messages or letters, use `LongEventHandler.ExecuteWhenFinished` (main thread) |

- **Stamp:** a `MugirlSaveSchema` GameComponent.
  - Constructor `(Game)`. The schema field defaults to 0.
  - `ExposeData`: on Saving, always write `CurrentSchema` under the stamp label, plus diagnostics such as the source game version and the list of applied step IDs. On loading, read the stored value only for diagnostics.
  - No explicit "set on success" step is needed: whatever is saved after an upgrade was written by new-schema code.
  - If the upgrade fails mid-way, log the failure. The DOM pass is deterministic, so the next load retries it, unless the player saved in between.
- **Log** one summary line per upgraded load (steps and counts).

### (vi) A save that never had Mugirl

- `modIds` lacks `har.mugirlrace(_steam)`. Skip the DOM pass and run no phases. The runtime fallbacks are inert: they act only on names that fail to resolve and appear in the map.
- The stamp component is created by `FillComponents`. Its `ExposeData` is not called during this load, so it saves `CurrentSchema` on the first save.
- A save that had Mugirl removed, was re-saved, and then got Mugirl back has nothing Mugirl-specific left (vanilla dropped it with errors). Treat it like the case above.

### Testing hook

The DOM pass is a pure function, so it can run in `Source/Tests` on real 1.6 and pre-refactor-1.7 saves. Assertions:
- every `Class` resolves, using the existing `Source/Tests/MugirlRace.Validation/Content/TypeResolver.cs`, a replica of `GenTypes`;
- every Mugirl `<def>` text exists in the new `DefIndex`;
- no legacy label or enum text remains;
- no comment or text node was introduced.

The in-game check is then one load and one save per fixture.

---

## 11. Pitfalls (all found in code)

1. **The identical-environment skip** (`BackCompatibility` L479-L498) disables converter-based type and defName mapping exactly when a player updates only the mod. The order-sensitive mod list and RimWorld hotfixes make it flip unpredictably. Do not rely on converters, except for their `Pre/PostLoadSavegame`.
2. **`GetBackCompatibleType` runs for every deep object**, including those without `Class` (`ScribeExtractor` L106). Postfix cost multiplies by the object count.
3. **No assignability check** before `(T)exposable` (L144): a wrong remap loses the object through `InvalidCastException`.
4. **`BackCompatibleDefName` never converts a name that still exists** (L123). Reusing old names for new Defs makes a postfix gated on "not found" silently wrong.
5. **Comments or text nodes inside list containers** cause an NRE at `SaveableFromNode` L95, outside its try. The enclosing deep object is lost. `Scribe_Collections` iterates `ChildNodes` (0x0600271F) and passes every node, element or not, to `SaveableFromNode`.
6. **Comp labels share a flat namespace** with Thing, Pawn and other comps (§4). Renames must be def-scoped, and new labels must not collide with vanilla labels (`story`, `health`, `def`, …).
7. **Absent label means the old default** (`Scribe_Values` omits defaults). Changing defaults silently changes old saves unless the step materializes the old value.
8. **Enum parse failure yields `default(T)`** (the 0 member) with one error, not the declared default.
9. **Index-based state:** JobDriver `curToilIndex`, Lord graph indices, `DefMap` positions and short hashes (§4).
10. **`GetUniqueLoadID` formats are part of the save format** (`LoadedObjectDirectory.RegisterLoaded`). Changing them orphans every reference; freeze them or rewrite all references in the DOM.
11. **New components are never exposed during the load that creates them.** Initializers must not imply "current schema". Components need a public `(Game)`, `(Map)` or `(World)` constructor, or `FillComponents` errors.
12. **`WorldComponent.FinalizeInit(fromLoad)` runs before cross-refs**, and MapComponent `FinalizeInit` runs before GameComponent `FinalizeInit` and `LoadedGame`. Upgrades that domain FinalizeInit depends on must happen in phase A.
13. **PostLoadInit order is a HashSet iteration order** (`PostLoadIniter` L8); never depend on it.
14. **`ScribeMetaHeaderUtility` statics are global and overwritten** by any later header load (ideo, xenotype, scenario, modlist, camera files, `Dialog_ModList_Load`). Snapshot them in the hook.
15. **`LoadGameDataHeader(Map)` runs twice per load** (the header-only pre-check, then the real one). Do not use it as a once-per-load trigger.
16. **Worker thread.** Load hooks are off the main thread, so no Unity or UI work there.
17. **Never call `InitLoading`/`GetSettings` inside load hooks.** `InitLoading` while `Scribe.mode != Inactive` force-stops the running load (`ScribeLoader` L21-L25).
18. **JIT inlining (migration doc E39).**
    - Small targets can be inlined into callers that reached Tier 1 before the patch: `BackCompatibleEnum` (60 IL bytes) and `GenTypes.GetTypeInAnyAssembly` (82).
    - `GetBackCompatibleType` and `BackCompatibleDefName` (168 each, with loops and exception handling) and `PreLoadSavegame` (125) are poor inlining candidates. `Game.LoadGame` is compiled only at the first load.
    - Apply all patches in the Mod constructor. The claim that RyuJIT's default inline limit is about 100 IL bytes, and that it applies to the Waylith runtime, is **[UNVERIFIED]**.
19. **Settings:** the file name depends on the Mod subclass short name and the folder name. The settings Class name only causes an error per startup. Mugirl reads settings before patching, so migrate the file textually.
20. **Third-party documents.** Player `.xtp` files contain gene defNames; `.rid`/`.rsc` files contain Def names and possibly classes. Only the runtime fallbacks reach them. DefInjected translation packs from third parties also need the `forDefInjections` path. **The following original conclusion is superseded by E53 and §13:** `TryAddInjection` converts the path before `SetDefFieldAtPath` reads it.
    - `DefInjectionPackage.SetDefFieldAtPath` (0x06000F41) converts the name for its existence check, but then looks up `list2[0]`, the *unconverted* path segment.
    - So third-party translations for renamed Defs still fail ("Def named X not found" load error). They cannot be fully back-compat'ed.
21. **Interplay with VEF.** If VEF is loaded, its converter is appended to the same chain. For types, the first non-null result wins; for defNames, results chain. Keep Mugirl's maps disjoint and do not depend on VEF's `DefMigrationDef`, which would add a hard dependency.

---

## 12. Findings worth adding to the migration knowledge base (for the orchestrator; I wrote nothing there)

- 1.6.9676 and 1.7.4977 have the same save-loading code paths. There is no `BackCompatibilityConverter_1_6`, so for 1.6 saves only `Universal` runs.
- `PreLoadSavegame` and `PostLoadSavegame` are **not** gated by the identical-environment check. A reflected converter therefore gets reliable savegame-only pre- and post-hooks without Harmony.
- `GetBackCompatibleType` runs for every deep object; there is no assignability check (L144); and there is an NRE pitfall at L95 for non-element nodes.
- The mod-list comparison is order-sensitive.
- The settings file name uses the Mod subclass **short** name and `FolderName`. The namespace only affects the Class attribute: data is kept, with one error per startup. This corrects the comment in `MugirlMod.cs` L5.
- `BackCompatibleDefName` has six callers, including DefInjected; `SetDefFieldAtPath` uses the unconverted name in its second lookup.
- Index-based save state is a hidden refactor hazard: JobDriver toil index, Lord graph indices, `DefMap` and short hashes.
- VEF's actual mechanism: one converter plus `RemovedDefs` injection, a Prefix on `GetBackCompatibleType`, a Postfix on `BackCompatibleDefName`, active only when the environment is identical, applied through `ExecuteWhenFinished`.

## 13. 1.7.4979 rev109 直接复核（2026-10-15，恢复任务）

证据见迁移知识库 [E57–E59](../../../../1.7迁移文档/资料来源与核验记录.md) 与[符号/源码哈希索引](../../../../1.7迁移文档/evidence/核验索引.json)。当前 Assembly-CSharp MVID 为 `425a593954d7432a8f44e3076b25fce9`。本轮服务初始没有旧上下文，4977 文件也已被更新覆盖；这是对当前版本的直接复读，不能表述为新增的两个版本逐方法相等证明。

- 已覆盖交接要求的 GenTypes null 缓存、BackCompatibility 门禁、PostLoadIniter、Map/World 组件初始化、Thing ID 解析、LoadedObjectDirectory、JobDriver/JobQueue、设置读取、隐含 Def 生成器及 DefAlias。未发现这些入口的原有关键结论失效。
- `DefOfHelper.BindDefsFor` 读取字段上的 `DefAliasAttribute.defName`，同名不同根的 DefOf 字段可以使用显式别名。
- `JobQueue` 的加载清理不容许列表项对象本身为 null；删除旧队列项时应删除整个元素，不能留下 null 的 `li`。
- 无法解析 `lastJobGiverKey` 时，当前 `jobGiver` 保持 null；下一次保存才写默认 -1 并省略标签。测试只有拿到对应解析失败证据时才能解释这个差异。
- 异常尸体的隐含名称是 `UnnaturalCorpse_` + 种族 defName，受 Anomaly 与 `hasUnnaturalCorpse` 控制；升级表原来遗漏这一项，已补充改名、ID/引用和删除测试。
- `DirectXmlLoader.XmlAssetsInModFolder` 使用 `DirectoryInfo.GetFiles("*.xml", AllDirectories)`，没有排序；工作线程按原索引填回结果。XML 文件拆分/移动不能仅凭文件名编号保证加载顺序。本 Mod 的服装候选会按 ThingDef 顺序消耗随机数，因此保持原加载文件及元素顺序是行为一致性的约束。
- `AllSubclassesNonAbstract` 与 `AllTypesWithAttribute` 使用未声明有序的 PLINQ；应分别验证程序集元数据相对顺序和实机可见结果，不能称编译项顺序本身保证引擎运行时顺序。不得为此修改全局 GenTypes 排序。
- DefInjected 的旧名字在 `TryAddInjection` 提前转换，§11 第20条“无法兼容”的原结论不成立；运行时改名后仍需真实注入断言。
