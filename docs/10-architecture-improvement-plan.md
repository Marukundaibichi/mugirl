# 后续架构改进计划

本文档基于两轮重构后的架构评价制定。它不是新一轮“全面重写”计划，而是给当前已经可维护的模块化单体继续加固边界、降低兼容风险、减少未来回归面的路线图。

当前判断：

- 当前架构已经从功能堆叠状态进入模块化单体状态。
- 发布、静态验证、语言一致性、Def 引用、Harmony 边界和发布卫生已经有较强的自动检查。
- 仍未达到 `docs/02-target-architecture.md` 中设想的完整终态，尤其是显式 patch 元数据、Compatibility 分层、命名空间边界和大模块内聚度。
- 后续改进应以“小步、可验证、行为锁定”为原则，不再做大范围无收益搬文件。

## 总目标

把当前架构从“重构后可维护”推进到“长期扩展抗压”：

1. 让补丁边界可审计。
2. 让第三方兼容入口可定位。
3. 让模块间依赖关系可解释。
4. 让大模块逐步收敛到服务、状态和表现分离。
5. 让 XML patch 的侵入性有记录、有门禁、有回退方案。

## 非目标

以下事情不应在本计划中顺手做：

- 不改核心玩法数值。
- 不改已有 `defName`、存档字段名和 XML `thingClass`，除非另开旧档兼容方案。
- 不拆成多个 DLL。
- 不为了命名空间洁癖做全项目搬迁。
- 不在缺少游戏内验证的情况下继续扩大行为改动。

## 原则

- 每个架构改动必须有一个清晰的验收点。
- 每次只推进一个边界，不把补丁、命名空间、兼容和功能逻辑混在同一批。
- 对 RimWorld 原版或第三方 mod 的侵入必须能回答三个问题：改了谁、为什么改、失败时怎么降级。
- 热路径改动必须说明 tick 频率、缓存生命周期和失效条件。
- 文档和静态验证要跟着代码一起变，避免文档继续停留在理想状态。

## 阶段 A：补齐真实游戏验证闭环

目的：先把“静态可信”升级为“运行时可信”，避免后续架构判断建立在旧 `Player.log` 上。

任务：

- 使用 `docs/tools/New-GameValidationConfigs.ps1` 生成的 6 组配置逐组启动 RimWorld。
- 每组都重新启动游戏，收集 fresh `Player.log`。
- 用 `docs/tools/Invoke-PlayerLogScan.ps1` 扫描 fresh 日志。
- 至少覆盖：仅核心依赖、全 DLC、HAR + FacialAnimation、常见兼容 mod、中文语言、英文语言。
- 每组记录加载结果、红字、黄字、进入地图结果、保存读档结果。

验收：

- fresh `Player.log` 不再命中旧 SoundDef 与翻译重复 key。
- 核心配置可进入地图、保存、读档。
- 若仍有红字，先进入 `09` 风格的整改清单，不继续推进架构阶段。

## 阶段 B：Patch 元数据与审计边界

目的：当前 Harmony patch 已由入口统一扫描，但仍缺少“补丁意图和风险”的结构化描述。下一步应让 patch 从“能被打上”变成“能被审计”。

任务：

- 新增轻量 `MooGirlPatchInfo` 数据结构，记录：
  - 模块名。
  - patch class。
  - 目标类型/方法。
  - Prefix/Postfix/Transpiler/Manual。
  - 是否可能阻断原方法。
  - 兼容风险等级。
  - 失败时行为。
- 先只登记高风险 patch：Transpiler、反射目标、全局热路径、会改变 job/verb/render 行为的 patch。
- `MooGirlPatchRegistry` 保持手动反射 patch 管理，但把人工登记信息输出到 DevMode 诊断或内部只读列表。
- Phase 6 增加门禁：新增高风险 Harmony 文件时必须有 patch info。

验收：

- 高风险 patch 不再只能靠文件名猜意图。
- `MooGirlBootstrap.PatchedClassNames` 与高风险 patch info 能交叉检查。
- 手动反射 patch 的失败信息仍保持一次性 warning，不产生加载刷屏。

## 阶段 C：Compatibility 层落地

目的：把 HAR、FacialAnimation、第三方 hediff/Def 识别和其他 mod 兼容从业务模块里逐步剥离出来。

建议目录：

```text
1.6/Source/Compatibility/
    AlienRace/
    FacialAnimation/
    MeleeAnimation/
    OtherMods/
```

迁移顺序：

1. 只迁移反射访问和第三方类型探测，不动业务行为。
2. 再迁移第三方 Def 识别，例如乳房 hediff profile、可选音效/贴图/兼容 Def。
3. 最后迁移 XML patch 对应的 C# glue。

验收：

- 业务模块不直接调用 `AccessTools.TypeByName("第三方类型")`。
- 可选依赖缺失时，兼容层负责返回 null/false，不让业务模块关心反射细节。
- `About.xml` 的硬依赖和运行时兼容语义在代码注释中保持一致。

## 阶段 D：命名空间和依赖边界

目的：当前目录已经分层，但命名空间仍基本是 `MooGirl`。这不影响运行，却会让跨模块调用缺少心理边界。

策略：

- 不做一次性全项目 namespace 大搬迁。
- 先从新代码开始使用更具体命名空间。
- 对低风险纯工具类逐步迁移，例如：
  - `MooGirl.Core`
  - `MooGirl.Features.Roping`
  - `MooGirl.Features.Mounting`
  - `MooGirl.Compatibility.*`
- 对 XML 直接引用的类型暂不迁移，除非同步处理全限定类名和旧档兼容。

依赖规则：

