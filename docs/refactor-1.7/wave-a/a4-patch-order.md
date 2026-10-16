# A4: explicit Harmony order on multi-patch targets (derivation)

Stream A4, 2026-10-15. Source of truth for "today's order": `DevData/Backups/PreRefactor-1.7-20261015/MugirlRace.dll`
(sha256 `220E5D1A…`, = the Wave 0 tree). Harmony = workshop 2009463077/1.7 `0Harmony.dll` 3.0.0.0.

## 1. Engine rules used (verified in the decompiled Harmony 3, context `harmony17-review`)

- `HarmonyBootstrap` registers patch classes sorted by ordinal `FullName`; `[MugirlPatch(Deferred = true)]` classes
  (only `Mugirl.Harmony_StylingStationRefresh`, sharing no target) are registered after all others (in game via
  `ExecuteWhenFinished`; since A4 also in the offline `Apply`).
- `PatchInfo.Add`: `index = max(existing index) + 1` per target and kind, i.e. registration order.
- `PatchSorter`: wrappers sorted by `PriorityComparer` = priority descending, then index ascending; then a
  topological pass over `before`/`after`, which match the OTHER patch's `owner` (Harmony id). All Mugirl patches
  share the owner `MugirlMod.Mod`, so Before/After cannot order them among themselves (critique-validation F6).
- Prefix skipping (`MethodCreator.EmitPrefixes` / `AffectsOriginal`): once a prefix sets `__runOriginal=false`,
  every later prefix that returns bool or takes a ref/out/reference-type argument is skipped. This is why the
  anchor of group G below is the wheel's bool prefix.

## 2. Method

Scratch tool `scratchpad/a4/PatchOrderProbe` (not in the repo): loads the 1.7 game + Harmony 3 + HAR 1.6 in .NET 10,
answers ModLister/ModsConfig offline (mode `default`: Core, DLCs, Harmony, HAR, Mugirl active; mode `all`: every
mod active, so all 14 compat classes that `Prepare()` skips by default are applied too), applies the patch classes
exactly like `ApplyAtStartup` (sorted, deferred last), and prints Harmony's own `PatchSorter.Sort()` result for
every target/kind with more than one Mugirl patch, plus every applied patch with its priority.
Outputs: `scratchpad/a4/{before,after}-{default,all}[-reverse].txt`.

## 3. Today's effective order and the assigned priorities (first runs first)

Anchor rule: the anchor keeps its current priority (Normal unless stated); members after it get Normal-1, -2 …,
so no Mugirl patch moves ahead of a same-priority patch of a mod loaded before Mugirl (its dependencies);
they can only move behind same-priority patches of mods loaded after Mugirl (dependents that care already use
`[HarmonyAfter("MugirlMod.Mod")]`, which still orders them after every Mugirl patch). Every member of a group now
carries an explicit `[HarmonyPriority]` on the patch METHOD (not the class), so no other target or kind changes.

| # | Target / kind | Order today (all at 400 unless noted) | Assigned |
|---|---|---|---|
| A | `Comp_WeaponWheel.IsFullFirepowerEligible` Postfix (compat; DualWield+RunAndGun active) | DualWield_WeaponWheelFullFirepower > RunAndGun_WeaponWheelFullFirepower | Normal, Normal-1 |
| B | `Comp_WeaponWheel.MoveActiveWeaponTo` + `NormalizeToPrimarySlot`, Prefix and Postfix (compat) | DualWield_WeaponWheelPrimarySwap > MeleeAnimation_WeaponWheelPrimaryChanged | Normal, Normal-1 (Prefix and Postfix; DualWield Finalizer alone, untouched) |
| C | `ChildcareUtility.SuckleFromLactatingPawn` Prefix | LactationExpansion_SuckleFromLactatingPawn_Mugirl (First=800, class-level, existing) > ChildcareUtility_SuckleFromLactatingPawn_Mugirl | existing First kept; Normal made explicit |
| D | `Game.ExposeSmallComponents` Prefix | CorporateValidationSaveMigration > NeuralShoulderSaveMigration | Normal, Normal-1 |
| E | `Pawn.DynamicDrawPhaseAt` Postfix | Lances.Harmony_LanceCharge_BlurRecovery > Harmony_MountRendering > Harmony_WeaponWheel_AnimationDraw | Normal, Normal-1, Normal-2 (MountRendering Prefix alone, untouched) |
| F | `PawnGenerator.GeneratePawn(PawnGenerationRequest)` Postfix | Harmony_CorporateUniforms > Harmony_PawnGenerator_NewbornVisuals > Harmony_WeaponWheel_EnemyLoadout > PawnGenerator_GeneratePawn_Patch (restraint lock) | Normal, Normal-1, Normal-2, **Last (0)** (C08: lock stays last, now also after other mods) |
| G | `Pawn_EquipmentTracker.Notify_EquipmentRemoved` Prefix | Harmony_MountedCombatController_EquipmentRemoved (void) > Harmony_WeaponWheel_EquipmentRemoved (bool, skips original during wheel transfers) | **Normal+1**, Normal (anchor = the bool prefix, see §4) |
| H | `Verb.TryStartCastOn` Prefix | Harmony_ThrowFiringLock_TryStartCastOn (High=600, existing) > Harmony_WeaponWheel_TryStartCastOn | existing High kept; Normal made explicit |
| I | `Verb.get_WarmupTime` Postfix | Harmony_MountedCombatController_WarmupTime > Harmony_WeaponWheel_WarmupTime | Normal, Normal-1 |

