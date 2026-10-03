# 背负武器布局数据化与调试窗口（2026-09-24）

## 用户预设与双轴镜像

- 采用用户粘贴的八条 XML 数据作为内置 Defaults：所有 Z=-0.08；槽位 1 北/东/南/西 X=0/-0.3/0/0.3、角度=60/90/60/-60；槽位 2 X=0/-0.5/0/0.5、角度=-60/-60/-60/90。图层和南向装弹系统 -8 均按原文保留，不将不对称角度自动对称化。
- 每条布局新增 `FlipHorizontal` / `FlipVertical`，窗口提供水平/垂直镜像复选框，默认关闭，可同时开启。XML 对应 `flipHorizontal` / `flipVertical`，旧配置缺项为 false；XML/C# 导出、恢复默认和预览均覆盖新字段。
- 镜像只翻转旋转前贴图的 UV，不改变几何顶点、绕序、偏移、角度或深度。使用启动主线程预建的有限网格副本，避免修改原版共享网格或在并行绘制中创建 Unity 对象；普通背负节点侦测镜像状态变化，刷新实际绘制请求。凹槽武器布局不在本轮镜像调整范围。
- 修改前静态基线通过：`TMP/back-mirror-baseline.log`；旧 DLL 和相关源码备份 `DevData/Backups/BackMirror-20260924`。
- 隔离游戏 `AutoloadingDepth-20260924-232502-Game/ProbeOutput` 检查通过（`failures=0`）：逐项核对八条预设、两槽位四朝向的 H/V/双镜像 UV、几何顶点和三角形保持不变、关闭镜像恢复原网格、绘制请求失效、XML 镜像状态往返、旧 XML 缺省关闭、C# 导出。输出四向预览及北向四种镜像组合图片，水平镜像预览已目检；实际打开调试窗口后的 fresh log 无可疑行（`TMP/back-mirror-playerlog-scan.txt`）。
- 正式 Release 构建及完整静态检查通过（`TMP/back-mirror-validation.log`），DLL 已更新。目录环路审计（`TMP/back-mirror-directory-audit.json`）确认 `affected_candidates=0`、`scan_errors=0`。

## 后续调整：固定槽位与数字输入

- 两个普通背负位置固定对应轮盘第 1/2 格，不再选前两把备用枪。未亮出的主武器可背负；手持、瞄准、架枪/退枪时隐藏原位置，后续格不补位。穿戴装弹系统也保留第 1/2 格的背负位置，第 3–6 格仍为凹槽武器。
- 参数行增加数字输入框，保留滑杆与 ± 按钮；支持负数、小数，范围外、NaN、Infinity 和不完整输入不会进入渲染参数。输入中的 '-' 和小数点保留供继续编辑，错误输入以下划红线提示。武器/朝向切换、重置及滑杆变动会同步数值。
- 开发者窗口内标签改为“轮盘第 1 格 / 第 2 格”。布局覆盖文件格式不变，原两条背负位置配置现在分别用于这两个固定槽位。
- 隔离游戏实际验证固定槽位、亮枪隐藏及数值解析通过，窗口打开后的 fresh log 无异常；详情与截图位置见[南向修复记录](autoloading-south-layer-2026-09-24.md)。

## 本轮重写：开发者入口与角色预览

- 入口移至开发者动作菜单 `Mugirl → Back weapon layout / 背负武器微调`，移除 `CompGetGizmosExtra` 中的调试 Gizmo。
- 阅读并参考指定 MRK 模组的 `Modules/MRK_ALG_WeaponBack/Source/WeaponBackEditor.cs` 与 `WeaponBackPreviewRenderer.cs`：采用角色离屏预览、朝向按钮、独立参数面板、脏标记刷新及关闭释放纹理的结构；不依赖 MRK 程序集。
- 左侧优先预览选中的雪牛娘，可切换已加载地图中的角色、四个朝向及缩放；右侧选择普通背负武器 1/2，滑杆连续调节，± 按钮精调偏移 0.005、角度 1°、图层 0.5。可重置当前朝向或全部默认，保留 XML/C# 复制、配置写入及打开文件夹。
- 预览直接使用正式 Render Node 与角色现有装备，打开时暂停游戏，不生成预览 Pawn 或替换装备。装弹系统仍只有一把普通背负枪，凹槽枪在预览中用于遮挡对照，锚点不加入调整范围。
- 同步预览期间通过 Pawn 自己的 Comp 临时允许肖像武器节点；`finally` 恢复标记并要求重算绘制请求，防止影响随后同向的普通头像。关闭窗口释放 RenderTexture；导出/预览异常显示窗口状态，不向 GUI 循环持续抛出。
- 配置沿用原格式；修正朝向数字超出 0..3 时的数组越界，以及 NaN/Infinity 进入绘制参数的问题。
- 修改前保留原有工作区差异，相关源码和正式 DLL 备份于 `DevData/Backups/WeaponLayout-20260924-rewrite`。基线通过（沙箱中的 Git 不可用，提权复查后通过）；日志 `TMP/weapon-layout-baseline.log`。
- 验证：隔离目录构建 0 警告、0 错误，完整静态检查及正式 Release 构建通过（`TMP/weapon-layout-validation.log`）。本轮未启动游戏，仍需检查四向切换、± 微调、导出重启加载、关闭后的普通头像及 fresh Player.log。

