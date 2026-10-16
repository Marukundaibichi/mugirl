# Wave S plan — module `combat` (Mounting, Lances, WeaponWheel, Weapons)

Companion of `spec/combat.json` (Wave R rename spec). Paths: `F/` = `Source/MugirlRace/Features/`, `D/` = `1.7/Defs/`,
`LZ/` = `1.7/Languages/ChineseSimplified/DefInjected/`. Line numbers are the 2026-10-15 working tree (pre-rename names).
Type names below use the NEW names from `combat.json` (`CompMount`, `CompWeaponWheel`, `MountUtility`, ...).
Real-save ids S1..S11 are those of `understand/real-saves.md` §1.

Verified facts this plan relies on (beyond the inventory):
- `Verse.Gen.IsHashIntervalTick(this Thing t, int interval, int delta)` exists in 1.7 (`Math.Abs(t.HashOffsetTicks() % interval) < delta`, rw17 0x0600045C).
- `ThingOwner.ExposeData` writes `maxStacks`(999999), `contentsLookMode`(Deep), `removeContentsIfDestroyed`(true), `dontTickContents`(false), then `innerList` (rw17 0x06002B1E/0x06002AEA). Real S10 mount node: `<innerContainer><maxStacks>1</maxStacks><removeContentsIfDestroyed>False</removeContentsIfDestroyed><innerList /></innerContainer>`.
- Scribe labels per build (git): `weaponWheel{ReserveWeapons,Slots,UnlockedSlots}` since 4e76859, `weaponWheelSwordDanceFailureStreak` since 0a6c435; `PawnFlyer_LanceCharge` exists since 37545af (0.6.4) and writes `hitPawns` in every build; `wallHitPointsRemaining`/`lastPassableCell` since 8cd70f9 (0.6.6); `CompLanceCharge` labels identical a172c15..50f7979; `Comp_MooGirlMount` (cacbc58) wrote the same 13 labels as today.
- Toil layouts: MountMugirl 3 toils and DismountMugirl 2 toils unchanged d9b9bb9..now; LoadWeaponWheel 2 toils since 4e76859; LanceCharge: a172c15 (0.6.3) had 2 toils (Goto + instant impact), 37545af+ has 1 instant toil.
- `Mugirl_LanceChargeSpeed` (HediffDef) is never added by any build (git grep 37545af..50f7979, 1.6/Source: only the DefOf field).
- Real saves: `innerContainer` on every Mugirl pawn of S6, S7, S10 (incl. S10 `corporateVault` li[Pawn]); `weaponWheel*` on S7 (2) and S10 (7) pawns; S6's 7 Mugirl pawns have NO wheel nodes (saved by a pre-4e76859 build); no `fireAtWill`/`turret*`/counter label at pawn level, no lance cooldown label, no PawnFlyer_LanceCharge, no Mugirl JobDriver in any real save. PMC_/OPC_ weapon instances exist in S4..S11.

---------------------------------------------------------------------------------------------------------------------
## S1. Legacy code removal (every legacy site of the module) and its replacement

Mechanisms: TYPE (LegacyTypes map, generated from spec), DEF (LegacyDefs map), LABEL (owner-scoped label rename/drop/materialise
table), DOM (structural step in `SaveUpgrade/Steps/Combat/`), POST (post-load phase), DROP (nothing needed; delete code/comment).

