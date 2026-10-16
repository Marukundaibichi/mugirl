#version 450

// Mugirl 骑枪运动模糊的片元阶段：沿位移方向对源图层做透明度加权卷积。

#include "Mugirl_LanceMotionBlurUniforms.glsl"
#include "Mugirl_Common.glsl"

layout(location = 0) in vec2 vUv;

layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 1) uniform sampler2D _MainTex;
layout(set = 2, binding = 2) uniform sampler2D _MaskTex; // @default = black

const int MugirlBlurSamples = 48;

void main()
{
    vec2 dx = MugirlFlipGradient(dFdx(vUv));
    vec2 dy = MugirlFlipGradient(dFdy(vUv));
    vec4 rect = uMaterial._MugirlUvRect;
    vec2 blurUv = uMaterial._MugirlBlurUv.xy;
    bool hasMask = uMaterial._MugirlHasMask > 0.5;

    // 横向色带取不同尾长，形成不齐整的尖尾；同一条色带沿运动方向保持连续。
    vec2 direction = normalize(blurUv + vec2(0.000001));
    float lane = dot(vUv, vec2(-direction.y, direction.x)) * 32.0;
    float band = floor(lane);
    float blend = smoothstep(0.2, 0.8, fract(lane));
    float noiseA = fract(sin(band * 127.1 + 17.7) * 43758.5453);
    float noiseB = fract(sin((band + 1.0) * 127.1 + 17.7) * 43758.5453);
    float reach = 0.4 + 0.6 * mix(noiseA, noiseB, blend);

    // 对后方轨迹做透明度加权卷积：透明边缘不变黑，也不出现多个人形轮廓。
    vec4 accumulated = vec4(0.0);
    float weightSum = 0.0;
    for (int i = 0; i < MugirlBlurSamples; i++)
    {
        float t = (float(i) + 0.5) / float(MugirlBlurSamples);
        float weight = 1.0 - 0.6 * t;
        vec2 uv = vUv + blurUv * t * reach;
        vec2 inside = step(rect.xy, uv) * step(uv, rect.zw);
        vec2 sampleUv = MugirlFlipV(clamp(uv, rect.xy, rect.zw));
        vec4 color = textureGrad(_MainTex, sampleUv, dx, dy);
        vec3 tint = uMaterial._Color.rgb;
        if (hasMask)
        {
            vec4 mask = textureGrad(_MaskTex, sampleUv, dx, dy);
            mask.rg *= MugirlMaskGate(mask);
            tint = mix(vec3(1.0), uMaterial._Color.rgb, mask.r) * mix(vec3(1.0), uMaterial._ColorTwo.rgb, mask.g);
        }

        color.rgb *= tint;
        color.a *= inside.x * inside.y * uMaterial._Color.a;
        accumulated.rgb += color.rgb * color.a * weight;
        accumulated.a += color.a * weight;
        weightSum += weight;
    }

    outColor = vec4(accumulated.rgb / max(accumulated.a, 0.00001), accumulated.a / weightSum * uMaterial._MugirlOpacity);
}
