#version 450

// Mugirl 额外描边（RimWorld 1.7 原生移植，原版为 docs/outline/MugirlExtraOutline.shader）。
// 四边形连同 UV 范围一起外扩：贴到贴图边缘的画面也有完整描边（含镜像网格）。外扩向量由 C# 按绘制矩阵算好，
// 与 1.6 相同；Waylith 没有动态合批，模型矩阵就是这次绘制的矩阵。
// 不写实例化相关的宏与内建变量（注释中也不写）：引擎见到它们会多编译一个实例化变体，而本效果总是逐个网格绘制。

#include "Mugirl_ExtraOutlineUniforms.glsl"

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
    // MeshMakerPlanes 的 UV 覆盖 [0, 1]，中心 0.5（1.6 的 _MeshUvCenter）；镜像网格只反转 U，角的符号仍对应 U/V 方向。
    vec2 corner = sign(inUv0.xy - vec2(0.5));
    vec3 world = (push.modelMatrix * vec4(inPosition, 1.0)).xyz;
    world += corner.x * uMaterial._MugirlPaddingU.xyz + corner.y * uMaterial._MugirlPaddingV.xyz;
    gl_Position = uVertex.ViewProj * vec4(world, 1.0);

    vec2 uv = inUv0.xy * uMaterial._MugirlSrcScaleOffset.xy + uMaterial._MugirlSrcScaleOffset.zw;
    vec2 center = (uMaterial._MugirlUvRect.xy + uMaterial._MugirlUvRect.zw) * 0.5;
    vUv = uv + sign(uv - center) * uMaterial._MugirlUvRadius.xy;
}
