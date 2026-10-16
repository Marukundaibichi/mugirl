# ADR-006: 1.7 不使用 Unity 兼容层

状态：Accepted（2026-10-16 复核）。对照 `BannedSymbols.txt`、`GlobalUsings.cs` 与验证器核实；当前 HAR 已发布原生 1.7 程序集，旧编译门面已移除。

## Decision

1.7 构建（`Source/`）不使用 `UnityStubs` 与 `Waylith.UnityCompat` 提供的任何功能。用到 Unity API 的部分一律改用 Verse 封装、Waylith 原生接口或 .NET 实现。`UnityEngine` 类型只在 RimWorld 自身 API 签名需要时作为数据传递。冻结的 1.6 构建不受本决策约束。

## Context

1.7 运行在 Waylith 引擎上，`UnityEngine.*` 由 UnityStubs 近似实现，部分成员语义与 Unity 不同（结构体 `==` 改为精确比较、`Mathf.Max` 遇 NaN 返回 NaN、`SmoothStep`/`Repeat` 公式不同、`Time.frameCount` 在同一引擎帧内加一、资源包 Shader 无 Vulkan 变体时回退到不透明默认 Shader 等）。依据与证据见 `../1.7迁移文档` 的兼容迁移指南“不再使用 Unity 兼容层”一节及证据 E16、E18、E27、E31、E41–E43。

## Rules

1. **边界类型只作数据。** 允许的边界类型由 `Source/MugirlRace/GlobalUsings.cs` 逐个列出：`Color`、`Color32`、`Material`、`MaterialPropertyBlock`、`Mesh`、`Rect`、`Shader`、`Texture`、`Texture2D`、`TextAnchor`，以及别名 `UVector2/UVector3/UVector4/UQuaternion/UMatrix4x4`。只能构造它们、读写字段、读取数据属性（如 `Rect.xMax`、`Texture.width`），并与游戏 API 交换。源码不写 `using UnityEngine;`；其他 Unity 类型只在游戏 API 签名强制时以全名书写。
2. **数学在 System.Numerics 与 `Mugirl.Mathematics` 中完成。** 无限定的 `Vector2/Vector3/Vector4/Quaternion/Matrix4x4` 一律是 System.Numerics 类型。进入时 `ToNumerics()`、离开时 `ToUnity()`。需要 Unity 语义的运算使用 `UnityMathf`、`UnityVector`、`UnityQuaternion`、`UnityMatrix`、`UnityColor`；这些实现逐步复现 Unity 公式。BCL 中外形相似但语义不同的成员（`Vector3.Normalize`/`Lerp`、`float.Lerp`、`Math.Clamp`、`MathF.Max/Min`、`Waylith.Math.Mathf`）被禁用。
3. **引擎服务走 Verse 或 Platform 层。** 绘制用 `GenDraw`/`MaterialPool`/`MeshPool`/`Graphic`；界面用 `Widgets`/`Text`/`TextBlock`/`GenUI`/`OriginalEventUtility`；时间与输入只经 `Mugirl.Platform`（`EngineClock`、`EngineInput` 等），它是唯一允许引用 `Waylith.*` 的命名空间。确实无替代的原始 GUI 调用集中在 `Mugirl.Platform.Ui`，逐处写明原因。
4. **对象判空用 `is null`。** UnityStubs 的 `Object`/`Texture` 运算符 `==` 检查释放状态，不是引用比较。
5. **自定义渲染用原生 ShaderDef 与 GLSL。** 文件放 `1.7/Defs/ShaderDefs` 与 `1.7/Shaders`，ShaderDef 名、GLSL 文件名（含 include）都带 `Mugirl_` 前缀；不使用资源包。

## Enforcement

- 编译期：`Microsoft.CodeAnalysis.BannedApiAnalyzers` 与 `Source/MugirlRace/BannedSymbols.txt`，违规报 RS0030。只允许在三处带理由抑制：`Mugirl.Platform`、`Mugirl.Mathematics`（复现 Unity 公式时有意调用被禁的 System.Numerics 成员），以及唯一的日志入口 `Mugirl.Core.MugirlLog`（`Verse.Log`）。每处抑制都要逐方法登记在 `Source/Tests/MugirlRace.Validation/Baselines/banned-symbol-exemptions.txt` 中并写明理由；验证器 `B.banned-symbols` 检查登记，并确认 `BannedSymbols.txt` 能解析、没有被绕过。
- 构建后：`Source/Tests/MugirlRace.Validation` 扫描程序集的全部 UnityStubs 类型与成员引用，必须都在 `Baselines/unity-boundary.json` 白名单中且各有理由；`Waylith.*` 只能出现在 `Mugirl.Platform`；程序集不得引用任何 `UnityEngine*` 或 `Waylith.UnityCompat` 程序集。
- 当前 HAR 1.7 的 `AlienRace.dll` 直接引用 UnityStubs 和 Assembly-CSharp 1.7，不再引用 UnityEngine.CoreModule。原来适配 HAR 1.6 的 `Source/Facades/UnityEngine.CoreModule` 工程、类型转发器和项目引用已移除；冻结的1.6目录及历史归档不动。验证器继续拒绝生产程序集或加载目录中的 UnityEngine 门面；负对照使用的假程序集仅用于检验这一拒绝规则。

## Consequences

- 渲染、界面和数学代码比 1.6 版多一层显式转换，但每个越过边界的位置都可审阅、可检查。
- 引擎或 UnityStubs 更新后，只需复核 `Mugirl.Platform` 与白名单；业务代码不依赖兼容层的近似行为。
- 原版自身仍在兼容层上运行。镜像原版缓存或比较结果的代码要跟随原版的实际语义（例如原版仍按兼容层帧号缓存 Gizmo 列表），这类例外在代码中注明。
