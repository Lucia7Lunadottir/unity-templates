#include "ShaderApiReflectionSupport.hlsl"

// Reflected-нода для Shader Graph: та же математика, что и в IndirectCube.shader (vert()),
// но оформленная как отдельная HLSL-функция, которую Shader Graph сам подхватывает как ноду
// (Unity 6.5+, Shader Function Reflection API). StructuredBuffer здесь - обычный глобал файла,
// Shader Graph про него ничего не знает (это не Blackboard-property) - привязывается он так же,
// как и раньше, через MaterialPropertyBlock.SetBuffer(Shader.PropertyToID("_InstanceBuffer"), ...)
// на C#-стороне (см. IndirectMeshRenderer.cs/InstanceBuffer.cs).
//
// Без namespace: glslang (HLSL-фронтенд для Vulkan) не поддерживает "::" - в отличие от DXC
// (D3D12) он падает с "compilation terminated" на любом namespace-квалификаторе.

// Layout обязан побайтово совпадать с InstanceData в InstanceData.cs.
struct IndirectInstanceData
{
    float3 position;
    float scale;
    float4 rotation;
};

StructuredBuffer<IndirectInstanceData> _InstanceBuffer;

float3 IndirectRotateByQuaternion(float3 v, float4 q)
{
    return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
}

///<funchints>
///    <sg:ProviderKey>IndirectCube.ApplyInstanceTransform</sg:ProviderKey>
///    <sg:DisplayName>Apply Instance Transform</sg:DisplayName>
///    <sg:SearchCategory>Custom/Indirect Rendering</sg:SearchCategory>
///</funchints>
///<paramhints name = "positionOS">
///    <Position />
///    <Default>ObjectSpace</Default>
///</paramhints>
///<paramhints name = "normalOS">
///    <Normal />
///    <Default>ObjectSpace</Default>
///</paramhints>
///<paramhints name = "tangentOS">
///    <Tangent />
///    <Default>ObjectSpace</Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void ApplyInstanceTransform(
    float3 positionOS,
    float3 normalOS,
    float instanceID,
    out float3 positionWS,
    out float3 normalWS,
    float3 tangentOS,
    out float3 tangentWS)
{
    // Защита для обычных MeshRenderer / SkinnedMeshRenderer (и любых не-indirect отрисовок).
    // Размер привязанного буфера говорит, что именно к нему подключено:
    //  - глобальный identity-буфер (IndirectInstanceGlobalDefault) - 1 элемент;
    //  - реальный буфер индиректного батча - N элементов;
    //  - ничего не привязано - 0 элементов.
    // Если instanceID выходит за пределы буфера (например, у обычного MeshRenderer с GPU Instancing
    // instanceID может быть > 0, а в identity-буфере только элемент 0), читать нечего - отдаём
    // вход без изменений. Для обычного рендерера это правильный результат: Shader Graph дальше
    // сам применит unity_ObjectToWorld к "object space" позиции, как для любого другого вертекса.
    // SkinnedMeshRenderer тоже проходит этим путём: скининг уже выполнен до вертексного шейдера,
    // сюда приходит готовая деформированная позиция.
    uint instanceCount, instanceStride;
    _InstanceBuffer.GetDimensions(instanceCount, instanceStride);
    uint id = (uint)instanceID;

    [branch]
    if (id >= instanceCount)
    {
        positionWS = positionOS;
        normalWS = normalOS;
        tangentWS = tangentOS;
        return;
    }

    IndirectInstanceData inst = _InstanceBuffer[id];
    float3 rotatedPositionOS = IndirectRotateByQuaternion(positionOS, inst.rotation);
    positionWS = rotatedPositionOS * inst.scale + inst.position;
    normalWS = IndirectRotateByQuaternion(normalOS, inst.rotation);
    // Тангент не масштабируется и не смещается - те же правила, что для normalOS выше:
    // это направление, а не точка, инстансу нужен только его поворот.
    tangentWS = IndirectRotateByQuaternion(tangentOS, inst.rotation);
}
