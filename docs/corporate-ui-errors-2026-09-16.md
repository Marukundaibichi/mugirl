# 巨企目录与存档红字修复（2026-09-16）

## 日志证据

原始故障日志保存在 `TMP/CorporateUiErrorEvidence-20260916/Player-prev.log`。玩家确认触发场景为浏览或搜索物资订单。

- 三次 `Tried to destroy non-destroyable thing Apparel_CerebrexNode...`：报价和商品预览使用临时实物，旧清理路径无条件销毁。实际 Def 中主脑节点虽标记为 `Sellable`，但同时 `destroyable=false`，属于不应进入通用目录的受保护物品。
- 读档时缺少 `Mugirl.CorporateRuntimeValidation`、`Mugirl.CorporateVisualValidation`，继而报 `Can't load abstract class Verse.GameComponent`：旧测试驱动继承了 `GameComponent`，游戏自动创建并保存该类；仅在驱动内部检查测试开关无法阻止自动序列化。

## 修复范围

- `IsOrderable` 拒绝不可销毁物品。已有不可交易、落地销毁、未完成物品与专用生成类型筛选继续生效。目录、搜索、报价、预览、旧现货显示及实际成交均不能引入主脑节点。回购同样拒绝不可销毁物品。
- 临时商品统一清理：普通商品正常销毁；旧受保护预览或托管样品只释放持有关系和引用，不修改 `ThingDef.destroyable` 或全局强制销毁开关。已付款历史订单保留原条款；取消历史托管也能安全清理。
- `Game.ExposeSmallComponents` 读档前置补丁，仅在 `LoadingVars` 阶段从内存 XML 的 `game/components/li` 中删除两个精确旧类名。保留所有其他数据，不直接写玩家存档。
- 两个验证驱动改为普通类，由仅测试构建包含的更新补丁驱动；跨测试回档的断言状态保存在测试进程中。验证启动器独立编译并运行，不再覆盖正式 DLL。
- 原有稀有定价与备货规则保留。机械液、训练器等正常稀有商品仍可采购。

## 验证说明

隔离测试启用原版五个 DLC、Harmony、HAR 和本模组。目录回归包含全部 697 项允许商品的报价、冷缓存双向排序、缓存复用、受保护物品搜索与成交拦截、预览替换和关闭、旧库存与订单托管清理。原版人格武器和特化武器以新实物身份生成特性，不同样品允许报价变化；同一缓存报价、其他普通商品报价及排序仍要求一致。

存档回归创建独立测试存档，注入两个旧测试节点，然后调用游戏真实加载与重存流程，核对订单、资金、库存、托管和人员报价。玩家原始存档仅做只读内存检查：精确删除两节点后的其他 XML 一致，检查前后原文件哈希相同。

`CorporateValidation-UiErrorFix3` 已通过：104 项业务与迁移断言、20 张真实界面截图、八页转场与弹窗关闭验证；运行约 74 秒，捕获 Error/Exception/Assert 为 0，真实保存→加载→重存验证成功。前两轮诊断发现测试将不同随机特性武器样品误视为固定报价，按实际原版生成逻辑修正了断言。

结果与完整游戏日志保存在 `TMP/CorporateValidation-UiErrorFix3-Evidence/`。Phase 6 静态检查通过：243 编译项、479 个具体 Def、1000 个中英对应键、1389 张 PNG。正式 Release 编译通过，Cecil 类型定义检查确认测试驱动不存在、正式迁移补丁存在。

已更新 `1.6/Assemblies/MugirlRace.dll`：718,848 字节，SHA-256 为 `6A231877CA56F19A3E9CEE8712A0D4AD1DFCAAF474C5CB1E7446F43188CAA418`。运行中的游戏仍持有旧 DLL，已将该文件移入 `TMP/CorporateUiFixRelease/MugirlRace.previous.dll` 后安全放入新版本；没有结束玩家游戏进程。重启游戏后加载修复版。

## 其他日志项

原日志还包含无调用栈的 `UnityEngine.InputLegacyModule` 反射依赖加载异常及 VEF 存档引用告警。后续日志快照 `Player-latest-172544.log` 出现 Debra 的 `PawnRenderer.RenderPawnAt` 空引用，包含 MRK、YaOpt 及本模组外层绘制补丁。该调用栈不经过巨企商品预览，且 Debra 不在现有存档内；现有证据不足以归因具体补丁，不以吞掉渲染异常的方式掩盖问题。这些记录不属于本轮已验证修复结果。