Groups A and B exist only when the compat mods are active (mode `all`); D, E, F, G, H, I, C also in mode `default`.
No existing Mugirl patch shares a target with the upgrader hooks A1 adds (`Game.LoadGame`, `ScribeLoader.FinalizeLoading`,
`Game.FinalizeInit`, `BackCompatibility.*`, `Game.InitNewGame`).

## 4. Inter-mod effects (deliberate, minimal)

Mugirl-internal order is unchanged everywhere. Relative to OTHER mods' patches at exactly priority 400:
- Members moved to Normal-k now run after same-priority patches of mods loaded AFTER Mugirl (before: ahead of them).
- G: anchoring the bool prefix keeps "later-loaded affecting prefixes are skipped during wheel-internal transfers"
  unchanged. The mounted void prefix (reference-type argument, hence skippable) now runs ahead of 400-priority
  prefixes of mods loaded BEFORE Mugirl; it only differs if such a mod cancels `Notify_EquipmentRemoved`.
- F: the restraint lock now runs after every other mod's GeneratePawn postfix (C08 / restraints plan).

## 5. Verification (exact)

- Order reproduced: `diff` of every `TARGET`/`ORDER` line, before DLL vs after DLL, modes default and all: identical.
- Priorities: the per-patch list (149 rows in mode all) differs from before ONLY in the 13 moved patch/target rows
  (399/398/401/0); the other 15 participating rows keep 400/600/800 (now explicit where it was the default).
- Name independence: applying the classes in REVERSED registration order (what any rename could do) gives the
  identical effective order for the after DLL in both modes. Control: the before DLL flips 10 of 12 target/kinds under
  reversal (all except C and H, whose priorities already differed), so the probe does detect registration dependence.
- Build: isolated slnx/mod build 0 warnings 0 errors (GameValidation errors belong to A5's in-progress work).

## 6. Follow-ups for other owners

- Stale comments (A4 may edit attribute lines only, so these stay until Wave S / the integrator rewords them to
  "order pinned by [HarmonyPriority]"):
  - `Features/Restraints/Patches/Harmony_PawnGenerator_Patch.cs:5-6` ("类型名排在…之后，仍按 1.6 的顺序最后执行") —
    now directly contradicts the `[HarmonyPriority(Priority.Last)]` below it.
  - `Features/WeaponWheel/Patches/Harmony_WeaponWheel_Verbs.cs:6-7` ("补丁类名决定 HarmonyBootstrap 的应用顺序（FullName 序），
    改名前复查同目标补丁") — false since A4 (combat-plan W7 already lists it).
  - `Features/WeaponWheel/Patches/Harmony_WeaponWheel_Generation.cs:6` ("彼此不依赖顺序").
  - `Compatibility/DualWield/DualWieldPatches.cs:61`, `Compatibility/MeleeAnimation/MeleeAnimationPatches.cs:7` ("不需要指定先后").
  - `Compatibility/RunAndGun/RunAndGunPatches.cs:6`.
- Spec conflict (orchestrator): `spec/combat-plan.md` S2-8 (lines 228-236) says "no gratuitous priorities" and lists
  Attribute "none" for `Pawn.DynamicDrawPhaseAt`, `Verb.get_WarmupTime`, `Notify_EquipmentRemoved` and
  `PawnGenerator.GeneratePawn`. decisions C08/F6 win; amend S2-8 to "keep A4's priorities (§3 above); golden =
  `scratchpad/a4/after-all.txt`; any change to a priority is a declared behaviour change", otherwise a Wave S combat
  implementer following S2-8 would delete them.
- A2 `B.patch-order`: same-kind Mugirl patches per target pairwise distinct priority; golden = §3 groups; additionally
  assert that the restraint lock (`PawnGenerator_GeneratePawn_Patch.Postfix`, renamed
  `Harmony_PawnGenerator_GeneratePawn_LockRestraints` in Wave R) has the minimum priority of all Mugirl `GeneratePawn`
  postfixes (C08). Until that rule lands, only this scratchpad probe guards the order.
- A1 (upgrader hooks): none of today's patches share a target with the hooks (§3). The `BackCompatibleDefName` postfix
  takes `Priority.Last` (decisions §2) and the `LoadGame` prefix `Priority.First`; any second Mugirl patch on those
  targets later needs a distinct explicit priority.
