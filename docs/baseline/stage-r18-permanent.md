# R18 Permanent Content Change

## Scope

本记录覆盖用户已确认的行为变更：取消 R18 设置按钮，R18 内容常驻。

该变更不涉及 XML 数值、装备数值、事件数值、tick、伤害、概率或产量。它只改变成人内容的可见性策略和旧清理逻辑。

## Completed Changes

### Settings

- 删除 `MechanoidWorkControlSettings.enableAdultContent` 字段。
- 删除 `enableAdultContent` 的 `Scribe_Values.Look`。
- 从设置窗口删除 R18 checkbox。

### Runtime Gate Removal

- 删除 `AdultContentUtility` 运行时门禁。
- 服装生成、榨乳器 gizmo、交易库存和地图物品不再执行成人内容过滤判断。
- 不保留 no-op 清理通道，避免后续代码误以为仍存在可关闭状态。

### XML Compatibility Shell

- 保留 `AdultContentExtension`，让 XML 中的 `adultOnly` 元数据继续可加载。
- 保留 `CompProperties_AdultContentControl` 和 `CompAdultContentControl`，让现有 Def 的 comp class 引用继续可加载。
- `CompAdultContentControl` 不再注册生成、交易、装备等运行时钩子。

## Removed Behavior

- 不再有玩家可见 R18 设置按钮。
- 不再保存或读取 R18 设置字段。
- 不再关闭成人内容。
- 不再清理地图、容器、商队、世界对象或穿戴中的成人内容物品。
- 不再从 trader stock 生成结果中剔除成人内容。

## Validation

已执行 Debug 重建：

```text
MSBuild 1.6/Source/WRace/MooGirlRace.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU
```

结果：通过，输出 `1.6/Assemblies/MooGirlRace.dll`。
