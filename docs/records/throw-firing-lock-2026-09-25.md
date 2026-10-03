# 投掷期间禁火记录

## 版本与范围

- 日期：2026-09-25，基于 0.6.7（131ee89）之后的工作区。
- 需求：用户反馈"投掷时，应该无法开枪射击"。
- 范围：雪牛投掷（`Thing_MugirlThrownObject`）进行中禁止持物者远程开火；近战指令与能力施放不在此约束内，灌篮（`Thing_MugirlDunkProp`）不受影响。

## 行为定义

- "投掷进行中" = 控制器处于 `Held`（举起瞄准）或 `Flying`（物体在空中）两个阶段；落地结算或安全放回后立即恢复。
- 原行为：举起期间下达射击指令会走 `TickHeld` 的 `otherPlayerOrder` 分支，物体被放回、投掷冷却重置后开始射击；飞行阶段（最长 75 tick）内施术者可以被立刻下令开枪。
- 新行为：射击菜单与小队齐射直接不可下达并显示理由；绕过菜单的射击在 `Verb.TryStartCastOn` 层被拒绝且不产生消息刷屏。

## 改动

| 文件 | 改动 |
| --- | --- |
| `1.6/Source/Features/Genes/Thing_MugirlThrownObject.cs` | 新增 `IsThrowInProgress`（`Held` 或 `Flying`） |
| `1.6/Source/Features/Genes/MugirlThrowUtility.cs` | 原 `FindHeldController` 重构为共享 `FindController` 扫描，新增 `FindThrowingController`（含飞行阶段） |
| `1.6/Source/Features/Genes/Harmony_ThrowFiringLock.cs` | 新增两个补丁：`Harmony_ThrowFiringLock_RangedAttackMenu` 后置 `FloatMenuUtility.GetRangedAttackAction`（可射击动作改为禁用理由）；`Harmony_ThrowFiringLock_TryStartCastOn` 高优先级前置 6 参 `Verb.TryStartCastOn`（仅拦 `Verb_LaunchProjectile`） |
| `1.6/Source/Core/MugirlPatchInfo.cs` | `MugirlPatchCatalog` 登记 2 条 Genes 模块补丁元数据 |
| `1.6/Source/MugirlRace.csproj` | 编入新源文件 |
| `1.6/Languages/{ChineseSimplified,English}/Keyed/Misc_Gameplay.xml` | 新增 `Mugirl.Throw.BusyHolding`（"{0}正举着待投掷的物体，无法开枪"） |
| `docs/content-update-guide.md`、`docs/validation-runbook.md` | 投掷小节与验证清单补充禁火条目 |

## 设计要点

- 原版 1.6 所有玩家射击指令入口（右键 `FloatMenuOptionProvider_DraftedAttack`、小队齐射/攻击 Gizmo `PawnAttackGizmoUtility`）都汇聚到 `FloatMenuUtility.GetRangedAttackAction`，单一后置补丁即可覆盖；征召小人无 AI 自动开火（Humanlike 思维树征召即待命）。
- Verb 层补丁只拦 `Verb_LaunchProjectile`（枪械、弓、手雷等弹道射击），不拦近战、`Verb_CastAbility` 类能力；用于兜底第三方战斗模组直接发起的射击与投掷前排队残留的射击任务。
- Verb 前置补丁用 `HarmonyPriority(Priority.High)` 先于武器轮盘的默认优先级前置换枪动画逻辑执行，拦截时不触发轮盘换枪动画与延迟施放记录，避免半启动状态。
- 拦截在 Verb 层静默返回 false，不做消息提示，防止 `AttackStatic` 每 tick 重试造成刷屏。

## 验证结果与限制

- `docs/tools/Invoke-Phase6StaticValidation.ps1`（PowerShell 7，完整构建）：通过，退出码 0；日志 `TMP/throw-firing-lock-validation.log`，补丁元数据审计 129 条全部通过，正式 DLL 已更新（2026-09-25）。
- 游戏内验证待做：按 `docs/validation-runbook.md` "投掷禁火"条目——举起瞄准与飞行期间右键射击菜单、小队齐射显示"正举着待投掷的物体"且不可下达；落地或放回后立即恢复；近战与能力施放不受影响。
- Windows PowerShell 5.1 无法解析本验证脚本（`??` 运算符与编码），必须用 `pwsh` 运行；这是既有脚本特性，非本轮引入。
