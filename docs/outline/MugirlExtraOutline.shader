Shader "Mugirl/ExtraOutline"
{
    Properties
    {
        _MainTex ("Silhouette", 2D) = "white" {}
        _WorldPaddingU ("World padding along UV U", Vector) = (0,0,0,0)
        _WorldPaddingV ("World padding along UV V", Vector) = (0,0,0,0)
        _MeshUvCenter ("Mesh UV center", Vector) = (.5,.5,0,0)
        _UvRect ("UV bounds", Vector) = (0,0,1,1)
        _UvRadius ("UV radius", Vector) = (0,0,0,0)
        _Opacity ("Opacity", Float) = 1
    }
    SubShader
    {
        // RimWorld's Custom/Cutout* shaders use Transparent-100, not AlphaTest.
        // Back-to-front sorting puts the union behind the normal pawn layers.
        Tags { "Queue"="Transparent-100" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        // Outlines overlap: a partially covered edge must not write depth and block
        // another part's solid outline, leaving a seam or a second contour.
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST, _WorldPaddingU, _WorldPaddingV, _MeshUvCenter, _UvRect, _UvRadius;
            half _Opacity;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v)
            {
                v2f o;
                // Extend the quad as well as the UV range: art touching a texture edge
                // must not have its outline clipped, including horizontally flipped quads.
                // Dynamic batching converts input vertices to world space and makes
                // unity_ObjectToWorld identity. UV corners survive that conversion.
                // Apply precomputed WORLD directions after ObjectToWorld, so batched
                // and unbatched draws expand identically, without disabling batching.
                float2 corner = sign(v.texcoord.xy - _MeshUvCenter.xy);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                world += corner.x * _WorldPaddingU.xyz + corner.y * _WorldPaddingV.xyz;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                float2 uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                float2 center = (_UvRect.xy + _UvRect.zw) * 0.5;
                o.uv = uv + sign(uv - center) * _UvRadius.xy;
                return o;
            }
            half silhouette(float2 uv, float2 dx, float2 dy)
            {
                float2 inside = step(_UvRect.xy, uv) * step(uv, _UvRect.zw);
                return tex2Dgrad(_MainTex, clamp(uv, _UvRect.xy, _UvRect.zw), dx, dy).a * inside.x * inside.y;
            }
            half4 frag(v2f i) : SV_Target
            {
                // Take derivatives before the early-out branch; implicit texture LOD
                // inside divergent flow can produce unstable/dashed alpha edges.
                float2 dx = ddx(i.uv), dy = ddy(i.uv);
                half a = silhouette(i.uv, dx, dy);
                // The solid interior needs only one sample; normal pawn sprites cover it.
                if (a < 0.99h)
                {
                    // Sixteen outer samples and eight inner samples avoid gaps around
                    // thin hair/weapon tips without repeated offset draw calls.
                    const float2 ring[16] = {
                        float2(1,0), float2(.923880,.382683), float2(.707107,.707107), float2(.382683,.923880),
                        float2(0,1), float2(-.382683,.923880), float2(-.707107,.707107), float2(-.923880,.382683),
                        float2(-1,0), float2(-.923880,-.382683), float2(-.707107,-.707107), float2(-.382683,-.923880),
                        float2(0,-1), float2(.382683,-.923880), float2(.707107,-.707107), float2(.923880,-.382683)
                    };
                    [unroll] for (int j = 0; j < 16; j++)
                        a = max(a, silhouette(i.uv + ring[j] * _UvRadius.xy, dx, dy));
                    [unroll] for (int k = 0; k < 8; k++)
                        a = max(a, silhouette(i.uv + ring[k * 2] * _UvRadius.xy * .5, dx, dy));
                }
                // Match cutout silhouettes, with a small antialias band at the edge.
                half coverage = smoothstep(.35h, .65h, a) * _Opacity;
                clip(coverage - .01h);
                return half4(0, 0, 0, coverage);
            }
            ENDCG
        }
    }
    Fallback Off
}
