// Trees, bushes, grass and flowers of the outdoor map with wind (#193). One shader for bark, leaf cards, billboards and
// grass: URP-lit shading (alpha-tested, both faces) plus a vertex sway. Everything bends downwind by its height above
// the plant's foot (squared, so the foot stays put), with a travelling gust wave from world position so a field
// ripples instead of nodding in step; leaf cards also flutter. Bark takes the same bend as the leaves above it, so
// nothing tears off its trunk. _GameWindBoost (global, set by AmbientBeds) raises the wind in rain and fog.
Shader "Game/WorldFoliage"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo (A = cut-out)", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap("Normal", 2D) = "bump" {}
        _BumpScale("Normal scale", Float) = 1
        _MaskMap("Mask (G occlusion, A smoothness)", 2D) = "white" {}
        _Smoothness("Smoothness", Range(0, 1)) = 0.1
        _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
        _WindHeight("Height at full sway (m, object space)", Float) = 12
        _WindBend("Downwind bend at that height (m)", Float) = 0.25
        _Flutter("Leaf flutter (m)", Float) = 0.04
        [Toggle(_MATTE)] _Matte("Matte (no highlights or reflections)", Float) = 1
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha clip", Float) = 1
        [Toggle(_NORMALMAP)] _UseNormal("Use normal map", Float) = 0
        [Toggle] _DryTint("Follow the ground's dry/lush tint (grass blades)", Float) = 0
        [HideInInspector] _Cull("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #pragma target 3.0
        #pragma shader_feature_local _MATTE
        #pragma shader_feature_local _ALPHATEST_ON
        #pragma shader_feature_local _NORMALMAP
        #pragma multi_compile_instancing
        #pragma instancing_options assumeuniformscaling

        #ifdef _MATTE
            #define _SPECULARHIGHLIGHTS_OFF
            #define _ENVIRONMENTREFLECTIONS_OFF
        #endif

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/_Project/Shaders/WorldGroundTint.hlsl"

        TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);
        TEXTURE2D(_MaskMap);  SAMPLER(sampler_MaskMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _DryTint;
            half _Smoothness;
            half _Cutoff;
            float _WindHeight;
            float _WindBend;
            float _Flutter;
        CBUFFER_END

        float _GameWindBoost; // global: 0 calm .. 1 storm
float4 _GameBends[4]; // global: someone walking through (world x, z, radius m, 0 = unused), set by GroundFx

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // The sway, applied to a world position. foot: the plant's origin in the world; height: the vertex's height
        // above it in object space.
        float3 Sway(float3 positionWS, float3 positionOS)
        {
            float3 foot = GetObjectToWorldMatrix()._m03_m13_m23;
            float h = saturate(positionOS.y / max(_WindHeight, 0.01));
            float strength = 1.0 + 1.6 * _GameWindBoost;
            float2 dir = float2(0.85, 0.53);
            float phase = dot(foot.xz, dir) * 0.11 - _Time.y * (1.25 + 0.5 * _GameWindBoost);
            // Two travelling waves and a steady lean downwind (gust is about 0.4 on average, never reversing far).
            float gust = 0.45 + 0.45 * sin(phase) + 0.25 * sin(phase * 2.7 + 1.3);
            positionWS.xz += dir * (gust * _WindBend * h * h * strength);
            // Walkers push low plants (grass, flowers, bushes) away from their feet and press them down (#205).
            float low = step(_WindHeight, 2.0);
            for (int b = 0; b < 4; b++)
            {
                float2 away = foot.xz - _GameBends[b].xy;
                float r = max(_GameBends[b].z, 0.01);
                float d = length(away);
                float push = saturate(1.0 - d / r) * step(0.01, _GameBends[b].z) * low;
                push *= push;
                positionWS.xz += away / max(d, 0.05) * (push * 0.5 * h);
                positionWS.y -= push * 0.3 * h * h;
            }
            // Flutter: small, quick, different for every vertex; stronger on the outer parts.
            float t = _Time.y * 4.6;
            float3 seed = positionOS * 3.7 + foot * 0.9;
            float3 jitter = float3(sin(t + seed.x + seed.z), sin(t * 1.17 + seed.y * 1.9), sin(t * 0.93 + seed.z + seed.x * 0.7));
            positionWS += jitter * (_Flutter * (0.35 + h) * strength);
            return positionWS;
        }

        half Opacity(float2 uv)
        {
            #ifdef _ALPHATEST_ON
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a;
            #else
                return 1.0h;
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                #ifdef _NORMALMAP
                    half4 tangentWS : TEXCOORD3;
                #endif
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 positionWS = Sway(TransformObjectToWorld(input.positionOS.xyz), input.positionOS.xyz);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.normalWS = nrm.normalWS;
                #ifdef _NORMALMAP
                    o.tangentWS = half4(nrm.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                #endif
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                #ifdef _ALPHATEST_ON
                    clip(base.a - _Cutoff);
                #endif
                half3 albedo = base.rgb * _BaseColor.rgb;
                if (_DryTint > 0.5h) albedo *= lerp(half3(0.85h, 1.0h, 0.9h), half3(1.45h, 1.3h, 0.6h), DryAmount(i.positionWS.xz, i.positionWS.y));
                half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, i.uv);

                half3 normalWS = i.normalWS;
                #ifdef _NORMALMAP
                    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                    half3 bitangent = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                    normalWS = TransformTangentToWorld(normalTS, half3x3(i.tangentWS.xyz, bitangent, i.normalWS));
                #endif
                normalWS = NormalizeNormalPerPixel(normalWS);

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

                half4 color = UniversalFragmentPBR(inputData, albedo, 0.0h, half3(0, 0, 0), _Smoothness * mask.a, mask.g, half3(0, 0, 0), 1.0h);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return half4(color.rgb, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 positionWS = Sway(TransformObjectToWorld(input.positionOS.xyz), input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 clip = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    clip.z = min(clip.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    clip.z = max(clip.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = clip;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #ifdef _ALPHATEST_ON
                    clip(Opacity(i.uv) - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformWorldToHClip(Sway(TransformObjectToWorld(input.positionOS.xyz), input.positionOS.xyz));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #ifdef _ALPHATEST_ON
                    clip(Opacity(i.uv) - _Cutoff);
                #endif
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformWorldToHClip(Sway(TransformObjectToWorld(input.positionOS.xyz), input.positionOS.xyz));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                #ifdef _ALPHATEST_ON
                    clip(Opacity(i.uv) - _Cutoff);
                #endif
                half3 n = NormalizeNormalPerPixel(i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    return half4(PackNormalOctQuadEncode(n) * 0.5 + 0.5, 0, 0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
