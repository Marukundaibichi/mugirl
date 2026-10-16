#version 450

// Mugirl 额外描边的片元阶段：对源贴图 alpha 做圆形膨胀，输出黑色描边。

#include "Mugirl_ExtraOutlineUniforms.glsl"
#include "Mugirl_Common.glsl"

layout(location = 0) in vec2 vUv;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;

float MugirlSilhouette(vec2 uv, vec2 dx, vec2 dy)
{
    vec4 rect = uMaterial._MugirlUvRect;
    vec2 inside = step(rect.xy, uv) * step(uv, rect.zw);
    return textureGrad(_MainTex, MugirlFlipV(clamp(uv, rect.xy, rect.zw)), dx, dy).a * inside.x * inside.y;
}

void main()
{
    // 先取导数再分支：分歧控制流中的隐式 LOD 会让 alpha 边缘不稳定、出现虚线。
    vec2 dx = MugirlFlipGradient(dFdx(vUv));
    vec2 dy = MugirlFlipGradient(dFdy(vUv));
    vec2 radius = uMaterial._MugirlUvRadius.xy;
    float a = MugirlSilhouette(vUv, dx, dy);
    // 实心内部只需一次采样，正常的人物图层会覆盖它。
    if (a < 0.99)
    {
        // 外圈 16 个、内圈 8 个采样，补上细发与尖端处的缺口，不需要重复绘制。方向为 22.5° 的整数倍（1.6 的 ring 常量表）。
        for (int j = 0; j < 16; j++)
        {
            float angle = float(j) * (MUGIRL_PI / 8.0);
            a = max(a, MugirlSilhouette(vUv + vec2(cos(angle), sin(angle)) * radius, dx, dy));
        }
        for (int k = 0; k < 8; k++)
        {
            float angle = float(k) * (MUGIRL_PI / 4.0);
            a = max(a, MugirlSilhouette(vUv + vec2(cos(angle), sin(angle)) * radius * 0.5, dx, dy));
        }
    }

    // 与镂空轮廓一致，边缘留一条小的抗锯齿带。
    float coverage = smoothstep(0.35, 0.65, a) * uMaterial._MugirlOpacity;
    if (coverage < 0.01)
    {
        discard;
    }

    outColor = vec4(0.0, 0.0, 0.0, coverage);
}
