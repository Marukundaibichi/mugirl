# Namespace Boundary Rules

本文档记录阶段 D 的命名空间治理规则。当前策略不是全项目大搬迁，而是先让新增类型有明确落点，让旧根命名空间类型逐步变成可解释的历史边界。

## 当前判断

- 现有 C# 类型大量位于 `MooGirl` 根命名空间，这是历史兼容状态，不作为立即整改项。
- XML 直接引用的类型、Scribe 存档类型、Harmony patch metadata 仍优先保持稳定，避免为了命名空间美化引入旧档或 Def 解析风险。
- 新增纯工具、兼容探测和不被 XML 直接引用的服务类型，应优先进入更具体的命名空间。

## 新代码规则

- Core 基础工具优先使用 `MooGirl.Core`。
- 第三方兼容入口优先使用 `MooGirl.Compatibility.<ModOrDomain>`。
- Feature 内部服务优先使用 `MooGirl.Features.<FeatureName>`。
- XML 直接引用类型继续允许留在 `MooGirl`，除非同步更新所有 XML 引用并确认不涉及旧档类名。
- Harmony patch 类型暂时允许留在 `MooGirl`，直到 patch metadata audit 支持完整命名空间类名。
- 任何新增根命名空间类型都应能说明原因：XML 直引、存档稳定、Harmony metadata 兼容，或临时迁移过渡。

## Phase 6 软报告

`docs/tools/Invoke-Phase6StaticValidation.ps1` 输出 namespace boundary report：

- root namespace type count；
- root namespace types referenced by XML；
- root namespace legacy/non-XML type count；
- specific MooGirl namespace type count；
- non-MooGirl namespace type count。

该报告当前不失败。下一步如果要收紧，可先建立允许清单，再把新增根命名空间类型改为失败项。
