# Mugirl 维护文档

这里保存 mod 日后维护、更新和发布需要用到的文档。旧的重构路线图、阶段记录和临时基线已经不再作为维护入口；需要追溯历史时请看 Git 历史。

## 常用入口

- `maintenance-guide.md`：日常改代码、改 Def、改资源前后的基本流程。
- `content-update-guide.md`：新增服装、物品、Hediff、Job、事件、种族外观等内容时的检查项。
- `compatibility-guide.md`：DLC、HAR、Facial Animation、Search and Destroy、VCookE 以及其他可选 mod 的兼容规则。
- `facial-animation-guide.md`：雪牛娘 Facial Animation 接入、贴图命名、动画调度和新增 FA 内容检查项。
- `weapon-wheel-design.md`：雪牛娘武器轮盘、火力满载、换枪动画与自驱装弹系统的设计和验收标准。
- [giant-corporation-design.md](giant-corporation-design.md)：巨企员工来访解锁主线、白色科幻通讯终端、交易、贷款、每周委托及牛聚变投资者支线的完整设计。
- [giant-corporation-v1-validation.md](giant-corporation-v1-validation.md)：巨企首版使用方式、已实现规则、真实游戏验证记录、复现步骤及已知限制。
- `localization-and-comments.md`：翻译键、玩家可见文本和中文注释风格。
- `validation-runbook.md`：静态验证、游戏内验证和 fresh `Player.log` 记录模板。
- `performance-optimization-2026-09-07.md`：性能优化的实施结果、原图备份和行为验证；优化前分析保留在 `performance-review-2026-09-07.md`。
- `release-checklist.md`：打包前的发布检查和 Workshop 包生成方式。
- `architecture/`：长期架构决策记录，解释单 DLL、Harmony 注册、兼容层范围和根命名空间保留规则。
- `tools/`：维护脚本，包括静态验证、日志扫描、验证配置生成和发布包生成。

## 维护原则

- 这是 RimWorld mod，不是生产级平台。维护时优先保证可读、可验证、低侵入，不追求过度抽象。
- 玩家可见行为和数值不要顺手改。确实要改时，在提交说明里列清楚原因、影响和验证方式。
- C# 负责规则和流程，XML/Def 承载数值与内容。不要把可配置数值散落到逻辑代码里。
- 可选 DLC 或第三方 mod 必须有 gate 或兼容层；缺失时应静默跳过或清晰降级。
- 热路径代码要克制。每 tick 扫描、全图 pawn 遍历、运行期 Def 字符串查找都需要明确理由。
- 注释统一写中文，保留 `Pawn`、`Def`、`Harmony`、`Hediff`、`Job` 等 RimWorld 技术名。

## 推荐维护顺序

1. 先跑一次静态验证，确认工作区基线是干净的。
2. 阅读相关功能目录和对应维护文档，确认改动边界。
3. 修改代码、Def 或资源；避免混入无关整理。
4. 再跑静态验证，必要时按 `validation-runbook.md` 做游戏内验证。
5. 发布前按 `release-checklist.md` 生成发布包并检查 `TMP` 输出。
