# 骑枪冲锋：破墙与运动模糊

适用 RimWorld 1.6；2026-09-21 按玩家要求增加破墙，并将分离的人形残影改为连续、不规则尾缘、落地回收的运动模糊。此前骑枪按钮、定点伤害、骑乘跟随等历史见 [9.20 更新记录](../records/update-2026-09-20.md)。

## 破墙规则

- 三种骑枪的沿途与定点冲锋，每次各有独立的 **1550 点墙体当前耐久**额度。它不是武器耐久消耗，也不乘角色近战或建筑伤害倍率。
- 每个飞行 tick 在移动前检查整段位移经过的网格。按先后顺序扣除墙的实际耐久；不足以摧毁下一面墙时，扣完剩余额度并在墙前最后一个可落脚格停止。
- 刚好耗尽额度且前方畅通时继续冲锋；若随后还有墙，立即停下。中途出现的新墙同样检查，原版跳跃的周期性落点改选不能绕过墙壁。
- 墙体使用游戏 `ThingDef.IsWall` 标识，兼容声明该标识的模组墙；不依据中文名称猜测。门、天然岩石及其他不可通行建筑不属于破墙目标，仍能挡停冲锋；可通行建筑不受损。
- 墙耐久归零后调用原版 `Kill`，保留建筑销毁反馈、残骸与重建处理。定点冲锋被挡后取消锁定目标的命中与该次命中的武器损耗；沿途攻击不能隔着未撞开的墙触及另一侧目标。
- 飞行器新增 `wallHitPointsRemaining` 和 `lastPassableCell` 存档字段；读档不补回已使用的额度。旧档默认额度 1550、落脚格未设置，从已有飞行位置继续检查并寻找此前可落脚格。

## 连续运动模糊

- `LanceMotionBlur` 每帧准备一次世界坐标的 `PawnRenderTree` 部件矩阵，避免图集路径残留原点矩阵；随后每个部件只提交一次方向卷积绘制。不复制 Pawn，不为多个历史位置重复执行 `ParallelPreDraw`，不生成一串密集人形副本。
- 专用透明着色器在贴图中进行 48 点透明度加权方向卷积，按冲锋方向扩展网格。横向色带采用不同长度，形成不齐整的尾缘。当前角色和骑枪继续使用原版绘制，保持清晰。
- 曝光长度相当于最近 5 tick 的位移。正常落地与撞墙停止后，拖尾用 18 tick（正常速度约 0.3 秒）平滑缩短并淡出，回收到角色当前位置。
- 按后续要求取消发动时的 `ShockwaveFast`；落地的原有冲击效果保留。
- 落地回收属于纯视觉状态，弱关联 Pawn，回收结束或换局时清除，不写入存档。飞行中读档会重建当前拖尾；落地瞬间保存不会保留未完成的视觉尾迹。
- 不改变全屏后处理，不分配人物 RenderTexture，不在运行时读取像素。固定材质和属性块按进程复用，网格元数据用弱引用缓存。

源码：`1.6/Source/Features/Lances/PawnFlyer_LanceCharge.cs`、`LanceMotionBlur.cs`、`Harmony_LanceChargeRendering.cs`。着色器与构建入口：`docs/lance-motion-blur/MugirlLanceMotionBlur.shader`、`docs/tools/Build-LanceMotionBlurAssets.ps1`。

## 构建与验证

`Build-LanceMotionBlurAssets.ps1` 使用 Unity 2022.3.62f3，在 `TMP/LanceMotionBlurShaderBuild` 创建无联接的构建工程，输出 `1.6/Resources/LanceMotionBlur`；`-ValidateOnly` 使用合成透明贴图做离屏 GPU 绘制并输出 `blur-gpu-check.png`。本机可构建 Windows（D3D11 / OpenGLCore / Vulkan）和 macOS（Metal）；未安装 Linux Build Support，Linux 资源尚未生成，缺少对应资源时只省略视觉拖尾，冲锋玩法照常运行。

`Start-LanceRuntimeValidation.ps1 -RunName <新名称>` 自动在 `%LOCALAPPDATA%/RimWorldModTests/Mugirl/` 建立隔离环境并构建 `EnableLanceValidation=true` 的独立 DLL。日志、截图与状态验证 XML 位于返回的 `SaveRoot`，测试完成自动退出。测试版不覆盖正式 `1.6/Assemblies/MugirlRace.dll`；禁止把测试环境联接回模组扫描目录。

本轮证据位于 `TMP/LanceWallCharge-20260921`。修改前正式 DLL SHA256 为 `4A79CFF0C5783F528EF7573D10B5E2C549008258BE392C2AEF68E53069763E6A`。工作区原有源码、DLL、贴图删除与文档整理均保留。

静态基线实际复查：`Mugirl_Shapes.xml` 两处原有行尾空格令标准脚本停止；独立纹理检查仍报告 42 张 FA 烘焙脸红贴图缺失。不恢复这些用户既有删改来消除失败。

最终 quicktest `0921-wall-blur-04`：**135 项通过、0 项失败**，完成标记 `PASS failures=0 2026-09-21T14:53:19.1582144Z`。覆盖累计耐久、刚好耗尽、下一面墙、合并 tick、斜线路径、起飞后落点新增墙、定点取消、Scribe 字段往返和旧档默认值、三把骑枪按钮、原有骑乘与跳跃，以及连续拖尾和落地回收。Scribe 测试用实际飞行器的空容器验证新增状态，不等同于完整骑乘存档的人工保存读档验收。

- 最终日志、检查清单与原始截图已复制到 `TMP/LanceWallCharge-20260921/RuntimeFinal`；`final-logscan.txt` 确认无可疑行。
- `lance-direct-flight.png` 已人工看图确认发动时无 ShockwaveFast，大图中角色后方可见连续不规则拖尾；`lance-blur-landing.png` / `lance-blur-retracting.png` 记录落地保留的冲击效果与缩短的拖尾，自动断言确认 18 tick 后清除回收状态。
- GPU 合成图验证输出了 87650 个有色像素，Windows 与 macOS 着色器构建完成，无编译错误。macOS 仅完成资源构建，真实游戏截图来自 Windows。
- 完整静态脚本已执行正式 Release 构建；构建无警告或错误，脚本继续在上述两处原有行尾空格停止。独立纹理检查的 42 张贴图缺失保持不变，本次涉及文件的差异空白检查通过。
- 正式 DLL SHA256：`0E72CC82306B0C3476091E74FDD243A63020D4F26A6731D68B22721D0DAF04A4`。原 DLL 备份为 `DevData/Backups/LanceWallCharge-20260921/MugirlRace-before.dll`，散列与修改前基线一致。
- 最终 `directory-audit-final.json`：`affected_candidates=0`、`scan_errors=0`、`cycle_components=0`。
