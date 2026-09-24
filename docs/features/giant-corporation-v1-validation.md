# 巨企首版：使用与验证记录

> 2026-09-24 人员交易及雪牛娘毛制家具收购已调整。下文“首版已包含”的旧人员报价、奴隶购买限制与产品收购范围用于解释当时的运行证据；当前规则见[巨企设计方案第 5、6 节](giant-corporation-design.md)，客户交易额去重见[客户服务规则](corporate-services.md)。既有通过记录不能替代本轮游戏内验证。

## 2026-09-24 补充白衬衫牛仔休闲套

将白色条纹衬衫「惑光半透」`Mugirl_Shirt` 与牛仔长裤「蓝境远行」`Mugirl_Jeans` 固定搭配加入展销清单，现共十二套。保留白蓝原色，沿用裤装贴图自带的鞋饰，不额外叠穿袜靴。两件服装分别位于 Middle / Torso、Shoulders 和 OnSkin / Legs，实际穿戴无冲突。

全 DLC 隔离运行 `CasualSet-20260924a` 为 **338 PASS / 2 FAIL**。十二套服装的 60 项检查全部通过，包含新增休闲套的完整穿戴、排除战斗服饰、原色保留、重验不换件及缺件拒绝；实际人员购买与完整存读档衣装检查也通过。两项失败分别为通讯台靠近移动断言（job 与 driver 正确，但 `moving=False`）及既有八人支援队装备断言，不记作全量通过。fresh `Player.log` 扫描仅命中 5 条测试 PASS 文本中的 `missing` / `failed`。证据位于 `%LOCALAPPDATA%/RimWorldModTests/Mugirl/CorporateValidation-CasualSet-20260924a-Game/Mods/Mugirl/TMP/CorporateValidation-CasualSet-20260924a/`。

普通 Release 构建已更新正式 DLL，SHA-256 为 `E9F0E0173BF921E63894688960B08E4E95B11BA053F1862DF8AF829DD730881C`；原 DLL 备份于 `DevData/Backups/CasualSet-20260924/MugirlRace-before.dll`。静态主体通过，资源阶段仍缺既有 42 张 FA 烘焙脸红贴图，基线与本轮结果分别见 `TMP/CasualSet-baseline-20260924.log`、`TMP/CasualSet-static-20260924.log`。`TMP/CasualSet-cycle-audit-20260924.json` 的 `cycle_components=0`、`affected_candidates=0`、`scan_errors=0`。

视觉运行 `CasualSetVisual-20260924a` 为 **PASS**，87 张图包含 51 张界面截图与 36 张游戏原生三向肖像，完整读档再保存检查通过；fresh `Player.log` 扫描有一条对 `Thing_Human5703` 的 `DirectPawnRelation` 引用警告，不记作零日志警告。本轮使用 `-SkipCompact`，未改界面布局，紧凑及窄窗口沿用上一轮独立验证记录。新增休闲套的正面、侧面、背面已目视确认白蓝原色、上下装腰线衔接和原有鞋饰。截图位于 `%TEMP%/CorporateValidation-CasualSetVisual-20260924a/ValidationShots/outfit-11-detail-2.png`（正面）、`outfit-11-detail-1.png`（侧面）、`outfit-11-detail-0.png`（背面）。

## 2026-09-24 展销非战斗主题套装扩充

在原有六套基础上加入五套固定搭配，共十一套。展销清单排除明显用于作战的盔甲、战斗服和战术装备，保留警察角色装束；配饰允许按风格复用，不再要求所有配件必须具有相同系列名。颜色仍保留贴图原色，每人按整套抽取，不分别随机抽衣裤或配饰。

| 新套装 | 固定搭配 | 搭配依据 |
| --- | --- | --- |
| OL | OL 套装、墨镜、链条手袋 | 黑白通勤配色，沿用主衣自带的裙装、丝袜和鞋 |
| 婚纱 | 婚纱、白金色面纱 | 呼应白色礼服与金色领饰，保留原有成套腿饰 |
| 旗袍 | 旗袍、链条手袋 | 白色花纹旗袍配小型黑色链条包，保留完整裙摆 |
| 仙女服 | 仙女服、白金色面纱 | 白金色与青色系带，保留轻薄长袖和衣摆 |
| 霜蓝仙子服 | 霜蓝仙子服、白金色面纱 | 保留霜蓝原色，以轻薄白纱呼应浅色袖口和金色领饰 |

普通 Release 构建通过并更新正式 DLL，原 DLL 备份于 `DevData/Backups/CivilianSets-20260924/MugirlRace-before.dll`。静态检查主体通过，资源检查仍因既有 42 张 FA 烘焙脸红贴图缺失退出；本轮未修改这些资源。基线和最终复查分别记录于 `TMP/CivilianSets-baseline-20260924.log`、`TMP/CivilianSets-final-static-20260924.log`。最终目录审计 `TMP/CivilianSets-final-cycle-audit-20260924.json` 为 `cycle_components=0`、`affected_candidates=0`、`scan_errors=0`。

全 DLC 隔离运行 `CivilianSets-20260924c` 完成 **333 PASS / 2 FAIL**。十一套的完整穿戴、无作战服饰、实际显示色不受材质染灰、重验保留原件和拒绝缺件共 55 项全部通过；人员购买交付及完整存读档中的未付款旧装修复、已付款衣装保留也通过。失败为四人和八人支援队装备断言 `Strike team contains four corporate Mugirl with excellent weapons`、`Advanced support really deploys eight equipped corporate troopers`，不记作整套通过。fresh `Player.log` 有一条 AncientMechs 地图散布失败提示与一条 `DirectPawnRelation` 引用警告；其余扫描命中为测试 PASS 文本中的 `missing` / `failed`，不能写作零日志警告。证据位于 `%LOCALAPPDATA%/RimWorldModTests/Mugirl/CorporateValidation-CivilianSets-20260924c-Game/Mods/Mugirl/TMP/CorporateValidation-CivilianSets-20260924c/`。

