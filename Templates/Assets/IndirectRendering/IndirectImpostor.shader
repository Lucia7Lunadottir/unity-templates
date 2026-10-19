Shader "Custom/IndirectImpostor"
{
    // Свойства названы так же, как у AmplifyImpostors ( _Albedo/_Frames/_ImpostorSize/_ClipMask ) -
    // чтобы Material.CopyPropertiesFromMaterial(bakedMaterial) переносил числа и текстуру
    // автоматически, без ручного ввода. См. Editor/ImpostorMaterialConverter.cs.
    Properties
    {
        _Albedo("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _Normals("Normals (RGB) Depth (A)", 2D) = "white" {}
        _Specular("Specular (RGB) Smoothness (A)", 2D) = "black" {}
        _Occlusion("Occlusion (RGB)", 2D) = "white" {}
        _Emission("Emission (RGB)", 2D) = "black" {}
        _Frames("Frames", Float) = 16
        _ImpostorSize("Impostor Size", Float) = 1
        _ClipMask("Clip", Range(0, 1)) = 0.5
        _TextureBias("Texture Bias", Float) = -1
        _Parallax("Parallax", Range(-1, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PROBE_VOLUMES_L1 PROBE_VOLUMES_L2
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "IndirectImpostorInstancing.hlsl"

            TEXTURE2D(_Albedo);
            SAMPLER(sampler_Albedo);
            TEXTURE2D(_Normals);
            SAMPLER(sampler_Normals);
            TEXTURE2D(_Specular);
            SAMPLER(sampler_Specular);
            TEXTURE2D(_Occlusion);
            SAMPLER(sampler_Occlusion);
            TEXTURE2D(_Emission);
            SAMPLER(sampler_Emission);
            float _Frames;
            float _ImpostorSize;
            float _ClipMask;
            float _TextureBias;
            float _Parallax;

            struct Attributes
            {
                float3 positionOS : POSITION;
                uint instanceID   : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS     : SV_POSITION;
                float3 positionWS     : TEXCOORD0;
                float4 uvFrame1       : TEXCOORD1;
                float4 uvFrame2       : TEXCOORD2;
                float4 uvFrame3       : TEXCOORD3;
                float3 blendWeights   : TEXCOORD4;
                float4 instanceRotation : TEXCOORD5; // одинаков по всему инстансу - интерполяция не влияет
                float fogFactor       : TEXCOORD6;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS;
                ApplyImpostorInstanceTransform(IN.positionOS, (float)IN.instanceID, _WorldSpaceCameraPos,
                    _Frames, _ImpostorSize, _Parallax, positionWS, OUT.uvFrame1, OUT.uvFrame2, OUT.uvFrame3,
                    OUT.blendWeights, OUT.instanceRotation);
                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            float4 SampleImpostorBlended(TEXTURE2D_PARAM(tex, samp), float2 uv1, float2 uv2, float2 uv3, float3 weights, float bias)
            {
                half4 s1 = SAMPLE_TEXTURE2D_BIAS(tex, samp, uv1, bias);
                half4 s2 = SAMPLE_TEXTURE2D_BIAS(tex, samp, uv2, bias);
                half4 s3 = SAMPLE_TEXTURE2D_BIAS(tex, samp, uv3, bias);
                float4 blended;
                BlendImpostorFrames(s1, s2, s3, weights, blended);
                return blended;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Параллакс: глубина сэмплится из альфы Normals с фиксированным bias -1 (как у
                // AmplifyImpostors.cginc), масштаб смещения (_Parallax) уже запечён в uvFrame.xy.
                float depth1 = SAMPLE_TEXTURE2D_BIAS(_Normals, sampler_Normals, IN.uvFrame1.zw, -1).a;
                float depth2 = SAMPLE_TEXTURE2D_BIAS(_Normals, sampler_Normals, IN.uvFrame2.zw, -1).a;
                float depth3 = SAMPLE_TEXTURE2D_BIAS(_Normals, sampler_Normals, IN.uvFrame3.zw, -1).a;
                float2 uv1, uv2, uv3;
                ApplyImpostorParallax(IN.uvFrame1, depth1, uv1);
                ApplyImpostorParallax(IN.uvFrame2, depth2, uv2);
                ApplyImpostorParallax(IN.uvFrame3, depth3, uv3);

                float4 blendedAlbedo = SampleImpostorBlended(TEXTURE2D_ARGS(_Albedo, sampler_Albedo), uv1, uv2, uv3, IN.blendWeights, _TextureBias);
                clip(blendedAlbedo.a - _ClipMask);

                float4 blendedNormal = SampleImpostorBlended(TEXTURE2D_ARGS(_Normals, sampler_Normals), uv1, uv2, uv3, IN.blendWeights, _TextureBias);
                float3 objectNormal = blendedNormal.rgb * 2.0 - 1.0;
                float3 worldNormal = normalize(IndirectImpostorRotate(objectNormal, IN.instanceRotation));

                // _Specular.rgb = цвет блика, .a = smoothness; _Occlusion.g - как у AmplifyImpostors.cginc.
                float4 blendedSpecular = SampleImpostorBlended(TEXTURE2D_ARGS(_Specular, sampler_Specular), uv1, uv2, uv3, IN.blendWeights, _TextureBias);
                float occlusion = SampleImpostorBlended(TEXTURE2D_ARGS(_Occlusion, sampler_Occlusion), uv1, uv2, uv3, IN.blendWeights, _TextureBias).g;
                float3 emission = SampleImpostorBlended(TEXTURE2D_ARGS(_Emission, sampler_Emission), uv1, uv2, uv3, IN.blendWeights, _TextureBias).rgb;

                // Настоящий основной свет сцены (направление/цвет) вместо захардкоженного - раньше
                // импостор не реагировал ни на положение, ни на цвет, ни на интенсивность света.
                Light mainLight = GetMainLight();
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float3 halfDir = normalize(mainLight.direction + viewDir);

                float ndotl = saturate(dot(worldNormal, mainLight.direction));

                // Амбиент: APV (Adaptive Probe Volumes), если включён в проекте - сэмплится по
                // реальной мировой позиции инстанса (у каждого своя, в отличие от Light Probe ниже).
                // Без APV - обычный Light Probe через RenderParams.lightProbeUsage (см.
                // IndirectLightingSettings), но он общий на ВЕСЬ батч (интерполируется по
                // worldBounds батча, не по позиции конкретного инстанса) - грубее, но лучше, чем ничего.
                // Лайтмапы для GPU-инстансированных импосторов в принципе не подходят - у инстансов
                // нет своего UV2/слота в атласе, лайтмап печётся под конкретный статичный объект.
#if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
                half3 vertexSH = SampleSH(worldNormal); // нет отдельной "вершинной" нормали у биллборда
                float3 ambient = SampleProbeVolumePixel(vertexSH, GetAbsolutePositionWS(IN.positionWS),
                    worldNormal, viewDir, IN.positionCS.xy);
#else
                float3 ambient = SampleSH(worldNormal);
#endif
                float3 diffuse = blendedAlbedo.rgb * (mainLight.color * ndotl + ambient) * occlusion;

                float smoothness = blendedSpecular.a;
                float specTerm = pow(saturate(dot(worldNormal, halfDir)), smoothness * 64.0 + 1.0) * smoothness;
                float3 specularColor = blendedSpecular.rgb * mainLight.color * specTerm;

                float3 finalColor = diffuse + specularColor + emission;
                finalColor = MixFog(finalColor, IN.fogFactor);
                return half4(finalColor, blendedAlbedo.a);
            }
            ENDHLSL
        }
    }
}
