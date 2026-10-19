Shader "Custom/IndirectCube"
{
    Properties
    {
        _Color ("Color", Color) = (0.2, 0.8, 1.0, 1.0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Layout обязан побайтово совпадать с InstanceData в InstanceData.cs.
            struct InstanceData
            {
                float3 position;
                float scale;
                float4 rotation; // кватернион (x, y, z, w)
            };

            StructuredBuffer<InstanceData> _InstanceBuffer;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                uint instanceID   : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            half4 _Color;

            // Стандартная формула поворота вектора кватернионом: v' = v + 2 * cross(q.xyz, cross(q.xyz, v) + q.w * v)
            float3 RotateByQuaternion(float3 v, float4 q)
            {
                return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
            }

            Varyings vert(Attributes IN)
            {
                InstanceData inst = _InstanceBuffer[IN.instanceID];
                float3 rotatedPositionOS = RotateByQuaternion(IN.positionOS, inst.rotation);
                float3 positionWS = rotatedPositionOS * inst.scale + inst.position;

                Varyings OUT;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.normalWS = RotateByQuaternion(IN.normalOS, inst.rotation);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                float ndotl = saturate(dot(normalize(IN.normalWS), lightDir)) * 0.5 + 0.5;
                return _Color * ndotl;
            }
            ENDHLSL
        }
    }
}
