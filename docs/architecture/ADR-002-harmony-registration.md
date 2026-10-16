# ADR-002: Harmony 注册、补丁元数据与补丁顺序

状态：Accepted（2026-10-15 修订）。1.6 版的 `MugirlPatchInfo` / `MugirlPatchCatalog` 手写元数据与 `MugirlPatchRegistry` 手动注册已在 1.7 中删除，由下文取代。

## Decision

- **只用特性补丁。** 所有 Harmony 补丁都是带 `[HarmonyPatch]` 特性的类，不写手动 `harmony.Patch(...)` 调用，也不调用 `PatchAll`。
- **按类隔离应用。** `Mugirl.Core.HarmonyBootstrap`（`Source/MugirlRace/Core/HarmonyBootstrap.cs`）在 Mod 构造函数中逐类调用 `harmony.CreateClassProcessor(type).Patch()`：
  - 一个类失败只影响它所属的功能，并记录一次带完整异常的警告。
  - 类按完整类型名的序数排序登记。
  - 标注 `Deferred = true` 的类推迟到加载长事件结束后登记。目前只有 `Harmony_StylingStationRefresh` 一个，因为它的目标类型的静态构造会加载贴图。
  - Harmony id 是 `MugirlMod.Mod`（`HarmonyBootstrap.HarmonyId`），属于公开 API（[ADR-008](ADR-008-public-api.md)）。
- **每个补丁类都标注 `[MugirlPatch(module, risk, failureBehavior)]`**（`Core/MugirlPatchAttribute.cs`），记录无法从代码推导的信息：
  - 所属模块；
  - 失效风险 `MugirlPatchRisk`（Low/Medium/High/Critical）；
  - 补丁未生效时玩家看到的行为。

  目标方法、补丁种类和是否跳过原方法，由 Harmony 特性与方法签名本身表达。
- **补丁顺序只用显式优先级。** 同一目标、同一种类上有多个 Mugirl 补丁时，每个补丁方法都写 `[HarmonyPriority]`，取值互不相同。
  - 本 Mod 的补丁共用一个 owner，`[HarmonyBefore]`/`[HarmonyAfter]` 无法在它们之间排序，只用于其他 Mod 的 Harmony id。
  - 执行顺序不能依赖登记顺序：给类改名就会改变登记顺序。
- **计划（Wave R2）：** 模块参数改为封闭枚举 `Mugirl.Core.PatchModule`，不再是任意字符串。规格见 `tools/RenameApply/specs/R2/patchmodule.json`：
  - 27 个成员，来自 Core、VanillaFixes、Rendering、SaveUpgrade，以及 `Features/` 与 `Compatibility/` 的文件夹名；
  - 按文件路径自动改写现有特性参数。

## Context

本 Mod 有一百多个小型补丁。2026-10-15 的统计：132 个补丁类，默认 Mod 组合下其中 14 个被 `Prepare()` 跳过。

1.6 已经逐类应用特性补丁，但有两处与此不同：

- 元数据写在单独的手写清单 `MugirlPatchCatalog` 中，与补丁类分离，容易漏登记。
- 少数补丁由 `MugirlPatchRegistry` 手动注册。

`Harmony.PatchAll` 没有隔离：第一个抛异常的类会让后面的类都不打补丁，所以 1.7 继续不用它。重构前，多个 Mugirl 补丁之间的执行顺序只由类名排序隐式决定。

## Rationale

- 逐类应用把故障范围限制在单个功能，日志能指出失败的类。
- 元数据写在补丁类上，不会与代码脱节；静态验证能逐类核对。
- 显式优先级让顺序在改名、移动文件之后保持不变。A4 以重构前的 DLL（sha256 `220e5d1a…`）为准，给多补丁目标加了显式优先级，复现当时的实际顺序（默认 Mod 组合下 7 组，全部兼容 Mod 启用时 12 组）：
  - 推导见 [a4-patch-order.md](../refactor-1.7/wave-a/a4-patch-order.md)；
  - 约束锁的 `GeneratePawn` 后缀固定为 `Priority.Last`（C08）。

## Enforcement

| 检查 | 内容 |
|---|---|
| `B.patch-metadata` | 每个补丁类都有 `[MugirlPatch]`；没有补丁的类不能带它 |
| `B.patch-targets` | 补丁目标解析结果与 `Baselines/patch-targets.txt` 一致 |
| `B.harmony-apply` | 离线用 `HarmonyBootstrap.Apply` 对 1.7 游戏打全部补丁，零失败后 `UnpatchAll` |
| `B.patch-order` | 多补丁目标的实际顺序与 `Baselines/patch-order.txt` 一致 |
| `B.transpilers` | Transpiler 插入的钩子与 `Baselines/transpiler-hooks.txt` 一致，IL 结构有效 |
| `B.save-confinement` | 给存档读取内部打补丁的类只能在 `SaveUpgrade/` 下（[ADR-007](ADR-007-save-schema-upgrade.md)） |
| 开发者模式日志 | 汇总 "Patch audit"：类数、元数据、失败、实际生效的类与高风险补丁 |

## Consequences

- 新增补丁时同时写 `[MugirlPatch]`。如果与已有 Mugirl 补丁共用目标，还要写 `[HarmonyPriority]`，并用 `--update-baselines` 有意识地更新 `patch-order.txt`。
- 补丁类命名为 `Harmony_<TargetType>_<TargetMember>[_<Purpose>]`，放在拥有该行为的功能中（[ADR-009](ADR-009-naming-and-layout.md)）。现有类名由 Wave R 统一改名。
- 读档升级钩子是 `SaveUpgrade/Hooks/` 中的普通特性补丁。`SaveUpgradeInstallation` 在启动时用 `Harmony.GetPatchInfo` 确认它们由本 Mod 安装（[ADR-007](ADR-007-save-schema-upgrade.md)）。
- Compatibility 层对 Mugirl 自己方法的补丁是待消除的债务（[ADR-003](ADR-003-compatibility-scope.md)）。
- 补丁必须在 Mod 构造函数中注册：只返回字段的小 getter 会被 JIT 内联进调用方，而调用方在补丁之后才编译，补丁才对它们可见。公开的可打补丁方法因此要带 `NoInlining`（[ADR-008](ADR-008-public-api.md)）。
