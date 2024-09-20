Shader "Custom/Character/CharacterBodyUnlitDOTS-Manual"
{
    Properties
    {
        _BaseColor("BaseColor", Color) = (1, 1, 1, 1)
        [NoScaleOffset]_MainTex("MainTex", 2D) = "white" {}
        _ColorLerp("ColorLerp", Range(0, 1)) = 0
        _SkinMatrixIndex("Skin Matrix Index Offset", Int) = 0
        
        [Header(Shadow)]
        [Toggle(_RECEIVE_SHADOWS_OFF)] _ReceiveShadowOff("关闭接收阴影", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "SimpleLit"
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

            // Render State
            Cull Back
            Blend One Zero
            ZTest LEqual
            ZWrite On

            HLSLPROGRAM

            // Pragmas
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _ _RECEIVE_SHADOWS_OFF
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            // Includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                // float4 tangentOS : TANGENT;
                float4 uv0 : TEXCOORD0;

                float4 weights : BLENDWEIGHTS;
                int4 indices : BLENDINDICES;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 texCoord0 : TEXCOORD0;
                half3 normalWS : TEXCOORD1;

                float3 positionWS : TEXCOORD2;

                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && !defined(_RECEIVE_SHADOWS_OFF)
                    float4 shadowCoord  : TEXCOORD3;
                #endif
                
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // --------------------------------------------------
            // Graph

            // Graph Properties
            CBUFFER_START(UnityPerMaterial)
                int _SkinMatrixIndex;
                half3 _BaseColor;
                half4 _MainTex_TexelSize;
                half _ColorLerp;
            CBUFFER_END

            #if defined(DOTS_INSTANCING_ON)
                // DOTS instancing definitions
                UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                    UNITY_DOTS_INSTANCED_PROP(int, _SkinMatrixIndex)
                UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
                // DOTS instancing usage macros
                #define UNITY_ACCESS_HYBRID_INSTANCED_PROP(var, type) UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(type, var)
            #elif defined(UNITY_INSTANCING_ENABLED)
                // Unity instancing definitions
                UNITY_INSTANCING_BUFFER_START(SGPerInstanceData)
                    UNITY_DEFINE_INSTANCED_PROP(int, _SkinMatrixIndex)
                UNITY_INSTANCING_BUFFER_END(SGPerInstanceData)
                // Unity instancing usage macros
                #define UNITY_ACCESS_HYBRID_INSTANCED_PROP(var, type) UNITY_ACCESS_INSTANCED_PROP(SGPerInstanceData, var)
            #else
                #define UNITY_ACCESS_HYBRID_INSTANCED_PROP(var, type) var
            #endif

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // Graph Functions

            uniform ByteAddressBuffer _SkinMatrices;

            half3x4 LoadSkinMatrix(int index)
            {
                int offset = index * 48;
                half4 p1 = asfloat(_SkinMatrices.Load4(offset + 0 * 16));
                half4 p2 = asfloat(_SkinMatrices.Load4(offset + 1 * 16));
                half4 p3 = asfloat(_SkinMatrices.Load4(offset + 2 * 16));
                return half3x4(p1.x, p1.w, p2.z, p3.y,p1.y, p2.x, p2.w, p3.z,p1.z, p2.y, p3.x, p3.w);
            }

            void Unity_LinearBlendSkinning_float(int4 indices, half4 weights, half3 positionIn, half3 normalIn, out half3 positionOut, out half3 normalOut)
            {
                positionOut = 0;
                normalOut = 0;
                for (int i = 0; i < 4; ++i)
                {
                    int skinMatrixIndex = indices[i] + UNITY_ACCESS_HYBRID_INSTANCED_PROP(_SkinMatrixIndex, int);
                    half3x4 skinMatrix = LoadSkinMatrix(skinMatrixIndex);
                    half3 vtransformed = mul(skinMatrix, half4(positionIn, 1));
                    half3 ntransformed = mul(skinMatrix, half4(normalIn, 0));

                    positionOut += vtransformed * weights[i];
                    normalOut += ntransformed * weights[i];
                }
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
            #if defined(UNITY_DOTS_INSTANCING_ENABLED)
                half3 linearBlendSkinnedPosition = 0;
                half3 linearBlendSkinnedNormal = 0;
                //half3 linearBlendSkinnedTangent = 0;
                Unity_LinearBlendSkinning_float(input.indices, input.weights, input.positionOS, input.normalOS,
                    linearBlendSkinnedPosition, linearBlendSkinnedNormal);
            #else
                half3 linearBlendSkinnedPosition = input.positionOS;
                half3 linearBlendSkinnedNormal = input.normalOS;
                //half3 linearBlendSkinnedTangent = input.tangentOS.xyz;
            #endif

                half3 positionWS = TransformObjectToWorld(linearBlendSkinnedPosition);
                half3 normalWS = TransformObjectToWorldNormal(linearBlendSkinnedNormal);

                // 计算阴影坐标
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR) && !defined(_RECEIVE_SHADOWS_OFF)
                    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
                        output.shadowCoord = ComputeScreenPos(output.positionCS);
                    #else
                        output.shadowCoord = TransformWorldToShadowCoord(positionWS);
                    #endif
                #endif

                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.texCoord0 = input.uv0;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half3 normalWS = input.normalWS.xyz;

                // 计算阴影坐标
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                    half4 shadowCoord = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    half4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    half4 shadowCoord = half4(0, 0, 0, 0);
                #endif

                half3 mainTexColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.texCoord0.xy).rgb;
                mainTexColor = saturate(mainTexColor);

                #ifdef _RECEIVE_SHADOWS_OFF
                    Light mainLight = GetMainLight();
                    half dotProduct = dot(mainLight.direction, normalWS);
                    half3 diffuse = saturate(dotProduct * mainTexColor * mainLight.color);
                #else
                    Light mainLight = GetMainLight(shadowCoord);
                    const half3 attenuatedLightColor = mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                    const half3 diffuse = saturate(dot(mainLight.direction, normalWS)) * attenuatedLightColor * mainTexColor;
                #endif

                half3 ambient = mainTexColor * 0.4h;

                half3 color = diffuse + ambient;

                half3 finalColor = lerp(color, _BaseColor, _ColorLerp);

                return half4(finalColor, 1.0h);
            }

            ENDHLSL
        }
    }
    CustomEditor "UnityEditor.ShaderGraph.GenericShaderGraphMaterialGUI"
    FallBack "Hidden/Shader Graph/FallbackError"
}