中间运行 `CivilianSets-20260924b` 为 323 PASS / 1 FAIL，但 `CivilianSetsVisual-20260924b` 暴露了两个着色问题：原版 `CompColorable.SetColor` 对默认白色直接返回，未激活颜色覆盖，导致婚纱、旗袍等继续使用灰色材质底色；霜蓝仙子服没有 `CompColorable`，直接调用通用染色扩展方法还会报错。现只对可染色衣物先激活颜色覆盖再设白色，不可染色衣物保留自身渲染，且加入每套实际 `DrawColor` 回归检查。中间视觉运行虽输出 108 张图，但结果为 FAIL，不作最终验收依据。

前一次 `CivilianSets-20260924a` 已通过全部服装检查，但随机生成的测试信仰强制支持奴役，导致切换到 `Slavery_Abhorrent` 时中断。根据原版 `IdeoFoundation.CanAdd` 与 Meme 的 `requireOne` 规则，隔离夹具在原有 Raider 排除项之外再排除 Supremacist、Inhuman；未改动正式游戏的信仰规则。三向大图导出所需的 `UnityEngine.ImageConversionModule` 也仅对验证构建添加引用。

最终视觉运行 `CivilianSetsVisual-20260924c` 为 **PASS**，共 108 张图（75 张界面截图、33 张游戏原生三向肖像），完整读档再保存检查通过，fresh `Player.log` 扫描为 **OK**。逐套目视复核十一套正面，并检查新增五套的侧面与背面：OL、婚纱、旗袍与仙女服恢复贴图原色，霜蓝仙子服保留原有霜蓝色，手袋与面纱未覆盖裙摆或袖口。证据位于 `%TEMP%/CorporateValidation-CivilianSetsVisual-20260924c/ValidationShots/`，新增五套对应 `outfit-6` 至 `outfit-10`；`-detail-2.png` 为正面、`-detail-1.png` 为侧面、`-detail-0.png` 为背面。最终正式 DLL SHA-256：`B7EFA27999979C8DA53F1B641CD743F94EB297182E504EC4380E6C9A504ED38A`。

## 2026-09-24 展销同系列套装修正

用户澄清“成套”指修女套、警察套等同系列上下装与配饰。旧实现把制服、牛仔裤、通用内衣等跨系列拼装，而第一次复查又误改成所有人只穿单件 `Mugirl_Uniform`；这两个方案均不符合设计意图。

上一轮按整套清单随机选择六种组合：修女经典款/清廉款、警察普通胸衣款/拉链胸衣款、兔女郎和高叉毛衣；配件严格属于相应系列，保留原配色。警察上衣与短裤同时生成，互斥胸衣只选一款；修女服对应头纱与眼罩。旧档只修复未付款的混搭或单件制服并重算报价，已有完整套装保留原件，已付款待交付人员保留签约衣装与金额。

验证驱动逐套实际穿戴，检查缺件、等件数跨系列混搭、完整套装重验不换件、购买交付及完整存读档。视觉驱动为六套分别拍摄实际人员肖像和衣装栏。

全 DLC 隔离运行 `MatchingSets-20260924a` 完成 **298 PASS / 0 FAIL**，六套穿戴、缺件/混搭拒绝、原件保留和完整存档迁移全部通过。fresh `Player.log` 扫描仅命中 5 条测试 PASS 文本中的 `missing` / `failed` 单词，未发现运行异常。普通 Release 构建通过；静态检查主体通过，资源阶段仍因根 README 已记录的 42 张 FA 烘焙脸红贴图缺失而失败，本轮未恢复或更改这些素材。目录审计 `TMP/MatchingSets-cycle-audit-20260924.json` 的 `cycle_components=0`、`affected_candidates=0`、`scan_errors=0`。

视觉运行 `MatchingSetsVisual-20260924a` 为 **PASS**，65 张截图覆盖正常、紧凑、窄窗口与六套各自的肖像/衣装栏；逐套目视确认配件来自同一系列，修女两款及警察两款的差异正确显示。截图位于 `%TEMP%/CorporateValidation-MatchingSetsVisual-20260924a/ValidationShots/outfit-0~5-portrait.png` 与对应 `-apparel.png`。fresh `Player.log` 扫描为 **OK**。

历史运行 `UniformReview-20260924a`（274 PASS / 0 FAIL）与 `UniformVisual-20260924a`（53 张截图）只证明误解后的单件制服实现能运行，**其设计验收结论已撤回**，不可作为当前套装方案通过的证据。

## 2026-09-24 人员购买布局与自驱装弹显示

人员页移除顶部的奴隶/殖民者双按钮，购买按钮右侧改为原版下拉菜单；有 Ideology 时列出两种身份，无 Ideology 时仅列“购买为殖民者”。正常、紧凑和窄窗口截图均显示操作区完整。`PeopleUIAutoloading-Fixed-20260924a` 全 DLC 隔离游戏的 Visual 检查为 **PASS**，共 52 张截图，并在实际游戏菜单中断言两种身份选项。

上一轮自驱装弹检查仅给第 3–6 格装枪，没有覆盖完整六格；将凹槽枪改为 `-2` 后虽然看似消除了横向断口，却把枪放到了装弹架前面。此渲染结论已撤回；当时的前后截图只作为错误层序对照，分别位于 `%TEMP%/CorporateValidation-PeopleUIAutoloading-Loaded-20260924a/ValidationShots/` 与 `%TEMP%/CorporateValidation-PeopleUIAutoloading-Fixed-20260924a/ValidationShots/`。

按用户明确的南向遮挡要求，装弹架保留原版 Belt 的 `-3` 层，第 3–6 格枪分别在 `-4` 至 `-7`，第 2 格备用枪在穿戴装弹系统时位于 `-8`；主手持武器和其他朝向不变。架体的实心横梁自然遮挡其后方枪身，四把凹槽枪不再共面重叠。隔离游戏 `AutoloadingBehind-FullWheel-20260924a` 的 Visual 结果为 **PASS**，共 53 张截图；先逐槽验证第 3–6 格的四向显示，再将第 1、2 格装枪，断言六格全部有枪、南向第 2–6 格均在架体后方且凹槽层级各异。截图位于 `%TEMP%/CorporateValidation-AutoloadingBehind-FullWheel-20260924a/ValidationShots/`，其中 `autoloading-south-full-wheel.png` 是完整六格南向图。fresh `Player.log` 扫描无可疑行。