- `Core` 可被所有模块引用。
- `Compatibility` 可被 Feature 引用，但不反向引用具体业务状态。
- Feature 之间默认不互相引用；必须共享时先抽到 Core 或明确的 service。
- UI 只能调用 settings、service 和只读状态，不直接修改复杂业务内部字段。

验收：

- 新增类型不再默认塞进 `MooGirl` 根命名空间。
- Phase 6 可先做软检查：报告新增根命名空间类型，不立即失败。
- 文档记录允许保留在根命名空间的 XML marker 类型。

## 阶段 E：大模块瘦身

目的：不追求“文件短”，而是让状态、规则、表现和外部 patch 各自有稳定位置。

优先级：

1. Mounting
   - `Comp_MooGirlMount` 保留容器和状态。
   - 骑乘资格、战斗、绘制、异常恢复继续拆成服务。
   - 所有临时修改原版对象状态的逻辑必须使用 acquire/release 结构。

2. Milk
   - 产奶状态、交互效果、动画表现继续分离。
   - 奶量消费保持原子提交。
   - 乳房 profile 与第三方 hediff 识别最终迁到 Compatibility/OtherMods。

3. Restraints
   - 束具锁状态、破解状态、高级束具效果和表现效果继续分层。
   - 电击、脑控、镣铐逐步显式状态机化。
   - 解锁/破解/掉落必须继续保持原子性和失败恢复。

验收：

- 每次拆分不改变 save key、不改变 Def 引用、不改变玩家可见数值。
- 旧文件只在职责明确减少时拆，不为了缩短行数拆。
- 每次拆分后运行 Release build、Phase 6、package。

## 阶段 F：XML Patch 侵入性治理

目的：当前 XML patch 风险可控，但仍需要更明确的治理规则，尤其是对 vanilla trader、recipe、precept 和第三方组件的追加。

任务：

- 为每个 patch 文件补一段顶部注释，说明：
  - patch 目标。
  - 是否原版目标。
  - 是否可选 DLC/第三方目标。
  - 重复应用防护方式。
  - 失败是否可接受。
- 对直接 `PatchOperationAdd` 继续分类：
  - 必需原版目标。
  - 可选外部目标。
  - 已有 conditional 防重复目标。
- Phase 6 输出分类统计，避免“直接 Add 数量”本身吓人但不可解释。

验收：

- 审查者能从 XML 文件内直接知道该 patch 为什么存在。
- 可选目标必须有 conditional 或 load folder gate。
- 新增 vanilla patch 必须说明是否影响非 MooGirl 内容。

## 阶段 G：静态状态和缓存生命周期

目的：当前静态字典数量不多，但都处在渲染、战斗、日志、动画或兼容缓存等敏感区域，需要明确清理策略。

重点对象：

- `MooGirlLog` 的 once warning 集合。
- 渲染刷新 pending 字典。
- 骑乘战斗的 verb/caster/warmup 缓存。
- 食物/hediff Def 缓存。
- 挤奶动画状态字典。

任务：

- 给每个静态缓存补生命周期说明：按游戏重置、按 pawn despawn 清理、按 verb 移除清理、还是进程级缓存。
- 对含 Pawn、Verb、Thing 引用的静态集合增加清理入口或弱约束。
- Phase 6 增加静态集合扫描报告，不一定全部失败，但必须显式允许。

验收：

- 静态集合不会默默持有已销毁 pawn/verb。
- 返回主菜单、新开局和读档后 once warning 与运行期状态不会产生误导。
- 所有含游戏对象引用的静态集合都有清理路径或可解释的生命周期。

## 阶段 H：文档同步和架构决策记录

目的：现有文档很全，但 `09` 已经是整改流水账，不适合承载长期架构结论。后续需要让文档变成可导航资产。

任务：

- 新增 `docs/architecture/` 或继续编号文档，记录关键 ADR：
  - 为什么仍保持单 DLL。
  - 为什么采用 Harmony 扫描 + 高风险 patch info，而不是完全手动注册。
  - 为什么 Compatibility 分层只迁移反射/探测，不一口气搬业务。
  - 哪些 XML marker 类型必须留在根命名空间。
- `09` 继续作为历史整改记录，不再往里面塞长期规划。
- `08-game-validation-runbook.md` 补 fresh log 验证记录模板。

验收：

- 新贡献者可以从 `00`、`02`、`10` 理解当前架构、目标架构和下一步计划。
- 历史整改、架构路线和验证手册不再互相混杂。

## 推荐执行顺序

```text
A  fresh 游戏内验证
B  高风险 patch info
G  静态缓存生命周期报告
C  Compatibility/AlienRace 与 OtherMods 先行
F  XML patch 注释和分类统计
D  新代码命名空间规则
E  按模块小步瘦身
H  ADR 与验证记录补齐
```

原因：

- 先做游戏内验证，避免继续在未知运行时状态上加架构。
- Patch info 和静态缓存报告投入小，收益高，能快速提升可审计性。
- Compatibility 和 XML patch 治理是兼容性核心，优先级高于命名空间美化。
- 大模块瘦身应放在边界清楚之后，否则容易拆出新的交叉依赖。

## 完成标准

本计划完成时，项目应达到以下状态：

- fresh 游戏内验证有记录，旧 `Player.log` 问题不再作为未知项悬挂。
- 高风险 Harmony patch 有结构化说明和静态门禁。
- 第三方兼容入口集中在 Compatibility 层。
- 新增代码有明确命名空间规则。
- XML patch 的侵入性可解释、可统计、可验证。
- 静态缓存生命周期有说明和清理路径。
- 继续扩展新功能时，不需要重新理解全项目才能判断改动边界。
