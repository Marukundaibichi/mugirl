# 自驱装弹系统南向绘制层级修复记录

## 最终修正：身体前、装弹架中、收纳武器后

- 用户复验 97 后仍有局部穿插，并指出装备遮住身体；此前 96/97 方案撤回。南向恢复架体 -3、凹槽枪 -4..-7，普通背负枪穿戴装弹系统时默认 -8。
- 实际程序集证据：`GraphicMeshSet(float,float)` 调用 `MeshMakerPlanes.NewPlaneMesh(..., backLift:true)`，顶点 Y 最高 0.0018292684；`PawnRenderUtility.AltitudeForLayer` 每层只有 0.0003658537，并钳制在 -10..100。武器旋转 90° 后，抬升斜面方向不同于装弹架，1–2 层中心高度差不能阻止局部交叉。
- 两种武器节点的 `GetMesh` 仅在南向改用无 backLift/twist 的原版缓存 `MeshPool.GridPlane(Vector2.one)`，保留完整尺寸、UV、镜像及锚点，其余朝向沿用原网格。没有修改共享网格顶点。
- 本轮同时按用户要求把背负固定到轮盘第 1、2 格：未亮出的主武器也背负，手持、瞄准与动画时隐藏，不从后续槽位补位；贴图读取与可见性分开，两个槽位的可见状态变化时刷新绘制请求。数值调试加入直接输入，见背负调试记录。
- 修改前静态基线通过（`TMP/autoloading-depth-baseline.log`）；原 DLL 与相关文件保留于 `DevData/Backups/AutoloadingDepth-20260924`。

### 隔离游戏验证（2026-09-24 23:07，本机）

- 使用原版全 DLC、Harmony、HAR、Mugirl 的独立 quicktest 地图，未读取用户存档；测试 DLL 独立构建于 `%LOCALAPPDATA%/RimWorldModTests/Mugirl/AutoloadingDepth-20260924-230702-Game`，无模组内目录联接。
- `ProbeOutput/probe-complete.txt`：`failures=0`。检查六格装入、无/有装弹系统时第 1/2 格固定映射、未征召主武器背负、亮枪隐藏且不补位、负小数与非法数字输入。
- 四把槽枪的南向顶点全部等高，实际变换后的最高顶点低于架体最低顶点；间隙依次为 0.0003658537、0.0007317073、0.001097561、0.001463415。北向仍保留原抬升网格。
- 输出四向 `preview-0..3.png`，南向预览已目检，身体遮住架体中央、架体遮住凹槽枪重叠部分；完整枪械伸出架体轮廓的部分仍正常显示。测试角色和装备组合不同于用户存档。
- 实际打开调试窗口并运行 GUI 绘制；fresh `Player.log` 经 `Invoke-PlayerLogScan.ps1` 检查未发现可疑行，扫描结果 `TMP/autoloading-depth-playerlog-scan.txt`。数值输入解析已自动检查，未自动模拟键盘逐字符输入。
- 正式 Release 构建及全套静态检查通过（`TMP/autoloading-depth-validation.log`），正式 DLL 已更新。目录环路审计 `TMP/autoloading-depth-directory-audit.json`：`affected_candidates=0`、`scan_errors=0`。

下文保留历史试调经过，不代表当前配置。

## 后续修正：97 层试调

- 本轮用户截图明确显示左侧凹槽枪局部盖过装弹架；此前记录把现象归因为手持枪，尚未完成游戏内验证，不能当作已修复证据。
- 按用户“先试 97”的要求，仅将南向装弹架 `apparel.drawData.dataSouth.layer` 从 96 改为 97；四把凹槽枪仍为 95..92，其余朝向和锚点不变。最高凹槽枪与架体的层级差从 1 增至 2。
- 下文保留第一次 96 层调整记录。97 是否足以消除截图中的局部穿插，仍须在游戏中按相同装备、南向检查；不据此断言贴图存在某个固定方向的深度坡度。
- 本轮隔离输出构建：0 警告、0 错误；完整静态检查及正式 Release 构建通过，日志 `TMP/weapon-layout-validation.log`。未启动游戏，本轮没有 fresh Player.log 或渲染截图验收。

## 版本与范围

- 日期：2026-09-24，基于 0.6.7（131ee89）之后的工作区。
- 现象：角色朝南穿戴自驱装弹系统时，当前手持武器斜跨遮挡左侧装弹口上的凹槽武器（用户报告"最左侧的一把武器有裁剪问题"），期望整套系统始终显示在手持武器上方。
- 范围：仅南向绘制层级；不改动凹槽锚点、武器尺寸、动画与其他朝向的遮挡关系。

## 根因

- 手持武器由原版 `PawnRenderNodeWorker_Carried` 绘制，`baseLayer=90`，仅朝北经 `PawnRenderNodeProperties_Carried` 的 drawData 降为 `-10`；朝南为全pawn最高层之一。
- 装弹架本体是 `Belt` 层 apparel。原版 `DynamicPawnRenderNodeSetup_Apparel` 对无自定义 drawData 的 utility 层服装默认给 `North=93 / South=-3`，南向画在身体后面。
- 本模组凹槽武器节点南向层级为 `-4 - DisplayIndex`（-4..-7）。三者叠加后手持武器（90）必然盖住装弹架（-3）与全部凹槽武器（-4..-7）。
- def 自带 `apparel.drawData` 时，原版直接采用 def 数据、跳过默认 pack 层覆盖（已核对 `DynamicPawnRenderNodeSetup_Apparel.ProcessApparel` 与 `PawnRenderUtility.RenderAsPack`：drawData 不影响 belt 偏移/缩放，仅改层）。

## 改动

| 文件 | 改动 |
| --- | --- |
| `1.6/Defs/Apparel/Apparel_WeaponWheel.xml` | `Mugirl_AutoloadingSystem` apparel 增加 `drawData`：`dataNorth=93`（保持原版 utility pack 层）、`dataSouth=96`（压过手持武器 90） |
| `1.6/Source/Features/WeaponWheel/PawnRenderNode_BackWeapon.cs` | `PawnRenderNodeWorker_AutoloadingWeapon.LayerFor` 南向由 `-4 - DisplayIndex` 改为 `95 - DisplayIndex`（95..92，全部高于 90 且低于架体 96，保持"枪在架体后方"与错深防共面）；背挂节点注释同步 |
| `docs/features/weapon-wheel-design.md` | 层级描述段落与渲染验收条目同步 |

## 调整后南向层序（后 → 前）

背挂武器（第 2 格，-8）→ 身体与普通服装 → 手持武器（90）→ 凹槽武器 index3..index0（92..95）→ 装弹架本体（96）。

北向（93 架体 / 94 凹槽 / -10 手持）与东西向层级均未改动。

## 验证结果与限制

- `docs/tools/Invoke-Phase6StaticValidation.ps1`（完整构建）：通过，退出码 0（2026-09-24，本机）。
- 游戏内验证待做：朝南穿戴装弹系统并手持长枪，确认最左侧凹槽武器与装弹架不再被手持武器裁剪、四把凹槽枪无共面闪烁、北向与东西向观感不变；头像/信息卡中装弹架显示不受层值影响（def drawData 仅参与排序）。
