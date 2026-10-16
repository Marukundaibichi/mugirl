#version 450

// Mugirl 骑枪冲锋运动模糊（RimWorld 1.7 原生移植，原版为 docs/lance-motion-blur/MugirlLanceMotionBlur.shader）。
// 四边形沿本帧位移拉伸、整体后移半个位移，UV 同步扩展；拉伸向量由 C# 按绘制矩阵算好，与 1.6 相同。
// 1.6 在这里把 world.y 再降 0.005；1.7 改由 C# 降低绘制矩阵的平移，几何相同，且透明排序键与几何一致（拖尾排在本图层之后）。
// 不写实例化相关的宏与内建变量（注释中也不写）：引擎见到它们会多编译一个实例化变体，而本效果总是逐个网格绘制。

#include "Mugirl_LanceMotionBlurUniforms.glsl"

layout(location = 0) in vec3 inPosition; // @vertex Position required
layout(location = 3) in vec4 inUv0;      // @vertex TexCoord0 required

layout(push_constant) uniform Push { mat4 modelMatrix; } push;

layout(set = 0, binding = 0) uniform VertexUniforms
{
    mat4 ViewProj;
} uVertex;

layout(location = 0) out vec2 vUv;

void main()
{
    // MeshMakerPlanes 的 UV 覆盖 [0, 1]，中心 0.5（1.6 的 _MeshUvCenter）。
    vec2 corner = sign(inUv0.xy - vec2(0.5));
    vec3 world = (push.modelMatrix * vec4(inPosition, 1.0)).xyz;
    world += corner.x * uMaterial._MugirlPaddingU.xyz + corner.y * uMaterial._MugirlPaddingV.xyz - uMaterial._MugirlTravel.xyz * 0.5;
    gl_Position = uVertex.ViewProj * vec4(world, 1.0);

    vec2 blurUv = uMaterial._MugirlBlurUv.xy;
    vUv = inUv0.xy * uMaterial._MugirlSrcScaleOffset.xy + uMaterial._MugirlSrcScaleOffset.zw
        + corner * abs(blurUv) * 0.5 - blurUv * 0.5;
}
