// Mugirl 原生着色器的共享函数。include 文件：没有版本指令，引擎把它原样粘贴进包含它的阶段。
// 移植约定：UV 运算保持 Unity 约定（v = 0 在图像底部），只在采样时翻转 V；Waylith 纹理首行在 v = 0。
#ifndef MUGIRL_COMMON_GLSL
#define MUGIRL_COMMON_GLSL

const float MUGIRL_PI = 3.14159265;

// Unity UV -> Waylith 纹理坐标。
vec2 MugirlFlipV(vec2 uv) { return vec2(uv.x, 1.0 - uv.y); }

// 与 MugirlFlipV 配套：textureGrad 的屏幕导数随坐标一起翻转 V。
vec2 MugirlFlipGradient(vec2 derivative) { return vec2(derivative.x, -derivative.y); }

// 同原版 MaskGate.glsl：Waylith 载入 PNG 时把颜色渗进全透明像素，遮罩的 r/g 通道按 alpha 门控。
// 文件按名字跨模组解析，所以用自己的副本，不包含原版文件。
float MugirlMaskGate(vec4 mask) { return clamp(mask.a * 255.0, 0.0, 1.0); }

#endif
