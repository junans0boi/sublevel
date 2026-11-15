// Road strips of the outdoor map (#192). URP-lit ground with alpha: the edge is ragged and fades into the terrain's
// own shoulder, asphalt gets worn tracks, repair patches, cracks, dirt creeping in at the edge and (on wide roads)
// worn centre and edge lines. Everything but the texture lookups is procedural from world position, so no road
// repeats. The mesh's second UV set carries: x = across (-1 left edge .. 1 right edge), y = metres along the road,
// z = 1 when the road carries lines, w = half width in metres.
Shader "Game/WorldRoad"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap("Normal", 2D) = "bump" {}
        _BumpScale("Normal scale", Float) = 1
        _MaskMap("Mask (R metallic, G occlusion, A smoothness)", 2D) = "white" {}
        _Smoothness("Smoothness", Range(0, 1)) = 0.5
        _Paved("Paved (1) or dirt track (0)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-100" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MaskMap);  SAMPLER(sampler_MaskMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale;
                half _Smoothness;
                half _Paved;
            CBUFFER_END
            float _GameWet; // global: 0 dry .. 1 soaked, set by GroundFx (#207)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 road : TEXCOORD1;
                float2 ends : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 road : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half3 normalWS : TEXCOORD3;
                half4 tangentWS : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                float2 ends : TEXCOORD6;
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.road = input.road;
                o.ends = input.ends;
                o.normalWS = nrm.normalWS;
                o.tangentWS = half4(nrm.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 w = i.positionWS.xz;
                float across = i.road.x, along = i.road.y, halfWidth = max(i.road.w, 0.5);
                float e = abs(across);                    // 0 on the centre line .. 1 at the edge
                float metres = across * halfWidth;
                float edgeNoise = Noise(w / 2.1) * 0.55 + Noise(w / 6.5 + 9.7) * 0.45;
                bool paved = _Paved > 0.5;

                // Ragged edge: the strip is cut away by noise and fades out over a short band.
                float cut = paved ? 0.03 + 0.08 * edgeNoise : 0.05 + 0.28 * edgeNoise;
                float soft = paved ? 0.09 : 0.45;
                half alpha = (half)saturate(((1.0 - e) - cut) / soft);
                // A track fades out over its last few metres instead of ending in a straight cut.
                alpha *= (half)saturate(min(i.ends.x, i.ends.y) / (paved ? 0.5 : 3.0));
                clip(alpha - 0.004h);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                // A second, larger and turned sample hides the repeat of the texture along a long road.
                float2 uv2 = float2(i.uv.x * 0.8 - i.uv.y * 0.6, i.uv.x * 0.6 + i.uv.y * 0.8) * 0.41 + 0.17;
                albedo = lerp(albedo, SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv2).rgb * _BaseColor.rgb, 0.45h * (half)smoothstep(0.3, 0.7, Noise(w / 15.0 + 4.1)));
                half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, i.uv);
                half smoothness = mask.a * _Smoothness;
                half3 dirtColour = half3(0.20h, 0.16h, 0.11h); // linear

                if (paved)
                {
                    // Wheel tracks: slightly darker and smoother where the tyres run.
                    float track = exp(-pow((e - 0.36) / 0.13, 2.0));
                    albedo *= 1.0h - 0.14h * (half)track;
                    // Repair patches and a few cracks.
                    float patch = smoothstep(0.64, 0.68, Noise(w / 5.5 + 7.3)) * smoothstep(0.2, 0.5, Noise(w / 30.0));
                    albedo *= 1.0h - 0.3h * (half)patch;
                    smoothness *= 1.0h - 0.4h * (half)patch;
                    float2 jag = w * 0.9 + (float2(Noise(w * 3.3), Noise(w * 3.3 + 17.0)) - 0.5) * 0.9;
                    float crack = 1.0 - smoothstep(0.0, 0.011, abs(Noise(jag + 3.1) - 0.5));
                    crack *= smoothstep(0.62, 0.8, Noise(w / 9.0 + 21.0));
                    albedo *= 1.0h - 0.4h * (half)crack;
                    // Dirt and sand creeping in from the verge.
                    float verge = saturate((e - 0.62 + edgeNoise * 0.22) / 0.3);
                    albedo = lerp(albedo, dirtColour, (half)(verge * 0.65));
                    smoothness *= 1.0h - 0.7h * (half)verge;

                    // Worn lines on the wider roads: dashed centre, solid edge lines, each broken up by wear.
                    if (i.road.z > 0.5)
                    {
                        float centre = 1.0 - smoothstep(0.07, 0.11, abs(metres));
                        centre *= step(frac(along / 8.0), 0.45);
                        float edgeLine = 1.0 - smoothstep(0.06, 0.1, abs(abs(metres) - (halfWidth - 0.5)));
                        float worn = smoothstep(0.3, 0.6, Noise(w * 1.4 + 2.2) * 0.7 + Noise(w * 6.0) * 0.3);
                        float paint = max(centre, edgeLine) * worn;
                        half3 colour = centre > edgeLine ? half3(0.74h, 0.66h, 0.36h) : half3(0.72h, 0.72h, 0.68h);
                        albedo = lerp(albedo, colour, (half)(paint * 0.8));
                    }
                }
                else
                {
                    // Dirt track: two rutted tracks and grass-green tufts creeping in at the sides.
                    float ruts = exp(-pow((e - 0.4) / 0.14, 2.0));
                    albedo *= 1.0h - 0.1h * (half)ruts;
                    float grassy = saturate((e - 0.55 + edgeNoise * 0.3) / 0.35);
                    albedo = lerp(albedo, half3(0.07h, 0.09h, 0.035h), (half)(grassy * 0.45));
                }

                // Rain: darker, glossier, and puddles in the worn hollows of the lane (#207).
                half wet = (half)_GameWet;
                half pud = wet * (half)(paved ? smoothstep(0.58, 0.68, Noise(w / 3.5 + 13.1)) : smoothstep(0.5, 0.62, Noise(w / 2.5 + 5.7)));
                albedo *= lerp(1.0h, 0.6h, wet) * lerp(1.0h, 0.5h, pud);
                smoothness = lerp(max(smoothness, wet * (paved ? 0.7h : 0.45h)), 0.96h, pud);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                normalTS = normalize(lerp(normalTS, half3(0.0h, 0.0h, 1.0h), pud));
                half3 bitangent = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(i.tangentWS.xyz, bitangent, i.normalWS)));

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogFactor);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, albedo, 0.0h, half3(0, 0, 0), smoothness, mask.g, half3(0, 0, 0), 1.0h);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
