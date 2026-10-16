# Wave S plan — module `core`

Scope: `Source/MugirlRace/{Core, Compatibility/**, Platform, Mathematics, Rendering, GlobalUsings.cs, MugirlRace.csproj}`, the former
`DefOf/`, `UI/`, `Features/Misc/RenderRefresh`, `Features/Incidents/VanillaFixes`, the shared helpers moved to Core, mod settings,
the lifecycle hub naming (design §5.8) and the Compatibility side of the extension points.
Companions: `spec/core.json` (type/def/key renames), `spec/core-member-moves.json` (DefOf split, static-member moves, `PatchModule` rewrite).
Paths are repo-relative; `S/` = `Source/MugirlRace/`. Line numbers are the 2026-10-15 working tree (pre-Wave-R names), verified by reading.
Engine facts: DecompilerServer `rw17-game` (1.7.4977). Inventory abbreviations: MC = `understand/module-core.md`, RS = `understand/real-saves.md`,
EH = `understand/engine-hooks.md`, CT = `understand/content.md`, MI = `understand/module-incidents.md`, CR = `spec/critique-upgrader.md`.

---------------------------------------------------------------------------------------------------------------------
## S1. Legacy code removal

| # | Legacy site (pre-Wave-R) | Legacy XML shapes handled (evidence) | Replacement mechanism (SaveUpgrade) | Domain code deleted |
|---|---|---|---|---|
| L1 | `S/Compatibility/Savegames/CorporateValidationSaveMigration.cs:8-52` (Prefix on `Game.ExposeSmallComponents`, class names :12-13) | `<game><components><li Class="Mugirl.CorporateRuntimeValidation">…</li>` and `…"Mugirl.CorporateVisualValidation"…` — components registered by validation-only builds (`e93677d`, MC A1). **No real save contains them** (RS:214). | **Type map Drop** in `LegacyTypes` (policy *RemoveElement*: delete the `li` from `game/components`). Both names go into the corpus `legacy-extra.txt` (they never shipped in a release DLL, design §6). DOM step 1 (type renames/drops on every `Class`). Port the golden cases of `Source/Tests/MugirlRace.Tests/Compatibility/CorporateValidationSaveMigrationTests.cs` to a step test. | The class, its test file, `[MugirlPatch("SaveCompatibility",…)]`, baseline rows. |
| L2 | `S/Compatibility/Savegames/NeuralShoulderSaveMigration.cs:7-213` (constants :11-12, reference rule :179-185) | Things `li`/`thing` with `<def>Mugirl_NeuralCombatShoulders</def>` **and** `<id>` (also nested, e.g. `apparel/wornApparel/innerList/li`); bills `li` with `<recipe>Make_Mugirl_NeuralCombatShoulders</recipe>` + `<loadID>`; leaf references `<id>` text, `Thing_<id>`, `Bill_Make_Mugirl_NeuralCombatShoulders_<loadID>`, and the def/recipe names in `li`/`def`/`thingDef`/`recipe`. Def existed only in 0.6.3 `a172c15` (MC A2). **No real save** (RS:212). | **Def map Drop** entries in `LegacyDefs`: `(ThingDef, Mugirl_NeuralCombatShoulders) → Drop(RemoveThingsAndReferences)`, `(RecipeDef, Make_Mugirl_NeuralCombatShoulders) → Drop(RemoveBillsAndReferences)` — executed by the generic def-drop machinery of DOM step 2 (design §5.3.2). Two defects of the old code must NOT be carried over: (a) leaf `li` removal under a Scribe dictionary must remove the `keys/li` + `values/li` pair together (EH/MC D3 "Keys count does not match"); (b) references in fields outside `li/def/thingDef/recipe` (e.g. Ideology `apparelDef`) are covered by the BackCompatibleDefName/WasDefRemoved fallback (design §5.2.3). Port `NeuralShoulderSaveMigrationTests.cs` golden XML + add a dictionary-pair case and an idempotence case. | The class, its test, baseline rows. |
| L3 | `S/Compatibility/Savegames/IncidentWorker_RetiredMugirlEvent.cs:5-9`; `1.7/Defs/Compatibility/RetiredEvents.xml:3-13`; `1.7/Languages/ChineseSimplified/DefInjected/IncidentDef/Incidents_Mugirl.xml:8` | IncidentDef `Mugirl_WildManWandersIn` (active 0.4.7…`50f7979`, retired in the frozen 1.6 working tree, MC A3). Vanilla shapes (rw17 decompile): `StoryState.ExposeData` → `<recentRandomIncidents><li>Mugirl_WildManWandersIn</li>` (LookMode.Def) and `<lastFireTicks><keys><li>Mugirl_WildManWandersIn</li></keys><values><li>N</li></values>` (Dictionary<IncidentDef,int>); `StoryState` is saved under `storyState` by `Map`, `World`, `Caravan`, `Gravship`; `Storyteller.ExposeData` → `incidentQueue` (Deep) → `queuedIncidents/li` → `firingInc` (Deep) → `<def>`. **No real save contains it** (RS:213). MooGirl-era twin `MooGirl_WildManWandersIn` (CT A.7). | **Def map Drop** `(IncidentDef, Mugirl_WildManWandersIn)` and `(IncidentDef, MooGirl_WildManWandersIn)` + **DOM structural step** `Steps/Core/RemoveRetiredIncidentReferences` (consumes every IncidentDef Drop entry): remove `storyState/recentRandomIncidents/li` items, remove `storyState/lastFireTicks` pairs index-wise, remove whole `storyteller/incidentQueue/queuedIncidents/li` whose `firingInc/def` matches (nulling `def` would leave a `FiringIncident` with a null def — not acceptable). A `WasDefRemoved` fallback alone is insufficient (dictionary "Null key", MC A3). Step test with all three shapes + idempotence. | Worker class, `1.7/Defs/Compatibility/` folder, the DefInjected line (incidents-owned file, S4), `docs/content-update-guide.md:79` paragraph (Wave D). |
| L4 | Settings contract comments `S/Core/MugirlMod.cs:5` (wrong: namespace does not matter, EH §6), `S/Core/MugirlSettings.cs:3` ("类名与 Scribe 标签…不能改名") | `Config/Mod_<folder>_MugirlMod.xml`: `<SettingsBlock><ModSettings Class="Mugirl.MugirlSettings"><enableExtraOutline>True</enableExtraOutline></ModSettings></SettingsBlock>` (real file `%LocalLow%/…/Config/Mod_mugirl_MugirlMod.xml`); folder is `mugirl` locally or `3578882219` for the workshop copy. | **SettingsUpgrade** (design §5.7) with the core table of S2-2. Not part of the save map. | Both freeze comments. |
| L5 | `S/Features/Misc/RenderRefresh/GameComponent_GhoulRenderingRefresh.cs:6,9` (freeze comment + name) | `<li Class="Mugirl.GameComponent_GhoulRenderingRefresh" />`, empty element, every Mugirl-era real save S5–S11 (RS:44, :82). | **Type map rename** → `Mugirl.Rendering.GameComponent_PawnRenderRefresh` (generated from core.json). | Freeze comment; no-op `ClearPending` + `GhoulRenderingRefreshUtility` (S2-7). |
| L6 | none in code (MooGirl era) | `<li Class="MooGirl.MooGirl_ConvertComp"><conversionDone>True</conversionDone></li>` (S1–S4), `<li Class="MooGirlSkinFix.GameComponent_MooGirlSkinOnce" />` (S2–S4), `<li Class="MooGirl.AdultContentCleanupGameComponent" />` (S3, S4) — RS:31-34, :73-77, :102, :125. No successor exists. | **Type map Drop(RemoveElement)** for these three (proposal S5-Q6: core owns MooGirl-era components without successor; `MooGirl_GameComp` → incidents, `MooGirl_XenotypeFix_GameComp` → xenotype). | — |
| L7 | none in code (MooGirl-era settings) | `Mod_<folder>_MooGirlMod.xml` with `Class="MooGirl.MooGirlSettings"` (labels `enableStructuralCrashEvent`, `enableFastMilking`, git `cb25751:1.6/Source/Core/MooGirlMod.cs:31-44`) or `Class="MooGirl.MechanoidWorkControlSettings"` (`enableAdultContent`), real files in the owner's Config folder. | **Drop (no import)** — proposal S5-Q4: 0.4.7 already reset these settings once; importing would resurrect stale values. Files are left untouched. | — |
| L8 | Patch-registry legacy keys | `Mugirl.PatchRegistry.{NullHarmony,PawnGeneratorGeneratePawn,MissingTarget,MissingPatchMethod,PatchFailed}` and `Mugirl.Settings.FusionInvestmentEvent.DevDeliver` (CT A.6.2; no reader). | **Drop** (content only). | Keys in EN + ZH (S4 if Misc_Gameplay is not split in Wave R). |
| L9 | History/"same as 1.6" comments: the 70 lines found by `grep -n "1\.6\|旧版\|旧档\|不能改名\|照搬"` over the core scope (e.g. `S/Core/HarmonyBootstrap.cs:8,28`, `S/Core/MugirlPatchAttribute.cs:13`, `S/Core/MugirlTickUtility.cs:5-6,60`, `S/Compatibility/ModCompat.cs:7-9,33`, `CompatAccessors.cs:12,105`, `RunAndGun/RunAndGunCompatibility.cs:11,27,47,70`, `YayoCombat/YayoCombatCompatibility.cs:11,18,109`, `MeleeAnimation/MeleeAnimationPatches.cs:62-63`, `YayoAnimation/YayoAnimationPatches.cs:17`, `AlienRace/Harmony_StylingStationRefresh.cs:19,102`, `Rendering/PawnEffects/*` (ExtraOutlineGeometry.cs:4,9,18; LanceMotionBlurGeometry.cs:4,9,12; PawnLayerQuad.cs:5,12,15,19; …), `Mathematics/*`, `Platform/Ui/*`, `1.7/Defs/ShaderDefs/Mugirl_Shaders.xml:6-7`) | — | **drop** (directive 4, §3.5): rewrite as the *current* reason (e.g. "Mono float→int semantics of UnityEngine.Mathf.RoundToInt" instead of "保留 1.6 的…"); behaviour unchanged. | — |
| L10 | Validator freeze rows for this scope: `Source/Tests/MugirlRace.Validation/Baselines/save-types.txt:7,12,13` (`GameComponent_GhoulRenderingRefresh`, `Mugirl.MugirlMod`, `Mugirl.MugirlSettings`), ADR-004 | — | Replaced by `C.save-upgrade` (A2) | Rows deleted by A2/Wave R; ADR-004 superseded in Wave D. |

