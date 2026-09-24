# 0.6.7 版本记录（2026-09-24）

本版从 `8cd70f9`（0.6.6）推进，收录本次提交前工作区的全部改动。具体内容分别见[性能优化实施](performance-optimization-2026-09-24.md)、[战斗服饰收购与售价调整](combat-apparel-trade-2026-09-24.md)、[FA 专题](../features/facial-animation-guide.md)与[验证手册](../validation-runbook.md)。

## 内容

- 性能优化：渲染热路径（武器轮盘背负绘制、骑乘绘制、每帧 Harmony 补丁）降为缓存/备忘查找；投掷与暴扣落点失败改为 30→60→120 tick 指数退避重试；冲锋/投掷迭代器分配消除；神经头盔黑暗精度补丁去装箱；巨企窗口各页派生数据按操作版本号缓存。无玩法数值、存档字段与 Harmony 目标变化，时序边界见性能记录。
- 战斗服饰收购：14 件服饰（含 `PMC_CaptainHelmet`）新增专用 `Mugirl_CombatApparelBuyOnly` 收购标签，地面与轨道作战补给商均可收购；删除 10 处 `SellPriceFactor=0.2`，玩家卖价回到默认乘数。
- FA 脸红基线修正：确认 Talos 参考包与 `1.6/FacialAnimation` 逐字节一致，资源检查契约改为"28 张 cover 叠层齐备、烘焙头图与旧 Emotions 脸红素材不得回流"。
- 暴扣头 `Thing_MugirlDunkHead` 独立类并缓存绘制解析结果；巨企新增展览人员 `Mugirl_CorporateShowcase` 及配套衣装配置 `CorporateShowcaseApparel`；新增投掷运行时验证工具 `Start-ThrowRuntimeValidation.ps1`。

## 验证与限制

- 修改 C# 后完整构建验证通过；随后 `Invoke-Phase6StaticValidation.ps1 -SkipBuild`（含 FA 资源检查）整体通过，约 38 项静态检查无违规。正式 DLL SHA256：`0B5BF9A06C63C282F5F679662B94109B1CBC5E9EBB24E508276A5C1D74BFD926`（860,160 字节，2026-09-24 20:05）。
- `New-WorkshopPackage.ps1 -SkipBuild` 预检成功，复制 1930 个文件，排除 275 个开发文件；发布 ZIP 已生成 `DevData/Releases/雪牛娘0924.zip`（顶层 `雪牛娘0924/`，1930 项，34,746,248 字节），ZIP 内 DLL 与正式 DLL 哈希一致，无源码、文档或临时文件泄漏。
- 未做游戏内 TPS/FPS/GC 采样与投掷、骑乘炮塔、轮盘轮射、巨企各页操作后的回归目检（建议抽查点见性能记录）；未在交易界面逐件试卖 14 件收购目标。
- RimSort 目录环路审计沿用 2026-09-24 投掷验证轮结果 `affected_candidates=0`、`scan_errors=0`，本轮未改动测试目录与联接。
