// The HDRI sky (as Skybox/Cubemap: _Tex, _Tint, _Exposure, _Rotation) with the scene's fog colour below the horizon (#453, ADR-550).
// The pure-sky HDRI mirrors its clouds into the lower half; from high ground that half shows between the sea's far
// clip edge and the horizon as an upside-down sky. With fog on, everything below the horizon and a thin band above
// it goes to the fog colour, which is exactly what the sea fades to before the far clip: the two meet without an edge.
Shader "Game/WorldSky"
{
    Properties
    {
        _Tint("Tint Color", Color) = (0.5, 0.5, 0.5, 0.5)
        [Gamma] _Exposure("Exposure", Range(0, 8)) = 1.0
        _Rotation("Rotation", Range(0, 360)) = 0
        [NoScaleOffset] _Tex("Cubemap (HDR)", Cube) = "grey" {}
        _HorizonFog("Fog band above the horizon (view dir y)", Range(0.001, 0.5)) = 0.12
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

            TEXTURECUBE(_Tex);
            SAMPLER(sampler_Tex);
            half4 _Tex_HDR;
            half4 _Tint;
            half _Exposure;
            float _Rotation;
            half _HorizonFog;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 texcoord : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float a = radians(_Rotation);
                float s = sin(a), c = cos(a);
                float3 p = input.positionOS.xyz;
                // As Skybox/Cubemap: the sphere turns, the lookup does not (a turn about y keeps the horizon where it is).
                output.positionCS = TransformObjectToHClip(float3(c * p.x - s * p.z, p.y, s * p.x + c * p.z));
                output.texcoord = p;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 color = DecodeHDREnvironment(SAMPLE_TEXTURECUBE(_Tex, sampler_Tex, input.texcoord), _Tex_HDR);
                color *= _Tint.rgb * 4.59479380h * _Exposure; // unity_ColorSpaceDouble in linear space, as Skybox/Cubemap
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    half y = normalize(input.texcoord).y;
                    color = lerp(color, unity_FogColor.rgb, 1.0h - smoothstep(0.0h, _HorizonFog, y));
                #endif
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
