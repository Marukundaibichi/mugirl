**性能优化实施记录 — 2026-09-07**

已根据性能审查完成资源、持续 Tick、图形刷新和兼容层优化，并重新构建 `1.6/Assemblies/MugirlRace.dll`。没有调整产量、营养值、伤害、连射速度、免疫规则或存档字段。以下数字是资源统计、调度规则或隔离测试结果，不是游戏内耗时测量。

| 范围 | 实施结果 |
|---|---|
| 贴图尺寸 | 用户选择本地脚本处理；358 张 555×555 RGBA PNG 整图等比缩放到 512×512，保留原图备份 |
| FA 条件加载 | 707 张贴图从根 Textures/FA 移到 1.6/FacialAnimation/Textures/FA，虚拟路径 FA/... 不变；未启用 FA 时只加载其余 682 张 PNG |
| 哺乳状态 | 常态由线性查找改为 O(1) 列表位置校验；失效后立即重新定位，目标被删除后仍在下一次组件 tick 恢复 |
| 牵引索引 | 每 10 tick 分批检查，稳定人口下 250 tick 完成一轮；重复关系快退，列表原地复用，不再周期性清空重建 |
| 图形刷新 | 体型正确时不入队；实际变化和尸鬼转换保留即时、30 tick、300 tick 刷新，取消中间重复失效 |
| Yayo 兼容 | 征召、Job 和 60 tick 错峰检查放在反射设置读取之前 |
| Melee Animation 兼容 | 缓存强类型字段访问器，用 ref 结构体状态恢复设置，避免绘制时创建恢复对象或装箱 |
| 骑乘瞄准 | 完全空闲时更早返回；仍保留目标、施放、暖机、busy stance、连射和取消处理 |
| 次要路径 | 挤奶工作先检查奶量；喂奶单次计算营养倍率；迁徙先判断能否进食再搜植物；洗脑表演缓存配置和结束时间 |

**资源验证与备份。**

PNG 总数仍为 1,389，没有删掉脸型、表情或其他图片。全部 358 张原图和转换结果均记录 SHA-256；707 张 FA 图片在移动前后逐文件核对哈希。包源中的 49 处 FA 资源引用均位于条件目录，迁移后可以解析。此项是静态引用闭合验证，不代替加载全部兼容 mod 后的合并 Def 验证。

缩放采用 Pillow LANCZOS 与预乘透明度，整张画布等比处理，没有裁边、改变 drawSize 或调整脸部比例。全部缩放图片的最大归一化透明度重心偏移为 0.000483515，折算到 512 图约 0.248 像素。已检查按层前后对比图和七组正面图层合成对比；未见新增明显偏移。合成图用于素材对齐检查，没有模拟 HAR/FA 的全部染色与渲染规则。

原来的 358 张图片因 555 不能被 4 整除，无法进入当前原版纹理压缩分支。缩放后满足压缩尺寸条件；开启原版纹理压缩时可减少这批纹理的数据量。PNG 磁盘体积与实际显存不是同一指标，本次未测量进程工作集、图集或显存。

本地备份和 QA 均位于 Git 忽略、不会进入发布包的目录：

- 原图：`DevData/Backups/PerformanceOptimization-20260907/OriginalTextures/`，保留原有相对路径，共 358 张。
- 原 DLL：`DevData/Backups/PerformanceOptimization-20260907/OriginalAssemblies/MugirlRace.dll`。
- 纹理清单：`DevData/Backups/PerformanceOptimization-20260907/textures.json`。
- FA 移动清单：`DevData/Backups/PerformanceOptimization-20260907/fa-move-hashes.json`。
- 分层对比：`DevData/Backups/PerformanceOptimization-20260907/texture-comparison.png`。
- 正面合成对比：`DevData/Backups/PerformanceOptimization-20260907/face-layer-comparison.png`。
- 完整验证日志：`DevData/Backups/PerformanceOptimization-20260907/static-validation.log`。

