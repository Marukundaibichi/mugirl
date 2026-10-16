# ADR-008: 依赖 Mod 使用的公开 API

状态：Accepted（2026-10-15）；2026-10-16已完成R/R2/S及本地交付验收。PublicApi别名、`IsMugirl`入口及`SlaveApparelDef`配置转换已实现，完整151项契约检查与实机X-alias双次读档均通过。未上传创意工坊。

## Decision

其他 Mod 从 Defs、Patches、Languages 或生产 C# 中引用的 Mugirl 名字是永久冻结的公开 API（owner 决定 D5 = 保持依赖 Mod 可用，见 [decisions.md](../refactor-1.7/decisions.md) §1、§5）。这些名字不改名、不删除，形态不变。

- 唯一的权威清单：[tools/RenameApply/specs/public-api.json](../../tools/RenameApply/specs/public-api.json)。
- 清单中 `validationOnly` 部分只被依赖 Mod 的自检代码使用，不冻结。它们的新名字交给 owner 迁移，见 [owner-migration-list.md](../../tools/RenameApply/specs/owner-migration-list.md)。
- 根命名空间 `Mugirl` 中的类型只允许是公开 API，并且只放在 `Source/MugirlRace/PublicApi/`。

## Context

2026-10-15 扫描了以下依赖 Mod（`specs/generators/scan_dependents.py` + `gen_public_api.py`；`usedBy` 记录 `文件:行` 与 production/validation）：

| 依赖 Mod | 位置 | 主要引用 |
|---|---|---|
| `MRK.WhiteFluff`（白绒物语） | `Mods/乳牛娘 白绒物语`（本地，1.6） | 用 Harmony 补丁 `IsMugirl`；`[HarmonyAfter("MugirlMod.Mod")]`；`Mugirl_Lactation`、`MugirlMilkFilth`、`MugirlBody` 等 defName；`CompProperties_MugirlMount`；FA 面部 Def |
| `har.mugirlrace.subspecies` | `Mods/MooGirl_Subspecies`（本地，1.6） | `<Mugirl.SlaveApparelDef>` 及其旧字段；`Mugirl.CompProperties_MooMilkable` 及其字段；`MugirlLike`、`Mugirl_Colony`；显示名 |
| `KindSeal.LOL` | 创意工坊 3555662938（1.6+1.7） | `Mugirl.StockGenerator_Mugirl_Slaves` 与 `slaveCountRange`；显示名（`PatchOperationFindMod`）；种族 `Mugirl` 的 XPath 路径 |
| `Kindseal.UFPC` | 创意工坊 3698316804（1.6+1.7） | JobDef `Job_Charge`、贴图 `UI/Mugirl_Charge`、packageId |
| `ZuoYao.RavenRace` | 创意工坊 3781005562（1.6）及本地副本 | `Job_Charge`、`Mugirl`、`Mugirl_Charge`、`Mugirl_Milk`、FA 表情 defName（来自编译后程序集中的字符串） |
| `Berserk.poisonless.milk` | 创意工坊 3601148404（1.6） | packageId |

前两个依赖 Mod 很可能由 owner 维护，但它们的 1.7 构建尚不存在，所以本 Mod 必须继续接受它们现有的 XML 与补丁。

## 冻结的类别

清单当前共 151 条，各类别如下：

| 类别（清单 `kind`） | 内容 | 保持方式 |
|---|---|---|
| `packageId`、`displayName` | `HAR.MugirlRace`、`Mugirl Race` | 不改 |
| `harmonyId` | `MugirlMod.Mod`（`HarmonyBootstrap.HarmonyId`） | 不改 |
| `typeShortName` | `MugirlMod`：设置文件名 `Mod_<文件夹>_MugirlMod.xml` 只用短名 | 命名空间可以改，短名不改 |
| `defName`、`defNamePattern` | 99 个 defName（含 `Mugirl`、`MugirlBody`、`Mugirl_Charge`、`Mugirl_Shout`、`Job_Charge`、`Ears`、`MugirlLike`、FA 面部 Def），以及模式 `Mugirl_*Normal*`、`Mugirl_SlaveApparelKey_*` | 不改名、不删除；按数据库根检查 |
| `abstractName` | `MugirlPawn`、`Mugirl_ApparelBase`、`Mugirl_BaseBackStory`、`Mugirl_BaseKind`、`Mugirl_BondageBase`、`Mugirl_HeadBase`、`Mugirl_OnSkinBase`、`Mugirl_SlaveApparel_Base` | 不改名 |
| `xmlClass` + `xmlField` | `Mugirl.StockGenerator_Mugirl_Slaves`、`Mugirl.CompProperties_MugirlMount`、`Mugirl.CompProperties_MooMilkable`、`Mugirl.SlaveApparelDef`，以及依赖 Mod 设置的 12 个字段 | 见下文“XML 类别名” |
| `type` + `member` + `harmonyParameter` | `Mugirl.MountedPawnUtility.IsMugirl(Pawn pawn)` | 见下文“可打补丁的扩展点” |
| `tag`、`texturePath`、`structure`、`xpathStructure` | 服装/交易标签；贴图路径；种族 `bodyAddons` 的顺序（HAR 按下标保存，只能在末尾追加）；依赖 Mod 用 XPath 补丁时经过的元素路径 | 不改 |
| `scribeLabel`、`type` | `mugirlSaveSchema` 与 `Mugirl.Core.GameComponent_Mugirl` | 这两项属于存档结构，不是依赖 Mod 的 API，同样永久不改（[ADR-007](ADR-007-save-schema-upgrade.md)） |

