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

## 11. 升级失败后重存档的当前 ThinkTree 布局（2026-10-17）

- 用户要求查明并修复 `Player (2).log` 中的升级失败，随后确认该档曾在升级失败后继续游玩并保存。失败会话保留来源 schema 的规则不变；普通 Job writer 已写当前键，因此不能仅因 schema 0 就排除当前布局。
- 当前布局从完整原版键分配流独立捕获，使用实际类型名和原始树名种子，与三套历史布局共同求全档交集。保存树名、捕获键与实际当前节点键均须吻合，不给当前布局优先权，不采用逐 Job 回退。歧义、混合布局、跨树、伪造当前键、旧树名配新键以及未知引擎仍拒绝。
- §9 的外部未知来源原键保留规则继续生效；“无历史证明”不再排除有完整当前账本证明的来源。修复只恢复仍能证明的节点身份，不重置任务、补猜已丢失状态或更改玩法。
- 本次修复授权不包括正式部署、提交 Git、打包发布或上传；实机通过临时候选模组验证，正式 `1.7/Assemblies` 保持不变。

## 12. 当前布局修复的正式编译与测试（2026-10-17）

- 用户先要求“编译并测试”，在正式部署操作因授权边界被自动审批拒绝后，又明确回复“允许进行部署”。本阶段可以通过 `DeployToMod=true` 更新正式 `1.7/Assemblies`，此前候选阶段不部署的记录保留。
- 编译使用独立源码副本，保留现有未提交修复和部署前的完整归档。验证以本次正式 DLL 的实际哈希为准；不手工复制 DLL，不写玩家存档或正常配置。
- 授权范围不包括 Git 提交、重新打包发布或上传。未知旧键仍须保持拒绝；另一个存档的原模组环境未重建，不将专项通过当作它已恢复。

## 13. 1016.3 本地打包要求（2026-10-17）

- 用户随后明确要求“打包发布版1016.3”。本轮据此制作当前已部署修复的本地发布包，沿用实际通过本轮测试的 DLL/PDB，不重建另一份产物；之前部署阶段不打包的范围不再适用于本次请求。
- 保留全部未提交改动，以整树归档和 `-AllowDirty` 清单记录来源；本次没有更新 Git、提交、推送或上传创意工坊的授权。
- 原备份 `566784108` 的来源环境仍未重建，1016.2 扩展控制组的历史失败也没有消除。1016.3 发布记录必须明确这些边界，不将本地包交付表述为原备份已恢复或完整发布验收已通过。

## 14. AI 来源警告策略与部署授权（2026-10-17）

- 用户明确要求：“保留原键和任务进度，无法恢复的 AI 来源降为警告，不再阻断整档升级。修完编译、部署并测试，重点复现这次日志里的两种失败。”本条取代 §9、§11、§12 中将未知、跨树或歧义来源作为整档失败的要求；其他结构与读写协议保护继续有效。
- 能证明的来源继续迁移。未知键、错树键不参与全局布局交集；已识别来源相互冲突时不择一猜测，相关来源全部保留。没有恢复的来源保持 null，不结束 Job，不改 JobDef、目标、计数、JobDriver 或 Toil 进度。
- 原整数随 Job 保存，并附可选布尔标签 `mugirlUnresolvedThinkSource`。再次读档时此标签阻止同号节点误绑定；Clone 续接，Clear 和身份变化清除。它是 schema 1 的可选兼容注记，不改变领域结构或默认值，登记在 K1 的 `runtimeProtocols`，配套真实 Scribe 黄金夹具。
- 警告写入诊断 JSON 和日志，首次升级成功信说明影响；阶段完整成功后保存 schema 1，不能因缺失 AI 来源再次落回 schema 0。未知引擎、损坏标记或真实读取协议异常仍保留失败，不把结构损坏标为成功。
- 允许隔离编译、通过 `DeployToMod=true` 正式部署和隔离游戏测试。不授权 Git 提交、重新打包或上传；已交付的 1016.3 ZIP 保持原样。

## 15. 1017 Git 同步与本地发布（2026-10-17）

- 用户随后明确要求“更新git，打包发布版1017”。本轮提交当前已部署的修复、相关测试和文档，并按现有 `main` → `origin/main` 流程正常推送；不强推，不改写历史。
- 1017 沿用已完成专项、默认游戏回归和历史读回的 DLL/PDB，打包前复核正式产物与来源。发布清单应绑定干净的来源提交；打包结果另外用文档提交记录，不为修改文档重新构建二进制。
- 本地交付包括未解析 AI 来源警告策略与原键/任务进度保留，也包含此前尚未提交的当前布局修复。1016.3 等旧 ZIP 保留，不覆盖。
- 已披露的完整玩家模组环境未重建、卫生模组驱动使用测试替身及旧扩展控制组失败仍保留。本次发布请求不把这些边界改判为完整验收通过，也不授权修改无关玩法或上传创意工坊。

## 16. 重审日更兼容与失败边界（2026-10-17）

