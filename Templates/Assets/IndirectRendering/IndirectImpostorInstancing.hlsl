#include "ShaderApiReflectionSupport.hlsl"

// Reflected-ноды для Shader Graph, дающие октаэдральный billboard-импостор (как рендерит
// AmplifyImpostors) поверх нашего GPU-driven инстансинга - тот же _InstanceBuffer/InstanceData,
// что и в IndirectInstancing.hlsl (одинаковый layout, побайтово совпадает с InstanceData.cs), так
// что импостор можно подставить как обычный меш+материал в InstanceBuffer/GpuIndirectCuller/
// IndirectRenderBatch без единой правки C# - например, как дальний LodLevel в
// IndirectMeshRenderer или как override-меш в TerrainTreeIndirectRenderer.
//
// Математика билборда и октаэдральных UV - порт AmplifyImpostors.cginc::OctaImpostorVertex,
// заменяющий unity_ObjectToWorld/WorldToObject (которых у RenderMeshIndirect-инстансов нет) на
// поворот/масштаб/позицию из _InstanceBuffer. Упрощённая версия: только полный октаэдр, без
// смещения пивота/атласа (_Offset/_AI_SizeOffset) и без depth-bias/hue variation трюков - если
// понадобится что-то из этого, добавляется отдельно.
//
// Меш для этого материала должен быть baked-квад AmplifyImpostors (тот же, что лежит в
// префабе, который создаёт их бейкер) - его вершины уже в нужном мировом масштабе, брать через
// GetComponentInChildren<MeshFilter>().sharedMesh, как и для деревьев/травы Terrain'а.
//
// Минимальный граф в Shader Graph - см. пошаговый список рядом с этим файлом.
//
// Без namespace - glslang падает на "::" (см. IndirectInstancing.hlsl).

struct IndirectImpostorInstanceData
{
    float3 position;
    float scale;
    float4 rotation;
};

StructuredBuffer<IndirectImpostorInstanceData> _InstanceBuffer;

float3 IndirectImpostorRotate(float3 v, float4 q)
{
    return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
}

float3 IndirectImpostorInverseRotate(float3 v, float4 q)
{
    return IndirectImpostorRotate(v, float4(-q.xyz, q.w));
}

float2 IndirectImpostorVectorToOctahedron(float3 N)
{
    N /= dot(1.0, abs(N));
    if (N.z <= 0)
        N.xy = (1 - abs(N.yx)) * (N.xy >= 0 ? 1.0 : -1.0);
    return N.xy;
}

float3 IndirectImpostorOctahedronToVector(float2 Oct)
{
    float3 N = float3(Oct, 1.0 - dot(1.0, abs(Oct)));
    if (N.z < 0)
        N.xy = (1 - abs(N.yx)) * (N.xy >= 0 ? 1.0 : -1.0);
    return normalize(N);
}

void IndirectImpostorRayPlaneIntersectionUV(float3 normal, float3 rayPosition, float3 rayDirection,
    out float2 uvs, out float3 localNormal)
{
    float lDotN = dot(rayDirection, normal);
    float p0l0DotN = dot(-rayPosition, normal);
    float t = p0l0DotN / lDotN;
    float3 p = rayDirection * t + rayPosition;

    float3 upVector = float3(0, 1, 0);
    float3 tangent = normalize(cross(upVector, normal) + float3(-0.001, 0, 0));
    float3 bitangent = cross(tangent, normal);

    uvs = -float2(dot(p, tangent), dot(p, bitangent));
    if (t <= 0.0) uvs = 0;

    float3x3 worldToLocal = float3x3(tangent, bitangent, normal);
    localNormal = normalize(mul(worldToLocal, rayDirection));
}

