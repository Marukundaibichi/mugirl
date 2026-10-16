# Orchestrator decisions (v1, 2026-10-15) — amends design.md; where they differ, THIS FILE WINS

Inputs: spec/critique-upgrader.md (U#n), spec/critique-validation.md (F#n), spec/consistency.md (C#n).
Items marked OWNER are pending the owner's answer; the default in brackets applies until answered.

## 1. Scope and public surface
- D1 (U#3): add era row "MugirlRace-era builds running on the 1.7 game (published Workshop build loads via the v1.6 fallback)" with FULL
  support. NO step, detector or rule may branch on `meta/gameVersion`; applicability is by XML shape only.
- D2 (F#2): legacy builds are identified by sha256 and provenance in three tiers: published (DevData/Releases/*.zip, Workshop 3578882219,
  git release commits) = required; observed (every name in a real save) = required; dev (other git blobs incl. refs/stash, refs/codex/*,
  WRace/obj, DevData/Backups/**, the frozen working-tree DLL + its content) = Info. The pre-refactor 1.7 snapshot is informational only.
- D3 OWNER (U#4) [default: (a)]: MooGirl era (0.1.0–0.4.4) = (a) best-effort: def/type names mapped, MooGirl race pawns kept (def `MooGirl`
  -> `Mugirl`, IDs rewritten), MooGirl-era-only labels Dropped with reasons, body/addon order verified equal to legacy `MooGirlBody` or
  remapped by a step; (b) alternative: Mugirl content dropped deliberately + one notice.
- D4 (U#1, U#2): the three XML classes used by other mods keep working through THIN PUBLIC ALIAS SUBCLASSES, not a GenTypes patch:
  `Mugirl.StockGenerator_Mugirl_Slaves : Mugirl.Features.Trade.StockGenerator_MugirlSlaves`,
  `Mugirl.CompProperties_MugirlMount : Mugirl.Features.Mounting.CompProperties_Mount`,
  `Mugirl.CompProperties_MooMilkable : Mugirl.Features.Milk.CompProperties_MilkProduction` — all in ONE file
  `Source/MugirlRace/PublicApi/XmlAliases.cs` (the only root-namespace types; validator allowlist; ADR documents them). The base classes keep the
  XML FIELD NAMES dependents set (Subspecies: displayString, milkDef, milkIntervalDays, milkAmount; KindSeal: slaveCountRange; WhiteFluff
  mount props fields) — field renames on these three are forbidden. Our own XML uses the new names. No `GenTypes` postfix (U#1, F#20).
  No `GetBackCompatibleType` postfix (U#15); validator rule: a renamed Mugirl type deriving from a side-document root (ScenPart, Precept,
  ritual types, …) makes that postfix mandatory.
- D5 OWNER (U#2) [default: keep]: names referenced by WhiteFluff/Subspecies (1.6-only local mods, likely the owner's): C# types
  `Mugirl.MountedPawnUtility.IsMugirl`, `Mugirl.Comp_MugirlMount`; defNames `Mugirl_Lactation`, `MugirlMilkFilth`, `Job_MugirlMilkingTargetWait`,
  `Mugirl_Charge`, `Mugirl_Shout`, `Mugirl_NobelPrize`, `MugirlBody`; abstract Names; `<Mugirl.SlaveApparelDef>` element. Default until the owner
  answers: defNames/Names/XML-visible names they use are FROZEN (added to §2); their C# compile-time references are NOT a supported API (a 1.7
  WhiteFluff build is needed anyway); `<Mugirl.SlaveApparelDef>` → see D6.
- D6: `SlaveApparelDef` → `RestraintExtension` (restraints plan) is accepted; Subspecies' `<Mugirl.SlaveApparelDef>` defs break unless the owner
  updates Subspecies (OWNER via D5). If D5 = keep, keep a public alias Def subclass `Mugirl.SlaveApparelDef : ThingDef` in PublicApi that maps
  its legacy fields into a RestraintExtension in ResolveReferences.
- D7: Public/frozen list additions: display name `Mugirl Race`; `Ears` BodyPartGroupDef (C30); `Job_Charge` (UFPC); `MugirlLike`.

## 2. Upgrader architecture amendments (design §5)
- Hook: keep `Game.LoadGame` Prefix (Priority.First). Read meta from `curXmlParent.OwnerDocument.DocumentElement["meta"]`, never from
  `ScribeMetaHeaderUtility` statics (U#17). Finalizer is `void`/returns `__exception` unchanged: it only clears the session (U#5).
- Installation check at startup: `Harmony.GetPatchInfo` proves the LoadGame / FinalizeLoading / FinalizeInit / BackCompatibleDefName patches
  are owned by `MugirlMod.Mod`; otherwise a startup error dialog and `Installed=false` (U#6).
- Steps are plan→apply: plan is pure and may throw, apply uses primitive DOM ops only. First failing plan aborts the pipeline; session =
  Failed(stepId); one main-thread dialog via `ExecuteWhenFinished` ("upgrade failed at X; do not overwrite this save; send the log") (U#5).
- Backup OWNER [default: yes]: on the first legacy load of a file, copy it once to `<name>.rws.mugirl-schema0.bak` beside it before any edit.
- Stamp (U#6): `GameComponent_Mugirl` writes `mugirlSaveSchema` with forceSave. It writes `Current` only for a new game, or when this game's
  session completed (or found the save already at Current); otherwise it writes the source schema and logs an error. Its ctor is trivial (no
  DefDatabase/Find/statics) — validator rule for all GameComponent/MapComponent ctors (U#13).
- Steps that materialise OLD defaults require positive legacy evidence on the owner element (legacy-only sibling label, legacy Class or legacy
  def); "no stamp" alone is not enough (U#6).
- Def renames (U#7): every Scribe_Defs-backed reference is renamed ONLY by the `BackCompatibleDefName` postfix:
  `[HarmonyPriority(Priority.Last)]`, keyed on the `defName` ARGUMENT, defType normalised to its database root (walk BaseType until
  `BaseType == typeof(Def)`), refuse when the root is `Def`, override only when `GetDefSilentFail(root, __result) == null`, never gated by the
  session (serves xenotype/ideo/scenario files and third-party DefInjected keys — U#16 corrects engine-hooks pitfall 20).
  The DOM renames def names only in: string-typed contexts from an explicit list (U#21), load-ID tokens (U#8), and drops. The DOM resolves
  def names virtually for owner selection; Things are recognised structurally (`<def>` + `<id>` = defName+digits, or in the ID map); hediffs only
  under `hediffSet/hediffs/li`.
- Load IDs (U#8): template table generated from the def map: `<id>`, `Thing_<ThingID>`, `Bill_<RecipeDef>_<n>` (incl. implicit
  `Make_<thingDef>`), verb ids `<ownerID>_<toolId>_<ManeuverDef>`, `<ownerID>_<index>`, `CompEquippable_<ThingID>_<i>`, derived
  Corpse/Blueprint/Blueprint_Install/Frame/Meat/Make defs. New ID = newDefName + vanilla-parsed number of the old ID. Rename targets of
  ThingDef/RecipeDef must not end in a digit; ManeuverDef and weapon tool ids are frozen unless a verb-ID step exists; motes excluded.
  2026-10-16 source correction: `Verb.CalculateUniqueLoadID` embeds `tool.id` and `maneuver.defName`, not ToolCapacityDef names.
  ToolCapacityDef retirement still needs a typed Def disposition; it must not claim to consume a nonexistent serialized verb-ID segment.
- In-flight state (U#9, F#10f): LordJob graph changes are DOM steps (remove lordToilData/triggerData/curLordToilIdx of the affected lordJob Class,
  or remove the lord with its references). Default policy on schema-0 loads: in Phase B (`Game.FinalizeInit` prefix) end in-flight jobs whose
  driver is a Mugirl type (`EndCurrentJob(InterruptForced)`) and queued jobs of Mugirl JobDefs, unless a step claims the type. Validator requires a
  policy or a claim for every legacy Mugirl JobDriver/LordJob type.
- Owner scoping (U#10): selectors for Mugirl defs evaluate the LEGACY def→comps relation, recorded in the corpus and compiled into the upgrade
  tables for the defs whose comp labels change; `IDefCatalog` abstracts DefDatabase (runtime) vs validator ContentModel (offline) (F#14).
- Defaults (U#11, F#10a/h): corpus records default constant (or "def-dependent"), forceSave, value type, Scribe kind, LookMode per (owner,label,build).
  Validator fails on default drift without a materialisation entry. Dynamic defaults (set from Props/Initialize) require forceSave.
- Lists (U#12): items may be removed only from containers the schema graph marks Removable (filter/HashSet lists, ThingOwner innerList, things
  lists); dictionaries pairwise; declared parallel groups together; everywhere else null out.
- Lifecycle hub (U#13, C17): `Mugirl.Core.GameComponent_Mugirl` + `GameLifecycle` registry. Ctor side-effect free; found via
  `Current.Game.GetComponent`. Events: `BeforeLoad` (raised from the LoadGame prefix before the DOM pass), `BeforeNewGame` (InitNewGame prefix),
  `FinalizeInit`, `StartedNewGame`, `LoadedGame`, `AddedToExistingGame` (Mugirl absent from the save's modIds). Registration order is explicit
  (sorted), never reflection order (F#7). Phase C copies session diagnostics into the hub.
- Newer schema (U#19): main-thread dialog "written by a newer Mugirl; saving here loses data"; no edits.
- Settings (U#18, F#11): XmlDocument parse, `settingsSchema` (forceSave), version-driven upgrade, temp-file + replace, settings class moves to
  `Mugirl.Core.MugirlSettings` and the documented consequence for the frozen 1.6 build (one error per 1.6 startup, renamed keys reset).
  MooGirl-era settings files (`Mod_*_MooGirlMod.xml`) are not migrated.
- Format/perf (U#22): DOM writes use Scribe formats (invariant culture, `True/False`, enum ToString); budget ≤ 1 full traversal + 1 ID pass and
  ≤ 2 s on the 67 MB save S7, measured by the corpus run and logged in the summary line. `MugirlLog` must be thread-safe (worker thread).
- Labels (U#23): labels are chosen equal to the field name when introduced and then frozen strings; the validator compares with the corpus, not
  with field names. Comp labels in SHARED elements use the `mugirl` prefix (`mugirlMilk…`, `mugirlMount…`), because third-party races carry
  Mugirl comps next to their own `milk…` comps.
- Upgrade code layout (C22, F#13): `SaveUpgrade/Engine/**` (framework), `SaveUpgrade/Hooks/**`, `SaveUpgrade/Modules/<Module>/` holding that
  module's table contributions (`partial` classes / registration), DOM steps and post-load steps; one owner per folder. Golden step fixtures in
  `Source/Tests/MugirlRace.Tests/SaveUpgrade/Golden/<StepId>/{before,after}.xml` contain only synthesized/anonymised Mugirl-owned nodes (F#15).

## 3. Execution amendments (design §4)
- Wave 0 (now): tree archive `DevData/Backups/PreRefactor-1.7-20261015/tree.zip` + manifest; baseline reports (Validate both runtimes + SelfTest,
  unit tests, ShaderCheck, SmokeTest, Test-InGame default) archived in `DevData/Evidence/Baseline-PreRefactor-20261015/`. Baseline commit = OWNER.
- Wave A (parallel, exclusive ownership per F#13 §5 table): A1 upgrader (`SaveUpgrade/**`, `Core/GameComponent_Mugirl.cs` + `Core/GameLifecycle*.cs`
  NEW files, `Tests/MugirlRace.Tests/SaveUpgrade/**`), A2 validator (`Tests/MugirlRace.Validation/**`, `Baselines/legacy/**`,
  `tools/Export-LegacySchema.ps1`: corpus over all tiers, C.save-upgrade in RATCHET mode, `--parity` IL+content mode with calibration and
  negative controls, `--save-corpus` mode, C.definjected-paths, C.content-shadowing, B.patch-order, namespace-shadow rule, root-namespace rule
  with PublicApi allowlist, GameComponent-ctor rule, confinement rule for Scribe internals), A3 spec finalisation + linter + tool hardening
  (scratchpad only), A4 build/bootstrap (`Directory.*.props`, csproj, slnx, `global.json` SDK pin, `Core/HarmonyBootstrap.cs`,
  `Core/MugirlPatchAttribute.cs`, `Core/MugirlMod.cs`, `[HarmonyPriority]` lines reproducing today's effective order on every multi-patch target,
  `tools/Validate.ps1 -WorkRoot`, OutputPath: builds never write into `1.7/Assemblies` except an explicit deploy target, `tools/SmokeTest.ps1`),
  A5 fixtures + in-game framework (`Tests/MugirlRace.GameValidation/**`, `tools/Test-InGame.ps1` -Fixture/lock/boots, `tools/New-SaveFixture.ps1`,
  P fixtures replayed from published builds 1004, 0927, 0920, 0.5.0, 0.4.7 (+ MooGirl 0.4.4 if D3=a) with a feasibility gate each).
  In-game runs are serialized by `%TEMP%\mugirl-ingame.lock`.
- Integration after A: the integrator (orchestrator) wires `SaveUpgradeBootstrap.Initialize(this)` into the Mod ctor, regenerates/reviews
  baselines, archives the pre-R build as parity baseline (F#3).
- Wave R = identity-preserving renames ONLY (F §2.1): namespaces, types, file moves incl. all cross-module moves and multi-type file splits,
  defNames + DefInjected keys/handles + XML references + abstract Names, translated baselines. Gate F §2.5 (IL parity zero unexplained,
  content parity zero, tests same set all green, validator without --update-baselines, residual-token scan clean, applied log == spec, in-game
  default suite + smoke no new failures).
- Wave R2 (central, sequential, own parity check): DefOf dissolution per the OWNER-of-the-Def rule (C04: a DefOf field lives in the DefOf of the
  feature that DEFINES the def; consumers reference that class), `PatchModule` closed enum, shared XML/Keyed file splits by feature (JobDefs.xml,
  Patches/MugirlPatch.xml, Keyed/Misc_Gameplay.xml, Thoughts_Traits.xml, Recipes_Mugirl.xml), date-named file renames, cross-owner member moves
  (C19). After R2 every file has exactly one Wave S owner.
- Wave S: per-module (own folder, own SaveUpgrade/Modules/<Module>, own tests, own content files), C.save-upgrade ratchet must not worsen; each
  module ends with an adversarial review + fix; behaviour changes only from the module's declared "deliberate change / defect fix" list.

## 4. Conflict resolutions (consistency.md)
C01 `CorporatePricingCompatibility` → `Mugirl.Features.Corporate.CorporateForeignBehavior` (corporate owns; core drops its row; test follows).
C02/C03 DefOf split in R2 from core-member-moves data; corporate.json drops its DefOf type rows. C04 owner-of-the-Def rule (above).
C05 JobDef field keeps the defName; the InteractionDef field gets a different field name bound by `[DefAlias("Mugirl_Nuzzle")]` if 1.7's
DefOfHelper honours DefAlias for binding (verify in decompile), else InteractionDefs go to `SocialInteractionDefOf`.
C06 targets use final names (`Mugirl.Features.Mounting.CompMount`). C07 combat-plan's hook API is authoritative; core plan adapts its
Compatibility side to it. C08 A4 reproduces today's effective order with explicit priorities incl. restraints' Last requirement.
C09/C10/C11/C24 shared/unowned files are split or assigned in R2; race-wide files (Mugirl_Race.xml, BodyDef.xml, BackstoryDef.xml,
Mugirl_ThinkTreeDefs.xml) are owned by content. C12 motes: saveVisible=true (map entry harmless; ID rewrite excludes motes).
C13 fix flags. C14 defType = database root short name (ThingDef for SlaveApparelDef rows). C15/C16 MooGirl maps are composed with the merged
spec into final names; hand overrides become machine-readable rows. C17 `AddedToExistingGame` lifecycle event (above). C18 talk-to-pawn bases →
`Mugirl.Core` (Core/Interaction). C19 R2 member moves. C20 union of both lists in one core step. C21 core provides EquipmentTransferScope and
`PawnPredicates` (IsHumanlike/HasCapacity/IsAwake) in Core; settings window must not reference feature types. C22 layout above. C23 machine-readable
`"transitional": {"waveS": "delete|drop|merge|keep", "upgrade": "<map disposition>"}`. C25 apparel code lives in `Features/Appearance/Apparel`,
namespace `Mugirl.Features.Appearance`; validator rule: no namespace segment may equal a Verse/RimWorld type short name used in Mugirl code
(add `Apparel`). C26 all def categories AFTER the prefix: `Mugirl_Bullet_*`, `Mugirl_Filth_*`, `Mugirl_Mote_*`, `Mugirl_Fleck_*` (rename the 4
existing `Bullet_Mugirl_*` too). C27 multi-type files are split in R via fileMoves/type rows. C28 test class renamed with its file. C29
`Mugirl.Compatibility` (no segment) is allowed for shared compat infrastructure. C30 `Ears` → public list. C31 fix prose. C32 accepted
(transitional names are deleted in S).

## 5. OWNER ANSWERS (2026-10-15, binding)
- D5 = KEEP DEPENDENTS WORKING. Every name that MRK.WhiteFluff (`Mods/乳牛娘 白绒物语`) and har.mugirlrace.subspecies (`Mods/MooGirl_Subspecies`)
  reference from Defs, Patches, Languages or PRODUCTION C# is frozen public API: defNames (e.g. HediffDef `Mugirl_Charge`, `Mugirl_Lactation`,
  ThingDef `MugirlMilkFilth`, `MugirlBody`, `Mugirl_Shout`, `Mugirl_NobelPrize`, FA `Mugirl_<Part>Normal*`, `Mugirl_cry`, `Mugirl_lovin`,
  `MugirlFaceSizeAndPositionDef`), abstract Names, XML class names AND their XML field names (`Mugirl.CompProperties_MooMilkable`,
  `Mugirl.CompProperties_MugirlMount`, `Mugirl.StockGenerator_Mugirl_Slaves`, `<Mugirl.SlaveApparelDef>` with its legacy fields), Harmony id
  `MugirlMod.Mod` (WhiteFluff uses `[HarmonyAfter("MugirlMod.Mod")]`), and the patchable C# extension point
  `Mugirl.MountedPawnUtility.IsMugirl(Pawn)` (WhiteFluff Harmony-patches it): it stays the REAL decision point that mounting code calls,
  `public static`, `[MethodImpl(MethodImplOptions.NoInlining)]`, documented as public API, in `Source/MugirlRace/PublicApi/`.
  Names used only by WhiteFluff's own validation code (Source/Validation/**: `Comp_MugirlMount`, `MountedPawnUtility.IsMounted`,
  `HasMountedPawn`, PawnKind lookups) are NOT frozen; they go into the migration list for the owner. A3 must scan both mods completely and
  produce `spec/public-api.json` (kind, name, used-by file:line, production|validation) — the validator reads it as frozen-forever input.
  `<Mugirl.SlaveApparelDef>` → PublicApi alias Def subclass `Mugirl.SlaveApparelDef : ThingDef` keeping the legacy field names, converting them
  into the new `RestraintExtension` (D6). All PublicApi types live in `Source/MugirlRace/PublicApi/` (root namespace allowed only there).
- D3 = MOOGIRL ERA NOT SUPPORTED. A save carrying MooGirl-era markers (Class `MooGirl.*`/`MooGirlSkinFix.*`, defNames `MooGirl*`/`MuGirl*`) is
  detected; the DOM strips MooGirl-era component elements and MooGirl-era def references from Removable containers (filters etc.); everything
  else is left to vanilla's missing-def handling; the player gets ONE notice (main thread) that MooGirl-era (≤0.4.4) Mugirl content is not
  supported and was discarded. No MooGirl→Mugirl name map (moogirl-defs.json only feeds the detector/stripper list). Fixture for this case: the
  owner's real saves S1–S4 (offline corpus run), no replay of MooGirl builds.
- D6 = NO GIT COMMIT. Baseline = `DevData/Backups/PreRefactor-1.7-20261015/tree.zip` + `tree-manifest.sha256` (done) and the end-of-Wave-A
  tree archived the same way as the parity baseline. No worktrees: parallel agents get strictly disjoint file ownership.
- BACKUP = YES: first legacy load copies the file once to `<name>.rws.mugirl-schema0.bak` (never overwrite an existing .bak).

## 6. Baseline facts (Wave 0, DevData/Evidence/Baseline-PreRefactor-20261015)
- Validate: 42/43 pass; only A.about fails (About.xml still declares 1.6 only — planned for Wave D). Both runtimes identical. SelfTest passed (114 rules).
- SmokeTest -QuickTest -DevMode: PASS (no Mugirl errors/warnings).
- Test-InGame default: 65 ok / 1 failed / 14 skipped. The failure is a TEST BUG in GameValidation `ui/corporate-comms`: it Harmony-patches
  `Window_CorporateComms.DoWindowContents`, which is only declared on the base `CorporateAnimatedWindow` ("You can only patch implemented methods").

## 7. OWNER 补充约束（2026-10-15，恢复重构后，优先于所有既有计划）

- **重构后的玩家可见文案与游戏行为必须与重构前一致，不得更改。** 仅允许保持语义的内部结构调整、性能优化和完成旧档一次性升级所需的兼容处理。
- 模块计划中列为“有意改变/缺陷修复”的玩家行为改动不再自动获准；不改变数值、概率、触发条件、默认选项、事件结果、内容或文本。已核实但与迁移无关的缺陷只记录。
- R/R2 改名只改内部标识符及其引用，翻译键可以对应改名，但玩家可见的翻译值必须逐项相同。反射顺序、Def 顺序或补丁顺序如果影响行为，必须保留；不能用 parity 接受清单豁免玩家行为变化。
- 存档结构可以重构，但旧档升级必须保留数据含义及可恢复的进行中状态。此前的技术退役/状态重置计划若与此要求冲突，应重新设计迁移方式，不把丢失状态作为成功。
- 用户已明确要求从中断点继续直至完成，并允许必要的实机验证；历史“暂停夹具制作”的请求已被本次恢复任务取代。实机验证必须静音、不抢前台焦点，继续使用运行锁与临时存档目录。

## 8. 1016 发布的追加要求（2026-10-16）

- 用户明确要求更新 Git 并打包 1016；本轮可以提交、推送当前成果。此前整树归档和历史“不提交”记录继续保留，不据此改写过去。
- 本机更新为 1.7.4980 后，用户选择先处理兼容问题并通过验证，再打包。未知引擎仍不得自动沿用已核验的读取协议。
- 用户要求旧档升级成功后以信封提示已自动升级并备份旧档，保存新档标记，再次载入新档不重复提示。这是对 §7 文案/行为保持规则的明确、有限追加；其他玩法与数值保持不变。

## 9. 外部未知 ThinkTree 键的处理边界（2026-10-16）

- 玩家报告要求修复 `Framework.ThinkTreeKeys` 误阻断：原版/其他模组的任务来源键也可能在游戏与模组更新后失效，不能把这些未被任何旧账本捕获的键当成雪牛娘历史布局的反证。
- 仅对所属可证明为外部的 ThinkTree、JobDef 和 driver，且所有历史账本与当前树均无该键时，交回原版读取。已捕获的受管键仍须共同布局一致；缺少归属、雪牛娘所属、跨树、当前键冒充旧键或真实查找返回不符，仍严格失败。
- 原版明确查找失败的外部来源必须保留其原整数，不能以删除 `lastJobGiverKey`、强制结束任务或猜节点来取得成功。弱引用记录须跟随真实 Clone，在 Clear 回收时移除；原版警告保留。该写出协议目前只核验了 4980，其他版本保持原来的严格边界。
- 这只是存档升级边界的修正，不改变 JobDriver 的目标、进度、计时或任务执行，也不宣称替全部第三方模组恢复了旧 ThinkTree 图。

## 10. 1016.2 本地交付要求（2026-10-16）

- 用户已收到迁移专项通过、完整控制组未全绿的报告，随后明确要求“更新git，打包发布版1016.2”。本轮据此提交、推送当前迁移修复，并制作本地 1016.2 发布包；沿用实际测试过且逐哈希确认的正式 DLL/PDB。
- 保留污渍读回差异、缺眼角色穿束具报错及全部失败证据，在发布记录中明确完整验收未通过。这次交付不等于这些失败已修复，也不更改后续发布的默认闸门。
- 不借打包改变束具、污渍等无关玩法，不覆盖原存档或旧发布包；没有上传创意工坊的授权。
