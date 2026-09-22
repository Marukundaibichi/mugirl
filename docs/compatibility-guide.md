# 兼容维护指南

本文档记录 DLC 和第三方 mod 兼容的维护方式。原则是低侵入、缺失可降级、兼容边界清楚。

## LoadFolders 与 MayRequire

- `LoadFolders.xml` 控制可选目录是否加载，例如 Biotech、Odyssey、Facial Animation、VCookE、Search and Destroy。
- 单个 Def 节点依赖 DLC 时使用 `MayRequire`。
- 可选目录里的补丁仍要自己保护目标存在，不要假设上游结构永远稳定。

## Compatibility 层

放在 `1.6/Source/Compatibility` 的内容适合处理：

- 第三方类型反射。
- 第三方 Def 探测。
- 第三方 mod 缺失时的 null-safe 查询。
- 与渲染或动画相关的临时兼容开关。

不适合放进去的内容：

- 本 mod 的主要业务规则。
- 复杂 UI、Job、Hediff 状态机。
- 只因为“和第三方 mod 有关系”就搬过去的大段功能代码。

## Harmony 兼容

- 普通 patch 可以使用 Harmony attribute 扫描。
- 手动反射 patch 由 `MugirlPatchRegistry` 管理。
- 新 patch 同步更新 `MugirlPatchInfo` 元数据。
- patch 失败时优先一次性 warning 或静默降级，不要在启动或 tick 热路径刷屏。

## XML Patch 兼容

每个 `<Patch>` 文件开头保留治理注释：

```xml
<!-- PatchGovernance:
Targets: 目标 Def 或上游结构。
Scope: 本补丁只做什么，不做什么。
Duplicate guard: 如何避免重复插入。
Failure: 目标缺失或上游变化时的预期行为。
-->
```

可选集成补丁应尽量使用 `PatchOperationConditional` 包住目标路径。目标缺失时允许 no-op，比红字更适合可选兼容。

## 当前重点兼容对象

- HAR：硬依赖。种族、body、渲染节点相关路径要谨慎。
- Facial Animation：条件目录加载；只在雪牛娘种族 Def 存在时追加 comp 和动画。新增或调整 FA 内容前先看 `features/facial-animation-guide.md`，尤其是 `raceName=Mugirl` 专属池、`normal_Mugirl` 底座动画、贴图回退、脸红必须走 head 层，以及当前整张眼睛贴图/高光路径。
- RJW / Rimworld-Animations：FA 条件目录内的动作映射、mod 名称和头部位移删除范围以用户提供的原包为准，不按本机安装情况扩展或裁剪；只修正社交动画 XPath 并增加逐列表去重和目标缺失保护。详情见 `features/facial-animation-guide.md` 的 2026-09-21 整合记录。
- Search and Destroy：只向雪牛娘行为树插入主动搜索歼灭分支，目标漂移时 no-op。
- VCookE/PipeSystem：只追加雪牛奶转奶酪工序，工序已存在或目标缺失时 no-op。
- Melee Animation：渲染兼容层不能因为对方内部字段变化打断绘制。
- 外部乳房 Hediff mods：只作为可选 HediffDef 探测，不混入本 mod 内部哺乳 Hediff。
