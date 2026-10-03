# 巨企见面礼科技资料实施记录

## 版本与范围

- 日期：2026-09-24，基于 0.6.7（131ee89）之后的工作区。
- 需求：巨企专员初次会面的见面礼增加一项——解锁雪牛娘科技树最左列中研究点低于 500 的科技，对话同步更改。
- 范围：主线第一章见面礼配置、开户结算逻辑、中英对话与完成信文案；不改动礼物物资、任务流程与其他业务。

## 改动内容

| 文件 | 改动 |
| --- | --- |
| `1.6/Source/Features/Corporate/CorporateIntroduction.cs` | `CorporateIntroductionDef` 新增 `researchGifts` 字段；`Complete()` 开户时调用新方法 `UnlockResearchGifts()` 完成研究并把实际解锁清单拼入完成信 |
| `1.6/Source/Core/MugirlGameUtility.cs` | 新增 `ResearchManager` 访问器，与既有 core helper 访问规范一致 |
| `1.6/Defs/Corporate/CorporateIntroduction.xml` | `researchGifts` 配置三项：`Mugirl_DairyProcessing`（400）、`Mugirl_RopeHandling`（300）、`Mugirl_DailyAttire`（400） |
| `1.6/Languages/{ChineseSimplified,English}/Keyed/CorporateUI.xml` | `Proposal` 对白提及入门技术资料；新增 `ResearchGiftLine` 键（占位符 `{0}` 为科技名列表），完成信动态追加 |
| `docs/features/giant-corporation-design.md` | 文档状态、9.6 见面礼、11 数值配置表同步 |

## 数值依据

雪牛娘科技树入口列（`researchViewX=0.80`，见[研究树设计](../features/research-tree-design.md)）共 4 项：乳品加工 400、束具与拴系 300、服装工艺 400、枪械 800。低于 500 点的为前三项，共 1100 研究点。枪械（800）不在赠礼内，保留战斗线的正常研究进度。

## 行为细节

- 解锁走 `ResearchManager.FinishProject(project)`，自动补前置与科技图纸；三项均为入口项目，无前置连带。
- 已完成研究的项目跳过（`IsFinished` 过滤），完成信只列实际新解锁的项目；全部已研究时不追加该行。
- 科技解锁在 `Complete()` 内、`completed = true` 之后执行，受一次性开户标记保护，不会重复发放。
- 科技解锁不依赖见面礼物资的实物交付；物资交付失败时保留待交付重发，科技仍即时生效。
- 三个项目 Def 无 `discoveredLetterTitle`，`FinishProject` 不会额外弹信，玩家通知由完成信中的动态行承担。

## 限制

- 已完成主线第一章的旧档不追溯补发科技（`Complete()` 不会再次运行）。
- 游戏内验证待做：建议按[验证手册](../validation-runbook.md)在新档走完送货员接触→专员来访→接受见面礼，确认研究页签三项直接完成、完成信列出科技名、对白文案正确；并抽查已自行研究其中部分项目的存档只解锁剩余项。

## 验证结果

- `docs/tools/Invoke-Phase6StaticValidation.ps1`（完整构建）：通过，退出码 0（2026-09-24，本机）。构建输出 `1.6/Assemblies/MugirlRace.dll`。
- 首次运行曾发现 `Current.Game` 直连违反 Current game access safety 扫描，已改为 `MugirlGameUtility.ResearchManager` 访问器后复验通过。
