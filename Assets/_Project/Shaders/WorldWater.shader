// Sea and wetland water for the outdoor map (ADR-052). One transparent plane at sea level.
// The project keeps the camera depth and opaque textures off, so nothing here reads them: how deep the water is
// comes from a depth map baked from the terrain heights by WorldBuilder (R: 0..1 = 0.._DepthRange metres),
// sampled by world position. Two scrolling layers of a generated normal map, sky reflection by fresnel and a
// highlight from the main light.
Shader "Game/WorldWater"
{
    Properties
    {
        [NoScaleOffset] [Normal] _NormalMap("Wave normals", 2D) = "bump" {}
        [NoScaleOffset] _DepthMap("Water depth (R)", 2D) = "white" {}
        _MapSize("Map size (m)", Float) = 1024
        _DepthRange("Depth at R = 1 (m)", Float) = 4
        _ShallowColor("Shallow colour", Color) = (0.21, 0.30, 0.26, 1)
        _DeepColor("Deep colour", Color) = (0.05, 0.13, 0.17, 1)
        _DeepDepth("Depth of the deep colour (m)", Float) = 3
        _FadeDepth("Shore fade depth (m)", Float) = 0.3
        _ShallowAlpha("Opacity in the shallows", Range(0, 1)) = 0.5
        _DeepAlpha("Opacity when deep", Range(0, 1)) = 0.95
        _WaveA("Layer A: tile (m), speed x, speed z (m/s)", Vector) = (7, 0.30, 0.18, 0)
        _WaveB("Layer B: tile (m), speed x, speed z (m/s)", Vector) = (19, -0.16, 0.24, 0)
        _NormalStrength("Wave strength", Range(0, 2)) = 0.55
        _Reflection("Sky reflection", Range(0, 2)) = 0.9
        _SpecPower("Highlight sharpness", Float) = 220
        _SpecStrength("Highlight strength", Float) = 1.1
        _FoamColor("Shore foam (a = strength)", Color) = (0.80, 0.84, 0.82, 0.3)
        _FoamDepth("Shore foam depth (m)", Float) = 0.12
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_DepthMap);
            SAMPLER(sampler_DepthMap);

            CBUFFER_START(UnityPerMaterial)
                float _MapSize;
                float _DepthRange;
                half4 _ShallowColor;
                half4 _DeepColor;
                float _DeepDepth;
                float _FadeDepth;
                half _ShallowAlpha;
                half _DeepAlpha;
                float4 _WaveA;
                float4 _WaveB;
                half _NormalStrength;
                half _Reflection;
                half _SpecPower;
                half _SpecStrength;
                half4 _FoamColor;
                float _FoamDepth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 xz = input.positionWS.xz;

                // Two layers of the same tileable map moving in different directions: the pattern never repeats visibly.
                float2 uvA = (xz + _Time.y * _WaveA.yz) / max(_WaveA.x, 0.01);
                float2 uvB = (xz + _Time.y * _WaveB.yz) / max(_WaveB.x, 0.01);
                half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvA), _NormalStrength);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvB), _NormalStrength);
                // The plane is horizontal: tangent space (x, y, z) is world (x, z, y).
                half3 normalWS = normalize(half3(nA.x + nB.x, nA.z + nB.z, nA.y + nB.y));

                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                half nDotV = saturate(dot(normalWS, viewDirWS));
                half fresnel = 0.02h + 0.98h * pow(1.0h - nDotV, 5.0h);

                float depth = SAMPLE_TEXTURE2D(_DepthMap, sampler_DepthMap, xz / max(_MapSize, 1.0)).r * _DepthRange;
                half deep = saturate(depth / max(_DeepDepth, 0.01));

                Light mainLight = GetMainLight();
                half nDotL = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = SampleSH(normalWS) + mainLight.color * nDotL;
                half3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, deep) * lighting;

                half3 reflectDir = reflect(-viewDirWS, normalWS);
                reflectDir.y = abs(reflectDir.y); // a wave tilted away from the eye still mirrors the sky, not the sea bed
                half3 sky = GlossyEnvironmentReflection(reflectDir, 0.08h, 1.0h) * _Reflection;

                half3 halfDir = normalize(mainLight.direction + viewDirWS);
                half highlight = pow(saturate(dot(normalWS, halfDir)), _SpecPower) * _SpecStrength;

                half3 color = lerp(body, sky, fresnel) + mainLight.color * highlight;
                half shore = smoothstep(0.0, max(_FadeDepth, 0.001), depth);
                half alpha = shore * saturate(lerp(_ShallowAlpha, _DeepAlpha, deep) + fresnel + highlight);

                // A thin pale band where the water meets the ground, broken up by the waves.
                half foam = smoothstep(0.0, 0.03, depth) * (1.0h - smoothstep(_FoamDepth * 0.5, max(_FoamDepth, 0.001), depth));
                foam *= _FoamColor.a * saturate(0.6h + 2.0h * (nA.x + nB.y));
                color = lerp(color, _FoamColor.rgb * lighting, foam);
                alpha = max(alpha, foam);

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
