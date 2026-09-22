# 0.6.6 版本记录（2026-09-23）

本版从 `de6753d`（0.6.5）推进，收录本次提交前工作区的全部改动。具体规则分别见[研究树实施](research-tree-2026-09-21.md)、[工作区整理](workspace-cleanup-2026-09-21.md)、[骑枪冲锋](../features/lance-charge.md)、[巨企验证](../features/giant-corporation-v1-validation.md)和[怀孕心情](pregnancy-mood-2026-09-21.md)。

## 内容

- 运货员在开局两只雪牛娘坠落成功后一个游戏日出现；按两人剩余锁数和钥匙类型逐把备齐，接受提案时重新核对。交谈改用标准对话，选项为“接受提案”“立即开战”。
- 收录工作区中的研究树、服装与资源、骑枪运动模糊、巨企及其他源码调整，以及根 README 统一文档入口和资源目录整理。原有未提交素材及源码改动按本次用户要求一并纳入。

## 验证与限制

- 独立 Release 重建成功，生成的 DLL 与正式 `1.6/Assemblies/MugirlRace.dll` SHA256 一致：`44D1D9DEE724460856F7998B80E6E3B7048506C1674A98006491F683944E9570`。
- `New-WorkshopPackage.ps1 -SkipBuild` 预检成功，复制 1925 个文件，排除 267 个开发文件；这只是包结构检查，没有发布至 Workshop。
- 静态验证通过源码、XML、Def、翻译、路径与发布卫生检查，最终在资源检查处因既有 42 张 FA 烘焙脸红头图缺失失败。`Mugirl_Shapes.xml` 两处行尾空格已清理；没有恢复用户删改的 FA 贴图。
- 本轮此前的隔离游戏完成启动与完整存读档，巨企回归为 259 PASS / 1 FAIL，失败项是高级支援队装备人数断言；测试没有实际走运货员交谈与解锁流程。详见[巨企验证记录](../features/giant-corporation-v1-validation.md)。
- RimSort 目录审计 `affected_candidates=0`、`scan_errors=0`，证据为 `TMP/CourierValidation/rimsort-cycle-audit.json`。
