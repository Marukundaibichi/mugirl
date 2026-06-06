# 阶段 9：Def/XML 重组记录

## 范围

- 删除旧档兼容专用 `1.6/Defs/Compatibility/LegacyDefs.xml`，不再为旧版本存档保留空壳 Def。
- 将源 Def XML 的玩家可见字段统一为英文原文，并在相邻 XML 注释保留中文对照。
- 补齐英文与简中 `DefInjected`，覆盖所有源 Def 玩家文本键，包括 backstory `baseDesc`、武器 projectile label、工具 label 和自定义 comp `useLabel`。
- 清理中文语言文件中的 TODO 与注释残留，简中翻译文件只保留节点正文。
- 将生产路径中的生成式/错误命名改为业务命名：
  - `AiGenerated_DefOf.cs` -> `MooGirlContentDefOf.cs`
  - `AiGenerated_Patch.xml` -> `MilkFood_TraderStock_Patch.xml`
  - `Apparel_NewAdded.xml` -> `Apparel_SpecializedGear.xml`
  - `Apparel_Stcoking.xml` -> `Apparel_Stockings.xml`
  - `Apparel_Layer_MooGirl_New.xml` -> `Apparel_Layer_Stockings.xml`
  - `Hediffs_Advance.xml` -> `Hediffs_AdvancedRestraints.xml`
  - `Apparel_Bongdage/BongdageApparel_*.xml` -> `Apparel_Bondage/BondageApparel_*.xml`
  - `NewBron` 源码目录 -> `Newborn`

## 行为保护

- 未改任何 `defName`，包括历史拼写已有问题的 `SlaveApperal` 系列 Def；这些属于加载标识，后续若要改名必须单独确认。
- 未改 `thingClass`、`compClass`、`texturePath`、`soundDef`、`weaponTags`、`apparel.tags` 或任何数值节点。
- 未改武器伤害、射程、射速、制作成本、装备数值、产量、事件点数、任务信号或派系关系。
- 语言文件补齐只改变本地化覆盖完整性；简中已有非 TODO 翻译优先保留，避免无意改变现有显示文案。
- `MooGirlContentDefOf` 只替换 C# 类型名和 `.csproj` 编译项，不改变任何实际 Def 名。

## 设计修正

- 源 XML 不再把中文正文作为原生玩家文本，统一改为英文原文加中文注释，满足英文源文件和简中 DefInjected 同时维护的规则。
- DefInjected 覆盖检查从“只看文件存在”提升到“按源字段生成实际翻译键”，避免 `tools.0.label`、`comps.CompX.useLabel`、`baseDesc` 这类嵌套字段漏翻。
- `AiGenerated`、`NewAdded`、`Advance`、`Stcoking`、`Bongdage`、`NewBron` 等路径命名改为业务语义或正确拼写，降低后续模块定位成本。
- 删除束具 XML 中整段已注释失效的旧 Def，避免死内容污染文本扫描。
- `MooGirlContentDefOf` 中的分组注释改为中文，并说明快递事件钥匙引用保留旧 defName 是为了保护掉落配置。

## 已知边界

- `SlaveApperal` 拼写仍存在于 defName、C# 类型和 XML class 引用中；这是加载标识和运行时类型边界，不在未确认情况下修改。
- `AdvancedSlaveApparel` 是语义正确的英文类型名，不属于本阶段要清理的 `Advance` 生成式命名。
- 文档基线中仍会记录历史文件名，例如 `AiGenerated_Patch.xml`；这些是审计历史，不属于生产命名残留。

## 验证

- `MSBuild /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU` 通过。
- 本阶段收尾构建无 warning。
- `1.6/Defs`、`1.6/Languages`、`1.6/Patches` 共 212 个 XML 文件全部通过解析。
- 源 Def 玩家字段扫描无中文正文残留。
- `1.6/Languages` 无 `TODO` 节点。
- `1.6/Languages/ChineseSimplified` 无 XML 注释残留。
- 源 Def 玩家文本键在英文和简中 `DefInjected` 中的缺失数为 0。
- C# 中文字符串字面量扫描缺失数为 0；本阶段发现的 DevMode 解锁文本、设置页标题和文本格式化日志已迁移到 Keyed。
- 生产路径与生产内容扫描无 `AiGenerated`、`NewAdded`、`LegacyDefs`、`Stcoking`、`Bongdage`、`Apparel_Layer_MooGirl_New`、`Hediffs_Advance.xml`、`NewBron` 残留。
