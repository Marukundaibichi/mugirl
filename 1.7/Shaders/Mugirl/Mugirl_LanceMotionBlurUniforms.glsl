// Mugirl_LanceMotionBlur 的材质参数块，顶点与片元阶段都包含它（引擎只从片元阶段解析布局，两阶段的声明必须相同）。
// 解析器保留块内的行注释，所以说明写在块外，块内每行只写注解：
//   _Color, _ColorTwo       源材质的主色与副色；主色已乘上受伤闪烁色（同 1.6）。
//   _MugirlSrcScaleOffset   源图层 _MainTex 的缩放（xy）与偏移（zw），Unity 顺序；不是 *_ST 槽位。
//   _MugirlUvRect           源贴图在 Unity UV 中的范围（min.xy, max.xy）。
//   _MugirlBlurUv           本帧位移在 UV 中的长度（xy，1.6 的 _BlurUV）。
//   _MugirlTravel           本帧世界空间位移（xyz）。
//   _MugirlPaddingU/V       世界空间的拉伸向量，沿网格 UV 的 U/V 方向（1.6 的 _PaddingU/V）。
//   _MugirlHasMask          有着色遮罩时为 1。
//   _MugirlOpacity          0.8 乘落地淡出乘隐身 alpha。
#ifndef MUGIRL_LANCE_MOTION_BLUR_UNIFORMS_GLSL
#define MUGIRL_LANCE_MOTION_BLUR_UNIFORMS_GLSL

layout(set = 2, binding = 0) uniform MaterialUniforms
{
    vec4 _Color;                // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _ColorTwo;             // @color @default = vec4(1.0, 1.0, 1.0, 1.0)
    vec4 _MugirlSrcScaleOffset; // @default = vec4(1.0, 1.0, 0.0, 0.0)
    vec4 _MugirlUvRect;         // @default = vec4(0.0, 0.0, 1.0, 1.0)
    vec4 _MugirlBlurUv;         // @default = vec4(0.0, 0.0, 0.0, 0.0)
    vec4 _MugirlTravel;         // @default = vec4(0.0, 0.0, 0.0, 0.0)
    vec4 _MugirlPaddingU;       // @default = vec4(0.0, 0.0, 0.0, 0.0)
    vec4 _MugirlPaddingV;       // @default = vec4(0.0, 0.0, 0.0, 0.0)
    float _MugirlHasMask;       // @default = 0.0
    float _MugirlOpacity;       // @default = 0.8
} uMaterial;

#endif
