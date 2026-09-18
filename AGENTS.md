# AI 维护入口

修改前先读 `docs/README.md` 和与本次改动相关的维护文档。

## RimSort 卡死事故：必须避免再次引入目录环路

2026-09-18 已确认本机 RimSort v1.13.2 点击模组时，会在 GUI 线程递归统计整个模组目录；它跟随 Windows Junction/目录符号链接，没有环路检测。此问题与 `About/About.xml` 的内容无关。完整事故、迁移记录和复查方法见 `docs/rimsort-directory-cycles-2026-09-18.md`。

- 禁止在模组内部创建指回本模组、其他开发模组或游戏根目录的测试游戏目录联接。`.gitignore`、`LoadFolders.xml` 和不启用该模组都不能阻止 RimSort 遍历这些目录。
- 含 Junction/目录符号链接的测试运行环境统一放到 `%LOCALAPPDATA%\RimWorldModTests\`，不得放进任意 RimSort 实例的 Mods、Workshop 或 Data 扫描目录。不能用原路径的 Junction 将外部测试环境挂回模组。
- 本仓库 `docs/tools/Start-CorporateValidation.ps1` 已使用外部测试目录。普通日志、配置、无联接的打包文件可以继续写入 `TMP`。
- 不要把 `RimSortEmergency-20260918-010258` 归档中的测试游戏搬回旧位置。需要复用时在扫描范围外使用，并更新启动路径；原路径到归档路径映射保存在 `docs/rimsort-emergency-migration-2026-09-18.json`。
- 改动测试目录、Junction 或验证脚本后，运行 `docs/tools/Audit-RimSortDirectoryCycles.py`；必须确认 `affected_candidates=0` 且 `scan_errors=0`。只检查 `os.path.islink()` 不够，Windows Junction 可能返回 False。
- 文件系统清理不能递归跟随联接删除目标；先核实绝对路径和重解析点属性。保留用户既有代码、DLL、存档和其他未提交改动。
