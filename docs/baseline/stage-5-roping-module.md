# 阶段 5：牵引模块重构记录

## 范围

- 新增 `RopeLink`、`MapRopingIndex`、`RopingService`，把牵引关系、拴点状态、临时拴墙状态集中到地图级运行期索引。
- 删除 `RopeStateTracker` 的静态 `HashSet<Pawn>`，删除完全注释掉的 `Harmony_CanTakeOrder.cs`，删除未编译且重复定义类型的 `Harmony_Patch_RopingDraw.cs`。
- `CompRopeToBuild`、牵引/解绳/拴墙 job、thought、逃跑阻止、开门、征召按钮、束缚信息、带去床等入口统一改为调用 `RopingService`。
- 给原版 `Pawn_RopeTracker.RopePawn`、`RopeToSpot`、`BreakAllRopes`、`UnropeFromSpot` 增加轻量索引通知，兼容其他代码直接调用原版 tracker。

## 行为保护

- 未改任何牵引数值：等待时间、牵引距离、行进队形偏移、拴墙等待时间、接受牵引概率公式均保持旧值。
- MooGirl 牵引对象判定继续使用 `RaceProps.body == MooGirl_DefOf.MooGirlBody`，没有扩大到 `MooGirlIdentity`。
- 菜单翻译键保持原有键名：`MooGirl.Rope.Target`、`MooGirl.Rope.SuccessChance`、`MooGirl.Unrope.Target`、`MooGirl.RopeToHitch`、`MooGirl.NoValidToRope`。
- 拒绝牵引时仍沿用原版逮捕概率、派系捕获通知、组件 `Notify_Arrested` 和狂暴反击逻辑。
- 被牵引时开门、阻止囚犯逃跑、隐藏原版束缚信息、禁用征召按钮、禁止带去床的外部行为保持原有意图。

## 设计修正

- 旧 `RopingTick` 在热路径扫描整张地图的全部 pawn；现在只遍历当前 tracker 的 `Ropees`，地图级索引每 250 tick 低频重建作为兼容兜底。
- 旧拴墙 pending 状态是全局静态集合，没有地图生命周期；现在归属 `MapRopingIndex`，随地图组件生命周期清理。
- 旧 `CompRopeToBuild` 每次打开菜单扫描全图并使用 LINQ；现在直接查询牵引者索引。
- 旧 thought 每次计算扫描玩家派系 pawn；现在从牵引者关系列表计数，最多数到 2 即停止。
- 旧 `PreventEscape` 在 `pawn.roping == null` 时可能空引用；现在通过服务层空安全判断。
- 旧未编译文件中存在重复类型和硬编码英文玩家文本；已删除，避免后续误加入项目。

## 已知边界

- `MapRopingIndex.RebuildFromMap()` 仍读取 `AllPawnsSpawned`，这是低频兜底，不在绘制、菜单、thought 或每 tick 关系检查热路径中使用。
- 当前阶段没有重命名 defName，也没有改 XML class 引用。
- 行为上修正了旧全图扫描可能把无关牵引关系误判为当前关系的问题；这属于缺陷修复，验收时需要重点观察多组牵引同时存在的场景。

## 验证

- `MSBuild /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU` 通过。
- 牵引目录扫描结果符合预期：无 `System.Linq`、`FirstOrDefault`、`SpawnedPawnsInFaction`、`RopeStateTracker`、未编译重复文件；仅 `MapRopingIndex` 保留低频 `AllPawnsSpawned`。