Not legacy (kept, documented): Harmony id `MugirlMod.Mod` (`S/Core/HarmonyBootstrap.cs:29`, design §2); `MugirlIdentity.IsMugirlPawn = race Mugirl ∨ body MugirlBody` (`S/Core/MugirlIdentity.cs:28-33`) — this is the live "Mugirl-like race" semantics (subspecies races inherit `MugirlBody`, CT §C), only its comment is rewritten; third-party version shims (RunAndGun kotobike, S5-Q5).

---------------------------------------------------------------------------------------------------------------------
## S2. Structural refactors

### S2-1 Lifecycle hub and per-game registry — MUST (implemented by A1, names/contract owned here)
- Files: `S/Core/Lifecycle/GameComponent_Mugirl.cs`, `GameLifecycle.cs`, `GameLifecyclePhase.cs`, `GameLifecycleHookAttribute.cs`; `S/Core/Lifecycle/Harmony_Game_InitNewGame_Lifecycle.cs`.
- Design:
  - `public sealed class Mugirl.Core.GameComponent_Mugirl : GameComponent` — name frozen forever. Only a public `(Game)` ctor; the ctor is trivial (no statics, no Defs, cannot throw: `Game..ctor` constructs every GameComponent 2–3× per load, CR #13/#6).
  - Writes `mugirlSaveSchema` with `forceSave: true` (CR #6); writes `SaveSchema.Current` only for a new game or when the upgrade session for this game completed (A1 policy). Holds the last upgrade diagnostics (S3).
  - `internal enum GameLifecyclePhase { GameStarting, FinalizeInit, StartedNewGame, LoadedGame }`. `GameStarting` = before a game is created or deserialised: raised as the **first statement of the SaveUpgrade `Game.LoadGame` prefix** (A1) and by a Core prefix on `Game.InitNewGame`; the three others are raised by the hub's overrides (vanilla order: FinalizeInit before StartedNewGame/LoadedGame, MI R2).
  - `[AttributeUsage(Method)] internal sealed class GameLifecycleHookAttribute(GameLifecyclePhase phase)` on `internal static void M()` methods. `internal static class GameLifecycle` discovers them once (reusing the HarmonyBootstrap type scan), sorts ordinally by `DeclaringType.FullName + "." + Name`, invokes each in try/catch (`MugirlLog.ExceptionOnce`). Hooks must not depend on each other.
  - Replaces `MugirlStoryState.ResetTransientRuntimeState` (`S/Features/Incidents/Story/MugirlStoryState.cs:71-80`): every reset keeps **FinalizeInit** timing (behaviour unchanged); `GameStarting` is available for state that must not leak into the next game's ExposeData.
  - Core's own hook: `MugirlLog.ResetOnceWarnings` (`S/Core/MugirlLog.cs:120`, comment :19) gets `[GameLifecycleHook(FinalizeInit)]`.
- Save schema: new element `game/components/li[@Class="Mugirl.Core.GameComponent_Mugirl"]` (S3). Old saves: the component is created by `FillComponents` and never exposed during that load (EH §5) → fields stay at "unknown" defaults. No upgrade step.
- Behaviour: none. Tests: unit — discovery finds only valid signatures, rejects instance/parameterised methods, deterministic order, one throwing hook does not stop the others; in-game — new game → save → `mugirlSaveSchema` present; FinalizeInit hooks run exactly once per game. Risk: medium (touches every module's reset; A1 + S4 coordination).

### S2-2 Mod settings — MUST
- Files: `S/Core/Settings/MugirlSettings.cs`, `S/Core/Settings/SettingsWindow.cs`, `S/Core/MugirlMod.cs`, table `S/SaveUpgrade/LegacyNames/Core/LegacySettings.cs` (location per S5-Q3; consumed by A1's `SettingsUpgrade`).
- Final names: Mod class `Mugirl.Core.MugirlMod` (short name frozen, design §2); settings `Mugirl.Core.MugirlSettings`; window `Mugirl.Core.SettingsWindow`; file `Config/Mod_<FolderName>_MugirlMod.xml` unchanged.
- Settings schema change (owner = `SettingsBlock/ModSettings`):

  | old label | new label | type | default (old = new) |
  |---|---|---|---|
  | `enableStructuralCrashEvent` | `enableOpeningStoryline` | bool | true |
  | `enableMugirlMigrationEvent` | `enableMigrationEvent` | bool | true |
  | `enableMugirlFusionInvestmentEvent` | `enableFusionInvestmentEvent` | bool | true |
  | `enableRunawayMugirlFarmQuest` | `enableRunawayFarmQuest` | bool | true |
  | — (new) | `settingsSchema` | int, `forceSave` | 0 = legacy; written as 1 (CR #18) |
  | `Class="Mugirl.MugirlSettings"` | `Class="Mugirl.Core.MugirlSettings"` | attribute | — |

  Unchanged: `enableFastMilking`, `enableVanillaMilkGauge`, `enableExtraOutline` (bool, false), `extraOutlineWidth` (float, 1.0, clamped 0.25–3 after read), `enableWeaponWheelDevLog`, `hideWeaponWheelBackWeapons` (bool, false). Defaults unchanged → no materialisation.
- Sequencing: the 4 C# field symbols are renamed in Wave R (`core-member-moves.json#memberRenames`, readers live in incidents/corporate); the label strings, `settingsSchema` and the upgrade table change in Wave S inside core-owned files only.
- Upgrade: in the `MugirlMod` ctor before `GetSettings` (`S/Core/MugirlMod.cs:16`, Harmony comes after at :18): parse with `XmlDocument`; if `settingsSchema` < 1 (absent) rename the Class attribute and the 4 labels (keep unknown labels), set `settingsSchema=1`, write temp + replace (CR #18); then `GetSettings`. Idempotent. MooGirl-era files are not read (L7).
- Core no longer depends on Features (design §3.5; today `MugirlSettings.cs:20,39-45` uses `MugirlExtraOutline` constants, `UI/MugirlSettingsWindow.cs:1,57,72,84` uses WeaponWheelDevLog/MugirlExtraOutline):
  - `MugirlSettings.ExtraOutlineDefaultWidth = 1f`, `ExtraOutlineMinWidth = 0.25f`, `ExtraOutlineMaxWidth = 3f` (values from `Features/Appearance/MugirlExtraOutline.cs:14-16`); Appearance reads them.
  - `SettingsWindow.Changed` (`internal static event Action<MugirlSettings>`) raised when edited values are flushed — on left-mouse release while drawing and in `MugirlMod.WriteSettings` (same deferral as today, `UI/MugirlSettingsWindow.cs:63-73,88-98`). Appearance subscribes from a `[StaticConstructorOnStartup]` binding class and refreshes only when its applied outline enable/width differ → identical refresh behaviour.
  - `SettingsWindow.AddDeveloperAction(string labelKey, Func<MugirlSettings,bool> visible, Action action)`; WeaponWheel registers "dump patch report" (visible when `enableWeaponWheelDevLog`) from a `[StaticConstructorOnStartup]` class; drawn at the same place (after the dev-log checkbox).
  - Layout/order of the window unchanged; translation keys per core.json `keyed` (OpeningStoryline).
- Behaviour: none, except the accepted 1.6↔1.7 alternation effect (S5-Q4). Tests: SettingsUpgrade golden files (legacy file → canonical; canonical untouched; unknown labels kept; workshop folder name; second run no-op), `SettingsWindow` change-detection unit (pure). Risk: low.

### S2-3 Extension points replace Compatibility self-patches — MUST (agreed interface; combat module implements the hook side)
Today 10 Harmony classes patch Mugirl's own methods (MC C3-2): `DualWieldPatches.cs:7-116` (4), `RunAndGunPatches.cs:7`, `YayoCombatPatches.cs:7-33` (2), `MeleeAnimationPatches.cs:9-33,63-89` (2), `YayoAnimationPatches.cs:7-46` (Transpiler); plus a Feature→Compatibility dependency (`Features/WeaponWheel/Combat/Comp_WeaponWheel.Combat.cs:90,147,163` call `TrainingFacilityCompatibility`; `Features/WeaponWheel/Dev/WeaponWheelDevLog.cs:213-216` reads four compat flags).

**Agreed interface (owned by the combat module, `Mugirl.Features.WeaponWheel` / `Mugirl.Features.Mounting`):**
```csharp
// S/Features/WeaponWheel/Integration/WeaponWheelHooks.cs   — registration only at startup; arrays are read-only afterwards.
// StaticCacheLifecycle: 进程级；第三方兼容在启动时注册，之后只读。
internal static class WeaponWheelHooks
{
    internal static void RegisterIntegration(string name);                                    // WeaponWheelDevLog lists these
    internal static void AddFullFirepowerVeto(Func<Comp_WeaponWheel, bool> vetoes);           // true = not eligible
    internal static void AddCastPresentationOwner(Func<Comp_WeaponWheel, bool> ownsCast);     // true = wheel passes the cast through untouched
    internal static void AddCombatBypass(Func<Pawn, bool> bypasses);                          // true = wheel does not take over this cast
    internal static void AddForeignEquipmentFilter(Func<ThingWithComps, bool> isForeign);     // true = equipment event ignored by the wheel
    internal static void AddPrimarySwapParticipant(IWeaponWheelPrimarySwapParticipant p);
    internal static void AddPrimaryWeaponChangedListener(Action<Pawn> listener);
    internal static void AddTickListener(Action<Comp_WeaponWheel> listener);                  // every CompTick, after the wheel's own logic
    internal static void AddGeneratedReserveWeaponListener(Action<Pawn?, ThingWithComps> listener);
    internal static void SetDrawPositionProvider(Func<Pawn, UVector3> provider);              // single slot; second set = warning, first wins
}
internal interface IWeaponWheelPrimarySwapParticipant
{
    bool TryBegin(Comp_WeaponWheel wheel, int targetSlot, out object? state);   // false = cancel the swap (method returns false)
    void End(Comp_WeaponWheel wheel, object? state);                            // always called (finally) after a successful TryBegin
}
// S/Features/Mounting/Integration/MountingHooks.cs
internal static class MountingHooks
{
    internal static void AddMeleeWeaponDrawScope(IMountedMeleeWeaponDrawScope scope);
}
internal interface IMountedMeleeWeaponDrawScope
{
    bool Begin(Pawn rider, ThingWithComps weapon);   // called only for a melee primary; returns whether something was suspended
    void End(bool begun);                             // finally; no allocation on the draw path
}
```
**Semantics that keep behaviour identical** (hook site ← current patch):

| Hook site (combat module) | Replaces | Exact rule |
|---|---|---|
| `IsFullFirepowerEligible` (`Comp_WeaponWheel.Combat.cs:39`) end | DualWield + RunAndGun Postfix | if base result true and any veto → false |
| `HandleBeforeTryStartCast` (`:79`) first statement | DualWield Prefix | if any owner → `result=false; return false` before the training-facility check |
| `:90,:147,:163` | direct `TrainingFacilityCompatibility` calls | `WeaponWheelHooks.BypassesCombat(Pawn)` |
| `NotifyExternalEquipmentAdded/Removed` (`Comp_WeaponWheel.Storage.cs:382,413`) entry | DualWield Prefix | non-null equipment that any filter marks foreign → return |
| `MoveActiveWeaponTo(slot)` (`:249`), `NormalizeToPrimarySlot()` (`:301`, targetSlot 0) | DualWield Prefix/Postfix/Finalizer; MeleeAnimation Prefix/Postfix | participants `TryBegin` in registration order (any false → return false, already-begun ones get `End`); body in try/finally → `End`; after a `true` result, if `Pawn.equipment.Primary` changed → changed-listeners |
| `CompTick` (`Comp_WeaponWheel.cs:96`) | YayoCombat Postfix | listeners run after the body on every tick, including the early-return paths (not on exception) |
| `TryAddGeneratedReserveWeapon` (`Storage.cs:113`) entry | YayoCombat Prefix | listeners `(Pawn, weapon)` before the body |
| `WeaponWheelAnimationRenderer.Draw` (`:10,:22`) | YayoAnimation Transpiler | every `pawn.DrawPos` read → `WeaponWheelHooks.PawnDrawPosition(pawn)` (provider or `pawn.DrawPos`) |
| `MountedPawnMeleeSupport.DrawWeapon` (`Mounting/Combat/MountedPawnMeleeSupport.cs:139`) | MeleeAnimation Prefix/Postfix/Finalizer | for a melee primary: `Begin`/`End` around the draw; `[MethodImpl(NoInlining)]` and the comment at :136-138 go away |

**Compatibility side (core):** each `<Mod>Compatibility` class becomes `[StaticConstructorOnStartup]` and registers in its static ctor when `ModCompat.<Mod>` is true (vanilla runs every such ctor isolated in try/catch: rw17 `Verse.StaticConstructorOnStartupUtility.CallAll`; hooks are only consulted in a running game). DualWield: veto, cast owner, foreign filter, swap participant (state = off-hand, replaces `DualWieldOffHandTransferState`); RunAndGun: veto; YayoCombat: tick listener + generated-weapon listener (its 3 vanilla-target patches stay); MeleeAnimation: changed-listener + mounted draw scope (its vanilla `Pawn_DrawTracker.Notify_MeleeAttackOn` patch stays); YayoAnimation: draw-position provider; TrainingFacility: combat bypass. Delete the 10 self-patch classes, `DualWieldPatches.cs`, `RunAndGunPatches.cs`, `YayoAnimationPatches.cs`, the remainder of `YayoCombatPatches.cs`/`MeleeAnimationPatches.cs`.
- Save schema: none. Behaviour: none (dev-log line "Known compatibility layers" lists registered integrations instead of four flags — diagnostic text only). Tests: combat module unit-tests hook composition (veto OR, swap `End` on exception, listener order); core adds a validator request (S4) "no Harmony patch targets a method declared in MugirlRace"; in-game StartupSuite: no patch owned by `MugirlMod.Mod` targets a Mugirl method. Risk: medium (swap path parity) — keep the old patches' unit/in-game scenarios as oracle.

### S2-4 Closed-set patch metadata — MUST (atomic; Wave R tool via `core-member-moves.json#patchModuleRewrite`, else A4)
`MugirlPatchAttribute(PatchModule module, PatchRisk risk, string failureBehavior)`; `PatchModule`/`PatchRisk` in `S/Core/Patching/`. Today 23 free strings (`grep [MugirlPatch("` : WeaponWheel 16, Corporate 15, Incidents 12, … `SaveCompatibility` 2, `Compatibility` 2). Behaviour none; validator `PatchMetadataCheck.cs:13`/`LogCheck.cs:22` adapt. Risk low.

### S2-5 DefOf structure — MUST (done by Wave R from `core-member-moves.json`; Wave S verifies)
17 feature/area DefOfs (`internal static`, public fields, `EnsureInitializedInCtor`), 48 unused fields deleted (MC C3-4), `[DefAlias]` trio deleted. Rule: a field lives in the DefOf of the feature that owns the Def; other features reference that DefOf; vanilla/DLC handles are declared per consuming feature (`ShockwaveFast` twice); race identity + `Mugirl_Milk` in `Mugirl.Core.MugirlDefOf`. Wave S: replace remaining `[MayRequire("Ludeon.RimWorld.Biotech")]` strings by `[MayRequireBiotech]` in core-owned DefOfs only (owners do theirs). Save schema none. Risk low.

### S2-6 Shared helpers in Core — MUST (names owned here; moves done in Wave R)
`Mugirl.Core.PlayerFactionUtility` (`PlayerFaction`, `IsPlayerFaction`, `IsHostileToPlayer`×2, `TrySetPlayerFaction`; source `Features/Incidents/WildSlaves/MugirlWildSlaveUtility.cs:9,48-75`), `Mugirl.Core.PlayerIdeoUtility.AdoptPlayerPrimaryIdeo` (`Features/Incidents/Shared/Mugirl_IdeoUtility.cs:6`), `Mugirl.Core.GeneratedPawnUtility` (`Features/Incidents/Shared/MugirlGeneratedPawnUtility.cs`, used by Corporate ×3 files). Wave S: members `internal static`, comments rewritten (`MugirlWildSlaveUtility.cs:6` "签名保持不变" disappears with the split). Behaviour none. Risk low.

### S2-7 Dead code — MUST (directive 4 "nothing to fault")
Delete (zero callers, verified by grep): `GameUtility.TryRenderPawnPreview` (`S/Core/MugirlGameUtility.cs:38-50`), `GameUtility.WorldPawnsContains` (:370-373); `MugirlLog.Message(string)`/`Message(Func<string>)` (`S/Core/MugirlLog.cs:23,26`); `MugirlIdentity.IsMugirlDef(ThingDef)`, `HasMugirlBody(ThingDef)`, `IsMugirlPawnDef` (`S/Core/MugirlIdentity.cs:13,23,35-38`); `GhoulRenderingRefreshUtility` + `GameComponent_PawnRenderRefresh.ClearPending` + its parameterless ctor (the component is recreated with each `Game`, so the FinalizeInit clear at `MugirlStoryState.cs:79` is a no-op); `LactationExpansionCompatibility` (a 2-line forwarder, `Compatibility/LactationExpansion/LactationExpansionCompatibility.cs:9-14`; patches use `ModCompat.LactationExpansion`/`LactationExpansionHarmonyId`). Behaviour none. Risk low.

### S2-8 Rendering area hygiene — SHOULD
- Split `Harmony_Hediff_PostAdd_RenderRefresh` (`Features/Misc/RenderRefresh/Harmony_Hediff_PostAdd_RenderRefresh.cs:19-27`): Rendering keeps the Ghoul branch; the pregnancy branch moves to an Apparel-owned `Harmony_Hediff_PostAdd_PregnantApparel` (S4) so Rendering does not depend on a Feature. Behaviour none (two postfixes instead of one on `Hediff.PostAdd`, negligible).
- `RenderingDefOf` gains `Ghoul` with `[MayRequireAnomaly]` (from `MugirlMiscDefOf`).
- Test oracle `Source/Tests/MugirlRace.Tests/Rendering/Legacy16PawnEffects.cs`: rename to `ReferencePawnEffects` (it is a parity oracle, not legacy support). Risk low.

### S2-9 Visibility — SHOULD
41 public top-level types in scope (MC C3-8). Make `internal`: all `Mugirl.Platform.*`, `Mugirl.Mathematics.*`, `VfxDraw`, `GuiPhase`, `MugirlText`, `HarmonyBootstrap`, DefOfs, compat classes. Keep `public`: `MugirlMod`, `MugirlSettings` (engine-instantiated), `GameComponent_Mugirl`, `GameComponent_PawnRenderRefresh`. Add `InternalsVisibleTo` for `MugirlRace.GameValidation` (compiles against `HarmonyBootstrap.PatchClasses`, StartupSuite.cs:98) and drop the "shape cannot change" note at `S/Core/HarmonyBootstrap.cs:23`. `sealed` on leaves. Behaviour none. Risk medium (test projects; S5-Q8).

### S2-10 External breast hediffs — SHOULD
`Compatibility/ExternalHediffs/ExternalBreastHediffDefs.cs:6-11` admits unverified packageIds and uses `GetNamedSilentFail`. Verify the providing mods (RJW for HugeBreasts/BionicBreasts/…, Genetic Rim for `GR_MuffaloMammaries`) and convert to a `[DefOf]` with `[MayRequire(<packageId>)]` per field, keeping field names (Milk's `MugirlBreastProfileUtility.cs:17-26` unchanged). If a packageId cannot be verified, keep the lookup and its `dynamic-lookups.allow.txt` justification. Behaviour none. Risk low-medium.

### S2-11 Comments — MUST
All L9 lines rewritten to current reasons; `StaticCacheLifecycle` comments updated to the lifecycle hook (`MugirlLog.cs:19`).

---------------------------------------------------------------------------------------------------------------------
## S3. New save schema of the module (after Wave S)

| Owner (save element) | Label | Kind | Type | Default |
|---|---|---|---|---|
| `game/components/li[@Class="Mugirl.Core.GameComponent_Mugirl"]` (type name frozen forever) | `mugirlSaveSchema` (frozen forever) | Scribe_Values, `forceSave: true` | int | 0 (= unknown; never "current") |
| same | `lastUpgrade` | Scribe_Deep (declared type == runtime type → no `Class`) | `Mugirl.SaveUpgrade.SaveUpgradeRecord` (A1) | null (game never upgraded) |
| `…/lastUpgrade` | `fromSchema` | Scribe_Values | int | 0 |
| `…/lastUpgrade` | `toSchema` | Scribe_Values | int | 0 |
| `…/lastUpgrade` | `stepIds` | Scribe_Collections, LookMode.Value | List<string> | null |
| `…/lastUpgrade` | `gameVersion` | Scribe_Values | string | null |
| `game/components/li[@Class="Mugirl.Rendering.GameComponent_PawnRenderRefresh"]` | — (empty element; schedule is not saved) | — | — | — |

Not a save, listed for the settings validator: `SettingsBlock/ModSettings[@Class="Mugirl.Core.MugirlSettings"]` with the labels of S2-2 (`settingsSchema` int forceSave 1; 9 bools/1 float as tabled).
No enums, no Def references, no load IDs are saved by this module. `PatchModule`/`PatchRisk` are metadata only.

---------------------------------------------------------------------------------------------------------------------
## S4. Shared-file edits requested from other owners

1. **incidents** — `Features/Incidents/Story/MugirlStoryState.cs:71-80`: delete `ResetTransientRuntimeState` (each callee becomes a `[GameLifecycleHook(FinalizeInit)]` method in its owner; `MugirlLog.ResetOnceWarnings` by core; `GhoulRenderingRefreshUtility.ClearPendingRefreshes()` :79 is deleted by core). Delete `1.7/Languages/ChineseSimplified/DefInjected/IncidentDef/Incidents_Mugirl.xml:8` (`Mugirl_WildManWandersIn.label`). Use `Mugirl.Core.PlayerFactionUtility`/`PlayerIdeoUtility`/`GeneratedPawnUtility` (already rewritten by Wave R). The four settings readers (`MugirlStoryService.cs:52`, `IncidentWorker_Mugirl_Migration.cs:17`, `IncidentWorker_Mugirl_FusionInvestment.cs:8`, `IncidentWorker_Mugirl_RunawayFarmQuest.cs:8`; corporate: `CorporateIntroduction.cs:234`) are already switched to the new field names by the Wave R symbol rename (`core-member-moves.json#memberRenames`); nothing to do in Wave S.
2. **milk-genes-appearance** — `[GameLifecycleHook(FinalizeInit)]` on `MugirlMilkingAnimation.ResetTransientState` (`Features/Milk/Animation/MugirlMilkingAnimation.State.cs:44`); delete the empty `MugirlFoodEffectUtility.ResetDefCache`/`MugirlNurtureUtility.ResetDefCache` (MI A22). Appearance: read outline constants from `MugirlSettings`, subscribe to `SettingsWindow.Changed` from a `[StaticConstructorOnStartup]` binding and call the existing refresh only when enable/width changed (`Features/Appearance/MugirlExtraOutline.cs:14-16,110`). Apparel: add `Harmony_Hediff_PostAdd_PregnantApparel` (postfix calling the pregnancy-apparel refresh for `HediffDefOf.PregnantHuman`); core then removes that branch from the Rendering patch (core waits for the Apparel commit).
3. **restraints-mount-weapons (combat)** — implement `WeaponWheelHooks`/`MountingHooks` exactly as in S2-3; replace `TrainingFacilityCompatibility` calls (`Comp_WeaponWheel.Combat.cs:90,147,163`) and the flag listing (`WeaponWheelDevLog.cs:213-216`); register the "dump patch report" developer action (`WeaponWheelDevLog.PrintPatchReport`) via `SettingsWindow.AddDeveloperAction`; `[GameLifecycleHook(FinalizeInit)]` on `MountedCombatController.ResetTransientState` (`Features/Mounting/Combat/MountedCombatController.VerbState.cs:15`); delete the "改签名前同步兼容层" comments (`Comp_WeaponWheel.Combat.cs:38,78`, `Comp_WeaponWheel.Storage.cs:112,248,300,381`, `WeaponWheelAnimationRenderer.cs:9`) and `NoInlining` at `MountedPawnMeleeSupport.cs:136-139`. Hook API lands first; core deletes the self-patches after the hooks exist (same wave, ordered).
4. **corporate** — owns `Features/Corporate/Pricing/OpaqueBehaviorAnalysis.cs` and `Source/Tests/MugirlRace.Tests/Corporate/OpaqueBehaviorAnalysisTests.cs` after Wave R; owns `CorporateDefOf`.
5. **A1 (SaveUpgrade framework)** — raise `GameLifecycle.GameStarting` first in the `Game.LoadGame` prefix; hub stamp policy (CR #6); def-drop policies with pairwise dictionary removal; consume `Steps/Core/RemoveRetiredIncidentReferences` and the `LegacySettings` table; LegacyTypes generator emits only `saveVisible` types (+ §2 public XML aliases).
6. **A2 / validator owner** — `legacy-extra.txt`: `Mugirl.CorporateRuntimeValidation`, `Mugirl.CorporateVisualValidation`. New rules: no Harmony target declared in the MugirlRace assembly; `[GameLifecycleHook]` methods are `static void` without parameters; GameComponent ctors under `Mugirl.Core` are trivial; DefOf shape (internal static, `EnsureInitializedInCtor`, no unused field). `Misc_Gameplay.xml`/`Core.xml` key moves do not trip `C.translation-keys`.
7. **Misc_Gameplay.xml owner (EN + ZH)** — if the Wave R split (S5-Q1) is not approved: move the core keys listed in core.json notes to `Keyed/Core.xml` and delete the 6 dead keys of L8.
8. **Wave D** — ADR-004 superseded; README "XML 类名、Scribe 字段、根命名空间…不能随意改名"; `docs/content-update-guide.md:79`; ADR for the extension-point pattern and the lifecycle hub.

---------------------------------------------------------------------------------------------------------------------
## S5. Open questions (with proposed answers)

- **Q1 Keyed split of `Misc_Gameplay.xml`.** Proposal: the Wave R tool moves keys by an explicit key→file list (core's list is in core.json notes), so no Wave S shared-file owner is needed.
- **Q2 `PatchModule` rewrite.** Proposal: Wave R tool (rule in `core-member-moves.json`), because the attribute signature change touches every module at once.
- **Q3 LegacyNames tables in Wave S.** Proposal: per-module partial files `S/SaveUpgrade/LegacyNames/<Module>/Legacy*.<Module>.cs` merged by the framework, so modules never co-edit one table file.
- **Q4 Settings across versions.** Proposal: accept and document that the frozen 1.6 build reading a 1.7-written file logs one "Could not find class Mugirl.Core.MugirlSettings" per startup and uses defaults for the 4 renamed keys (CR #18); do not import MooGirl-era settings files (L7).
- **Q5 RunAndGun kotobike fork** (`Compatibility/ModCompat.cs:17,61`, field path `RunAndGunCompatibility.cs:70-76`; workshop 3523879860 declares only 1.6). Proposal: keep (third-party support, still loadable through version fallback; not Mugirl legacy); comments rewritten.
- **Q6 MooGirl-era components without successor** (L6). Proposal: core owns their Drop entries.
- **Q7 Race-core content files** (`1.7/Defs/ThingDefs_Races/Mugirl_Race.xml`, `Bodies/BodyDef.xml`, `BackStoryDefs/BackstoryDef.xml`). Proposal: owned by milk-genes-appearance (body addons dominate); core owns only `MugirlDefOf`.
- **Q8 Visibility tightening** (S2-9) needs `InternalsVisibleTo("MugirlRace.GameValidation")`. Proposal: yes, in `MugirlRace.csproj` (core-owned).
- **Q9 `GameStarting` phase** (CR #13). Proposal: include it now (cheap; the combat module plans a process-static roping set reset per game, module-restraints-mount-weapons.md A8); existing resets keep FinalizeInit timing.
