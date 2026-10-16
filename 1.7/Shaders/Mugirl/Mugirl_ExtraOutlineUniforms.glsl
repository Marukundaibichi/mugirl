// Mugirl_ExtraOutline 的材质参数块，顶点与片元阶段都包含它（引擎只从片元阶段解析布局，两阶段的声明必须相同）。
// 解析器保留块内的行注释，所以说明写在块外，块内每行只写注解：
//   _MugirlSrcScaleOffset  源图层 _MainTex 的缩放（xy）与偏移（zw），Unity 顺序；不是 *_ST 槽位，引擎不重排也不翻转。
//   _MugirlPaddingU/V      世界空间的外扩向量，沿网格 UV 的 U/V 方向（1.6 的 _WorldPaddingU/V）。
//   _MugirlUvRect          源贴图在 Unity UV 中的范围（min.xy, max.xy）。
//   _MugirlUvRadius        描边宽度换算到 UV 的半径（xy）。
//   _MugirlOpacity         人物着色 alpha 乘源材质 alpha。
#ifndef MUGIRL_EXTRA_OUTLINE_UNIFORMS_GLSL
#define MUGIRL_EXTRA_OUTLINE_UNIFORMS_GLSL

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _MugirlSrcScaleOffset; // @default = vec4(1.0, 1.0, 0.0, 0.0)
    vec4 _MugirlPaddingU;       // @default = vec4(0.0, 0.0, 0.0, 0.0)
    vec4 _MugirlPaddingV;       // @default = vec4(0.0, 0.0, 0.0, 0.0)
    vec4 _MugirlUvRect;         // @default = vec4(0.0, 0.0, 1.0, 1.0)
    vec4 _MugirlUvRadius;       // @default = vec4(0.0, 0.0, 0.0, 0.0)
    float _MugirlOpacity;       // @default = 1.0
} uMaterial;

#endif