| # | Site (pre-rename) | Legacy shape handled | Disposition | Domain code deleted / rewritten |
|---|---|---|---|---|
| M1 | `F/Mounting/Comp/Comp_MugirlMount.Storage.cs:3,6-28`; `Comp_MugirlMount.cs:10-23` | 13 flat labels in the shared pawn element, frozen "1.6 names": `innerContainer` (S6/S7/S10 every Mugirl pawn), `fireAtWill` (default = `Props.turretFireAtWillDefault`, Def-dependent), `physiologicalTickCounter`, `safetyTickCounter`, `turretTickCounter`, `riderMeleeTickCounter`, `turretBurstCooldownTicksLeft`, `turretAimTarget`, `turretAimTicksLeft`, `turretAimTicksTotal`, `turretCastStartTick`(-1), `turretLastAttackedTarget`, `turretLastAttackTargetTick`. Same labels in MooGirl era (`Comp_MooGirlMount`, cacbc58). | LABEL + DOM step `Combat.MountSchema` (S2-4): `innerContainer`→`mountRiders`; `fireAtWill`→`mountFireAtWill`, materialise when absent with the owner def's `CompProperties_Mount.turretFireAtWillDefault`; drop 4 counters + `turretAimTicksTotal`; move the 6 remaining `turret*` into new child `<mountTurret>` with prefix stripped. | "Scribe labels identical to 1.6" comments; counter fields; `MugirlTickUtility.Add/ConsumeReady` uses. |
| M2 | `Comp_MugirlMount.Storage.cs:10-14` (`MakeContainer` after `Scribe_Deep`, "old saves miss the node") | none of our builds: the comp existed since MooGirl era; MooGirl-era saves hold no Mugirl pawns (real-saves §1) | DROP the legacy reasoning. Keep ONE invariant guard `riders ??= NewRiderOwner()` / `turret ??= new()` after Scribe in LoadingVars, commented as "the def may gain this comp after the thing was saved (third-party races WhiteFluff/Subspecies use it)". | comment rewritten |
| M3 | `Comp_MugirlMount.cs:7`, `CompProperties_MugirlMount.cs:3` (names "cannot change") | XML `Class="Mugirl.CompProperties_MugirlMount"` in third-party defs | TYPE alias (public, design §2): `Mugirl.CompProperties_MugirlMount`→`...Mounting.CompProperties_Mount` via LegacyTypes/GenTypes postfix. Comp short name never saved (claim in :7 is false). | comments |
| M4 | `F/Mounting/Jobs/JobDriver_MountMugirl.cs:5`, `JobDriver_DismountMugirl.cs:5` | curDriver `Class="Mugirl.JobDriver_{Mount,Dismount}Mugirl"` (and `MooGirl.JobDriver_{Mount,Dismount}MooGirl`); curJob `def` `Job_MountMugirl`/`Job_DismountMugirl` (`Job_*MooGirl`) | TYPE + DEF (spec + MooGirl hand map). Toil layout unchanged in all builds → no POST step. | comments "written to saves" |
| M5 | `MountedCombatController.Turret.cs:5-6` ("state fields on the comp, Scribe labels unchanged") | see M1 | DOM (M1). Turret state becomes `MountTurret : IExposable` (S2-4). | static writes to comp `internal` fields (`Comp_MugirlMount.cs:17-27`, `Lifecycle.cs:18-20`) |
| M6 | `MountedCombatController.VerbState.cs:9-27` + caller `F/Incidents/Story/MugirlStoryState.cs:77-79` | not a save shape: per-game static registry reset by an unrelated feature | DROP: register `MountedVerbRegistry.Reset` (incl. `MountedWarmupOverride.Clear`) with the Core lifecycle hub (§5.8). Comment :13-14 ("1.6 reset ...") removed. | call in MugirlStoryState (S4) |
| M7 | `F/Mounting/MountedPawnUtility.cs:10-14` `IsMugirl` ("history entry") | old API name (callers `MountEligibilityService.cs:18`, `UI/FloatMenuProvider_MountMugirl.cs:25`) | DROP; callers use the carrier predicate of S2-11 (or `MugirlIdentity.IsMugirlPawn` if S5-Q3 is declined). Generic predicates `IsHumanlike/HasCapacity/IsAwake` (:42-66) leave Mounting (S4). | alias + predicates |
| M8 | `F/Mounting/Combat/VanillaMeleeAccess.cs:8,70,93` ("using the 1.6 fallback") | game-version double path | DROP wording: missing vanilla member = one startup self-check error naming the member; behaviour of the degrade path unchanged. | log texts/comments |
| M9 | `MountedPawnMeleeSupport.cs:136-139` (`[MethodImpl(NoInlining)]`, frozen signature for MeleeAnimation) | Harmony self-patch coupling | Hook API (S2-1, `MountingHooks`). | attribute + comment |
| M10 | `Comp_MugirlMount.Ticking.cs:29-32,39,50,62,67` | saved accumulation counters | DROP labels (M1); scheduling by `carrier.IsHashIntervalTick(interval, delta)` (S2-4). | counters |
| M11 | comments `MountedVerbScope.cs:5`, `MountedWarmupOverride.cs:4`, `MountDrawOffsets.cs:3,6,44`, `CompProperties_MugirlMount.cs:3-4` | history ("1.6 ...") | DROP: rewrite to current reason; history → migration doc (Wave D). | comments |
| L1 | `F/Lances/Flight/PawnFlyer_LanceCharge.cs:77-81` `hitPawns ??=` ("old saves lack the node") | none: every flyer build (37545af..50f7979) writes `hitPawns` | DROP the guard; field initializer + Scribe is enough. | :77-81 |
| L2 | `PawnFlyer_LanceCharge.cs:74-75` (`wallHitPointsRemaining`, "old flyers get the full budget") | 0.6.4/0.6.5 in-flight flyer without the node | No step: absent node = schema default 1550 = today's meaning. | comment rewritten |
| L3 | `PawnFlyer_LanceCharge.cs:178-191` (`lastPassableCell` rebuilt from `startVec` when Invalid) | 0.6.4/0.6.5 in-flight flyer without `lastPassableCell` (none in real saves; flyer lives <1 s) | DOM step `Combat.LanceFlyer`: write `<lastPassableCell>` = `(trunc(startVec.x), 0, trunc(startVec.z))` (the launch cell, always standable). Domain initialises the field in `SpawnSetup(respawningAfterLoad:false)` (identical result for new charges: today the branch runs at `ticksFlying==0` and yields `startVec.ToIntVec3()`). Documented approximation for legacy flyers: cells already crossed before the save are not candidates. | :178-191 branch |
| L4 | `PawnFlyer_LanceCharge.cs:31-32,71-72,119-130` `smokeTick`, `cameraShakeTick` | saved visual tick counters | DOM drop labels (same step as L3); domain uses `this.IsHashIntervalTick(8, delta)`. | 2 fields |
| L5 | `F/Lances/Charge/CompLanceCharge.cs:23-24` | unprefixed comp labels `pointCooldownUntilTick`/`lineCooldownUntilTick` in the shared weapon element (§3.4) | LABEL owner ThingWithComp(CompLanceCharge): → `lancePointCooldownUntilTick`/`lanceLineCooldownUntilTick`. | — |
| L6 | `D/HediffDefs/Hediffs_Lances.xml` `Mugirl_LanceChargeSpeed`; `DefOf/Mugirl_DefOf.cs:67`; `LZ/HediffDef/Hediffs_Lances.xml` | dead def, never instantiated | DROP (delete def file, DefInjected file, DefOf field). Corpus disposition Dropped("never instantiated"); no map entry needed. | 3 files/lines |
| L7 | LanceCharge toil layout 0.6.3 (2 toils) → 0.6.4+ (1 instant toil) | 0.6.3 save with a lance job at curToilIndex 0 (Goto) | No step (verified a172c15): index 0 resumes as the launch toil (job.count = -1 → launch), index 1 is an instant toil and cannot be persisted. | — |
| L8 | comments `PawnFlyer_LanceCharge.cs:6`, `CompLanceCharge.cs:85`, `CompProperties_LanceCharge.cs:46`, `LanceChargeGridTraversal.cs:10`, `LanceChargeImpactUtility.cs:79`, `LanceMotionBlur.cs:124`, `LanceChargeDrawingUtility.cs:37` | history | DROP (rewrite). | comments |
| W1 | `F/WeaponWheel/Core/Comp_WeaponWheel.cs:13,85-93,227-264` (`EnsureCollections` null-guards, "save misses the node") + per-tick call :99 | real S6 shape: Mugirl pawns saved before 4e76859 have no `weaponWheel*` nodes | DOM step `Combat.WheelMaterialize`: owner lacking `weaponWheelReserveWeapons` gets `<weaponWheelReserveWeapons><innerList /></weaponWheelReserveWeapons><weaponWheelSlots />`. Domain keeps only `EnsureCapacity()` (pads/truncates slot records to `Props.maxSlots`, applies base/research unlocks) called from Initialize/PostPostMake/PostLoadInit; the per-tick call (:99) is deleted. Null guard kept only as the "def gained this comp" invariant (same wording as M2). | :99, null-tolerant branches in :227-264 |
| W2 | `Comp_WeaponWheel.cs:81-82` parallel lists `weaponWheelSlots` (refs) + `weaponWheelUnlockedSlots` (bools) | S7 (2 pawns), S10 (7 pawns incl. corporate vault) | DOM step `Combat.WheelSlotRecords` (S2-3) → one `List<WeaponWheelSlot>`. | parallel list code |
| W3 | `F/WeaponWheel/AI/JobDriver_LoadWeaponWheel.cs:6` | curDriver `Class="Mugirl.JobDriver_LoadWeaponWheel"`, curJob def `Job_LoadWeaponWheel` | TYPE + DEF (spec). | comment |
| W4 | `F/WeaponWheel/AI/WeaponWheelJobs.cs:5-6` (Job.count = slot+1 "cannot change") | in-flight load job | Keep the encoding (it is schema, S3-E); comment rewritten to state the schema, not a freeze. | comment |
| W5 | compat-coupling comments `Comp_WeaponWheel.cs:8`, `Combat.cs:38,78`, `Storage.cs:112,248,300,381`, `Rendering/WeaponWheelAnimationRenderer.cs:9` | Harmony self-patches by Compatibility | Hook API (S2-1). | comments, frozen signatures |
| W6 | `CompProperties_WeaponWheel.cs:5`, `CompProperties_AutoloadingSystem.cs:3`, `CompAutoloadingSystem.cs:5` ("cannot change") | own XML only | TYPE rename (tool rewrites own XML); no alias. | comments |
| W7 | `Patches/Harmony_WeaponWheel_Verbs.cs:6-7,97-98` (order by class name, "1.6 used int.MaxValue") | patch order by FullName sort | explicit ordering table S2-8. | comments |
| W8 | `Rendering/WeaponWheelBackLayoutData.cs:86`, `WeaponWheelBackLayout.cs:6-8,18-23,75-105` | user/dev override file `Config/Mugirl/WeaponWheelBackLayout.xml` older format | S2-13 (read only in DevMode, S5-Q4); the "old file without mirror fields" branch stays as file-format default (not save data). | comment :86 |
| W9 | `Comp_WeaponWheel.cs:433-443` static dash counter "not reset per save" | per-game static | lifecycle hub reset (S2-6). | comment |
| W10 | `Combat.cs:90,147,163` direct calls to `TrainingFacilityCompatibility`; `Dev/WeaponWheelDevLog.cs:213-216` reads 4 compat `Active` flags | Feature → Compatibility dependency (§3.5) | Hook API (S2-1: `AddCombatBypass`, `RegisteredOwners`). | 7 call sites |
| W11 | history comments `WeaponWheelEnemyLoadoutGenerator.cs:4`, `SwordDanceDashCurve.cs:81`, `WeaponWheelAnimationCurves.cs:7`, `Combat.cs:70`, `CompAutoloadingSystem.cs:6`, `CompProperties_WeaponWheel.cs:26`, `Patches/WeaponWheelHarmonyUtility.cs:9`, `WeaponWheelBackLayout.cs:14`, `UI/ITab_Pawn_WeaponWheel.cs:338,387` | bit-parity / history notes | DROP (rewrite as current reason). | comments |
| X1 | `F/Weapons/MugirlRaceRestrictedExtension.cs:3-6` | own XML only; field `enabled` never written by XML | TYPE rename; delete `enabled` (presence of the extension is the flag; patch reads `GetModExtension<>() != null`). | field |
| X2 | `D/ThingDefs_Weapon/RangedWeaponsEnemy.xml` `PMC_*`/`OPC_*` + `Bullet_*` | things/filters/bills/battle logs in S4..S11 | DEF + ThingID rewrite + implied `Make_` RecipeDefs (spec notes). | — |
| X3 | file names with dates `MeleeWeapons_0920.xml`, `RangedWeapons_0920.xml`, `LZ/ThingDef/Weapons_0920.xml` | — | content moves (spec); split AT23 entries of `Weapons_0920.xml` into `LZ/ThingDef/RangedWeapons_Autocannon.xml` (Wave S). | — |
| X4 | MooGirl-era `MuGirl_Gun_*` (S3 `thingDef<li`), 0.6.3 `Bullet_Mugirl_AT23` | name-level | DEF hand map (spec notes). | — |
| X5 | content comments `D/Apparel/Apparel_WeaponWheel.xml:3-4` (changelog "2026-09-29 ..."), `D/ResearchProjectDefs/ResearchProjects_WeaponWheel.xml:9` ("cost 2500→2000"), `D/ThingDefs_Weapon/RangedWeapons.xml:277` + `1.7/Mods/Biotech/Languages/ChineseSimplified/DefInjected/ThingDef/RangedWeapons_Biotech.xml:4` (shipped placeholder "reuses vanilla code, to be changed later") | history / placeholder text | DROP changelog comments; replace the player-visible placeholder description by a real description (verified content defect, content.md E5). | text |

