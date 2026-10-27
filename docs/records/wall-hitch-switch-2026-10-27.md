# 墙壁栓点多余开关修复

## 原因与修复

`1.7/Defs/ThingDefs_Building/Building_Rope.xml` 的 `Mugirl_WallRopeHitch` 显式添加了 `CompProperties_Flickable`。原版组件默认使用电力开关的图标、文字与快捷键；生成按钮只要求建筑属于玩家，不要求存在供电组件。点击后会创建原版拨动开关任务。

栓点及其原版父定义不含供电组件。`CompUsable_RopeToHitch`、`JobDriver_RopeToHitch` 和牵引状态维护均不读取开关状态，因此该组件没有栓系用途。本轮按[决策 §20](../refactor-1.7/decisions.md#20-墙壁栓点多余开关修正2026-10-27)仅删除这条 XML 组件声明。建筑身份、原有栓系组件、配方、数值和语言文件保持。

这是内容修正，重新加载模组定义即可生效，不需要重编译或部署正式 DLL。没有改动模组保存的类型、Def 名、字段或任务结构。旧开关的 `switchOn`、`wantSwitchOn` 属于原版组件写出的额外叶子；原版读取不要求消费这些叶子。旧的 Flick 待办由原版任务遍历剩余组件后正常删除，不需要新增存档迁移或领域兼容分支。

## 基线与验证范围

- 开始时保留了投掷建筑连接修复的 6 个已修改文件和 4 个未跟踪文件。Git 跟踪及未忽略的未跟踪开发文件共 4935 项归档于 `DevData/Backups/PreWallHitchSwitch-20261027/tree.zip`，每个条目解压核对 SHA-256；已忽略的历史资料原样保留。
- 当前游戏为 **1.7.4990 rev1069**。正式 DLL 保持 10-25 的 `C3ED6AA47454470F4EC61C2DDCAE114BA6ACEB97A2330C16FE4904003507DF5A`，修复不依赖尚未部署的投掷代码。
- 原版依据由 DecompilerServer 核对 `CompProperties_Flickable`、`CompFlickable.CompGetGizmosExtra/PostExposeData`、`FlickUtility`、`WorkGiver_Flick` 和 `JobDriver_Flick`。
- 证据目录：`DevData/Evidence/WallHitchSwitch-20261027/`。隔离游戏探针保留为 `WallHitchSwitchProbe.cs`，只在本次验证时临时登记，运行源码与原有套件登记表在构建后恢复。

## 离线检查

- 修复前、后在标准 .NET 与游戏运行时上均为 **44/45**。唯一未通过项都是 `C.textures` 的同一批 17 条原版贴图引用，修复前已经存在，没有为通过检查补回或替换资源。
- 基线常规单测 1101 项中 1099 项通过、2 项显式未运行，零失败；Shader 检查通过。生产 C# 没有变化，修复后不重复这两项。
- `baseline-*.json`、`fixed-*.json` 与 `validation-comparison.json` 保存两轮报告和比较结果。沙箱首次构建的 NuGet/缓存访问失败另保留于 `baseline.log`，隔离重试结果为 `baseline-retry.log`。
- 文档基线有 2 处既有失效链接，均指向缺失的 `雪牛娘1017-兼容修复.zip`，另有 4 个仓库外链接警告；未恢复该历史资源。
- 修复后的文档检查仍只有上述 2 处失败和 4 个警告，新增文档链接有效，`git diff --check` 通过。

## 游戏内验证

专项验证通过 `Test-InGame.ps1` 的共享锁、现有 Steam 安装和临时 `savedatafolder` 执行。核对新栓点的实际组件和命令、真实套绳与栓系菜单任务；在独立建筑实例上恢复原开关组件，生成开、关、待关三种真实 Scribe 状态，再以修复后的定义读回，检查绳索保持、待办结束、重存档及拆除解绳。

最终运行 `game/20261027-152238` 为 **26 通过 / 0 失败 / 0 跳过**，专项推进 518 tick。配置为 NoOdyssey（其余 DLC 启用）、中文、隐藏窗口；实际枚举建筑命令，没有截图或人工视觉验收。`artifacts/wall-hitch-switch.json` 保存实际组件与按钮清单，`report.txt` 和采集的两份存档保存交互与读回证据。结果确认：

- 新建筑没有开关或供电组件，也没有电力开关命令；套绳、右键栓系菜单及真实任务完成。
- 在原组件实例上复现开关按钮，真实写出关机与待关字段；重新载入后四个新/旧状态建筑均无开关，栓系组件和已系绳索仍在。
- 原版拨动任务能安全清除旧待办；继续 tick 后绳索保留，重存档不再写入开关字段，销毁栓点正常解绳。

加载阶段记录了 1 条未归因于模组的 `TicksAbs/gameStartAbsTick` 原版错误，框架没有将其计为专项失败；专项本身按 AnyError 策略无错误。不把此次 26/0/0 表述为整个启动日志完全无错误。

前两次探针编译分别发现报告接口参数遗漏和测试空值标注问题，未进入游戏，日志保留。首轮 `game/20261027-151603` 的 AllDlc 启动失败：4990 的 `-mod=` 日志明确表示只在本地 Mods 找依赖，因 Harmony/HAR 位于创意工坊而忽略它们，继而出现类型加载错误并等待地图超时。最终使用脚本既有的 `-Config NoOdyssey` 分支，通过临时 ModsConfig 加载依赖，35 秒正常退出。没有修改依赖、正常配置或测试脚本，也不将首轮失败改写为通过。

## 交付边界

只修改 1.7 XML 和相关文档。正式程序集、冻结的 1.6 内容、用户原有改动和正常游戏配置保持。没有 Git 提交、打包或上传。

`delivery-audit.json` 对照归档逐文件核对 4935 项：没有缺失文件，原有生产与测试源码、正式 DLL/PDB、冻结 1.6 和 `LoadFolders.xml` 均逐字节保持。临时探针只保存在证据目录；套件登记表恢复到本次开始时的内容，保留先前的投掷套件登记。