如需恢复单张图片，从 OriginalTextures 复制到其当前位置即可；原路径以 Textures/FA 开头的图片当前位于 1.6/FacialAnimation/Textures/FA。该备份不会随 Workshop 包发布，请勿清理前忘记另存需要的原始素材。

**行为边界。**

- 哺乳缓存每次验证实际列表位置，不缓存“缺失”结果，不要求其他 mod 发送通知。增删、重排、整个列表替换和读档均会失效或重新定位。产量的原有经过时间累计不变。
- 牵引仍持续轮询空索引地图，能够发现其他 mod 绕通知直接改 tracker 的关系；初始化立即修复，跨图和毁坏关系定期清理。地图人物列表持续变化时，个别对象可能延至下一轮校验。
- 默认系到建筑的雪牛娘仍按原版 roper.roping.Ropees 顺序选择，不受地图索引初始化次序影响。
- 图形刷新队列仍保留最终 300 tick 兜底；新通知重新安排时间。体型实际改变时仍立即标记，未修改外部动画参数。
- Melee Animation 只缓存字段访问器，运行时读取当前 Settings 实例；Postfix/Finalizer 共享 ref 状态，异常与嵌套调用均只恢复一次。
- 洗脑表演缓存假设单次表演期间 Def 配置稳定；开始、读档与传入配置引用变化时重建。运行时原地修改同一配置对象的字段，需要重新开始表演才能重新计算结束时间。

报告中的持续免疫组件、农场强制移动节流、射线检查降频未在本轮改变：前两者涉及免疫/任务抢占时机，后者可能影响瞄准和取消行为。当前先完成保持既有行为的优化，后续如采样显示这些路径占比高，再单独设计变更。

**验证结果。**

| 检查 | 结果 |
|---|---|
| Release 构建与全套 Invoke-Phase6StaticValidation | 通过，包括 XML、类型引用、生命周期、补丁元数据、资源引用等 |
| Test-MilkCache.ps1 | 16 个隔离行为断言通过，编译当前生产方法，覆盖缓存失效与恢复时机 |
| Test-RopingIndex.ps1 | 16,552 个断言通过，直接编译生产索引和选择方法，覆盖分批预算、顺序、共享列表与关系变更 |
| Test-RenderingRefresh.ps1 | 24 个断言通过，编译生产源码，覆盖调度、重入、新通知、毁坏和跨档清理 |
| Test-MeleeAnimationScope.ps1 | 22 个断言通过，使用实际引用的 Harmony DLL 生成并执行补丁，覆盖正常、嵌套、异常与设置实例替换 |
| Invoke-TextureAssetValidation.ps1 | 1,389 个 PNG 文件头、49 处 FA 路径和条件加载检查通过，无 555×555 残留 |
| Optimize-TextureAssets.py --verify | 358 张新图与原图备份的哈希、尺寸、格式全部通过 |
| git diff --check | 通过 |
| 发布包预检查 | 通过，1,652 个文件，707 张 FA 贴图位置正确，DLL 与当前构建哈希一致 |

验证包位于 `TMP/WorkshopPackage/MugirlPerformance-20260907/`，尚未发布至 Workshop。

本次顺带修正了原静态检查中把子命名空间误判为缺失类型的问题，并补齐两个既有字段/补丁的维护说明；没有因此改变游戏数据。静态检查针对新缓存结构更新，仍保留生命周期、条件加载和恢复安全约束。

需要复查时，从模组根目录执行：

```powershell
.\docs\tools\Invoke-Phase6StaticValidation.ps1
.\docs\tools\Test-MilkCache.ps1
.\docs\tools\Test-RopingIndex.ps1
.\docs\tools\Test-RenderingRefresh.ps1
.\docs\tools\Test-MeleeAnimationScope.ps1
python .\docs\tools\Optimize-TextureAssets.py --verify
```

测试夹具不模拟整个 RimWorld，也未进行游戏内 TPS、FPS 或显存采样。实际游戏仍应抽查保存读档、成长与尸鬼外观、牵引跨图、骑乘连射、原版/兼容婴儿喂食，以及 FA 启用/禁用两种组合。当前 DLL 和资源将在下次启动游戏时加载。
