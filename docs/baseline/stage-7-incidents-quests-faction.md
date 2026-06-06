# 阶段 7：事件、任务与派系模块重构记录

## 范围

- 将 `MooGirl_GameComp` 的运行期字段收束为 `MooGirlStoryState`，并新增 `MooGirlStoryService` 管理开局坠舱与快递袭击计时。
- 将开局坠机事件 worker 重命名为 `IncidentWorker_MooGirlStructuralCrashMission`，删除旧档兼容命名依赖。
- 快递任务、快递对话、快递日记、索赔对话和物资扣除逻辑改为明确服务/QuestPart 边界。
- `FactionUtility`、`QuestNode_Root_MooGirl_CourierRaid`、`StockGenerator_MooGirl_Slaves` 去除 LINQ 热路径与不必要分配。
- 快递日记、快递任务、快递事件、野生奴隶事件和相关 JobDef 的玩家文本完成 Keyed/DefInjected 迁移。

## 行为保护

- 未改开局坠舱检查初始 tick `360`、重试 tick `1000`、开局任务点数 `10000f`。
- 未改快递袭击延迟 `5 * 60000`、快递任务点数 `200f`、快递袭击生成延迟 `1`。
- 未改索赔物资数值：钢铁 `6000`、零部件 `300`。
- 未改奴隶商队库存数量范围 `1..8`，也未改 `PawnGenerationRequest` 的生成参数。
- 巨企敌对派系仍使用 `MooGirl_GiantCorporations_Hostile`；新档行为不依赖 legacy 派系迁移。

## 设计修正

- 旧 GameComponent 同时保存状态和触发业务；现在状态字段与事件推进逻辑分离，后续可以更容易检查 Scribe 字段和触发条件。
- 快递索赔的物资检查从 LINQ 改成显式遍历，避免在对话分支上产生额外枚举对象。
- 奴隶商队 Ideo 奴隶制检查保留旧版 `All()` 语义，但改成 `foreach`，不再依赖 LINQ。
- 开局坠舱多角色信件的硬编码中文迁入 `MooGirl.OpeningPodCrash_MultipleIntro`。
- `Jobs_CourierDiary.xml`、`Script_CourierRaid.xml`、`Incidents_CourierRaid.xml`、`Incidents_MooGirl.xml` 源 XML 改为英文原文加中文注释，并补齐中英文 DefInjected。

## 已知边界

- `QuestPart_MooGirlRescueJoin` 中的 `RemoveAll` 是 `List<T>` 原生方法，不是 LINQ，保留不改。
- 英文翻译中将野生奴隶事件末尾统一为 `tame`，与源中文“驯服”和该事件 UI 行为一致；这属于翻译一致性修正，不改变实际任务流程。
- 开局坠舱旧存档字段名已改变，不提供旧档迁移。

## 验证

- `MSBuild /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU` 通过。
- 第七阶段 C# 扫描无 `System.Linq`、`.Any()`、`FirstOrDefault`、`OrderBy`、`Where()`、`ToList()` 残留；仅 `RemoveAll` 方法名和说明旧 `All()` 语义的注释被文本扫描命中。
- 第七阶段新增/修改 XML 文件通过 `[xml]` 解析。
- 第七阶段源 XML 玩家字段扫描无中文正文残留。
