Shader "Mugirl/LanceMotionBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _MaskTex ("Recolor mask", 2D) = "black" {}
        _Color ("Primary color", Color) = (1,1,1,1)
        _ColorTwo ("Secondary color", Color) = (1,1,1,1)
        _HasMask ("Has mask", Float) = 0
        _Opacity ("Opacity", Float) = 0.8
    }
    SubShader
    {
        // 在原版人物前提交透明拖尾，当前帧身体与装备始终清晰。
        Tags { "Queue"="Transparent-100" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
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
            sampler2D _MainTex, _MaskTex;
            float4 _MainTex_ST, _MeshUvCenter, _UvRect, _BlurUV, _Travel, _PaddingU, _PaddingV;
            half4 _Color, _ColorTwo;
            half _Opacity, _HasMask;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v)
            {
                v2f o;
                float2 corner = sign(v.texcoord.xy - _MeshUvCenter.xy);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                world += corner.x * _PaddingU.xyz + corner.y * _PaddingV.xyz - _Travel.xyz * .5;
                world.y -= .005;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1));
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex)
                    + corner * abs(_BlurUV.xy) * .5 - _BlurUV.xy * .5;
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                float2 dx = ddx(i.uv), dy = ddy(i.uv);
                float4 accumulated = 0;
                float weightSum = 0;
                // 各横向色带使用不同尾长，形成不齐整的尖尾；同一条色带沿运动轴保持连续。
                float2 direction = normalize(_BlurUV.xy + float2(.000001, .000001));
                float lane = dot(i.uv, float2(-direction.y, direction.x)) * 32;
                float band = floor(lane);
                float blend = smoothstep(.2, .8, frac(lane));
                float noiseA = frac(sin(band * 127.1 + 17.7) * 43758.5453);
                float noiseB = frac(sin((band + 1) * 127.1 + 17.7) * 43758.5453);
                float reach = .4 + .6 * lerp(noiseA, noiseB, blend);
                // 对后方轨迹做透明度加权卷积，防止透明边缘变黑或出现多个人形轮廓。
                [unroll] for (int sampleIndex = 0; sampleIndex < 48; sampleIndex++)
                {
                    float t = (sampleIndex + .5) / 48;
                    float weight = 1 - .6 * t;
                    float2 uv = i.uv + _BlurUV.xy * t * reach;
                    float2 inside = step(_UvRect.xy, uv) * step(uv, _UvRect.zw);
                    half4 color = tex2Dgrad(_MainTex, clamp(uv, _UvRect.xy, _UvRect.zw), dx, dy);
                    half4 mask = 0;
                    if (_HasMask > .5) mask = tex2Dgrad(_MaskTex, clamp(uv, _UvRect.xy, _UvRect.zw), dx, dy);
                    half3 tint = lerp(_Color.rgb, lerp(half3(1,1,1), _Color.rgb, mask.r)
                        * lerp(half3(1,1,1), _ColorTwo.rgb, mask.g), _HasMask);
                    color.rgb *= tint;
                    color.a *= inside.x * inside.y * _Color.a;
                    accumulated.rgb += color.rgb * color.a * weight;
                    accumulated.a += color.a * weight;
                    weightSum += weight;
                }
                return half4(accumulated.rgb / max(accumulated.a, .00001), accumulated.a / weightSum * _Opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
