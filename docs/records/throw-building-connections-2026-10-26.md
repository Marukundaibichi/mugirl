# 投掷建筑连接修复

## 问题与授权

玩家反馈任何建筑只要旁边有另一座建筑就举不起来。旧版 `ThrowUtility.IsConnectedBuilding` 扫描目标全部占地及四边邻格，只要存在另一个已生成的 `ThingCategory.Building` 就拒绝，根本没有核对连接关系。同格隐藏电缆也会导致家具被误禁。

用户明确要求只限制墙、电缆等实际连接的建筑，并确认普通设备接电后仍可举起。见[约束性决策 §19](../refactor-1.7/decisions.md#19-投掷建筑连接限制修正2026-10-26)。

## 修复后的条件

- 新的 `ThrowBuildingConnections` 扫描范围仍为整个占地与四边邻格，排除目标自身、未生成对象及非建筑。只斜角接触、隔一空格都不算。
- 图形结构连接按原版 `graphicData.linkType/linkFlags` 判断相容类别，排除仅用于地图边缘绘制的 `MapEdge`。支持 `Asymmetric` 的额外类别及门端点，并检查两个方向，避免围栏禁止举起而相连的门却能拔起。
- 普通墙与墙、墙与山体、山体之间、围栏与围栏/门、沙袋之间会触发。不同类别不因相邻就互相连接，墙与电缆也不会因同格而被锁在一起。
- 电缆由 `CompPowerTransmitter` 识别，同格或四边邻格有另一输电端时禁止拔起，含明线、隐藏电缆及电池等输电端。按输电能力判断，断电不解除限制。
- 普通用电设备没有这些结构连接属性时，接入电网不限制举起。电池与电缆相邻时，限制作用在输电线本体，电池仍按普通设备处理。
- 家具靠墙、桌椅并排、家具上/下有隐藏电缆均可通过连接检查；后续仍检查重量与其他能力条件。
- Odyssey 下图形结构连接不跨越飞船地基边界。第三方建筑使用相同连接标记（含 `Custom1` 等）时自动适用；仅在第三方组件内部实现的管网或自定义图形连接算法，没有通用安全识别接口，本轮不声称覆盖。

源码不访问/实例化图形来判断，直接按两个建筑自己的连接属性配对，避免同格无关设施混入 linkGrid 的合并标记。没有改动重量、冷却、伤害、公开 API 或存档字段。

## 基线与原版依据

- 开始时 `git status --short` 为空。开发树文件（Git 跟踪及未忽略的未跟踪文件）共 4931 项已归档至 `DevData/Backups/PreThrowConnections-20261026/tree.zip`，每个条目解压计算 SHA-256，与 `tree-manifest.sha256` 逐项相符。忽略的历史备份/证据原样保留。
- 正式 DLL SHA-256：`C3ED6AA47454470F4EC61C2DDCAE114BA6ACEB97A2330C16FE4904003507DF5A`。反编译确认它包含旧的无差别相邻拒绝逻辑。
- 游戏为 1.7.4985 rev1269。反编译核对 `CellRect.AdjacentCellsCardinal`、`GenAdj.OccupiedRect`、`Graphic_Linked.ShouldLinkWith`、`Graphic_LinkedAsymmetric.ShouldLinkWith`、`Graphic_LinkedTransmitter.ShouldLinkWith`、`LinkGrid.Notify_LinkerCreatedOrDestroyed`、`GraphicData.Linked`、`LinkFlags`、`CompProperties_Power.transmitsPower`。
- 真实加载的 Def 属性由实机套件导出至 `artifacts/throw-connections.json`，不使用未合并的原始 XML 推断 Def。墙为 `CornerFiller + Rock/Wall`，围栏为 `Asymmetric + Fences` 并连接 `Rock/Wall` 与门；桌椅、炉灶为 `None`；明线与隐藏电缆都是 `Transmitter + PowerConduit`。

## 验证

所有日志与被测候选身份位于 `DevData/Evidence/ThrowConnections-20261026/`。

- 修改前隔离离线验证通过，见 `baseline.log`。首次沙箱启动未完成，随后使用模组外独立工作目录重新执行成功。
- 修改后离线验证通过，见 `validation.log`：两个运行时均通过全部 45 项检查；单测 1101 项中 1099 项通过、2 项显式未运行，零失败；Shader 检查通过。新增 11 项连接配对单测覆盖相容/不相容类别、普通家具、第三方 Custom 标记、MapEdge、非对称连接与门。
- 新增 `throw-connections` 实机套件，默认不运行，可显式选择或随 `all` 运行。它调用被测构建自己的连接判定和 `CanPickUp`，记录实际拒绝原因；普通设备用真实 `CompPower.ConnectToTransmitter` 接电后测试。
- 旧正式构建：`game-before/20261026-234244`，22 个场景，69 通过 / 0 失败 / 0 跳过，复现普通家具与接电炉灶被误禁。
- 修复候选：`game-fixed/20261026-234342`，相同 22 个场景，69/0/0，普通家具、靠墙桌椅、覆盖隐藏电缆和接电炉灶均通过实际 `CanPickUp`；墙、山体、围栏、门、沙袋与电缆的真实连接继续被拒绝。
- 最终扩展候选：`game-fixed/20261026-234544`，24 个场景，75/0/0。新增电缆接电池及反向举起电池的检查：电缆被连接规则拒绝，电池能通过 `CanPickUp`。
- 候选 DLL SHA-256：`89E7547EACE6285ACEE9A5CF1AE46EA4286D3EFDDD2B93F5939242760479622E`，由隔离源码副本通过 `DeployToMod=true` 构建到带标记的候选模组目录，未覆盖正式 DLL。
- 首次测试编译发现报告路径可能为空，已补检查；首轮旧版实机的 67/1/0 是测试错误地假定孤立电缆足够轻。实测电缆因最大耐久 80 而重 800 kg，高于施术者 550 kg 搬运上限；修正为检查连接放行而不忽略重量，原失败记录保留。
- 文档检查仍有 2 处既有失败：根 README 与 `thinktree-key-migration-2026-10-16.md` 指向缺失的 `DevData/Releases/雪牛娘1017-兼容修复.zip`。开始时版本已含这两个链接；没有为通过检查恢复缺失的旧资源。新增链接有效，`git diff --check` 通过。另有 4 个既有仓库外链接警告。

游戏内测试全部经 `Test-InGame.ps1` 的共享锁、现有 Steam 安装及临时 `savedatafolder` 运行，静音隐藏窗口。未访问玩家真实存档或改动正常配置。此轮验证范围是举起连接判定，未重新验证飞行、爆炸和第三方专有管网。

## 交付状态

源码与规则文档已更新，正式 `1.7/Assemblies` 仍为 10-25 构建；本轮没有 Git 提交、正式部署、打包或上传。

收尾核对 852 个候选构建输入与当前生产源码一致。候选 DLL/PDB 已保存至证据目录 `tested-candidate-assemblies.zip` 并逐条目解压核对哈希；核实绝对路径、专用标记及全树无链接后，已删除本轮临时候选模组。正式 DLL 未变，冻结目录及 `LoadFolders.xml` 没有差异，详见 `delivery-audit.json`。
