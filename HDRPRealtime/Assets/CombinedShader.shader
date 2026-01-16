Shader "Custom/HDRP/LucarioDissolveWobble"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.2, 0.6, 1, 1)

        _NoiseTex ("Noise Texture", 2D) = "white" {}
        _NoiseScale ("Noise Scale", Float) = 2.0
        _Dissolve ("Dissolve", Range(0,1)) = 0.0
        _EdgeWidth ("Edge Width", Range(0.001, 0.2)) = 0.05
        _EdgeColor ("Edge Color", Color) = (0.3, 0.9, 1.0, 1)

        _WobbleAmp ("Wobble Amplitude", Range(0,0.2)) = 0.03
        _WobbleFreq ("Wobble Frequency", Float) = 4.0
        _WobbleSpeed ("Wobble Speed", Float) = 2.0
        _MainTex ("Main Texture", 2D) = "white" {}

    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="HDRenderPipeline"
            "Queue"="AlphaTest"
            "RenderType"="TransparentCutout"
        }

        Pass
        {
            Name "Unlit"
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"


            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);


            float4 _BaseColor;
            float _NoiseScale;
            float _Dissolve;
            float _EdgeWidth;
            float4 _EdgeColor;

            float _WobbleAmp;
            float _WobbleFreq;
            float _WobbleSpeed;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert (Attributes IN)
            {
                Varyings OUT;

                float t = _TimeParameters.x * _WobbleSpeed;
                float wobble = sin(IN.positionOS.y * _WobbleFreq + t) * _WobbleAmp;
                float3 displaced = IN.positionOS + IN.normalOS * wobble;

                OUT.positionCS = TransformObjectToHClip(displaced);
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 Frag (Varyings IN) : SV_Target
            {
                float2 uv = IN.uv * _NoiseScale;
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv).r;

                float cut = noise - _Dissolve;
                clip(cut);

                float edge = saturate(1.0 - cut / max(_EdgeWidth, 1e-4));
                float4 baseCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float4 col = lerp(baseCol, _EdgeColor, edge);

                return col;
            }
            ENDHLSL
        }
    }
}