无 Ideology 隔离运行 `PeopleUIAutoloading-NoIdeology-20260924b` 的菜单断言与人员交易均通过；整套检查为 **266 PASS / 1 FAIL**。失败项是既有的四人支援小队装备复合断言，此断言曾在 2026-09-20 同配置运行失败，后续又通过；本次日志没有逐 Pawn 细节，不能把整套结果写成通过。证据位于 `%LOCALAPPDATA%/RimWorldModTests/Mugirl/CorporateValidation-PeopleUIAutoloading-NoIdeology-20260924b-Game/Mods/Mugirl/TMP/CorporateValidation-PeopleUIAutoloading-NoIdeology-20260924b/`。

南向层级修正的不含验证驱动 Release Rebuild 成功，正式 `1.6/Assemblies/MugirlRace.dll` SHA-256 为 `B1E4221A2CF16BA7E2186C81B77CB71B9A25DF783A5CEAFF3FCE9BB353746331`。前一正式 DLL 已备份于 `DevData/Backups/AutoloadingBehind-20260924/MugirlRace-before.dll`，SHA-256 为 `3142947AB884EC3322F7CBE66E30DE04BE2C9F9C0B3EC4CD539A70D8EDA312C4`；再前一个版本的备份仍在 `DevData/Backups/CorporateUIReload-20260924/`。`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 的静态主体通过，最终仍因工作区既有 42 张 FA blush 贴图缺失而在资源检查退出。最终 RimSort 目录审计 `TMP/AutoloadingBehindCycleAudit-20260924.json` 的 `cycle_components=0`、`affected_candidates=0`、`scan_errors=0`。

## 2026-09-24 最终构建与复查

无 Ideology 修复后，全 DLC 隔离游戏 `PersonFurnitureFinal-20260924a` 完成 **272 PASS / 1 FAIL**。共同人员报价、两种身份的实际购入、同一 Pawn 交易额去重、雪牛娘毛制家具成交、完整存读档及派系 Ideo 断言均通过；唯一失败仍是既有的 `Advanced support really deploys eight equipped corporate troopers`，故不记作整套通过。fresh `Player.log` 未见其他异常或 `DirectPawnRelation` 警告。证据位于 `%LOCALAPPDATA%/RimWorldModTests/Mugirl/CorporateValidation-PersonFurnitureFinal-20260924a-Game/Mods/Mugirl/TMP/CorporateValidation-PersonFurnitureFinal-20260924a/`。

不含验证驱动的 Release Rebuild 成功。更新正式 `1.6/Assemblies/MugirlRace.dll` 前，原 DLL 已备份至 `DevData/Backups/CorporateTrade-20260924/MugirlRace-before.dll`（SHA-256 `B333AD29C73FAD9C1AFDFEB09CB82A35742F7434A07E3292F5699661195BACD8`）；新 DLL 为 854,016 字节，SHA-256 `D8066391094399778D4F00C9047ACBF8F3DB49B9E61A3EB6D11701A4F1A4A9FD`。`Invoke-Phase6StaticValidation.ps1 -SkipBuild` 的静态检查全部通过，仅在资源检查中因工作区既有的 42 张 FA blush 贴图缺失而退出。最终 RimSort 目录审计见 `TMP/CorpTradeCycleAudit-Final-20260924.json`：`cycle_components=0`、`affected_candidates=0`、`scan_errors=0`。

## 2026-09-24 界面与展销服装目视验证

隔离游戏 `PersonOutfitVisual-20260924a` 的 `visual-result.txt` 为 **PASS**，生成 48 张截图，覆盖正常、紧凑及窄窗口界面。实际截图确认人员页的奴隶/殖民者购买选项、基础报价和两项费用、展销人员肖像，以及衣装详情中的成套服装均可见；紧凑界面的操作按钮仍位于可见区域。正式游戏存档未用于测试。截图和结果位于 `%TEMP%/CorporateValidation-PersonOutfitVisual-20260924a/ValidationShots/`。

## 2026-09-24 无 Ideology 的展销人员修复

首轮无 Ideology 隔离运行 `PersonFurnitureNoIdeo-20260924a` 虽为 265 PASS / 0 FAIL，完整存读档后的 `Player.log` 仍出现一次 `IdeoManager.Remove` 空引用。展销 Pawn 当时以无派系身份生成，原版为她随机分配了未受派系持有的 Ideo；卸载 Ideology DLC 后，该 Ideo 被排队移除时会访问不存在的 `ChangedIdeo` 历史事件。现在展销 Pawn 以巨企派系生成，使用该派系持有的稳定 Ideo；购买后仍按所选身份加入玩家派系。

修复后的隔离运行 `PersonNoIdeoStable-20260924b` 完成 **266 PASS / 0 FAIL**，新增断言确认未售展销 Pawn 的 Ideo 由巨企派系持有，人员购买、家具出售和完整存读档均通过；fresh `Player.log` 未再出现该空引用。日志仍有两条 `DirectPawnRelation` 引用解析警告，不记作零警告。证据位于 `%LOCALAPPDATA%/RimWorldModTests/Mugirl/CorporateValidation-PersonNoIdeoStable-20260924b-Game/Mods/Mugirl/TMP/CorporateValidation-PersonNoIdeoStable-20260924b/`。中间一次运行 `PersonNoIdeoStable-20260924a` 因轨道交易夹具未提供约定商品提前终止，不作为完整运行结果。

## 2026-09-24 毛制家具收购

北京时间 2026-09-24 11:07，隔离游戏 `WoolFurniture-20260924a` 完成 **271 PASS / 1 FAIL**。新增四项家具检查全部通过：已安装家具不能直接出售，打包雪牛娘毛椅按内部实际材质获得制品报价，普通布料制作的同款家具不符合收购条件，供电贸易信标范围内的打包毛制家具可实际成交且计入交易额。前述人员交易回归检查也通过。

唯一失败为既有高级支援队检查 `Advanced support really deploys eight equipped corporate troopers`；本次结果不能写作整套验证通过，此失败不属于家具收购检查。fresh `Player.log` 未见实际 Error 或 Exception，且未出现上一轮的 `DirectPawnRelation` 引用警告。证据位于 `%LOCALAPPDATA%/RimWorldModTests/Mugirl/CorporateValidation-WoolFurniture-20260924a-Game/Mods/Mugirl/TMP/CorporateValidation-WoolFurniture-20260924a/` 的 `corporate-checks.txt`、`checks-complete.txt` 与 `Player.log`。游戏使用测试目录中的隔离 DLL，未覆盖正式 DLL。

## 2026-09-24 人员交易定价、身份与展销衣装

北京时间 2026-09-24 11:00，隔离游戏 `PersonTrade-20260924a` 完成 **268 PASS / 0 FAIL**。实际运行覆盖共同基础报价、手续费与运费、殖民者 50% 加价、奴隶与殖民者购入、展销人员成套服装、同一 Pawn 买入再卖出仅计一次客户交易额、巨企派系原版交易去重、免费名额、意识形态拒绝及完整游戏保存读档。读档后已计额人员 ID、交易额和客户等级仍保留。此轮还未覆盖新加的毛制家具收购。

证据位于 `%LOCALAPPDATA%/RimWorldModTests/Mugirl/CorporateValidation-PersonTrade-20260924a-Game/Mods/Mugirl/TMP/CorporateValidation-PersonTrade-20260924a/`：`corporate-checks.txt`、`checks-complete.txt`、fresh `Player.log` 和 `Saves/CorporateRoundtrip.rws`。隔离验证 DLL 在该测试游戏的 `Mods/Mugirl/1.6/Assemblies/MugirlRace.dll`，未覆盖正式 DLL。`Player.log` 有一条 `DirectPawnRelation` 对 `Thing_Human69411` 的引用无法解析警告，与此前巨企夹具的同类警告一致；因此不记作零日志警告，也不能据此断言本轮全部存档关系引用正常。

## 2026-09-23 运货员钥匙、时序与对话

- 运货员原先给不够钥匙的原因是粗糙/精致钥匙各只创建一个物品，再分别写入 `stackCount=6/4`；钥匙禁止堆叠。现在记录开局坠落的两只 Pawn，运货员按两人仍穿戴的锁定束具、剩余锁数和对应钥匙 Def 逐把生成，接受提案时重新核对并替换背包钥匙，兼容已生成运货员的旧档。运货员在坠落成功后一个游戏日触发，对话使用标准 `Dialog_NodeTree`，按钮为“接受提案”“立即开战”。
- Release 构建在隔离输出目录成功，随后备份原正式 DLL 到 `DevData/Backups/Courier-20260923/MugirlRace-before.dll` 并更新正式 DLL；最终 DLL SHA256 为 `44D1D9DEE724460856F7998B80E6E3B7048506C1674A98006491F683944E9570`。原 DLL SHA256 为 `A7D13E4D4B1C5043DF8D23187400C0DFF0925AF3119082F49F74209F0A6B0EF3`。
- `Invoke-Phase6StaticValidation.ps1 -SkipBuild` 通过 XML、翻译、事件和 Scribe 等检查，仍在既有 `Mugirl_Shapes.xml` 两处行尾空格处中止。隔离游戏 `CorporateValidation-Courier20260923a` 完成启动及完整存读档，`259 PASS / 1 FAIL`；失败项为其他功能的“高级支援队实际部署八名装备完整的队员”。隔离运行后补了缺少目标时的日志翻译及接受提案时的钥匙复核，并重新构建正式 DLL。本次运行没有实际交谈和使用钥匙，不能作为运货员流程的游戏内验收。fresh `Player.log` 位于 `%LOCALAPPDATA%/RimWorldModTests/Mugirl/CorporateValidation-Courier20260923a-Game/Mods/Mugirl/TMP/CorporateValidation-Courier20260923a/`。目录审计见 `TMP/CourierValidation/rimsort-cycle-audit.json`，`affected_candidates=0`、`scan_errors=0`。

## 2026-09-22 牛聚变研究站地板与强制生成

- 原因：`CorporateFusionResearchSite.xml` 只保存了 `things`，缺少原版 Prefab 需要的 `terrain` 段；“保留地图原有地板”不会自动恢复蓝图中的地板。现按房间用途补齐 937 格四种地板，包含墙下和门槛。
- 选址仍为 2–10 格内可到达站点，优先平地或小丘陵、无水岸河流、年均温高于 0°C、沼泽度低于 0.25、无特殊地貌修饰器；没有优选地形时回退到普通可用站点。地图下限为 100×100，保证 51×42 蓝图及入口有足够空间。
- 先铺地板再做建筑检查；薄冰属于独立临时层，`SetTerrain` 不会移除它，所以清场显式移除临时地形并保留原有承载力修复。地形检查仍不通过时整平占地，继续生成并补建被原版静默跳过的条目，防止玩家进入空任务图。按用户要求只修复以后生成的地图，不添加旧图迁移。
- 专项入口：`Start-CorporateValidation.ps1 -Mode FusionFloors -RunName <新名称>`。覆盖普通地面、薄冰、深水、泥地、桥梁、山岩与喷泉、模拟第三方放置拒绝；检查完整地板与蓝图内容、发电机燃料、四向可达，以及无优选地形时实际接取任务、真实 `PostMapGenerate` 四人团队和完整保存读档。验证驱动只编入隔离 DLL。

北京时间 2026-09-22 00:18，`FloorAndSiteFix-20260922-Verified` 完成 **51 PASS / 0 FAIL**。七类夹具均有 **937/937 格地板、668/668 项蓝图内容**，四边入口可达；强制让原版拒绝发电机的夹具仍补齐六台满燃料发电机。模拟无优选地形时真实接取任务成功，实际任务地图、四人团队及全部地板建筑在完整保存读档后保留。最终 fresh `Player.log` 扫描无可疑行。

- 证据：`DevData/Evidence/FusionFloorAndSiteFix-20260922/` 的 `Player.log`、`corporate-checks.txt`、`checks-complete.txt` 和基线/静态/目录审计日志。此前山岩夹具曾把天然石地面误断言为必须等于土壤，修正为验证其保持可通行自然地面后重新完成整轮；没有为通过测试改写地形行为。
- 正式 Release Rebuild 成功，DLL SHA256：`A7D13E4D4B1C5043DF8D23187400C0DFF0925AF3119082F49F74209F0A6B0EF3`；正式元数据含选址和补建方法，不含 `CorporateRuntimeValidation` 类型。改动前 DLL 保存在 `DevData/Backups/FusionFloorFix-20260922/MugirlRace-before.dll`。
- 完整静态检查通过构建与本轮涉及的源码/Def 检查后，仍被原有 `Mugirl_Shapes.xml` 两处行尾空格中止；单独资源检查仍报告既有 42 张 FA 烘焙脸红贴图缺失，未擅自恢复资源。目录审计 `cycle_components=0`、`affected_candidates=0`、`scan_errors=0`。测试游戏均在 `%LOCALAPPDATA%/RimWorldModTests/`，未修改玩家存档。

## 2026-09-21 牛聚变保护分支稀有谢礼

- 选择保护研究团队后，赠送科技核心 ×1、复活机械液 ×2、治愈机械液 ×2、雪牛娘陈年奶酪 ×30。科技核心是 `TechprofSubpersonaCore`，不是飞船 AI 核心。奖励在 XML 集中配置，确认对白、任务面板和感谢信读取同一份内容。
- 谢礼沿用 `Vault` 和 `pendingGoods` 保存，自动空投至家园；无家园时保留。发货不受巨企交易权限限制，攻击团队会销毁尚未交付的谢礼。完成状态防止重发，旧档已完成的结局不补发；巨企合同报酬和基础 -60 好感规则保持原样。
- `Start-CorporateValidation.ps1 -Mode Fusion -RunName <新名称>` 新增专门的任务结算与完整存读档验证，避免依赖人员买卖信仰夹具和随机站点地形。验证按存档前的原物品 ID 检查 Vault 或运输舱中的货物；`Game.UpdatePlay` 首帧可能已自动发出谢礼，不能将空的待发列表误判成丢失。

北京时间 2026-09-21 17:53，`FusionRareRewardsTwo-Focused-20260921` 完成 **57 PASS / 0 FAIL**。覆盖奖励准确数量与分堆、发货失败保留、重复保护、攻击撤回、自动送货、巨企报酬关闭及完整保存读档。读档后记录为 `pending=0, restored=6, expected=6`，原有六堆货物均已交付到家园运输舱，数量符合配置，重复选择不生成新物品。

- 证据副本：`DevData/Evidence/FusionRareRewards-Final/Player.log`、`corporate-checks.txt`、`checks-complete.txt`。日志扫描仍有一条测试夹具已销毁 Pawn 的 `DirectPawnRelation` 引用提示（`Thing_Human5076`）；奖励物品与任务记录没有引用错误，不将此次测试记作零日志警告。
- 真实站点与确认对白已在 `FusionRareRewardsTwo-20260921` 通过。此前宽范围运行还遇到随机 `ThinIce` 导致研究站 ToyBox 地形验证失败，以及既有信仰夹具 `Slavery_Abhorrent` 无效；这两项不属于奖励逻辑，本次没有改动。
- 正式 Release Rebuild 与完整静态检查通过；正式 DLL 元数据不含运行时验证驱动类型，仅保留原有 `CorporateValidationSaveMigration`。DLL SHA256：`BCE939AEFCC2038FCC353F07B6F79FD4438F7C41A1A445481C4BA025335A9B6A`。
- `DevData/Evidence/FusionRareRewards-directory-audit.json`：`cycle_components=0`、`affected_candidates=0`、`scan_errors=0`。全部测试游戏位于 `%LOCALAPPDATA%/RimWorldModTests/`，未使用玩家正式存档。

## 2026-09-21 支援队撤退、友伤红字与包扎修复

- 原版 `Lord.SetJob` 会依据派系 `autoFlee` 自动追加减员逃亡，即使自定义图没有撤退分支仍然生效。支援 `LordJob` 现关闭 `AddFleeToil`，保留一天支援期满离场。旧档通过新增 `supportGraphVersion` 区分：缺失字段为 0，先按原图恢复 toil/trigger 索引，再于首个 tick 移除自动逃亡分支；已逃跑的队伍回到战斗，仍在战斗的队伍保留计时。旧版原生包扎 Job 同时重新分配。
- `Faction.TryAffectGoodwillWith` 的实际参数为 `GlobalTargetInfo?`，旧补丁错误声明为 `LookTargets`，导致子弹命中支援队时空引用。修正参数类型及目标派系校验，保留 95% 友伤折扣；空目标和格子目标保持原版惩罚。好感最终数值仍经过原版自然好感修正。
- 支援队使用专用 `JobDriver_CorporateSupportTend`，预约成功后让可行动的伤员等待医生，覆盖接近、取背包医药和包扎过程。只释放本次 Job 创建的等待；医生中断、患者接受新指令或等待到期时终止追逐。征召、战斗中及已有玩家强制指令的伤员不被自动打断；自疗、卧床和倒地目标仍走原版治疗流程，医药耗尽时可徒手处理。
- 新增隔离验证入口：`Start-CorporateValidation.ps1 -Mode Support -RunName <新名称>`。原服务回归中的旧 `HuntEnemiesIndividual` 职责断言同步改为当前专用职责。

验证时间：北京时间 2026-09-21 01:54。RimWorld 1.6.4871 rev591，Harmony + Core + 全 DLC + HAR + Mugirl；运行 `SupportFixes-0921-final`，**64 PASS / 0 FAIL**，游戏自动退出。覆盖 23 件新服装成人限制、4/8 人队伍损失至仅剩一人仍不退、一天后正常离场、真实友伤回调、空/格子目标、移动患者治疗完成、中断与玩家命令、自疗、医药耗尽，以及治疗中、旧战斗图和旧逃亡图的完整游戏存读档。该结果不代表用户完整第三方模组组合的兼容验收。

- 日志与检查副本：`DevData/Evidence/SupportFixes-0921-final/Player.log`、`support-checks.txt`、`checks-complete.txt`；`Invoke-PlayerLogScan.ps1` 无可疑行。完成标记：`PASS checks=64 failures=0 2026-09-20T17:54:48.4359321Z`。
- 完整 `Invoke-Phase6StaticValidation.ps1` 通过，正式 Release DLL 已更新；SHA256：`2D94A5981746D4263602981B6FC8C084DA51B9069F701B0F28B9EFA2DDA55F80`。验证构建使用外部测试游戏，未覆盖正式程序集或使用玩家存档。
- `DevData/Evidence/rimsort-directory-audit-support-0921-final.json`：`affected_candidates=0`、`scan_errors=0`、`cycle_components=0`。

更新：2026-09-16。适用：RimWorld 1.6.4871。完整玩法设计见 [巨企通讯与业务系统设计方案](giant-corporation-design.md)。

## 如何开始

1. 新档在送货员接触任务结束后自动安排巨企员工来访。放行、交战与杀害会得到不同回应，但均允许保留雪牛娘，并提出相同见面礼。
2. 中途加入旧档会立即登记来访任务，无需重跑送货员事件；时间久远或历史不明时使用相应对白。实际员工入场需要有效殖民地及适合来访的局势，当前袭击会推迟入场。
3. 与员工交谈并完成接洽，领取 500 白银、100 钢铁、10 工业零部件、10 工业时代药品，解锁商业网络。后续主线显示“敬请期待”。
4. 选择殖民者操作通讯台中的“联系巨企商业网络”，或让远行队抵达巨企据点并停下，使用当地终端。两个入口共享库存、人员、订单、贷款和任务。
5. 地图付款与交货使用有效贸易信标范围内的实物；远行队使用队伍物品。抵押品、备货订单与待交付人员有独立保管状态。终端显示当前服务地点和可用白银。

界面使用白色、浅灰与青蓝色企业主题，共八页：业务总览、物资交易、物资订单、雪牛娘交易、贷款与抵押、委托中心、主线档案、合同记录。参考 MRK_ALG_UniversalTradeHub 的页面组织，运行时不依赖该模组。

## 首版已包含

| 系统 | 实际规则 |
| --- | --- |
| 产品收购 | 原奶、原毛按市场价值 ×1.6；认可制品 ×1.3。识别实际毛料制品及保留奶原料记录的食品；拒绝变质、尸体污染和生物编码货物。普通钢铁等物资没有出售入口。 |
| 常驻物资 | 每 7 日刷新 16 项有限库存，只能购买。重复打开和读档不会重抽同一周库存。 |
| 特别订单 | 当前规则：普通 ×2.2、3–5 日；稀有 ×6、12–20 日；执政官级 ×10、20–35 日；特供 ×20 且每件至少 20,000 白银、30–60 日。最多 5 单，首日可取消并退 80%；付款后保存实物和签约到货时刻。历史测试结果不代表新价格规则已完成运行验证；新规则由 CorporatePricingChecks 验证。 |
| 人员业务 | 每周 4 名实际成年雪牛娘，购入 ×2.0、收购 ×1.5。只收购符合条件的囚犯或奴隶；保留原版奴隶贸易历史、信仰限制及额外 -8 心情、8 日记忆。购入后作为奴隶加入，需要 Ideology。 |
| 抵押贷款 | 一笔 500–30,000 白银，本金不超过抵押估值 70%；期限 14 日，剩余本金每日 2% 单利，逾期 3%。首轮追偿在违约后 1 日，随后每 2–4 日真实袭击；还清解除债务敌对锁并停止新增追偿。 |
| 抵押处理 | 保管实际抵押物，保留材质、品质、耐久与身份；还清后取回。违约后可主动按签约估值 50% 清算抵债；不会无提示没收后仍索取全部原债。 |
| 每周委托 | 默认 6 份、同时进行 3 份。加工供应实际原料并收取可退保证金；肃清生成敌方站点；投资签约固定结算结果；分销按真实对外销售记进度；适用的外部委托转介到任务列表。 |
| 牛聚变支线 | 原投资结算后，商业网络解锁时在 1–3 日发一次邀请。优先在平坦、少水域的地块选址；无优选地形时通过局部整地强制生成。任务地图中央生成带 937 格地板的 `Mugirl_CorporateFusionResearchSite` 蓝图，移除临时地表、补足承载力及原版跳过的设施，填满现场燃料，并保留四条弯曲的边缘通道。到研究站交谈，执行后通过实际敌对和目标状态履约，报酬 4,000 白银、基础 +10 好感；保护研究团队则关闭企业报酬，获得 1 枚科技核心、2 份复活机械液、2 份治愈机械液及 30 份陈年奶酪，并承担基础 -60 好感。好感遵循原版自然好感调整，确认时显示预计扣减。 |

报价、轮换、利率、人数与奖励等主要参数位于 `1.6/Defs/Misc/`、`1.6/Defs/QuestScriptDefs/CorporateQuests.xml` 和 `1.6/Defs/Corporate/CorporateIntroduction.xml`。已签约的重要条款随合同保存；改配置不会重抽投资结果或改变已有分销销量目标。

债务敌对是独立限制：赠礼或正好感不能解除欠款带来的敌对，还清也不会抹去其他原因造成的外交损失。已交付到地图的追偿者不会被还款凭空删除。

## 验证方法与实际证据

下方首版历史测试使用仓库 `TMP/CorporateValidation-*` 下独立的配置、存档和日志；本轮 2026-09-24 测试游戏位于 `%LOCALAPPDATA%/RimWorldModTests/Mugirl/`，Visual 的截图和存档位于 `%TEMP%`。均不使用玩家正式存档。驱动操作真实 RimWorld 对象、任务、交易、地图和界面，同时记录具体断言。

快速目视验证可开启开发者模式，在调试操作菜单选择 `Mugirl → Test map: fusion research site`；它会在当前地图中央调用与正式任务相同的 Prefab、自然清场和通道生成逻辑。该操作会覆盖中央区域，仅用于测试存档。

| 运行 | 已取得的证据 | 结果 |
| --- | --- | --- |
| Visual4 | 员工实际入场、真实对白选项完成任务并解锁；八个终端页在 1920×1080 和 1280×720、100% UI 缩放的截图 | PASS，18 张截图；已检查中文、搜索提示和滚动布局 |
| AllDlc9 | 启用全部五个 DLC；实际主线/入口、业务、两轮袭击、人员后果、加工、商船销售、四张外勤地图、完整存读档、机械尾异步生成 | **190 PASS / 0 FAIL**，游戏正常完成并退出 |
| NoIdeology4 | 游戏内确认 Ideology 未启用；同样完成业务、外勤、存读档和异步生成，人员采购入口正确拒绝奴隶购买 | **183 PASS / 0 FAIL**，游戏正常完成并退出 |

证据位置（均相对仓库根目录）：

- `DevData/Evidence/CorporateValidation-Visual4/ValidationShots/visual-result.txt` 与同目录 PNG。
- `DevData/Evidence/CorporateValidation-AllDlc9/corporate-checks.txt`、`Player.log`、`checks-complete.txt`、`Saves/CorporateRoundtrip.rws`。
- `DevData/Evidence/CorporateValidation-NoIdeology4/` 下的同名文件。
- AllDlc8 的关系断言、NoIdeology3 的机械尾崩溃等历史失败日志保留用于追溯，不计入最终通过结果。

### 已通过的真实流程

- **主线与入口：** 新档锁定；杀害、放行、冲突、近期、久别与历史不明对白；旧档立即登记且不重复任务；缺失派系只补建一次，保留已有外交且不强行插入世界据点。真实通讯作业包含预留/行走，断电使入口和已开会话失效；据点指令要求实际抵达且停止，离开使会话失效。
- **交易与订单：** 精确扣银、库存递减、部分堆栈收购、奶/毛/毛料服装/实际含奶食品、普通货物排除；订单实物保管、提前领取拒绝、失去入口后保留、到期一次领取、取消退款和缺 Def 退款防重复。
- **贷款与人员：** 真实抵押拆分与返还、跨日单利、先息后本、敌对双向锁、正外交不能绕过、两批实际追偿人物、还清不再派人；实际奴隶身份、信仰拒绝、SoldSlave 历史与 SoldPrisoner 轶事、额外记忆和周轮换对象清理。
- **日常业务：** 简单餐配方的营养单位换算、实际原料与保证金、仅交付约定数量、一次退款及奖励；投资到期前后结果固定；分销不重复分配数量，实际商船 TradeSession/Tradeable.ResolveTrade 完成货物交割并触发进度。
- **外勤：** 三张实际研究站地图分别测试执行、保护、未表态时非玩家死亡；一张实际肃清地图验证真实敌方编组。到场打开真实对白并激活选项。击杀由测试调用 Pawn.Kill 模拟，再走正式目标计数和任务结算；此项不等于人工战斗通关。
- **存档：** 创建完整游戏存档并实际读取，验证解锁、准备中订单、同一周库存、单一货物持有者及无重复引用。未将只调用 Scribe 的局部读写视为完整存读档。

### 本轮修复

1. 主线和企业外勤使用自己的 QuestScriptDef 根，通过正常 QuestUtility 生成；修复 Ideology 在任务结束清理时读取空根的问题，并兼容早期开发存档的缺失引用。
2. 来访阻挡使用原版“当前有效敌对威胁”判断；休眠远古危险中的敌人不会永远阻止员工入场。
3. 加工交付和产品收购使用同一有效贸易信标范围的专用合同货物集合。原版简单餐为仅买入物品，普通商人卖出列表会漏掉它；现在可以正确交付和收购实际含奶食品，仍独立验证货物质量。
4. 新分销合同保存销量目标；旧合同缺字段时按首版 80% 补齐，避免更改配置使已经接受的目标变化。
5. 研究站未表态时的非玩家死亡会中断任务并保留“未决定”，不会替玩家选择执行；选择保护时展示按原版外交机制计算的预计变化。
6. 测试外勤地图在完整初始化之后清理，再执行存档；避免测试过早卸图引起的 MapDrawer 空引用。测试驱动不进入正式 DLL。
7. 真实建图暴露已有机械尾组件在工作线程创建 Unity Mesh 的崩溃。`Comp_SegmentedMechTail.PostSpawnSetup` 改为长事件结束后在主线程初始化，防止重复排队且跳过已经销毁或离图的对象。两次最终运行均实际异步生成带尾机械体，确认工作线程不分配网格、回到主线程后两套网格正常创建。
8. 后续试玩发现研究站可能落在轻型桥梁等 foundation 上。RimWorld 1.6 的承载力查询会优先读取 foundation，原逻辑只把表层改成 `Concrete`，因此日志虽然显示混凝土地面，奶发电机仍会因下层承载力不足而令整张任务地图生成失败。现在建筑实际占地会移除承载力不足的 foundation、再按需补混凝土，同时清除残留的地形施工蓝图；满足承载力的重型 foundation 仍会保留。
9. 历史失败日志还记录了 `ChunkGranite` 与新生成的 `Filth_RubbleRock` 占据 Prefab 建筑落点。`GenSpawn.CanSpawnAt` 会先因石块的 `PassThroughOnly` 通行性拒绝该格，尚未进入实际生成时的 wipe；旧清场只处理建筑、植物、Filth 和地形蓝图，所以结果取决于随机地形。现在清场会以最多四轮的有界循环移除建筑销毁后新产生的残渣，以及所有非 Pawn 的非 `Standable` 阻塞物。后续随机图又抓到 `SteamGeyser`：它属于不可销毁的自然建筑，普通 `Destroy` 只报错而不会清除；现仿照原版 `LayoutWorker`，仅在清除该对象的 `try/finally` 范围内临时开启 `Thing.allowDestroyNonDestroyable` 并恢复旧值。失败日志会记录任务、地块、生态、地图尺寸、父对象和完整异常堆栈。旧版若已在 `PostMapGenerate` 失败，还会永久消耗一次性邀请；载入旧档时现仅对“最新聚变合同已失败、地图存在、队伍从未生成”这一故障指纹重新开放邀请，不会复活正常超时、放弃或进图后的剧情失败。

修复后使用 `CorporateValidation-FusionFoundationFix-20260920` 隔离运行实际生成三张研究站地图，三次 `PostMapGenerate`、团队生成与分支流程全部通过，fresh `Player.log` 未出现 Error、Exception 或同类地形校验警告。整轮共 245 PASS / 2 FAIL；两项失败均属于本轮未改动的高级支援小队人数/职责断言，因此该次运行只作为研究站修复的针对性证据，不记为整套巨企业务全通过。

石块清场修复后又使用 `CorporateValidation-FusionBlockerFix-NoIdeology-20260920` 运行独立真实游戏：显式放置花岗岩块的回归断言通过，三张研究站地图均完成真实 `PostMapGenerate`、生成四人团队并打开到场对白；日志中没有研究站 Prefab、地形或建图异常。整轮为 236 PASS / 2 FAIL，两项仍是与本轮无关的支援小队装备/职责断言。另一次全 DLC 运行在到达外勤阶段前被测试夹具缺少 `Slavery_Abhorrent` 中止，不作为研究站结果。运行时检查现在也会把真实研究站夹具模拟成旧版“地图存在但队伍未生成”的失败状态，确认旧档恢复逻辑会解锁一个替代邀请。

补齐不可销毁自然物后，`CorporateValidation-FusionNaturalBlockerFix-20260920` 最终隔离实机为 239 PASS / 1 FAIL：显式花岗岩块、显式蒸汽喷泉、`Thing.allowDestroyNonDestroyable` 恢复、旧档替代邀请、三张随机研究站的真实 `PostMapGenerate`、四人团队与到场对白全部通过；日志不再出现不可销毁物清理错误或研究站生成异常。唯一失败仍是未改动的支援小队落地职责断言。随后 RimSort 目录审计为 `cycle_components=0`、`affected_candidates=0`、`scan_errors=0`。

原版 `Faction.CalculateAdjustedGoodwillChange` 会将向自然好感靠拢的关系变化放大 25%；因此现场从 100 好感输入基础 -60，实际变为 -75，结果为 25。首版保留原版外交规则；对白显示基础值及当前预计结果，测试核对实际基础好感和显示好感均符合该 API，不把额外幅度当作重复处罚。

## 复现与构建

可选驱动在 `docs/tools/Corporate*Validation.cs` 与 `Corporate*RuntimeChecks.cs`。启动器将验证程序集编译到独立游戏目录，保留正式 DLL；运行时同时要求命令行标记及实际存档路径位于允许的临时目录。驱动不再继承 `GameComponent`，不会自动写入存档。

```powershell
& '.\docs\tools\Start-CorporateValidation.ps1' -Mode Economy -RunName MyFreshRun
# 另可选择 -Mode WithoutIdeology 或 -Mode Visual；Visual 可加 -SkipCompact。
# 每次使用新的 RunName，不要将验证程序集输出到正式 Assemblies 目录。
```

启动器创建独立配置并使用真实图形设备。不要用 `-nographics` 验证依赖纹理图集的 RimWorld。Steam 和 Workshop 依赖需要正常访问权限；未加载 Harmony/HAR 的失败启动不作为业务结果。

生产代码变动后执行不含测试开关的 Release Rebuild，再运行 `docs/tools/Invoke-Phase6StaticValidation.ps1`。正式 DLL 中不应定义 CorporateRuntimeValidation、CorporateVisualValidation 或相应检查驱动类型；兼容迁移有意保留两个旧类名字符串，因此应检查类型定义，不能仅搜索字符串。

### 巨企首版初次构建（后续启动修复前）

- 正式 Release Rebuild 通过，已恢复 `1.6/Assemblies/MugirlRace.dll`；五个验证驱动的类型名称均未包含于 DLL 元数据字符串中。
- 完整 `Invoke-Phase6StaticValidation.ps1` 通过，包括构建、XML/Def、语言键对应、Harmony 登记、架构约束、存档字段默认值与资源路径检查；235 个编译项、479 个具体 Def、923 个英文到中文对应键、1,389 张 PNG 检查通过。
- DLL 大小：664,576 字节。SHA-256：`5F1BF503BE6BE13E9C2FFFD8628E9FFA71446327647A0C0592D604FD2025A8FE`。
- 正式构建不自动运行测试、不更改玩家存档。上面的实际运行证据来自同一业务源码的显式验证构建。

## 尚未覆盖与首版限制

- 未进行长期经济平衡、全套大型模组包兼容、所有异常物流与反复远行队建图/重进测试；目前数值是首轮可调配置。
- 真实通讯作业已验证行走与入口失效，但“行走中保存再读档”的专门场景未单独跑过。通用完整存读档已验证。
- 站点生成、对白选择与目标计数已验证；手动寻路赴约、完整战斗、俘虏释放与再次进入研究站仍需玩家试玩。
- 特别订单排除货币、活体、任务专用对象，以及需要独立生成流程的书籍/基因包等特殊物品；首版最高品质为极佳。
- 加工候选排除特殊 RecipeWorker、手术和随机复杂产物。检查研究与工作台存在，但不替玩家保证殖民者技能、供电、燃料或生产时间；允许已有合格库存履约。
- 外部委托首批为 TradeRequest 和 OpportunitySite_ItemStash。已接入原版生成及任务列表，未手动完整通关这两类转介任务。
- 分销按商品类型和材质累计真实销量，同类合格库存可以履约；不逐件追踪初始批次。
- 保护分支在 2026-09-21 增加稀有谢礼：1 枚科技核心、2 份复活机械液、2 份治愈机械液及 30 份雪牛娘陈年奶酪（基础总价值 7,300 白银），通过运输舱送至家园。无家园时保留，发货前攻击研究人员会取消未交付的谢礼；旧档已完成的结局不追溯补发。
- 不启用 Ideology 时不提供购入奴隶，不会自动改成免费殖民者。旧档缺失巨企时补建派系而不追加世界据点，通讯台可用。
- 初次运行存在其他已安装模组的 About.xml 元数据提示，以及 HAR 三个造型台图标的线程错误。后者经用户提供完整堆栈确认由 Mugirl 提前注册造型台补丁触发，后续修复及新的 DLL 记录见 [造型台修复记录](styling-station-refresh.md)；原先将其统称为环境提示不准确。
