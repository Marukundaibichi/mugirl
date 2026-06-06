# 阶段 6：骑乘模块重构记录

## 范围

- 新增 `MountEligibilityService`，集中处理上骑条件和自动下骑条件。
- 新增 `MountedVerbScope`，统一封装 `Verb.caster` 的临时替换与恢复。
- `Comp_MooGirlMount` 保留容器、状态字段、存档字段和 tick 调度；上骑条件、自动下骑条件、战斗控制均委托到服务类。
- `MountedPawnCombatTurret` 重命名为 `MountedCombatController`，作为骑乘远程战斗控制器入口。
- 骑乘与牵引互斥改为复用 `RopingService.HasAnyRope` 和 `RopingService.BreakAllRopesAndNotify`。
- `MountedPawn` 查询从 LINQ 改为直接遍历 `ThingOwner`，减少热路径分配。

## 行为保护

- 未改任何数值：`physiologicalTickInterval = 150`、`safetyCheckInterval = 250`、`turretTickInterval = 60`、`turretMinAimTicks = 15`、骑手绘制偏移和高度偏移全部保持原值。
- 上骑、下骑、自动下骑、骑手生理 tick、远程自动射击、近战辅助、骑手武器绘制的触发条件保持原有逻辑。
- 骑手远程射击仍使用载体位置寻找目标与开火；没有改为虚拟 caster 或自定义 verb。
- 载体移动时仍限制远程自动射击，除非当前存在近战目标。
- `fireAtWill` 默认值和存档字段名保持不变。

## 设计修正

- 原代码长期把骑手武器 `Verb.caster` 改成载体，依赖下骑时恢复；现在所有原版 verb 调用都通过 `MountedVerbScope` 临时替换，作用域结束即恢复。
- 原近战辅助手写 `try/finally` 恢复 caster；现在和远程战斗共用同一作用域类，同时保留 stance 的 `finally` 恢复。
- 原 `Comp_MooGirlMount.CanMount` 包含较长条件表；现在改由 `MountEligibilityService` 持有，便于后续兼容扩展。
- 原 `MountedPawnUtility.ShouldAutoDismount` 保留为兼容包装，实际逻辑进入 `MountEligibilityService`。
- C# 中骑乘“标签 + 原因”的玩家文本拼接改为翻译键 `MooGirl.Mount.LabelWithReason`。
- `JobDefs_Mounting.xml` 源 XML 改为英文原文加中文注释，并补齐中英文 DefInjected。

## 验证

- `MSBuild /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU` 通过。
- 骑乘目录扫描结果符合预期：无 `System.Linq`、`FirstOrDefault`、旧 `MountedPawnCombatTurret` 引用、长期 `Verb.caster = carrier` 赋值或硬编码“标签: 原因”拼接。
