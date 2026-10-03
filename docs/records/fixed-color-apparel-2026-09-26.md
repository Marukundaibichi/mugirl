# 固色服装名单扩展与隐藏背负武器设置（2026-09-26）

## 版本与范围

- 版本：未发布（基于 0.6.7 之后的更新工作区）。
- 需求：
  1. 一批成套服装取消染色，无论材料与造型台染色都保持贴图原色。
  2. 新增模组设置项“隐藏武器轮盘背负武器”，只隐藏背上的两把普通背负武器。

## 取消染色

### 机制

- 原版穿着渲染的颜色来自 `Apparel.DrawColor`（材质色、`colorGenerator`、造型台染色都会写它），`ApparelGraphicRecordGetter.TryGetGraphicApparel` 直接用它请求穿着图形；造型台（`Dialog_StylingStation.DrawApparelColor`）对所有穿着服装开放染色入口，`ApparelProperties` 没有 def 级“禁止染色”字段（1.6 反编译核实，无 `careIfDyed`/`dyable`）。
- 原版“海军装甲不被染色”实为“不可制作材料 + 灰阶 colorGenerator”，它本身带 `CompColorable`、仍可在造型台被玩家染色；不存在可借鉴的原版固色开关。
- 因此沿用现有 `Harmony_Apparel_DrawColor`（Prefix 跳过 `Apparel.DrawColor` getter，返回 def 固有颜色）作为颜色锁定入口，把判定从硬编码 defName（原仅 `Mugirl_Spacesuit`、`Mugirl_ImmortalFairyAzure`）改为 apparel 标签 `MugirlFixedColor`（判定收敛到 `MugirlFixedColorUtility`）。两件旧固色服装已同步打标签，行为不变。
- 造型台染色入口：新增 `Harmony_StylingStationFixedColor_Ctor` / `_Reset` 两个 Postfix，在 `Dialog_StylingStation` 构造函数与 `Reset` 填充 `apparelColors` 后剔除固色成员（原版两处只按 `CompColorable` 过滤，私有字段经 `AccessTools` 反射读取，字段名变动时安全跳过）。染色 Tab 不再列出这些服装，`ApplyApparelColors` 也不会再写 `DesiredColor`。
- 保留行为：`WornByCorpse` 腐烂色、雕像 `StatueColor` 仍按原样叠加；`MugirlPatchInfo` 已登记全部三个补丁。

### 名单（28 个 def，全部在 `apparel.tags` 追加 `MugirlFixedColor`）

- `Apparel_0920.xml`：SisterMask（大姐姐口罩）、BrandBag、LeatherHeels、Police 全套 8 件（Shirt/Shorts/Thong/ZipperBra/Bra/CrossStickers/Boots/Cap）、Nun 全套 4 件（DressClassic/DressSimple/Veil/Blindfold）、KnightMount 三件（PlateWhite/Coat/Helmet，后两件原描述“可染色”，按需求改为固色）。
- `Apparel_Head.xml`：BunnyGirlHeaddress；`Apparel_OnSkin.xml`：BunnyGirlTop、OL、ImmortalFairyAzure（迁移）；`Apparel_Stockings.xml`：BunnyGirlStocking。
- `Apparel_Shell.xml`：RA_Carrying_Equipment（特化携行具，原有 tags 追加）。
- `Apparel_SpecializedGear.xml`：ReverseBunnySuit、ReverseBunnySticker、SpacesuitHeadgear、Spacesuit（迁移）。

### 影响

- 这些服装的材料类别与材料护甲加成（0.6.7 与本次头饰护甲补齐加入的 `StuffEffectMultiplierArmor`）全部保留，仅取消视觉染色；`graphicData.ignoreThingDrawColor`（部分头饰已有）今后只是冗余，不影响结果。
- 生成、交易、研究解锁、穿戴层数均不变。

## 隐藏背负武器设置

- `MugirlSettings.hideWeaponWheelBackWeapons`（默认关闭，`Scribe_Values` 持久化，键 `hideWeaponWheelBackWeapons`）。
- `MugirlSettingsWindow` 在“武器轮盘开发者日志”前新增复选框，键 `Mugirl.Settings.HideBackWeapons.Label/.Desc`（中英已同步）。
- 生效点：`PawnRenderNodeWorker_BackWeapon.CanDrawNow` 在设置开启时返回 false；仅作用于普通背负武器两把（第 1、2 格渲染节点）。自驱装弹系统凹槽武器（`PawnRenderNodeWorker_AutoloadingWeapon`）与手持武器渲染不受影响。开发者布局调试窗口的离屏预览（`BackLayoutPreviewActive`）不受此开关影响，便于调布局时仍可预览。
- 无新 Harmony 补丁；`Comp_WeaponWheel.CompTick` 的可见性刷新照常运行，仅绘制被拦截。

## 验证与限制

- 六个被改 XML 均通过解析检查；28 处标签插入逐一核对（脚本 `TMP/add-fixed-color-tags.ps1`，插入/追加各 25/3 处，输出见执行记录）。
- 修改 C# 后执行完整 Release 构建与静态验证，结果见当次验证输出。
- 未做游戏内视觉验收（fixed Player.log 缺失）：需在游戏内确认成套服装在布料/皮革制作后仍显示原色、造型台染色 Tab 不列出它们、设置开关即时生效、凹槽武器不受影响。
- 已知限制：AI 依意识形态配色自行染色（`JobGiver_OptimizeApparel`）不在造型台补丁范围内，信奉彩色服装意识形态的小人仍可能对固色服装排队染色 Job；颜色锁定使其无视觉效果，仅浪费染料。
- 武器轮盘背负布局默认值烤入：等待用户提供导出配置（`Config/Mugirl/WeaponWheelBackLayout.xml` 当前不存在，剪贴板亦无导出内容），暂未执行。
