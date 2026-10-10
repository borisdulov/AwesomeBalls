// World-space triplanar grid for greybox geometry: readable scale and speed at any object size.
Shader "AwesomeBalls/ProtoGrid"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.5, 0.5, 0.5, 1)
        _LineColor ("Line Color", Color) = (0, 0, 0, 0.35)
        _GridSize ("Grid Size (m)", Float) = 1
        _LineWidth ("Line Width (m)", Range(0, 0.2)) = 0.03
        _MajorEvery ("Major Line Every N", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _LineColor;
                float _GridSize;
                float _LineWidth;
                float _MajorEvery;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float GridLines(float2 uv, float size, float width)
            {
                float2 coord = uv / size;
                float2 dist = abs(frac(coord - 0.5) - 0.5);
                float2 aa = fwidth(coord);
                float2 halfWidth = 0.5 * width / size;
                float2 lines = 1.0 - smoothstep(halfWidth - aa, halfWidth + aa, dist);
                return max(lines.x, lines.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 an = abs(n);
                float2 uv = an.y >= an.x && an.y >= an.z ? i.positionWS.xz
                          : an.x >= an.z ? i.positionWS.zy
                          : i.positionWS.xy;

                float minor = GridLines(uv, _GridSize, _LineWidth) * _LineColor.a;
                float major = GridLines(uv, _GridSize * _MajorEvery, _LineWidth * 2.0) * saturate(_LineColor.a * 2.0);
                half3 albedo = lerp(_BaseColor.rgb, _LineColor.rgb, max(minor, major));

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = saturate(dot(n, light.direction));
                half3 color = albedo * (light.color * ndl * light.shadowAttenuation + SampleSH(n));
                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
