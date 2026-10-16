# AI 协作规则

修改前先读根目录 [README.md](README.md)，再按其中的任务表阅读相关专题。根 README 是唯一开发手册，本文件只保留 AI 必守规则。

- 不在子目录新增 `README.md`、`AGENTS.md` 或同用途的重复入口。通用流程、目录说明和文档导航只维护根 README。
- 新文档使用明确的主题名，优先补充已有专题，并在根 README 添加链接；移动或合并文档时同步修正文档链接及 `tools/Test-Docs.ps1` 的必需文档清单。
- 先记录工作区基线，保留用户现有改动。历史验证记录不代表当前工作区已经通过检查，不为消除旧失败擅自恢复用户删改的资源。

## 1.7 重构

- 开始前先读[重构完成记录](docs/records/refactor-1.7-resume-2026-10-15.md#最终验收与交付)和[约束性决策](docs/refactor-1.7/decisions.md)。原交接与波次计划仅供追溯，不对当前树重放。`decisions.md` 与 `design.md` 冲突时以 `decisions.md` 为准。
- 除非 owner 明确要求，不执行 `git commit`、`stash`、`checkout`、`reset`、`clean`，也不建 worktree。基线是 `DevData/Backups/` 中的整树归档，不是 git 提交。
- 并行代理的文件归属必须严格不相交：每个文件同一时间只有一个负责人，其他人通过报告提出修改请求。Wave R2 之后以 `tools/RenameApply/specs/ownership-postR2.json` 为准。
- 隔离构建：每个代理用独立的 `tools/Validate.ps1 -WorkRoot`，或在 `%TEMP%` 的源码副本中构建；工作目录不能位于模组目录内。dotnet 在 `Source/` 中启动。
- 只有在任务明确要求部署时，才写 `1.7/Assemblies`，而且只能用 `-p:DeployToMod=true`。不要手工复制 DLL。
- 游戏内运行（`SmokeTest.ps1`、`Test-InGame.ps1`、`New-SaveFixture.ps1`）由 `%TEMP%\mugirl-ingame.lock` 串行化。不绕过锁，不另行启动 RimWorld。
- owner 的存档、`Config/` 和 `ModsConfig.xml` 只读。测试使用临时 `-savedatafolder`，结束时只删除本次带标记的临时目录。
- 旧名字和旧存档结构只写在 `Source/MugirlRace/SaveUpgrade`，领域代码不承载旧档兼容（ADR-007）。
- 冻结的公开 API（ADR-008）不改名、不改形状。权威清单是 `tools/RenameApply/specs/public-api.json`（151 项），验证器 `C.public-api` 直接读取该清单；不恢复旧的 107 项副本。
- 未完成验收的集成构建不得发布，也不得读写 owner 的真实存档；游戏内验证只用测试脚本的临时存档目录。旧档升级表已接入，当前验收状态以根 README 和恢复记录为准。
- 不修改 `1.6/`、`Bio_1.6/`、`Odyssey_1.6/`、`Versions/1.6/` 和 `LoadFolders.xml` 的 `v1.6` 列表。

## 测试与临时文件

- 2026-09-29 用户要求清理旧测试游戏目录；RimSort 的外置副本规避规则已撤销。
- 默认使用构建、静态检查和已有的 Steam 游戏安装。不要为每轮验证复制 RimWorld 启动程序或运行库，也不要建立带游戏资源联接的独立游戏树。
- 复制游戏的旧启动脚本已停用。历史文档中的副本路径和命令只作记录，不据此重新生成测试环境。
- 必要的游戏内验证可以隔离配置和测试存档，保留用户正常游戏配置、存档及正式模组文件。清理仅限本次测试生成的数据。
- 文件系统清理核实绝对路径并跳过目录链接目标，保护源码、原始素材及用户未提交的改动。
