namespace Mugirl.Rendering;

/// <summary>额外描边着色器的逐图层参数（材质参数块中同名字段）。</summary>
/// <param name="SrcScaleOffset">_MugirlSrcScaleOffset：源贴图缩放与偏移，Unity 顺序。</param>
/// <param name="PaddingU">_MugirlPaddingU：沿 U 方向的世界外扩。</param>
/// <param name="PaddingV">_MugirlPaddingV：沿 V 方向的世界外扩。</param>
/// <param name="UvRect">_MugirlUvRect：源贴图的 UV 范围 (min.xy, max.xy)。</param>
/// <param name="UvRadius">_MugirlUvRadius：描边在 UV 中的半径。</param>
internal readonly record struct OutlineLayerParameters(Vector4 SrcScaleOffset, Vector4 PaddingU, Vector4 PaddingV, Vector4 UvRect, Vector4 UvRadius);
