# 多选雪牛娘挤奶 Gizmo 合并与名字条弹窗（2026-09-24）

## 版本与范围

- 版本：未发布（基于 0.6.7 工作区）。
- 范围：`1.6/Source/Features/Milk/Gizmo_MilkGauge.cs`、`1.6/Languages/{ChineseSimplified,English}/Keyed/Misc_Gameplay.xml`。无 Harmony 补丁、无 Def 改动。
- 需求（经三轮反馈收敛）：多选雪牛娘时奶量量杯 Gizmo 合并为一格；悬停后在 Gizmo 上方弹出量杯等宽的**单行**名字条，可切换量杯对应哪位雪牛娘；名字放不下时被裁剪，鼠标停在名字区左右边缘自动滚动查看；名字条最左为「全选」单元格，开启后拖动阈值统一修改所有选中雪牛娘（在按钮描述中提示）；样式为原版蓝色窗口底框，单元格之间用分隔线，不用土黄色 ButtonText。

## 原版机制依据（RW 1.6 反编译核实）

- `Verse.GizmoGridDrawer.DrawGizmoGrid`：对全部选中对象的 Gizmo 按 `GroupsWith` 归组并调用组头的 `MergeWith`，每组只绘制一个代表格。原版 `Gizmo.GroupsWith` 默认 `false`，因此改动前多选会并排出现 N 个 158px 量杯。
- `Verse.Gizmo_RoomStats.GizmoOnGUI`：悬停弹出交互窗的现成先例——`Mouse.IsOver` 时每帧调用 `WindowStack.ImmediateWindow(id, rect, WindowLayer.Super, func)`；`ImmediateWindow` 同 ID 复用、不再请求即消失，窗口内容为局部坐标（`Window.InnerWindowOnGUI` 中 `windowRect.AtZero()`）。

## 视觉调整（同日后续，以本节为准）

- 名字条与量杯之间增加 8px 留白，保持等宽对齐；弹窗打开后，留白纳入悬停走廊，鼠标跨越时不会关闭或清空选择。
- 修正内容从局部原点开始绘制造成的内边距不对称：四周统一 4px。保留蓝灰窗口底色，使用淡粉选中底色、粉色细下划线和浅色文字，分隔线降低对比度。
- 溢出时在名字区两端显示独立箭头；点击移动 28px，悬停以 120px/s 滚动，端点箭头变暗。名字本身不再触发边缘滚动，防止选择时目标滑动。保留底部滚动进度线。
- 绘制前保存、结束后恢复字体、文字对齐和换行状态，避免影响其他 Gizmo。
- 本轮基线静态检查在 Git 所有权检查处失败；以进程内 `safe.directory` 配置解决后，完整 Release 构建及静态/贴图检查通过。日志：`TMP/milk-gizmo-visual-validation.log`。构建前 DLL 保留于 `TMP/MilkGizmoVisualBackup/MugirlRace-before.dll`。
- 本轮未运行游戏内视觉验收，没有 fresh Player.log 作为运行证明。需复查 8px 间隙往返、箭头两端、多名字裁剪、中文/英文布局和全选阈值操作。下文紧贴、18px 隐形边缘区、320px/s 与原版选中高亮属于调整前记录。

## 实现要点

- `Gizmo_MilkGauge` 重写 `GroupsWith`（同类归组）与 `MergeWith`（组头收集各成员 `CompMooHasBodyResource`）。列表随帧重建，不跨帧缓存。
- 弹窗：紧贴量杯上沿、无间隙（悬停判定连续不闪烁）；悬停区 = 量杯矩形 ∪ 弹窗矩形。单行分段框：「全选」固定最左（不随滚动），右侧名字区超出宽度即裁剪；鼠标停在名字区左右 18px 边缘区内按 320px/s 自动滚动（仅 Repaint 事件累加），可滚动时底部画细进度线提示；单元格为文字 + 原版高亮（悬停 `DrawHighlight`、当前对象/全选开启 `DrawHighlightSelected`），相邻单元格与「全选」之间用 `Widgets.DrawLineVertical` 分隔；超长名字按单元格宽度截断并转由 tooltip 显示全名。首轮的 ButtonText 样式与二轮的折叠换行均已按反馈替换。
- 阈值：`SetThresholdFromMouse` 在「全选」开启且处于合并态时写入全部成员的 `MilkThreshold`，否则只写当前查看对象。量杯显示、tooltip 数据均取自当前查看对象。
- 静态 UI 状态（当前查看对象/全选/滚动/会话标记）按仓库规范写 `StaticCacheLifecycle:` 注释：Gizmo 每帧重建故放静态字段，鼠标离开或弹窗两帧未请求即复位，选中集变化时查看对象自动回退为组头。
- `Find.WindowStack` 访问走 `MugirlGameUtility.Windows`（Find access safety 规则）。

## 翻译键（中英已同步）

- `Mugirl.Milk.Gauge.ApplyAll.Label`（全选 / Apply All）
- `Mugirl.Milk.Gauge.ApplyAll.Desc`（全选提示：开启后阈值统一应用）
- `Mugirl.Milk.Gauge.MultiHint`（多选状态下附加在量杯 tooltip 末尾的操作提示）

## 验证结果

- `docs/tools/Invoke-Phase6StaticValidation.ps1`（pwsh，完整构建）：通过，exit=0；Release DLL 已输出至 `1.6/Assemblies/MugirlRace.dll`。日志：`TMP/phase6-validation.log`。
- 游戏内验证（待做，需多只产奶期雪牛娘）：
  1. 单选行为与旧版一致（量杯 + 拖阈值 + tooltip），不弹名字条。
  2. 多选 2/3/N 只：只出现一个量杯格；悬停弹名字条，点击名字切换；tooltip 显示当前对象数据并带 MultiHint。
  3. 名字较多：名字条单行显示且不超过量杯宽度；鼠标停在名字区左右边缘自动滚动，移开即停；可滚动时底部有细进度线，越界名字被裁剪。
  4. 全选开启（高亮态）后拖阈值：所有选中雪牛娘阈值同步变化；关闭后仅改当前对象；按钮悬停可见描述提示。
  5. 鼠标在量杯与名字条之间往返、以及点击名字时，弹窗不闪烁、点击不穿透到地图。
  6. 切换选中集/打开主标签页/截图模式后再回来，状态无残留。

## 已知限制

- 合并组代表为原版选定的组头（首个非 Disabled 实例）；若第三方模组禁用我们的 Gizmo，会退化为单只显示（无名字条），不影响正确性。
- 弹窗只在合并态（≥2 只）出现；阈值拖动期间弹窗保持，因为鼠标始终在悬停区内。
- 「全选」作用范围是本次选中的雪牛娘，不保存跨会话状态；每次悬停会话开始时复位为关闭。
- 名字条与量杯等宽，单行内每次只能看到 2~3 个名字，主要靠边缘悬停滚动浏览；如后续觉得滚动效率低，可再评估加宽弹窗或分页。
