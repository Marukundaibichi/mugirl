# ADR-001: 保持单运行时 DLL

## Decision

MooGirl 继续使用一个运行时 DLL，不拆成多个程序集。

## Context

当前 mod 的主要复杂度来自 RimWorld Def、Harmony patch、XML patch、资源路径、可选 DLC/第三方 mod 组合和游戏内行为验证，不来自程序集粒度本身。拆 DLL 会增加加载顺序、引用路径、发布包、PDB/构建卫生和跨程序集 internal 边界成本。

## Rationale

- 一个 DLL 更符合 RimWorld/Harmony mod 的常见加载模型。
- 当前静态验证已能审计 csproj、XML 类型引用、patch metadata、静态缓存、发布卫生和语言一致性。
- 模块边界优先通过目录、命名空间、服务类和静态审计建立，而不是先靠程序集拆分。
- 可选内容通过 `LoadFolders.xml`、XML patch gate、Compatibility 层和 null-safe Def/反射探测表达。

## Consequences

- 代码仍需依赖 Phase 6 和文档规则维持边界。
- 新类型应放入明确目录和命名空间，不能因为单 DLL 就回到根命名空间堆叠。
- 如果未来出现真正的独立发布单元，再单独评估拆 DLL。
