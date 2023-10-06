Shader "Custom/Buildings/BuildingLitManual"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "UniversalForward"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull Back
            Blend One Zero
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half2 texCoord0 : TEXCOORD0;
                half3 normalWS : TEXCOORD1;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                half3 _BaseColor;
                half4 _MainTex_TexelSize;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                half3 positionWS = TransformObjectToWorld(input.positionOS);
                half3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                output.normalWS = normalWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.texCoord0 = input.uv0;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half3 worldSpaceNormal = normalize(input.normalWS.xyz);

                half3 mainTexColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.texCoord0).rgb;

                half3 mainLightDir = half3(0.5h, 0.5h, -0.5h);

                half dotProduct = dot(mainLightDir, worldSpaceNormal);
                half3 diffuse = saturate(dotProduct * mainTexColor) * 1.5h;

                half3 ambient = mainTexColor * 0.3h;

                half3 color = diffuse + ambient;

                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
}