---------------------------------------------------------------------------------------------------------------------
## S2. Structural refactors

### S2-1 MUST — Hook API replacing the Compatibility self-patches (interface exposed by this module; core owns the Compatibility side)
Files: new `F/WeaponWheel/Hooks/WeaponWheelHooks.cs`, `F/WeaponWheel/Hooks/IWeaponWheelPrimarySwapGuard.cs`, `F/Mounting/Hooks/MountingHooks.cs`,
`F/Mounting/Hooks/IMountedMeleeWeaponDrawObserver.cs`; call sites in `CompWeaponWheel` collaborators, `WeaponWheelAnimationRenderer`, `MountedMeleeSupport`,
`WeaponWheelDevLog`. Replaces 10 Harmony classes in `Compatibility/` (DualWield ×4, RunAndGun ×1, YayoCombat ×2 self-patches, MeleeAnimation ×2 self-patches,
YayoAnimation transpiler ×1) and the direct `TrainingFacilityCompatibility` calls.

```csharp
namespace Mugirl.Features.WeaponWheel;

// 武器轮盘对兼容模块开放的扩展点。兼容模块在 Mod 构造期确认对方 Mod 启用后注册（core 的兼容引导负责调用）。
// StaticCacheLifecycle: 进程级注册表，只在主线程的 Mod 构造期写入，之后只读；不持有游戏对象，不随存档变化。
internal static class WeaponWheelHooks
{
    // 火力满载资格否决：轮盘自身判定为真后依次询问，任一返回 true 即不具备资格。（RunAndGun 移动施放、DualWield 持有副手）
    internal static void AddFullFirepowerVeto(string owner, Func<Pawn, bool> vetoes);
    // 施放拦截否决：TryStartCastOn 处理的第一步（先于训练场旁路与开发日志）；返回 true 时轮盘不接管本次施放。（DualWield 持有副手）
    internal static void AddCastInterceptionVeto(string owner, Func<Pawn, bool> vetoes);
    // 战斗旁路：TryStartCastOn、施放开始、连射完成三个入口都忽略该 Pawn。（Training Facility 靶场 Job）
    internal static void AddCombatBypass(string owner, Func<Pawn, bool> bypasses);
    // 外部装备过滤：返回 true 的装备不属于轮盘，外部装备增删同步直接忽略。（DualWield 副手）
    internal static void AddForeignEquipmentFilter(string owner, Func<ThingWithComps, bool> isForeign);
    // 主手切换守卫：MoveActiveWeaponTo / NormalizeToPrimarySlot 入口、主手与目标槽武器不同时调用；见接口说明。（DualWield 暂存副手）
    internal static void AddPrimarySwapGuard(string owner, IWeaponWheelPrimarySwapGuard guard);
    // 主手已改变：上述两方法返回 true 且 Primary 与进入时不同。（MeleeAnimation 清除缓存的空闲动画）
    internal static void AddPrimaryChangedListener(string owner, Action<Pawn> onPrimaryChanged);
    // 储备武器周期任务：CompTick 末尾在 pawn.IsHashIntervalTick(intervalTicks) 时调用。（YayoCombat 自动装填，60 tick）
    internal static void AddReserveWeaponTicker(string owner, int intervalTicks, Action<CompWeaponWheel> tick);
    // 生成储备武器初始化：TryAddGeneratedReserveWeapon 的第一步，先于任何校验。（YayoCombat 初始弹药）
    internal static void AddGeneratedReserveWeaponInitializer(string owner, Action<Pawn?, ThingWithComps> initialize);
    // 轮盘过渡图形使用的身体位置；未注册时为 pawn.DrawPos。只允许一个提供者，重复注册报错。（YayoAnimation）
    internal static void SetBodyDrawPositionProvider(string owner, Func<Pawn, UVector3> bodyDrawPosition);
    // 诊断：已注册的兼容模块（WeaponWheelDevLog 打印，取代对各兼容类的直接引用）。
    internal static IReadOnlyList<string> RegisteredOwners { get; }
}

// 主手切换期间需要暂时改动装备、结束后归还的兼容模块（DualWield 副手）。
internal interface IWeaponWheelPrimarySwapGuard
{
    // 返回 false：取消本次切换（方法返回 false，不改动槽位）。restoreToken 非 null 时，切换结束后（含异常）调用 EndPrimarySwap 一次。
    bool TryBeginPrimarySwap(CompWeaponWheel wheel, out object? restoreToken);
    void EndPrimarySwap(CompWeaponWheel wheel, object restoreToken);
}
```
Query/command surface the Compatibility layer may use (all `internal`): `WeaponWheelLookup.For(Pawn?)` (race gate + `GetComp`, render-thread safe),
`WeaponWheelLookup.HoldingReserveWeapon(Thing?)` (`ParentHolder as CompWeaponWheel`, for YayoCombat `Wearer`/`OwnerOf`), `CompWeaponWheel.Pawn`, `.IsBusy`,
`.ReserveWeapons` (`ThingOwner<ThingWithComps>`), `.IsSwordDanceStrikePending` (renamed from `ShouldSuppressExternalMeleeAttackAnimation`, used by core's vanilla
patch on `Pawn_DrawTracker.Notify_MeleeAttackOn`), `.TryMoveEquipmentToReserve(ThingWithComps)` (wraps `equipment.TryTransferEquipmentToContainer` in the
internal-transfer scope; DualWield detach), `.ReturnToReserve(ThingWithComps)`/`.RemoveFromReserve(ThingWithComps)` (DualWield restore/rollback).

