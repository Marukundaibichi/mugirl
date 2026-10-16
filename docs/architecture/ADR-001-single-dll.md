# ADR-001: 保持单运行时 DLL

状态：Accepted（2026-10-15 复核：1.7 重构后仍然有效）。

## Decision

Mugirl 继续使用一个运行时 DLL，不拆成多个程序集。

## Context

当前 mod 的主要复杂度来自 RimWorld Def、Harmony patch、XML patch、资源路径、可选 DLC/第三方 mod 组合和游戏内行为验证，不来自程序集粒度本身。拆 DLL 会增加加载顺序、引用路径、发布包、PDB/构建卫生和跨程序集 internal 边界成本。

## Rationale

- 一个 DLL 更符合 RimWorld/Harmony mod 的常见加载模型。
- 当前静态验证（`tools/Validate.ps1`，即 `Source/Tests/MugirlRace.Validation`）已能审计 csproj、XML 类型引用、patch metadata、静态缓存、发布卫生和语言一致性。
- 模块边界优先通过目录、命名空间、服务类和静态审计建立，而不是先靠程序集拆分。
- 可选内容通过 `LoadFolders.xml`、XML patch gate、Compatibility 层和 null-safe Def/反射探测表达。

## Consequences

- 代码仍需依赖静态验证和文档规则维持边界；1.7 的目录与命名空间约定见 [ADR-009](ADR-009-naming-and-layout.md)。
- 新类型应放入明确目录和命名空间，不能因为单 DLL 就回到根命名空间堆叠。
- 如果未来出现真正的独立发布单元，再单独评估拆 DLL。
- 1.7 重构（2026-10-15）按命名空间与文件夹划分模块，统一存档升级层 `Mugirl.SaveUpgrade` 也在同一程序集中（[ADR-007](ADR-007-save-schema-upgrade.md)），没有引入新的运行时程序集。`1.7/Assemblies` 只放 `MugirlRace.dll` 及其 PDB；测试、验证、ShaderCheck 与 RenameApply 等工具程序集从不进入加载目录（[ADR-005](ADR-005-version-layout.md) 规则 3）。
