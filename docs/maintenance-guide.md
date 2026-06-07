# 日常维护指南

本文档用于普通 bugfix、小功能调整和轻量内容维护。目标是让后续维护者知道从哪里下手、哪些东西不能随便碰，以及怎么验证。

## 目录速查

- `1.6/Source/Core`：启动、日志、Def 缓存、patch 元数据、通用工具。
- `1.6/Source/Features`：主要功能模块，例如 Milk、Restraints、Roping、Mounting、Incidents、Genes。
- `1.6/Source/Compatibility`：第三方 mod 反射、探测和兼容 glue。
- `1.6/Defs`：核心 Def、种族、装备、Job、Hediff、事件、场景。
- `1.6/Patches`：根补丁；可选集成补丁放在 `Versions/1.6/Integrations/*` 或条件加载目录。
- `1.6/Languages`、`Bio_1.6/Languages`、`Odyssey_1.6/Languages`：翻译文件。
- `Textures`、`Sounds`：资源文件。源素材和发布无关临时文件不要放进发布路径。

## 改代码前

先确认改动属于哪类：

- 规则变化：优先在对应 `Features` 模块处理。
- Def 引用：优先使用 `DefOf`、`MooGirlRequiredDefs` 或 `MooGirlOptionalDefs`。
- 第三方 mod 访问：优先放到 `Compatibility`，业务层只拿 null-safe 结果。
- Harmony patch：同步补 `MooGirlPatchInfo` 元数据，并说明失败行为。
- 存档状态：新增字段必须有默认值和 `Scribe` 兼容说明。

## 代码约束

- 运行期不要直接 `DefDatabase.GetNamed` 查字符串；需要缓存或通过 DefOf。
- 静态缓存必须写 `StaticCacheLifecycle:` 注释，说明生命周期和清理点。
- 每局游戏状态必须在新建、读档或初始化路径清理，避免跨档残留。
- Harmony patch 尽量小而明确。高风险 patch 要能解释是否跳过原方法、失败时如何降级。
- 反射访问要集中，缺字段或缺方法时不能在 tick 或渲染路径抛异常。
- 日志走 `MooGirlLog`，不要在业务代码里直接 `Verse.Log`。

## 改 XML/Def 前

- 改数值时记录原因。包括 tick、概率、产量、伤害、容量、价格、生成权重、库存数量、Hediff 严重度等。
- 可选 DLC 内容使用 `MayRequire` 或条件加载目录。
- XML patch 顶部保留 `PatchGovernance:`，并写清 `Targets`、`Scope`、`Duplicate guard`、`Failure`。
- 不要把补丁直接打到第三方 mod 目标，除非有条件保护和缺失兜底。
- 玩家可见文本要进入语言文件；Def 里的英文 label/description 属于内容文本，维护时按翻译规则处理。

## 验证命令

常规静态验证：

```powershell
.\docs\tools\Invoke-Phase6StaticValidation.ps1
```

只检查日志：

```powershell
.\docs\tools\Invoke-PlayerLogScan.ps1
```

发布包预检查：

```powershell
.\docs\tools\New-WorkshopPackage.ps1 -SkipBuild
```

## 常见风险

- 修改 XML 类名会影响 RimWorld 反射加载。
- 修改 Scribe 字段名会影响已有存档。
- 修改根命名空间类型可能影响 XML、Harmony metadata 或存档类名。
- 修改补丁 xpath 可能在缺 DLC 或缺第三方 mod 时红字。
- 新增贴图、音效后必须确认路径大小写和发布包包含情况。