```csharp
namespace Mugirl.Features.Mounting;

// StaticCacheLifecycle: 同上，进程级、Mod 构造期写入。
internal static class MountingHooks
{
    // 骑手近战武器绘制前后（MeleeAnimation 暂停空闲武器动画）。
    internal static void AddMeleeWeaponDrawObserver(string owner, IMountedMeleeWeaponDrawObserver observer);
}

internal interface IMountedMeleeWeaponDrawObserver
{
    // 返回非 null 时，绘制结束后（finally）以该值调用 EndMeleeWeaponDraw。主线程、每帧调用：实现不得分配。
    object? BeginMeleeWeaponDraw(Pawn rider, ThingWithComps weapon);
    void EndMeleeWeaponDraw(object token);
}
```
Equivalence (behaviour must be identical; each row is a unit/in-game test):

| Old patch (file:line) | Old semantics | New call site |
|---|---|---|
| `DualWieldPatches.cs:7-20` Postfix `IsFullFirepowerEligible` | true→false if off-hand | end of `WeaponWheelCombat.IsFullFirepowerEligible`, only when the built-in result is true |
| `RunAndGunPatches.cs:7-20` same target | true→false if moving cast | same list (order irrelevant: both only clear true) |
| `DualWieldPatches.cs:22-40` Prefix `HandleBeforeTryStartCast` | with off-hand: whole method skipped, `result=false`, return false | first statement of `HandleBeforeTryStartCast` (before the training bypass and DevLog); a DevLog line is added (diagnostics only) |
| `Combat.cs:90,147,163` TrainingFacility | bypass in 3 entry points | `AddCombatBypass` checked at the same 3 places |
| `DualWieldPatches.cs:42-59` Prefix(`__0`) `NotifyExternalEquipmentAdded/Removed` | skip for off-hand | first statement of both methods |
| `DualWieldPatches.cs:62-116` Prefix/Postfix/Finalizer `MoveActiveWeaponTo`, `NormalizeToPrimarySlot` | if `Primary != WeaponAt(target)` (target = arg or 0): detach off-hand, abort with false when detach fails; restore exactly once after (also on exception) | top of both methods with the same condition, `try/finally` around the body; applies to internal calls too (TrySwapSlots, Abort, PromoteSlotToPrimary) exactly like the Harmony patch |
| `MeleeAnimationPatches.cs:8-33` Prefix/Postfix same two methods | if `__result` and Primary changed → notify | after the body returns true, compare with Primary captured at entry; raised after the guard's restore |
| `YayoCombatPatches.cs:8-18` Postfix `CompTick` (+ `IsHashIntervalTick(pawn,60)` inside, `YayoCombatCompatibility.cs:32`) | per-tick postfix | end of `CompTick` when `pawn.IsHashIntervalTick(60)`; compat drops its own interval check |
| `YayoCombatPatches.cs:20-30` Prefix `TryAddGeneratedReserveWeapon(weapon)` | runs before any validation | first statement of the method |
| `YayoAnimationPatches.cs:8-46` Transpiler on `WeaponWheelAnimationRenderer.Draw` | replaces the single `pawn.DrawPos` read (`WeaponWheelAnimationRenderer.cs:22`) | `Vector3 pawnDrawPosition = WeaponWheelHooks.BodyDrawPosition(pawn)` |
| `MeleeAnimationPatches.cs:62-89` Prefix/Postfix/Finalizer `MountedPawnMeleeSupport.DrawWeapon` | suspend idle animation while drawing a melee weapon | inside `MountedMeleeSupport.DrawWeapon` after its own guard, `try/finally`; `[MethodImpl(NoInlining)]` and the frozen signature are removed |

Save impact: none. Behaviour impact: none (DevLog text lists registered owners instead of four fixed flags). Tests: registry unit tests (veto only clears
true; guard abort returns false and leaves slots untouched; restore runs once on normal return, on abort and on exception; listener only on real change;
ticker phase = `IsHashIntervalTick`); GameValidation startup check "no Mugirl Harmony patch targets a Mugirl method" (patch-targets baseline);
in-game smoke with DualWield/YayoCombat when present. Risk: medium.