下文保留初次实现记录；其中 Gizmo 入口、无预览窗口和较窄滑杆范围已被本轮重写替代。当前行为见[轮盘设计](../features/weapon-wheel-design.md)。

## 版本与范围

- 版本：0.6.7 之后的开发中改动（未单独发版）。
- 范围：武器轮盘普通背负武器（第 1、2 格）的摆放默认值调整；新增开发者模式布局调试窗口与导出。自驱装弹系统凹槽武器（第 3–6 格）不在本次范围内。

## 改动内容

### 1. 背负位置默认值调整

`PawnRenderNodeWorker_BackWeapon` 的偏移、角度、图层不再写死，改由 `WeaponWheelBackLayout`（新文件）按“武器 × 朝向”提供。默认值前后对照：

| 参数 | 旧值 | 新值 |
| --- | --- | --- |
| 南北向左右分开 | ±0.11 | ±0.15 |
| 南北向交叉中心 Z | -0.04 | +0.02 |
| 交叉角度 | ±43° | ±45° |
| 东西向横向 | ±0.0605（0.11×0.55） | ±0.0825（0.15×0.55） |
| 东西向 Z / 摆角 | +0.03 / ±8° | 不变 |
| 图层（北/南/东西 1/东西 2） | 22 / -2 / 18 / -1 | 不变 |
| 南向·装弹系统层级 | -8 | 不变 |

南北朝向两把武器仍交换左右位置（第 1 把南向 X 为负、北向为正），该行为现在直接体现在默认表中，而不是代码分支。

### 2. 调试窗口（开发者模式）

- 选中任意雪牛娘出现“背负武器布局调试”Gizmo（`Comp_WeaponWheel.CompGetGizmosExtra`，仅 `Prefs.DevMode`），打开 `Dialog_WeaponWheelBackLayoutDebug`。
- 按朝向分组，同屏两栏微调两把武器的横向偏移 X（步进 0.005）、纵深偏移 Z（步进 0.005）、旋转角度（步进 1°）与图层级（步进 0.5）；南向时另有“穿戴装弹系统时层级”滑杆。
- 布局数据为进程级全局状态，渲染 Worker 每帧读取（`PawnRenderTree.TryGetMatrix`/`GetTransform` 每帧调用 Worker），滑杆拖动对场上所有雪牛娘立即生效，不需要 SetDirty 或重建渲染树。

### 3. 导出与导入

- “复制 XML”/“写入配置文件”输出同一份 XML；文件写到 RimWorld 的 `Config/Mugirl/WeaponWheelBackLayout.xml`（`GenFilePaths.ConfigFolderPath` 下），下次启动游戏时自动覆盖内置默认值（首次访问时一次性加载，加载与失败日志走 `MugirlLog.DevMessage` / `WarningOnce`）。
- “复制 C# 默认值”输出可直接粘贴回 `WeaponWheelBackLayout.Defaults` 的数组字面量，便于固化新默认值。
- XML 中 `weaponIndex` + `facing`（名称或数字）定位条目，缺项回退 0；解析失败整体回退内置默认值。数值读取时钳制：偏移 ±1.5、角度 ±180、图层 -50..150。
- 覆盖文件只服务开发调试；数值烤进 `Defaults` 后必须删除该文件，否则旧文件会继续压过新默认值。

## 验证

- `docs/tools/Invoke-Phase6StaticValidation.ps1`（完整构建 + 静态检查）通过，Release DLL 已输出到 `1.6/Assemblies/MugirlRace.dll`。期间修复两处检查器意见：功能代码开窗改走 `MugirlGameUtility.TryAddWindow`；动态拼接的翻译键改为静态键数组。
- 游戏内验证待做：选中雪牛娘查看开发者 Gizmo、四朝向滑杆即时生效、XML 写入/重启加载、装弹系统层级联动。按[验证手册](../validation-runbook.md)检查 fresh `Player.log`。

## 限制

- 调试窗口滑杆范围固定：偏移 ±1、角度 ±180、图层 -20..100（文件导入允许更宽的 ±1.5 / -50..150）。
- 布局是全局共享的，不支持按 Pawn 或按武器类型微调；对巨人症等体型变化没有专门适配（沿用原版行为，偏移不随体型缩放）。
- 凹槽武器（第 3–6 格）的锚点仍是固定表，未纳入本窗口。
