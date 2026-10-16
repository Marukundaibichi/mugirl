namespace Mugirl.Rendering;

/// <summary>骑枪运动模糊着色器的逐图层几何参数（材质参数块中同名字段）。</summary>
/// <param name="SrcScaleOffset">_MugirlSrcScaleOffset：源贴图缩放与偏移，Unity 顺序。</param>
/// <param name="UvRect">_MugirlUvRect：源贴图的 UV 范围 (min.xy, max.xy)。</param>
/// <param name="BlurUv">_MugirlBlurUv：位移在 UV 中的长度。</param>
/// <param name="Travel">_MugirlTravel：本帧世界位移。</param>
/// <param name="PaddingU">_MugirlPaddingU：沿 U 方向的拉伸。</param>
/// <param name="PaddingV">_MugirlPaddingV：沿 V 方向的拉伸。</param>
internal readonly record struct BlurLayerParameters(Vector4 SrcScaleOffset, Vector4 UvRect, Vector4 BlurUv, Vector4 Travel, Vector4 PaddingU, Vector4 PaddingV);