### XML 类别名

依赖 Mod 在 XML 中使用的三个旧类名改为放在 `Source/MugirlRace/PublicApi/XmlAliases.cs` 中的空公开子类（decisions D4）：

```csharp
namespace Mugirl;
public class StockGenerator_Mugirl_Slaves : Features.Trade.StockGenerator_MugirlSlaves { }
public class CompProperties_MugirlMount : Features.Mounting.CompProperties_Mount { }
public class CompProperties_MooMilkable : Features.Milk.CompProperties_MilkProduction { }
```

- 该文件已由 Wave R 建立。三个空子类是经过审阅的公开 XML 名称契约，属于一文件多类型的明确例外；实现仍位于功能命名空间。
- 本 Mod 自己的 XML 使用新类名。
- 依赖 Mod 设置的 XML 字段名在基类上永久不改：`slaveCountRange`、`displayString`、`milkDef`、`milkIntervalDays`、`milkAmount`。
- 不使用 `GenTypes` 后缀做类型别名。

`<Mugirl.SlaveApparelDef>` 是 Def 元素名，不能只保留空子类（decisions D6）。当前实现：

1. 文件位于 `Source/MugirlRace/PublicApi/SlaveApparelDef.cs`，仍为 `Mugirl.SlaveApparelDef : ThingDef`，全部外部 XML 字段保留。
2. `ResolveReferences` 把实际业务字段转换为 `Mugirl.Features.Restraints.RestraintExtension`，保留身体部位列表等对象引用。重复解析只刷新该别名自己生成的扩展；XML 已明确提供的新扩展不被覆盖。没有业务读取方的冻结元数据也不能擅自删除。

### 可打补丁的扩展点

白绒物语用 Harmony 补丁 `Mugirl.MountedPawnUtility.IsMugirl(Pawn)`，让自己的种族也能被骑乘。该方法必须满足：

- 保持 `public static bool IsMugirl(Pawn pawn)`，参数名 `pawn` 也冻结（Harmony 按名称绑定参数）。
- 带 `[MethodImpl(MethodImplOptions.NoInlining)]`：调用方被 JIT 内联后，补丁对它不可见。
- 是骑乘代码真正调用的判定点，不能被另一个内部判定绕过。当前调用方包括 `MountEligibilityUtility` 与 `FloatMenuOptionProvider_MountMugirl`。

现状（2026-10-16）：方法位于 `Source/MugirlRace/PublicApi/MountedPawnUtility.cs`，根命名空间 `Mugirl`，已带 NoInlining。其余原成员已移出，通用骑乘查询在 `Mugirl.Features.Mounting.MountUtility`。不能以“组件存在即可骑乘”替代此真实扩展点。

## 如何修改

- **不允许**：改名或删除冻结条目；改 XML 字段名；改 `IsMugirl` 的签名、参数名或可见性；去掉 `NoInlining`。
- **新增条目**：发现新的依赖 Mod 或新引用时，用上述生成器重新扫描，更新 `specs/public-api.json`，并确认验证器识别其类别（见下文“检查”）。
- **退役条目**：只有在 owner 决定、并且依赖 Mod 已发布不再使用该名字的版本之后才可以。把决定记入 decisions.md，再从清单中删除。
- **新增公开类型**：放进 `Source/MugirlRace/PublicApi/`，使用根命名空间 `Mugirl`，写文档注释说明谁在使用。实现放在功能命名空间，PublicApi 中只保留名字。

## 检查

- **RenameApply**：用 `--frozen-names tools/RenameApply/specs/public-api.json` 调用（默认守卫 `tools/RenameApply/frozen-names.json` 与该文件逐字节相同）。任何改名冻结名字的规格行都报错；规格检查器 `specs/generators/lint_spec.py` 的 L05/L06 拒绝这类行。
- **验证器 `C.public-api`**：直接读取 `tools/RenameApply/specs/public-api.json`，逐项检查名字仍然存在、形态不变：
  - XML 可见类型为 public，字段存在；
  - Harmony id；
  - 方法为 public static 且带 NoInlining；
  - defName 位于正确的数据库根；
  - 抽象 Name；
  - 显示名。
- **验证器 `B.namespaces`**：根命名空间中只允许 PublicApi 下、列在 public-api.json 的类型。R/R2 已清理原根命名空间债务；后续新增代码仍受此规则约束。

## Consequences

- 验证器直接使用唯一权威清单的完整 151 项，包含 tag、texturePath、structure、xpathStructure 等类别；旧 `Baselines/public-api.json` 已移除。107 项是 A2 初版的历史数字，不能作为现在的检查范围。新增类别要有对应正反对照，不能只增加清单而没有对应检查。
- 根命名空间不能完全清空。PublicApi 中的类型是唯一的例外，它们的实现都在功能命名空间中。
- 只要别名、字段名与扩展点不变，`Features/` 中实现类的改名不影响依赖 Mod 现有的 XML 与补丁。依赖 Mod 的 1.6 构建能否在 1.7 游戏上运行，由它们自己的维护者确认，不在本 ADR 范围内。