### S2-2 MUST — `CompWeaponWheel` split into collaborators, comp as facade
Today: one partial class of 2 578 lines (Core 444, Storage 619, Combat 879, SwordDance 539, Rendering 97).
New design (namespace `Mugirl.Features.WeaponWheel`, all `internal sealed` except the comp):
- `CompWeaponWheel : ThingComp, IThingHolder` (public sealed, facade): owns the collaborators, `Props`, `Pawn`, all ThingComp overrides (Initialize, PostPostMake,
  PostExposeData, CompTick, PostDestroy, CompRenderNodes), stays the `IThingHolder` and the reserve `ThingOwner` owner (keeps `weapon.ParentHolder is CompWeaponWheel`
  and the Pawn→comp→weapon holder chain used by `Harmony_Pawn_GetChildHolders` and YayoCombat), public/internal API for UI (ITab), jobs, patches, hooks; CompTick keeps
  today's order (maintenance → autoloading cache → reserve verbs → mounted state → open state → dash → pending sword dance → combat state → back-weapon visibility → hook tickers).
- `WeaponWheelStorage`: reserve `ThingOwner<ThingWithComps>`, `List<WeaponWheelSlot>`, active slot index, research unlock, `EnsureCapacity`, slot queries
  (WeaponAt, IsSlotUnlocked, IndexOf, Contains, OccupiedUnlockedCount), mutations (attach ground weapon, add generated reserve weapon, swap, drop, MoveActiveWeaponTo,
  NormalizeToPrimarySlot, external equipment sync, reserve tracking, drop/forbid everything, hunting-verb sync), reserve mass cache, reserve verb tickers,
  `ExposeData` for `weaponWheelReserveWeapons`/`weaponWheelSlots`. Busy checks are passed in (preserve reason precedence: SlotLocked before Busy in TrySwapSlots,
  CannotDrop before Busy in TryDropSlot, etc.).
- `WeaponWheelCombat`: the runtime state machine (all fields of `Combat.cs:10-36` + `wasWeaponOpenlyHeld`, `wasMountedDisabled`, mount-state cache), Verb entry points,
  eligibility, abort/stop, attack-verb correction, facing, autoloading cache, animation snapshot/aim hand-off. Never saved; `Reset()` on PostLoadInit.
- `WeaponWheelSwordDance`: pending strike, dash animation, rotation buffers, eligibility, delayed strike, dash draw position/facing, ram damage, haste,
  `failureStreak` + `ExposeData` for `weaponWheelSwordDanceFailureStreak`; static `AnyDashActive` counter (reset via lifecycle hub, S2-6).
- `WeaponWheelRenderCache`: back/autoloading render-node lists, visibility mask, recache requests, node construction for `CompRenderNodes`, render-thread read-only
  queries (`GetBackWeapon`, `IsBackWeaponVisible`, `GetAutoloadingWeapon`).
Cross-collaborator calls go through narrow internal methods on the siblings (e.g. `combat.BeginSwordDanceSwitch(...)`, `storage.PromoteSlotToPrimary(...)`);
render-thread code only reads. Files: the five `CompWeaponWheel.*.cs` partials become `CompWeaponWheel.cs` + four collaborator files in Core/Combat/SwordDance/Rendering.
Save impact: none from the split itself (labels written by collaborators into the same pawn element). Behaviour: none. Tests: existing pure tests stay
(SlotMath, curves, rules, scopes); add `WeaponWheelStorage` capacity/unlock tests where no game state is needed; in-game: UI smoke `weapon-wheel-tab`,
Render suite, a scripted full-firepower cycle and a sword-dance switch on a test pawn. Risk: medium-high (largest file set; do it after S2-1 and S2-3).

