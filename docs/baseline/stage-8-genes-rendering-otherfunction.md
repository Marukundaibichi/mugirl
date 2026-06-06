# 阶段 8：基因、渲染与横切补丁重构记录

## 范围

- 将出生与兜底异种修正整理为 `MooGirlXenotypeService`，出生入口即时修正，GameComponent 只保留 300 tick 低频兜底扫描。
- 将幼年体型工具重命名为 `LifeStageVisualService`，统一处理出生、生命周期变化和读档后的体型刷新。
- `LifeStageWorker_MooGirlAdult` 只保留生命周期快照入口，背景恢复和图形刷新委托给服务。
- `JobDriver_CastCharge` 去除 `OfType` 热路径枚举、共享空 `VerbProperties`，删除能力执行期间的成功/失败刷屏日志。
- `ThoughtWorker_SingleshotWeaponAversion`、`JobGiver_Nuzzle`、`Patch_DrugAdministerDefs` 去除 LINQ。
- 删除完全注释掉的旧 HAR patch 文件，并将新生儿生成补丁改名为 `Harmony_PawnGenerator_NewbornVisuals`。

## 行为保护

- 未改异种修正兜底计时 `checkTimer = 300`，未改出生即时修正的触发入口。
- 未改冲锋能力数值：经过伤害半径 `2f`、小体型阈值 `1f`、烟雾间隔 `10`、烟雾数量 `3..5`、经过伤害 `3..5`/`2..3`、终结伤害 `50f`、击退距离 `1`/`6`。
- 未改亲昵 Job 的最大距离 `40f`、过期时间 `3000`、行走速度和候选条件。
- 未改药物施用配方字段：`surgerySuccessChanceFactor = 99999f`、默认 `workAmount = 250`、原料数量 `1f`。
- 未擅自启用 `durationTick`，因为旧强制任务入口没有实际应用该字段；这类行为修正需要单独确认。

## 设计修正

- 旧异种修正用 LINQ 检查父母和 xenogene；现在显式循环，并明确不重置 UniqueXenotype 或已有 xenogene 的 pawn。
- 旧冲锋 Job 每 tick 用 LINQ 过滤附近 Thing；现在直接遍历 `Thing` 并转换为 Pawn，减少能力执行时的分配。
- 旧亲昵 JobGiver 用 LINQ 查询候选；现在使用蓄水池抽样，保持所有合法候选等概率随机。
- `LifeStages.xml` 源 XML 改为英文原文加中文注释，中文 LifeStage DefInjected 中的 TODO 已补齐。
- `MooGirl_Newborn` 背景补齐中英文 DefInjected；源 XML 新生儿文本改为英文原文加中文注释。

## 已知边界

- `BackstoryDef.xml` 其它大量背景仍保留中文源正文，作为阶段 9 全局 Def/XML 收束内容处理。
- `Hediffs_Bondage.xml` 等非第八阶段核心 XML 仍有中文源正文，阶段 9 统一处理。
- 冲锋能力的调试日志被删除，仅影响开发者日志噪声，不改变玩家玩法。

## 验证

- `MSBuild /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU` 通过。
- 第八阶段目录扫描无 `System.Linq`、`.Any()`、`FirstOrDefault`、`OrderBy`、`Where()`、`ToList()`、`OfType`、旧 `MooGirlJuvenileGraphicUtility` 或旧 HAR 空壳文件引用。
- `LifeStages.xml`、新生儿背景相关语言文件通过 `[xml]` 解析。
