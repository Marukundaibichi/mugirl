# 机械体暂缓、投掷暴力限制与吃草调查

## 范围与基线

- 日期：2026-09-26，基于已有大量未提交改动的工作区；保留原有源码、正式 DLL、Def、翻译及素材。
- 基线状态、差异、DLL 备份和静态日志位于 `TMP/graze-release-20260926/`。静态基线使用 PowerShell 7 和仅对当前进程设置的 Git safe.directory 后通过；未修改全局 Git 配置。

## 机械体暂缓发布

- 整组注释四个 Def 文件：`Bodies/Mugirl_MechanoidBodies.xml`、`DamageDefs/Mugirl_MechanoidAttacks.xml`、`ThingDefs_Races/Mugirl_Mechanoids.xml`、`PawnKindDefs/Mugirl_Mechanoids.xml`。
- 同时注释六个中文 `DefInjected/*/Mugirl_Mechanoids.xml` 文件，避免翻译引用已经停用的 Def。
- 源码与贴图保留；恢复时须同时启用种族、PawnKind、身体、攻击及翻译。开发者模式生成过机械体的测试存档依赖停用 Def，不能作为普通存档兼容通过的证据。

## 投掷暴力限制

- `Mugirl_Throw.casterMustBeCapableOfViolence` 改为 true，走原版 `Ability.GizmoDisabled` 的暴力禁用提示。
- `MugirlThrowUtility.CanPickUp` 兜底拒绝禁用暴力的施术者，覆盖直接调用和已排队的能力。
- 举起期间失去暴力能力时安全放回；落点验证同步拒绝投出。已进入飞行阶段的物体正常完成落地，避免实体遗失。
- 其余投掷伤害、2 小时冷却、禁火及开发者灌篮规则沿用工作区现状。

## 吃草调查

- 用 DecompilerServer 核对本机 `Assembly-CSharp.dll` 的 `JobDriver_Ingest.MakeNewToils`、`Toils_Ingest.ChewIngestible`、`Toils_Ingest.FinalizeIngest`、`Thing.Ingested`、`Plant.IngestedCalculateAmounts`。
- 现有补丁只替换准备阶段，原版仍执行咀嚼、消耗植物和营养结算。咀嚼按 `baseIngestTicks / EatingSpeed` 计时；吃掉整株时销毁，所需营养小于整株营养时仅降低 `Growth`。
- 第一轮有效隔离运行显示：饥饿时约 590 tick 后草销毁、营养增加；接近吃饱时 `Growth` 从 1 降到约 0.84，营养补满。两种情况均转入 `Wait_Combat`，没有卡在进食。初版验证按 Job 引用比较结束状态，被原版对象池复用误导；已改为检查实际 JobDef 后重跑。
- 最终回归未复现卡住：饥饿、接近吃饱、已吃饱、自动连续吃草均正常结束；用户确认是殖民者后，追加“草”食物方案自然觅食，确认能自行开始吃植物、补满营养并停止。共 17 项检查全部通过，`failures=0`。
- 保留现有吃草运行逻辑。当前证据只能说明本工作区、隔离配置正常，不能断言报告不存在；特定第三方模组组合或旧存档需要对应存档、模组列表及卡住时的 Player.log 才能继续定位。

## 验证与证据

- 隔离验证入口：`docs/tools/Start-GrazeRuntimeValidation.ps1`，驱动仅在 `EnableGrazeValidation=true` 时编入隔离 DLL。
- 最终隔离运行：`%LOCALAPPDATA%/RimWorldModTests/Mugirl/GrazeValidation-Graze-20260926-04-Game/Mods/Mugirl/TMP/GrazeValidation-Graze-20260926-04/`，RimWorld `1.6.4871 rev591`。
- 目录环路最终审计：`TMP/graze-release-20260926/rimsort-audit-final.json`，2853 个候选目录，`affected_candidates=0`、`scan_errors=0`。
- 完整 Release 构建及静态验证退出码 0；正式 `1.6/Assemblies/MugirlRace.dll` 已更新，验证驱动未编入正式 DLL。日志为 `TMP/graze-release-20260926/final-validation.log`，后续静态复查为 `final-static-recheck.log`。
- fresh `Player.log` 扫描通过，无可疑异常；隔离配置包含 Harmony、Core、全部 DLC、HAR、Mugirl。启动时扫描其他 Workshop 模组元数据会打印既有依赖信息，但这些模组未启用。
- 完成后的检查结果、进度、fresh log、配置、静态日志及最终环路报告归档在 `DevData/Evidence/MechThrowGraze-20260926/`，保留原测试环境和早期运行证据。