### S2-3 MUST — weapon-wheel slot records (design §3.4 "no parallel lists")
`weaponWheelSlots` (List<ThingWithComps?> by reference) + `weaponWheelUnlockedSlots` (List<bool>) → `weaponWheelSlots` = `Scribe_Collections.Look(ref slots,
"weaponWheelSlots", LookMode.Deep)` of `WeaponWheelSlot : IExposable` { `weapon` (Scribe_References ThingWithComps), `unlocked` (bool, default false) }.
Upgrade step `Combat.WheelSlotRecords` (owner ThingWithComp(CompWeaponWheel), any depth incl. `innerList/li[Pawn]`, corporate vault, corpses):
applicable iff a direct child `weaponWheelUnlockedSlots` exists; n = max(count of the two lists' `li`); build `<weaponWheelSlots>` with n `<li>`: child
`<weapon>` = old slot text when it is not `null`/empty, child `<unlocked>True</unlocked>` when the old bool is `True`; replace the old `weaponWheelSlots` node,
remove `weaponWheelUnlockedSlots`. Idempotent (marker removed). Golden test: S10 pawn `Mugirl27505` (6×`null` + 6×`True`). Behaviour: none (EnsureCapacity
keeps padding/truncation and base/research unlock rules). Risk: medium (reference resolution inside nested deep list — vanilla pattern, cover with a
round-trip test: load S7 fixture, slot weapons resolve to the reserve/primary instances).

### S2-4 MUST — `CompMount` schema and `MountTurret`
- Riders: field `riders` (`ThingOwner<Pawn>`, oneStackOnly, removeContentsIfDestroyed:false), label `mountRiders` (Scribe_Deep, ctorArg comp). Saved node shape is
  identical to today's `innerContainer` (ThingOwner has no Class attribute; `li Class="Pawn"` items).
- `fireAtWill` label `mountFireAtWill`, `Scribe_Values.Look(ref fireAtWill, "mountFireAtWill", true, forceSave: true)`; field initializer `true`;
  `Initialize` still sets it from `Props.turretFireAtWillDefault` for new comps.
- Delete the four accumulation counters; `CompTickInterval` schedules physiology/safety/turret/rider-melee with `carrier.IsHashIntervalTick(interval, delta)`
  and passes `interval` as the elapsed time (RiderTickDriver, turret). Same rates; only the phase within one interval changes (no gameplay-visible effect;
  over a mount session the rider's needs integrate ±<1 interval).
- `MountTurret : IExposable` (internal sealed), label `mountTurret`, fields `aimTarget`, `aimTicksLeft`, `castStartTick`(-1), `burstCooldownTicksLeft`,
  `lastAttackedTarget`, `lastAttackTargetTick` + the turret logic of `MountedCombatController.{Turret,Targeting,WeaponEligibility,Lifecycle}` as instance
  methods taking the `CompMount`; the cached primary weapon/verb (`Comp_MugirlMount.cs:25-27`) becomes non-saved turret state. `turretAimTicksTotal` is removed:
  its only reader is `IsTurretIdle` (`Turret.cs:138-139`), and `total != 0` implies `aimTarget.IsValid` (it is set only together with a valid aim target,
  `Turret.cs:69-72`, and every path that invalidates the target goes through `ClearAim`, which zeroes it, `Turret.cs:288-294`), so the term is redundant —
  cover with a unit test of the idle predicate.
- Upgrade step `Combat.MountSchema` (owner ThingWithComp(CompMount): every element whose remapped `<def>` has `CompProperties_Mount` — Mugirl, Subspecies and
  WhiteFluff races — at any depth, including riders nested in another carrier's riders): (1) first direct child `innerContainer` → `mountRiders` (move children,
  ReplaceChild; mirrors the old reader's first-child semantics); (2) `fireAtWill` → `mountFireAtWill`; if absent, materialise `<mountFireAtWill>` =
  the owner def's `turretFireAtWillDefault` as `True/False` (old absent = def default; all known defs: true); (3) drop `physiologicalTickCounter`,
  `safetyTickCounter`, `turretTickCounter`, `riderMeleeTickCounter`, `turretAimTicksTotal`; (4) create `<mountTurret>` and move+rename
  `turretBurstCooldownTicksLeft→burstCooldownTicksLeft`, `turretAimTarget→aimTarget`, `turretAimTicksLeft→aimTicksLeft`, `turretCastStartTick→castStartTick`,
  `turretLastAttackedTarget→lastAttackedTarget`, `turretLastAttackTargetTick→lastAttackTargetTick` (TargetInfo text/`Thing_` refs move unchanged; the DOM runs
  before cross-ref registration). Collect all edits first, then apply (nested owners). Idempotent: (1)-(4) are no-ops once `mountRiders`/`mountFireAtWill`/
  `mountTurret` exist.
Save impact: labels per S3-B. Behaviour: scheduling phase only (above). Tests: golden before/after (S10 pawn, synthetic carrier with rider + all 13 labels,
nested Mugirl rider), idempotence, hash-scheduling unit test (fires once per interval for deltas 1..15), turret idle-predicate equivalence. Risk: medium.

### S2-5 SHOULD — `MountedCombatController` dissolves
`MountedVerbRegistry` (static: original-caster registry, ensure/restore/forget, `IsMountedVerb`, warmup override; lifecycle-hub reset),
`MountedWeaponRenderer` (static: ranged weapon draw, melee delegated to `MountedMeleeSupport`), turret logic in `MountTurret` (S2-4). Patches call the new
owners. Save impact none; behaviour none; risk low-medium.

### S2-6 MUST — per-game transient state through the Core lifecycle hub (§3.5, §5.8)
Register: `MountedVerbRegistry.Reset` (today `MountedCombatController.ResetTransientState`, called by `MugirlStoryState.cs:78`), sword-dance dash counter
reset (`Comp_WeaponWheel.cs:435`, today never reset), `WeaponWheelDevLog` per-pawn dedup dictionaries (`WeaponWheelDevLog.cs:18-21`, keyed by thingIDNumber).
`LanceMotionBlur` uses a `ConditionalWeakTable` (no reset needed). Behaviour: the dash counter can no longer stay inflated after a game switch (perf gate only).

### S2-7 MUST — Lances cleanup
L1–L5 (guards, counters, label prefix, `lastPassableCell` initialised at spawn). Move the lance-flyer clause out of the WeaponWheel `DrawEquipmentAiming` prefix
(`Patches/Harmony_WeaponWheel_Rendering.cs:35-45`) into `F/Lances/Patches/Harmony_PawnRenderUtility_DrawEquipmentAiming_LanceCharge.cs` (two pure predicate
prefixes; result identical). Removes the WeaponWheel→Lances dependency. Upgrade steps: LABEL (L5) + DOM `Combat.LanceFlyer` (L3/L4). Tests: golden flyer node
(0.6.5-shaped without `lastPassableCell`, with counters), weapon node with both cooldowns. Risk: low.

### S2-8 MUST — patch classes
One type per file, names per spec, `internal static`, `[MugirlPatch(<closed module>)]` with modules `Mounting`, `Lances`, `WeaponWheel`, `Weapons`.
Same-target table (order expressed only where a dependency exists; Harmony priorities are global, so no gratuitous priorities):

| Target | Patches (new names) | Dependency | Attribute |
|---|---|---|---|
| `Pawn.DynamicDrawPhaseAt` | Lances `..._LanceBlurRecovery` (Postfix), Mounting `..._Rider` (Prefix south / Postfix other facings), WeaponWheel `..._WeaponWheelAnimation` (Postfix) | none: wheel animation returns early for any pawn involved in a mount (`IsCombatDisabledByMount`, `Combat.cs:244-261`), rider drawing only for carriers, blur only for landed lance users | none (document) |
| `Verb.get_WarmupTime` | `..._MountedCast`, `..._WheelChain` | none: disjoint verbs (wheel combat is disabled for mounted pawns), both only set 0 | none |
| `Pawn_EquipmentTracker.Notify_EquipmentRemoved` | WeaponWheel Prefix+Postfix, Mounting `..._MountedVerbs` Prefix | none: the Mounting prefix tests the internal-transfer flag itself | none |
| `Verb.TryStartCastOn` | Abilities `Harmony_ThrowFiringLock_TryStartCastOn` (Prefix, `Priority.High`), WeaponWheel Prefix/Postfix | throw lock first — already explicit | keep |
| `PawnGenerator.GeneratePawn` | Corporate uniforms, LifeStages, Restraints, WeaponWheel loadout (Postfixes) | functionally independent (apparel/locks vs reserve weapons); only RNG draw order differs | none (document) |
| `Pawn.TryGetAttackVerb` | `..._ReserveWeaponGuard` | must run last | keep `[HarmonyPriority(int.MinValue)]` |

### S2-9 MUST — feature DefOfs and dead content
`MountingDefOf` (JobDef `Mugirl_Mount`, `Mugirl_Dismount`), `LancesDefOf` (`Mugirl_LancePointCharge`, `Mugirl_LanceLineCharge`, ThingDef `Mugirl_LanceChargeFlyer`),
`WeaponWheelDefOf` (`Mugirl_LoadWeaponWheel`, HediffDef `Mugirl_LinkedSwordDance`, ResearchProjectDef `Mugirl_Autoloading`), each `internal`, with
`static <X>DefOf() => DefOfHelper.EnsureInitializedInCtor(...)`. Delete `Mugirl_LanceChargeSpeed` (L6) and the unused field `Mugirl_AutoloadingSystem`
(`Mugirl_DefOf.cs:55`; existence becomes a validator content contract if wanted). SHOULD: drop the apparel tag `MugirlAutoloadingSystem`
(`Apparel_WeaponWheel.xml:50`, no PawnKind references it).

### S2-10 MUST — Weapons content
`MugirlOnlyEquipmentExtension` without `enabled`; PMC/OPC renames (spec); content moves (spec); split AT23 entries out of the moved DefInjected file;
`Mugirl.Weapon.MugirlOnly` moves from `Keyed/Lances.xml:11` to new `Keyed/Weapons.xml` (key unchanged; also used by the AT23 autocannon, not only lances);
replace the flame-thrower placeholder description (X5). Risk: low.

### S2-11 SHOULD — mount carriers are declared by the comp (S5-Q3)
Today a carrier must pass `IsMugirl` (`MountEligibilityService.cs:18`, `FloatMenuProvider_MountMugirl.cs:25`) and rider rendering gates on
`MugirlIdentity.IsMugirlPawn(carrier)` (`Harmony_MountRendering.cs:33`); WhiteFluff Harmony-patches `IsMugirl` to opt in. New: a carrier is any pawn whose def has
`CompProperties_Mount`; `MountUtility.CanCarry(ThingDef)` answers from a bool array indexed by `def.index`, built once on startup (StaticCacheLifecycle: process).
For all known defs (Mugirl, Subspecies via MugirlBody, WhiteFluff via its patch) the result is identical; no Harmony on Mugirl methods is needed by dependents.

### S2-12 SHOULD — break the Mounting↔WeaponWheel cycle
Mounting reads `WeaponWheelTransferScope.IsInternalTransfer` (`Harmony_MountedCombatController_EquipmentRemoved.cs:13`) while WeaponWheel reads mount state.
Move the "internal equipment transfer in progress" flag to Core (`Mugirl.Core.EquipmentTransferScope`, written by WeaponWheel, read by Mounting) — core owns the file.

### S2-13 SHOULD — back-layout override file only in DevMode (S5-Q4)
`WeaponWheelBackLayout` reads `Config/Mugirl/WeaponWheelBackLayout.xml` in every session; a stale export silently beats the shipped defaults (its own comment
`:8`). Read it only when `Prefs.DevMode`; export stays a dev tool. Behaviour change only for non-dev users with a leftover file (fixes that defect).

### S2-14 MUST — no "Moo", member names
`MooPawn`→`Carrier`, `MountedPawn`→`Rider`, `HasMountedPawn`→`HasRider`, `MooInd`/`Moo` (JobDrivers, FloatMenu provider) →`CarrierInd`/`Carrier`,
`GetMountComp`→`MountOf`, `GetMountForRider`→`MountCarrying`, `IsMounted`→`IsRiding`; `ShouldSuppressExternalMeleeAttackAnimation`→`IsSwordDanceStrikePending`.
Cross-owner call sites in S4.

### S2-15 SHOULD — smaller cleanups
`CompMount.Props` non-null cast like the wheel; JobDrivers/props `sealed`; wheel maintenance by `pawn.IsHashIntervalTick(60)` instead of
`nextMaintenanceTick` (runtime only); per-tick `EnsureCollections` removed (W1); public→internal for types not instantiated by reflection.

---------------------------------------------------------------------------------------------------------------------
## S3. New save schema after Wave S

A. Pawn element, owner ThingWithComp(`CompWeaponWheel`) (race `Mugirl`)
| label | kind | type | default / notes |
|---|---|---|---|
| `weaponWheelReserveWeapons` | Deep (ctorArg comp) | `ThingOwner<ThingWithComps>` | always written; children `maxStacks`(999999) `contentsLookMode`(Deep) `removeContentsIfDestroyed`(true) `dontTickContents`(false) `innerList` (`li Class="ThingWithComps"`) |
| `weaponWheelSlots` | Collections Deep | `List<WeaponWheelSlot>` (no Class attr) | always written; length = `maxSlots` (6) |
| ↳ `li/weapon` | References | `ThingWithComps` | null; points into the reserve owner or the pawn's primary |
| ↳ `li/unlocked` | Values | bool | false |
| `weaponWheelSwordDanceFailureStreak` | Values | int | 0 (clamped to 0..GuaranteeFailures-1 on load) |
Removed: `weaponWheelUnlockedSlots`.

B. Pawn element, owner ThingWithComp(`CompMount`) (Mugirl; third-party: Subspecies races, WhiteFluff)
| label | kind | type | default |
|---|---|---|---|
| `mountRiders` | Deep (ctorArg comp) | `ThingOwner<Pawn>` | always written (`maxStacks` 1, `removeContentsIfDestroyed` False, `innerList` of `li Class="Pawn"`) |
| `mountFireAtWill` | Values, forceSave | bool | true |
| `mountTurret` | Deep | `MountTurret` (no Class attr) | always written |
| ↳ `aimTarget` | TargetInfo | LocalTargetInfo | Invalid |
| ↳ `aimTicksLeft` | Values | int | 0 |
| ↳ `castStartTick` | Values | int | -1 |
| ↳ `burstCooldownTicksLeft` | Values | int | 0 |
| ↳ `lastAttackedTarget` | TargetInfo | LocalTargetInfo | Invalid |
| ↳ `lastAttackTargetTick` | Values | int | 0 |
Removed: `innerContainer`, `fireAtWill`, `physiologicalTickCounter`, `safetyTickCounter`, `turretTickCounter`, `riderMeleeTickCounter`, `turret*` (7).

C. Weapon element, owner ThingWithComp(`CompLanceCharge`) (`Mugirl_NormanLance`, `Mugirl_HeavyKnightLance`, `Mugirl_SteamKnightLance`)
| label | kind | type | default |
|---|---|---|---|
| `lancePointCooldownUntilTick` | Values | int (absolute tick) | 0 |
| `lanceLineCooldownUntilTick` | Values | int (absolute tick) | 0 |
Removed: `pointCooldownUntilTick`, `lineCooldownUntilTick`.

D. Thing element `Class="Mugirl.Features.Lances.PawnFlyer_LanceCharge"` (def `Mugirl_LanceChargeFlyer`), besides vanilla `PawnFlyer` labels
| label | kind | type | default |
|---|---|---|---|
| `lineCharge` | Values | bool | false |
| `steamTrail` | Values | bool | false |
| `pointTarget` | References | Pawn | null |
| `pointImpactApplied` | Values | bool | false |
| `hitPawns` | Collections Reference | `List<Pawn>` | always written |
| `wallHitPointsRemaining` | Values | int | 1550 |
| `lastPassableCell` | Values | IntVec3 | Invalid (valid from spawn on, so always written) |
Removed: `smokeTick`, `cameraShakeTick`.

E. Jobs (vanilla Job/JobDriver nodes)
| JobDef | driver Class | toils | Job fields used |
|---|---|---|---|
| `Mugirl_Mount` | `Mugirl.Features.Mounting.JobDriver_MountMugirl` | 3 (goto, wait 45, mount) | targetA carrier |
| `Mugirl_Dismount` | `Mugirl.Features.Mounting.JobDriver_DismountMugirl` | 2 (goto unless self, dismount) | targetA carrier |
| `Mugirl_LoadWeaponWheel` | `Mugirl.Features.WeaponWheel.JobDriver_LoadWeaponWheel` | 2 (goto, attach) | targetA weapon; `count` = slot index + 1 |
| `Mugirl_LancePointCharge` / `Mugirl_LanceLineCharge` | `Mugirl.Features.Lances.JobDriver_LanceCharge` | 1 (instant launch) | targetA pawn/cell, targetB point cell; `count` 1 = launched (job also sits in the flyer's `jobQueue`) |

F. Save-visible defNames of the module: JobDefs above; ThingDefs `Mugirl_LanceChargeFlyer`, `Mugirl_AutoloadingSystem`, lances, `Mugirl_Gun_*`,
`Mugirl_AT23Autocannon`, `Mugirl_PMC_*`/`Mugirl_OPC_*` and their `Bullet_Mugirl_*` projectiles (+ implied `Make_*`); HediffDef `Mugirl_LinkedSwordDance`;
ResearchProjectDef `Mugirl_Autoloading`. Enums saved by name: none. Custom `GetUniqueLoadID`: none. No GameComponent/MapComponent in this module.

SaveUpgrade steps of the module (`Source/MugirlRace/SaveUpgrade/Steps/Combat/`), after the generated type/def/ThingID passes, in this order:
`Combat.MountSchema`, `Combat.WheelMaterialize`, `Combat.WheelSlotRecords`, `Combat.LanceCharge` (L5, may live in the LegacyLabels table instead),
`Combat.LanceFlyer`. No post-load (phase A/B/C) step is needed.

---------------------------------------------------------------------------------------------------------------------
## S4. Edits needed from other owners

1. Core (`Source/MugirlRace/Core`, `DefOf`, `Compatibility`, `UI`):
   - Compatibility rewrite onto S2-1: delete the 10 self-patch classes listed in S2-1 and register instead (DualWield: full-firepower veto, cast-interception
     veto, foreign-equipment filter, primary-swap guard; RunAndGun: full-firepower veto; YayoCombat: reserve ticker (60) + generated-weapon initializer, keep its
     vanilla patches but use `WeaponWheelLookup.HoldingReserveWeapon`/`.For`; MeleeAnimation: primary-changed listener + `MountingHooks` draw observer, its vanilla
     `Notify_MeleeAttackOn` patch uses `IsSwordDanceStrikePending`; YayoAnimation: body draw-position provider; TrainingFacility: combat bypass).
   - Lifecycle hub registration API (S2-6) and removal of `MountedCombatController.ResetTransientState()` from the hub's predecessor.
   - `MugirlTickUtility.Add`/`ConsumeReady` (`Core/MugirlTickUtility.cs:53-71`): no remaining user after S2-4 → delete (core.json planned to move them to Mounting; not needed).
   - `[MugirlPatch]` closed module set: `Mounting`, `Lances`, `WeaponWheel`, `Weapons`.
   - `DefOf/Mugirl_DefOf.cs`: fields `:46,48,50,55,57,63,65,67,69,75` leave for the feature DefOfs (S2-9); delete `:55`, `:67`.
   - `UI/MugirlSettingsWindow.cs:57,72` must not reference `WeaponWheelDevLog` (settings write the flag; the dev log reads settings / registers its own section).
   - Optional S2-12: add `Mugirl.Core.EquipmentTransferScope`.
   - A home for `IsHumanlike`/`HasCapacity`/`IsAwake` (today `MountedPawnUtility.cs:42-66`) or inline vanilla calls.
2. Incidents: delete `MountedCombatController.ResetTransientState();` at `F/Incidents/Story/MugirlStoryState.cs:78`.
3. Corporate: `F/Corporate/People/CorporateNetwork.People.cs:372-379` → `MountUtility.MountOf(pawn)`, `.HasRider`, `TryDismount`, `MountUtility.MountCarrying(pawn)`.
4. Abilities: `F/Genes/Shared/MugirlJumpUtility.cs:56-67` → `MountOf`, `.Rider`, `TryDismount`, `TryEmergencyDismountNear`.
5. Roping: `F/Roping/UI/FloatMenuProvider_RopeMoo.cs:31` stops using `MountedPawnUtility.HasCapacity` (Core helper or vanilla `health.capacities.CapableOf`).
6. Restraints: its post-load lock step (module report A4) enumerates pawns inside riders' containers via `CompMount.Rider`/`riders`, not `innerContainer`.
7. Race content owner (`1.7/Defs/ThingDefs_Races/Mugirl_Race.xml`): nothing beyond the tool's `Class=` rewrite at `:1040,1054` (props field names unchanged).
8. Content keyed split (`spec/content-keyed-split.json`, owner of `Misc_Gameplay.xml`): move 47 `Mugirl.WeaponWheel.*` keys → `Keyed/WeaponWheel.xml` and
   34 `Mugirl.Mount.*` keys → `Keyed/Mounting.xml` in both languages; this module owns the new files afterwards. `Mugirl.Settings.WeaponWheelDevLog.*` and
   `HideBackWeapons` stay with Settings.
9. Rendering: `Rendering/PawnEffects/LanceMotionBlurPass.cs:98` keeps using `Mugirl.Lance.MotionBlur.Unavailable` from `Keyed/Lances.xml` (no change, noted).
10. Validator: regenerate `patch-targets.txt`; legacy corpus dispositions (Dropped `Mugirl_LanceChargeSpeed`; consumed labels per S2-3/S2-4/S2-7);
    allow the `Bullet_Mugirl_` projectile prefix if S5-Q2 is accepted; drop E8 baseline entries of this module.
11. Tests owner of GameValidation: UI/Render/Startup suites keep `ITab_Pawn_WeaponWheel` (unchanged name); add the upgrade-suite fixtures S6 (no wheel nodes),
    S7/S10 (wheel + mount nodes).

---------------------------------------------------------------------------------------------------------------------
## S5. Open questions (proposed answer first)

1. Restructure `weaponWheelSlots`+`weaponWheelUnlockedSlots` into slot records (S2-3)? **Yes** — design §3.4 forbids parallel lists; one DOM step, real fixtures exist.
2. Projectile names: keep the vanilla role prefix (`Bullet_Mugirl_*`, 4 unchanged, 8 renamed `Bullet_PMC_*`→`Bullet_Mugirl_PMC_*`) or strictly
   `Mugirl_Bullet_*` (12 renames)? **Keep `Bullet_Mugirl_*`** and list `Bullet_` with the vanilla-implied exceptions of §3.3.
3. Mount carriers declared by the comp instead of `IsMugirl` (S2-11)? **Yes** — identical for all known races; WhiteFluff's 1.7 port then needs no Harmony
   patch on `Mugirl.MountedPawnUtility.IsMugirl` (that method disappears either way).
4. Back-layout override only in DevMode (S2-13)? **Yes.**
5. Prefix lance cooldown labels (`lance…`)? **Yes** (§3.4 shared-element rule; no real save holds them).
6. Owner of `1.7/Mods/Biotech/Languages/ChineseSimplified/DefInjected/ThingDef/RangedWeapons_Biotech.xml` (only the flame thrower)? **combat** (claimed in spec).
7. Step folder name: `SaveUpgrade/Steps/Combat/` (module key) rather than one folder per feature? **Combat.**