///<funchints>
///    <sg:ProviderKey>IndirectImpostor.ApplyImpostorInstanceTransform</sg:ProviderKey>
///    <sg:DisplayName>Apply Impostor Instance Transform</sg:DisplayName>
///    <sg:SearchCategory>Custom/Indirect Rendering</sg:SearchCategory>
///</funchints>
///<paramhints name = "positionOS">
///    <Position />
///    <Default>ObjectSpace</Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void ApplyImpostorInstanceTransform(
    float3 positionOS,
    float instanceID,
    float3 worldSpaceCameraPos,
    float framesPerAxis,   // = _Frames запечённого материала
    float impostorSize,    // = _ImpostorSize запечённого материала
    float parallaxScale,   // = _Parallax запечённого материала
    out float3 positionWS,
    out float4 uvFrame1,   // xy = масштаб параллакс-смещения, zw = базовый UV кадра
    out float4 uvFrame2,
    out float4 uvFrame3,
    out float3 blendWeights,
    out float4 instanceRotation) // для поворота сэмпла нормали во фрагменте - см. IndirectImpostor.shader
{
    // Полный октаэдр, без смещения пивота/атласа (_Offset/_AI_SizeOffset у большинства бейков и
    // так нулевые) - для hemi-октаэдра или ненулевого оффсета понадобится отдельный вариант ноды.
    IndirectImpostorInstanceData inst = _InstanceBuffer[(uint)instanceID];

    float prevFrame = framesPerAxis - 1;
    float3 fractions = 1.0 / float3(framesPerAxis, prevFrame, impostorSize);
    float fractionsFrame = fractions.x;
    float fractionsPrevFrame = fractions.y;
    float fractionsUVscale = fractions.z;
    float parallax = -parallaxScale; // см. AmplifyImpostors.cginc::OctaImpostorVertex

    // Позиция камеры в системе координат инстанса (вместо mul(unity_WorldToObject, ...)) -
    // повернуть на обратный кватернион и поделить на масштаб (масштаб у нас однородный).
    float3 instanceCameraPosition = IndirectImpostorInverseRotate(worldSpaceCameraPos - inst.position, inst.rotation)
        / max(inst.scale, 1e-5);
    float3 instanceCameraDirection = normalize(instanceCameraPosition);

    float3 upVector = float3(0, 1, 0);
    float3 horizontal = normalize(cross(instanceCameraDirection, upVector));
    float3 vertical = cross(horizontal, instanceCameraDirection);

    float3 billboard = horizontal * positionOS.x + vertical * positionOS.y;
    float3 rayDir = billboard - instanceCameraPosition;

    // Квантование - только для выбора кадра октаэдра, не для billboard-базиса (см. AmplifyImpostors.cginc).
    float3 quantizedCameraDirection = trunc(instanceCameraDirection * 65536.0) / 65536.0;
    float2 frameOcta = IndirectImpostorVectorToOctahedron(quantizedCameraDirection.xzy) * 0.5 + 0.5;

    float2 prevOctaFrame = frameOcta * prevFrame;
    float2 baseOctaFrame = floor(prevOctaFrame);
    float2 fractionOctaFrame = baseOctaFrame * fractionsFrame;

    // Кадр 1
    float2 octaFrame1 = (baseOctaFrame * fractionsPrevFrame) * 2.0 - 1.0;
    float3 octa1Dir = IndirectImpostorOctahedronToVector(octaFrame1).xzy;
    float2 uv1; float3 localNormal1;
    IndirectImpostorRayPlaneIntersectionUV(octa1Dir, instanceCameraPosition, rayDir, uv1, localNormal1);
    uvFrame1 = float4(localNormal1.xy * fractionsFrame * parallax, (uv1 * fractionsUVscale + 0.5) * fractionsFrame + fractionOctaFrame);

    // Кадр 2
    float2 fractPrevOctaFrame = frac(prevOctaFrame);
    float2 cornerDifference = lerp(float2(0, 1), float2(1, 0), saturate(ceil(fractPrevOctaFrame.x - fractPrevOctaFrame.y)));
    float2 octaFrame2 = ((baseOctaFrame + cornerDifference) * fractionsPrevFrame) * 2.0 - 1.0;
    float3 octa2Dir = IndirectImpostorOctahedronToVector(octaFrame2).xzy;
    float2 uv2; float3 localNormal2;
    IndirectImpostorRayPlaneIntersectionUV(octa2Dir, instanceCameraPosition, rayDir, uv2, localNormal2);
    uvFrame2 = float4(localNormal2.xy * fractionsFrame * parallax, (uv2 * fractionsUVscale + 0.5) * fractionsFrame + (cornerDifference * fractionsFrame + fractionOctaFrame));

    // Кадр 3
    float2 octaFrame3 = ((baseOctaFrame + 1) * fractionsPrevFrame) * 2.0 - 1.0;
    float3 octa3Dir = IndirectImpostorOctahedronToVector(octaFrame3).xzy;
    float2 uv3; float3 localNormal3;
    IndirectImpostorRayPlaneIntersectionUV(octa3Dir, instanceCameraPosition, rayDir, uv3, localNormal3);
    uvFrame3 = float4(localNormal3.xy * fractionsFrame * parallax, (uv3 * fractionsUVscale + 0.5) * fractionsFrame + (fractionOctaFrame + fractionsFrame));

    // Веса блендинга трёх кадров - те же, что и в AmplifyImpostors.cginc::OctaImpostorFragment.
    float2 fraction = frac(prevOctaFrame);
    float2 invFraction = 1 - fraction;
    blendWeights = float3(min(invFraction.x, invFraction.y), abs(fraction.x - fraction.y), min(fraction.x, fraction.y));

    positionWS = IndirectImpostorRotate(billboard, inst.rotation) * inst.scale + inst.position;
    instanceRotation = inst.rotation;
}

///<funchints>
///    <sg:ProviderKey>IndirectImpostor.ApplyImpostorParallax</sg:ProviderKey>
///    <sg:DisplayName>Apply Impostor Parallax</sg:DisplayName>
///    <sg:SearchCategory>Custom/Indirect Rendering</sg:SearchCategory>
///</funchints>
UNITY_EXPORT_REFLECTION
void ApplyImpostorParallax(float4 uvFrame, float depthSample, out float2 uv)
{
    uv = (0.5 - depthSample) * uvFrame.xy + uvFrame.zw;
}

///<funchints>
///    <sg:ProviderKey>IndirectImpostor.BlendImpostorFrames</sg:ProviderKey>
///    <sg:DisplayName>Blend Impostor Frames</sg:DisplayName>
///    <sg:SearchCategory>Custom/Indirect Rendering</sg:SearchCategory>
///</funchints>
UNITY_EXPORT_REFLECTION
void BlendImpostorFrames(float4 sample1, float4 sample2, float4 sample3, float3 blendWeights, out float4 blended)
{
    blended = sample1 * blendWeights.x + sample2 * blendWeights.y + sample3 * blendWeights.z;
}
