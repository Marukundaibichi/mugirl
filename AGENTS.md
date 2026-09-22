# AI 协作规则

修改前先读根目录 [README.md](README.md)，再按其中的任务表阅读相关专题。根 README 是唯一开发手册，本文件只保留 AI 必守规则。

- 不在子目录新增 `README.md`、`AGENTS.md` 或同用途的重复入口。通用流程、目录说明和文档导航只维护根 README。
- 新文档使用明确的主题名，优先补充已有专题，并在根 README 添加链接；移动或合并文档时同步修正文档链接及静态验证清单。
- 先记录工作区基线，保留用户现有改动。历史验证记录不代表当前工作区已经通过检查，不为消除旧失败擅自恢复用户删改的资源。

## RimSort 卡死事故：必须避免再次引入目录环路

2026-09-18 已确认本机 RimSort v1.13.2 点击模组时，会在 GUI 线程递归统计整个模组目录；它跟随 Windows Junction/目录符号链接，没有环路检测。此问题与 `About/About.xml` 的内容无关。完整事故、迁移记录和复查方法见 `docs/rimsort-directory-cycles-2026-09-18.md`。

- 禁止在模组内部创建指回本模组、其他开发模组或游戏根目录的测试游戏目录联接。`.gitignore`、`LoadFolders.xml` 和不启用该模组都不能阻止 RimSort 遍历这些目录。
- 含 Junction/目录符号链接的测试运行环境统一放到 `%LOCALAPPDATA%\RimWorldModTests\`，不得放进任意 RimSort 实例的 Mods、Workshop 或 Data 扫描目录。不能用原路径的 Junction 将外部测试环境挂回模组。
- 本仓库 `docs/tools/Start-CorporateValidation.ps1` 已使用外部测试目录。普通日志、配置、无联接的打包文件可以继续写入 `TMP`。
- 不要把 `RimSortEmergency-20260918-010258` 归档中的测试游戏搬回旧位置。需要复用时在扫描范围外使用，并更新启动路径；原路径到归档路径映射保存在 `docs/rimsort-emergency-migration-2026-09-18.json`。
- 改动测试目录、Junction 或验证脚本后，运行 `docs/tools/Audit-RimSortDirectoryCycles.py`；必须确认 `affected_candidates=0` 且 `scan_errors=0`。只检查 `os.path.islink()` 不够，Windows Junction 可能返回 False。
- 文件系统清理不能递归跟随联接删除目标；先核实绝对路径和重解析点属性。保留用户既有代码、DLL、存档和其他未提交改动。
