# 额外描边

在模组设置中开启“额外描边”，粗细范围为 0.25–3 倍，默认 1 倍；旧设置文件默认关闭。松开粗细滑块后刷新人物图集和头像缓存，暂停时也可刷新。设置字段为 `enableExtraOutline` / `extraOutlineWidth`，不改变存档人物数据。

## 渲染方式

- 读取 `PawnRenderTree.Draw` 中已经筛选好的绘制请求及最终矩阵。身体、头发、种族附件和衣服的黑色膨胀轮廓统一放在所有身体层后方，由正常人物贴图遮盖内部交叠处。
- 所有武器都不参与额外描边，包括手持武器、背挂武器、自动装填槽武器，以及武器轮盘切换、剑舞和瞄准交接动画中的武器。
- 各可见人物部件增加一次 GPU 绘制；着色器最多采样 25 次，实心区域提前返回。共用一个材质和属性块，网格元数据使用弱引用缓存。运行期不读回像素、不生成贴图、不分配渲染目标、不扫描全地图人物，也不重算动画。
- 网格外扩方向由真实 UV 角点与绘制矩阵计算，在着色器转换到世界坐标后应用。Unity 动态合批会预先转换顶点，不能再以局部原点判断顶点位于哪一侧；当前算法保留动态合批，兼容镜像 UV 和人物旋转缩放。
- 与本机原版 `Custom/Cutout*` 使用相同的 `Transparent-100` 队列，保留深度测试但不写入描边深度，避免半透明边缘遮挡其他部件的实心轮廓。透明度采样在分支前计算梯度，避免分支中的隐式 LOD 导致边缘不稳定。
- 远处人物和头像通过原版 `DrawNow` 路径把描边烘进缓存；不关闭原版图集优化。`DrawNow` 不支持属性块，因此分别处理即时材质参数和延迟绘制属性块，并验证两条路径一致。
- 跳过武器、隐身、雕像、伤口及特效材质；保留深度测试。兼容正常 XZ 四边形及镜像 UV。第三方完全替换人物绘制、使用非标准几何或自定义材质的路径需要另外接入。

## 资源构建

运行 `docs/tools/Build-OutlineAssets.ps1`，使用 Unity 2022.3.62f3。脚本在 `TMP` 中建立独立工程，输出到 `1.6/Resources/Outline`，不修改现有 Unity 工程。游戏通过平台路径加载专用着色器，普通玩家不需要安装 Unity。

当前已构建 Windows（D3D11 / OpenGLCore / Vulkan）与 macOS（Metal）资源。当前编辑器未安装 Linux Build Support，所以没有生成 Linux 包；安装对应模块并再次运行脚本即可生成。缺少对应资源或 GPU 不支持着色器时保留正常绘制，仅限次记录描边不可用。

## 验证

2026-09-13 本次针对不均匀、断裂和多重描边的修正已完成 C# Release 编译及 Windows/macOS 着色器资源构建。依用户要求，本次未运行渲染测试或操作游戏；游戏内效果由用户手动验收。下述工具和既有 QA 图片不能视为本次修正已验收的证据。

`docs/tools/Build-OutlineAssets.ps1 -ValidateOnly` 在 Unity 中编译**生产代码** `MugirlExtraOutline.cs`，用最小游戏对象替身和实际 Windows 资源包离屏渲染，检查：

- 身体/衣服交叠的原有像素不变，轮廓外有黑边；
- 粗细增加时黑边面积随之增加；
- 背挂与自动装填槽武器不生成额外描边；
- 翻转 UV 的粗细一致，接触贴图边界的轮廓不被裁掉；
- 即时和延迟绘制结果一致；
- 隐身标志不生成描边；设置变化使图集和头像失效。

QA 图片位于 `TMP/OutlineShaderBuild/QA`。这些是受控 GPU 测试，不代表已经完成真实存档和所有兼容模组的游戏内验收。游戏内还应检查四朝向、穿脱衣服、换发型、倒地/躺床、开关描边、缩放跨越图集阈值，以及手持、背挂和轮盘动画武器均保持无描边。未提供实际存档的 FPS 测量。

接口证据：本机 RimWorld 1.6 `Assembly-CSharp.dll`，MVID `61e4173561894da49d210260257b5097`。核对了 `PawnRenderTree.Draw/ParallelPreDraw`、`PawnRenderer.RenderPawnAt/ParallelGetPreRenderResults/RenderCache` 和 `GenDraw.DrawMeshNowOrLater`。武器绘制路径不再安装额外描边补丁。

合批行为参考：[Unity：Disable dynamic batching of a shader](https://docs.unity.cn/6000.0/Documentation/Manual/writing-shader-tags-disable-dynamic-batching.html)。本机游戏资源中的 `Custom/Cutout`、`Custom/CutoutRecolor`、`Custom/CutoutHair` 的序列化渲染状态确认为 `Transparent-100`、`ZTest LEqual`、透明混合；旧版描边误用了 `AlphaTest` 队列。临时逐部件诊断日志已移除。
