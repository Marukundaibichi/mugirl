# 梳妆台首次选择不刷新（2026-09-14）

已安装 HAR `AlienRace.dll`（MVID `71176dfdd54d4f06985e6706cfd7af4e`）的
`StylingStation.DoAddonInfo` 会直接写入 `alienComp.addonVariants`，但没有使人物图形失效。
附件颜色分支只标记头像缓存，`DoChannelInfo` 的颜色编辑也没有重建人物渲染树。
因此，选择数据可以已经改变，而人物仍显示此前的附件。

`Harmony_StylingStationRefresh` 在 HAR 的 `DoRaceTabs` 输入处理前后比较附件变体、
附件颜色和颜色通道。发生变化后调用 `SetAllGraphicsDirty` 与 `PortraitsCache.SetDirty`。
HAR 的 `CompRenderNodes` 在重建时会通过 `RegenerateAddonGraphic` 读取最新保存编号。
补丁只作用于奶牛娘，不跳过原方法，不修改选择数据，不保存跨帧或跨存档状态。
颜色元组必须按值复制，因为 HAR 原地修改这些对象。

Layout/Repaint 不创建快照；未改变外观的输入不触发刷新。
HAR 原有 `ResetPostfix(true)` 仍负责恢复数据并刷新图形，确认时仍使用原有应用流程。

验证：Release 编译及 `docs/tools/Test-StylingStationRefresh.ps1` 的 10 项断言通过。
测试执行实际补丁源码，使用受控的 HAR、Unity 和渲染桩；不代表已完成游戏内视觉验证。

游戏内回归步骤（需重启以加载新 DLL）：

1. 打开奶牛娘的梳妆台，切换身体附件，单击另一款式，检查预览即时更新。
2. 确认并关闭窗口，检查地图人物与所选附件一致，再打开窗口检查选择保持。
3. 更换附件颜色、清除覆盖色和修改种族颜色，检查预览与地图人物同步。
4. 更换款式后分别使用重置和取消，检查原来的外观恢复。
5. 检查普通人类的发型更改仍按原有梳妆台工作流程应用。

## 2026-09-16：启动时 HAR 图标在错误线程加载

用户日志明确指向 `MugirlBootstrap.TryPatchClass → Harmony/Mono 方法编译 → AlienRace.StylingStation..cctor`。
该静态构造加载 `LinkChain`、`ClearButton` 和 `LinkVanilla` 三个 UI 贴图；原来的注册在 Mod 构造函数所在的加载线程发生，提前触发了静态构造。
YaOpt 的资源检查会报告错误，但这个调用链由 Mugirl 的造型台补丁触发，不能归为无关的 HAR 启动提示。

已核查实际 HAR `71176dfdd54d4f06985e6706cfd7af4e:0600019E:M` 的 IL，三个图标均在静态构造中立即加载。
真实游戏 `LongEventHandler.ExecuteWhenFinished` 及其执行队列按登记顺序处理完成回调。

修复集中在 `MugirlBootstrap`：

- 仅将 `Harmony_StylingStationRefresh` 的注册放入加载长事件的完成回调，在主线程执行。
- 回调仍通过既有 `TryPatchClass`，保留成功登记、异常隔离和现有补丁元数据。其他需要在 Def 加载期执行的补丁照常注册。
- 最终补丁审计也排在延迟注册之后；重复调用 `Initialize` 仍由现有 Harmony 实例检查阻止重复注册。
- 外观比较与刷新行为不变；不替换 HAR 的贴图字段、不屏蔽线程错误，也不需要改动 HAR 或 YaOpt 文件。

可选冷启动驱动为 `docs/tools/StylingStartupValidation.cs`，只在显式 `EnableStylingValidation=true` 构建中包含。
`Start-StylingStartupValidation.ps1` 为每次运行新建独立 `TMP/StylingStartup-*` 配置，支持 `-WithYaOpt`（同时加入其延迟纹理加载所需的 Prepatcher）；驱动检查真实图标、Harmony Prefix/Postfix 数量及重复初始化后状态，随后退出，不创建或读取玩家世界。
Prepatcher 会从内存重新加载程序集，测试路径核对使用实际 `MugirlMod.ContentRoot`，不依赖可能为空的 `Assembly.Location`。

实际验证：

| 隔离运行 | 环境与结果 |
| --- | --- |
| `TMP/StylingStartup-WithoutYaOpt1` | Harmony、全部 DLC、HAR、Mugirl：18 PASS / 0 FAIL |
| `TMP/StylingStartup-WithYaOpt1` | 加入 YaOpt，未加入 Prepatcher：18 PASS / 0 FAIL；此轮日志显示延迟纹理加载未启用，不作为该优化路径的验证 |
| `TMP/StylingStartup-WithYaOpt3` | Prepatcher、Harmony、全部 DLC、YaOpt、HAR、Mugirl：19 PASS / 0 FAIL；Prepatcher 已实际完成程序集重载，YaOpt 正常启用 |

三轮完成日志中均无图标异线程错误或静态初始化异常。三个 HAR 静态字段均持有正常 Texture2D，且与对应 ContentFinder 资源为同一实例；真实目标上的 Mugirl Prefix/Postfix 各一份，重复初始化后保持一份。每个目录保留 `Player.log`、`styling-startup-checks.txt` 和 `styling-startup-complete.txt`。
`WithYaOpt2` 是修复测试路径检测前的失败夹具，未计入通过结果，其独立进程已关闭。

原有外观刷新 10 项断言、完整静态检查（跳过构建阶段）及独立正式 Release Rebuild 均通过。正式 DLL 已排除造型台与巨企验证驱动，大小 664,576 字节，SHA-256 为 `09865E981EF4B834E0A2AAEE436032A8FD87591198AB8823633716C040E7D02B`。

替换 DLL 后需要完全退出并重新启动游戏：已经在旧进程触发过的静态初始化不会因重新读取存档而重跑。
