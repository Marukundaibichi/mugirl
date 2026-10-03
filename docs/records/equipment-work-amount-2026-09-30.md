# 固定材料装备制作工作量修复（2026-09-30）

## 根因与边界

蒸汽骑士枪的基础 `WorkToMake=120000`，原版详情以 `ceil(work/60)` 显示为 2000。其配方继承 `UnfinishedWeapon`，半成品具有材质，而成品是固定材料装备。原版 `Toils_Recipe.CalculateDominantIngredient` 会从可作为材质的原料中按数量随机选择半成品材质；`RecipeDef.WorkAmountTotal` 随后将该材质传入 `WorkAmountForStuff`。配方未指定 `workAmount` 时，原版会查询成品的 `WorkToMake` 并叠加该材质的倍率，即使成品自身没有可选材质。

选到玻璃钢时实际工作量为 `120000 × 2.2 = 264000`，显示 4400。这是固定材料配方误用半成品材质倍率，未发现骑枪功能代码增加制作工作量的设计。

本次只在存在该路径的 11 件固定材料装备上设置显式 `recipeMaker.workAmount`，与已有基础工作量一致。成品属性、成本、研究、工作台、半成品类型和正式 DLL 均保留。可选材质装备的原版材料修正及原有显式工作量设计继续保留。

## 修复名单

| 装备 | Def | 固定工作量 | 当前材料原先的偏差 |
| --- | --- | ---: | --- |
| 蒸汽骑士枪 | `Mugirl_SteamKnightLance` | 120000 | 玻璃钢 2.2 倍 |
| 三级机械师背包 | `Mugirl_RA_MechanicBackpack_C` | 1000 | 玻璃钢 2.2 倍 |
| 四级机械师背包 | `Mugirl_RA_MechanicBackpack_D` | 1000 | 玻璃钢 2.2 倍或铀 1.9 倍 |
| 白雪公主骑士铠 | `Mugirl_SnowPrincessArmor` | 32000 | 黄金 0.9 倍，钢铁 1 倍 |
| 白雪公主王冠 | `Mugirl_SnowPrincessCrown` | 5600 | 黄金 0.9 倍 |
| 白色骑士坐骑甲 | `Mugirl_KnightMountPlateWhite` | 42000 | 当前仅钢铁，倍率为 1，补齐同类隐患 |
| 榨乳器、隐藏式榨乳器 | `Mugirl_MilkingDevice`、`Mugirl_MilkingDeviceHidden` | 各 4000 | 当前仅钢铁，倍率为 1，补齐同类隐患 |
| 墨镜「霓虹幻镜」 | `Mugirl_Sunglasses` | 4000 | 当前仅钢铁，倍率为 1，补齐同类隐患 |
| 一级、二级机械师背包 | `Mugirl_RA_MechanicBackpack`、`Mugirl_RA_MechanicBackpack_B` | 各 1000 | 当前仅钢铁，倍率为 1，补齐同类隐患 |

表中为内部工作量单位，玩家详情按 60 换算并向上取整。墨镜、王冠沿用具体 Def 的 `WorkToMake`；未在头饰或服装父定义中锁定数值。

## 工作区基线与验证

保留本轮开始时已有的 132 项工作区改动。基线状态、涉及 Def 的原始副本、DLL 哈希、运行结果与临时探针源码归档在 `DevData/Evidence/EquipmentWorkAmount-20260930/`；日志中的 `TMP/EquipmentWorkAmount-20260930/` 为当时的实际隔离运行目录。rimsearcher 现有快照没有收录 0920/0926 新装备，本轮以现有 Steam 游戏重新加载当前模组和全 DLC 后的实际 Def、RecipeDef 及原版工作量函数确认范围。

- 修改前静态检查通过，未构建或覆盖正式 DLL。
- 修改前 `Baseline2`：实际加载 116 个装备配方，确认 11 个固定材料成品仍使用材质半成品且未锁定工作量；2536 项检查通过、0 失败。`equipment-work.tsv` 记录 2000/4400 及其他材料偏差。
- 修改后 `Fixed`：116 个装备配方，2580 项检查通过、0 失败；同类未锁定工作量配方归零，11 件装备在钢铁、玻璃钢、黄金和铀输入下均返回原有基础工作量。
- 比较修改前后运行期结果：2660 条非目标装备/材料记录完全一致；30 条目标记录的工作量均固定为基础值。6 个 Def 文件逐字段比较，仅新增目标配方的 `workAmount`。
- 修改后 Phase 6 静态检查和差异空白检查通过。fresh `Player.log` 扫描只命中未启用的 `tmp`、`牢外的mod` 共 4 行 About 元数据缺失警告；另有已安装第三方模组的依赖元数据警告。没有本模组、探针或 XML/cross-reference 异常，不将此日志记作零警告。
- 正式 DLL SHA256 前后均为 `5D8C1EFF046D79625D251DF403D2CA26DCD2BF7B7227E2344ED7379AF7F8E2B8`；本次纯 Def 修复无需重建 DLL。临时探针进程已退出；核对目录无联接、仅含本轮两个文件且与保留副本 SHA256 一致后，已删除临时探针模组，正常游戏配置、存档和正式模组程序集均未覆盖。

测试使用现有 Steam 安装、独立配置和命令行限定的临时探针模组，没有复制游戏、建立游戏资源联接或改动正常配置和存档。最初 `Baseline` 探针在主菜单调用 `ThingMaker.MakeThing`，因没有地图游戏的 `uniqueIDsManager` 失败；随后仅构造实际工作量函数读取的半成品 `def` / `Stuff` 输入，不分配地图 ID。该失败属于测试夹具，原日志保留，不计为产品验证结果。

本次验证覆盖当前装备配方加载、原版工作量计算和材料行为；未进行完整小人搬料、制作、读档的人工操作。已开工半成品的 `workLeft` 会由原版存档保留，本次不改写玩家进度；重启加载更新后，新开工任务使用修正工作量。