- 用户提供 `Player (5).log` 与实际旧档备份，明确要求不能因游戏的小版本更新禁用整套机制，也不能把局部迁移问题报告成全部失败；并指出旧 1.6 模组在 1.7 游戏中保存的存档是必须正视的兼容场景。本条优先于前文以引擎 MVID 白名单作为放行条件的要求。
- 生产升级层按正在运行的方法签名、真实字段写出约定及实际键分配结果工作。构建号/MVID 用于诊断和当前模块身份核对，不作为“列入版本名单才可升级”的开关；不靠继续追加 4981/4982 名单解决日更问题。
- 旧 AI 布局捕获是可降级能力，缺失或算法不吻合时保留原键并警告，不禁用独立的数据迁移。缺树、晚到/重复来源、合法整数查询被改写、局部来源查找异常同样保留；缺树不执行会空引用的原版查询，但仍执行 Job 的其他初始化，不结束驱动。
- 不能安全读写的损坏字段、实际必需读取接口缺失、引用字典结构错位等仍保留备份和未完成标记。提示必须说明具体未完成步骤、原因及构建身份，不能继续只显示同一个不透明弹窗。
- 验证重心是实际旧档内容、保存/重载及任务续接，不以编译和测试数量替代结论。玩家原文件只读，缺少第三方模组的环境不得冒充完整玩家世界；所有失败证据保留。

## 17. 代码质量优化授权（2026-10-22）

- 用户在三维度代码审查后明确要求：“按照你的想法进行优化，更新文档，并根据文档做优化。”审查结论、本轮范围与暂缓项见[代码质量审查与优化](../records/code-quality-optimization-2026-10-22.md)。
- 本条取代 §7 中“与重构前逐位一致、反射/类型枚举顺序与随机数调用次数必须保留”的部分：此后不再要求与 1.6 或重构前构建在浮点位、随机数调用次数、类型元数据顺序上一致。玩家可感知的规则、数值、文案与默认选项仍不变；存档语义和可恢复状态的保护（ADR-007）继续有效。
- 本轮允许修复两个已核实缺陷：骑乘期间骑手逐 tick 状态不推进；挤奶释放音效重复播放。其他已核实但未列入范围的缺陷只记录。
- 本轮不改变存档写出结构，唯一例外是删除重复音效组件的瞬态字段并登记丢弃。授权不包括正式部署、Git 提交、打包发布或上传；游戏内验证使用临时候选或测试目录，正式 `1.7/Assemblies` 保持不变。

## 18. 觅食性能修复的部署与 Git 同步（2026-10-25）

- 用户先要求检查性能异常（DPA 中 Mugirl 的 DoTick 尖峰），审阅调查结论后明确要求“部署并推进git”。调查与修复见[觅食植物扫描性能调查](../records/performance-food-search-2026-10-25.md)。
- 部署的是当前完整源码。除觅食修复外，还包括：
  - 已于 10-17 部署、但未提交 Git 的 4981 兼容修复；
  - 已在源码完成、尚未部署的巨企供货兼容修复（10-19）与代码质量优化（10-22，§17）。

  部署使用 `Test-InGame.ps1 -BuildMod -Deploy`，即 `DeployToMod=true`。
- 同一请求授权提交当前源码、测试、文档和新部署的 DLL/PDB，并沿用 §15 的 `main` → `origin/main` 流程正常推送；不强推，不改写历史。
- 不授权打包发布或上传创意工坊。10-22 记录中需要手动核对的实机项（骑乘中骑手推进、挤奶音效等）仍按原状态披露，不因本次部署改判为已验证。

## 19. 投掷建筑连接限制修正（2026-10-26）

- 用户明确预期为“只禁止拔起连接的建筑，如墙体或电缆等，或其它有连接属性的建筑”，并确认普通设备接电后仍可举起。
- 原先所有 `Building` 同格或四边相邻就拒绝的规则改为检查相容的结构连接属性及输电线的输电端连接。普通家具、设备的摆放相邻或接电不触发这条限制；重量、暴力能力、冷却、投掷伤害与存档结构不变。
- 本轮修正源码、文档并进行隔离验证；未授权更新正式 `1.7/Assemblies`、Git 提交、打包或上传。验证见[投掷建筑连接修复](../records/throw-building-connections-2026-10-26.md)。

## 20. 墙壁栓点多余开关修正（2026-10-27）

- 用户明确要求删除墙壁栓点多余的开关组件，并修复、验证。仅从 1.7 的 `Mugirl_WallRopeHitch` 定义移除 `CompProperties_Flickable`；栓系入口、建筑身份、数值与公开 API 保持。
- 这是 XML 内容修正，使用当前正式 DLL 验证，不需要重新部署程序集。验证覆盖实际按钮、栓系任务、旧开关状态读档及拆除解绳；游戏使用共享运行锁和临时存档目录。
- 本轮不提交 Git、打包或上传，冻结的 1.6 内容不变。结果与限制见[墙壁栓点修复记录](../records/wall-hitch-switch-2026-10-27.md)。

## 21. 1027 本地提交与发布包（2026-10-27）

- 用户明确要求“提交git，并打包1027发布版”。本轮提交当前源码、内容、测试、正式 DLL/PDB 和发布记录，生成本地 1027 ZIP；不推送远程、不上传创意工坊，不覆盖旧包。
- 当前投掷修复尚未进入正式 DLL，因此按发布清单先隔离构建，并通过 `DeployToMod=true` 部署候选，再验证同一产物、绑定干净来源提交。原有改动及冻结 1.6 内容先做整树归档并核对。
- 为完成本轮发布验证，修正工具与当前引擎的兼容问题；不放宽真实内容错误、存档比较或历史失败。具体修改、结果与边界见[1027 发布记录](../records/release-1027-2026-10-27.md)。
