# GPU-culling через ComputeShader

Это реальный работающий пример, а не гипотетическое "можно было бы сделать так". Файл —
`IndirectCulling.compute`, включается галочкой `_useComputeCulling` + назначенным `_cullingShader`
(практика включения — [Быстрый старт](01-quickstart.html)).

## Идея

Вместо того чтобы CPU каждый кадр решал, что видно, а что нет (перебор тысяч инстансов на C# —
дорого и вообще не то, для чего затевался indirect-рендер), решение принимает GPU: compute shader
пробегает по всем инстансам параллельно, для каждого проверяет условие видимости и **сам** решает,
кому попасть в этот кадр. Результат — новый, более короткий список, и число элементов в нём CPU
даже не обязан знать.

## Три теста подряд, от дешёвого к дорогому, каждый отключаем отдельно

Compute shader делает не одну проверку, а три, последовательно — если инстанс не прошёл более
дешёвый тест, дорогой для него даже не считается. Ниже — реальный текущий код `CSMain`,
диагностические блоки (запись в `_Debug*`-буферы) убраны для читаемости — разбор в
[Диагностике](04-diagnostics.html):

```hlsl
#pragma kernel CSMain

struct InstanceData
{
    float3 position;
    float scale;
    float4 rotation;
};

StructuredBuffer<InstanceData> _AllInstances;
AppendStructuredBuffer<InstanceData> _VisibleInstances;
Texture2D<float> _CameraDepthTexture;

uint _InstanceCount;
float3 _CullOrigin;
float _CullRadius;
float _LodMinDistance;
float _MeshBoundsRadius;
float3 _MeshBoundsExtents;
float4x4 _ViewProjection;
float4x4 _PrevViewProj;
float2 _ScreenSize;
int _UsesReversedZ;
int _EnableDistance;
int _EnableFrustum;
int _EnableOcclusion;
int _InvertOcclusion;

[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint index = id.x;
    if (index >= _InstanceCount) return;

    InstanceData inst = _AllInstances[index];

    // 1. Дистанция - самый дешёвый тест первым. Диапазон, не просто верхняя граница -
    // нижняя (_LodMinDistance) нужна только при LOD-бакетах, иначе всегда 0.
    if (_EnableDistance != 0)
    {
        float dist = distance(inst.position, _CullOrigin);
        if (dist > _CullRadius || dist < _LodMinDistance) return;
    }

    // 2. Frustum - учитывает направление взгляда камеры.
    if (_EnableFrustum != 0)
    {
        float radius = _MeshBoundsRadius * inst.scale;
        if (!IsInsideFrustum(inst.position, radius)) return;
    }

    // 3. Occlusion - самый дорогой тест (чтение текстуры), поэтому последний.
    if (_EnableOcclusion != 0)
    {
        bool occluded = IsOccluded(inst.position, inst.scale, inst.rotation);
        if (_InvertOcclusion != 0) occluded = !occluded;
        if (occluded) return;
    }

    _VisibleInstances.Append(inst);
}
```

- **`StructuredBuffer<InstanceData> _AllInstances`** — тот же самый `_instances.Buffer` (реальный
  `GraphicsBuffer` внутри `InstanceBuffer`, см. [Архитектуру](02-architecture.html#1-instance-буфер-graphicsbuffertargetstructured)),
  что заполнял C# при расстановке. Здесь он только читается.
- **`AppendStructuredBuffer<InstanceData> _VisibleInstances`** — особый тип буфера с **встроенным
  счётчиком на GPU**. `.Append(x)` атомарно дописывает элемент в конец и увеличивает счётчик — это
  безопасно делать параллельно из тысяч потоков одновременно, GPU сам гарантирует, что потоки не
  затрут данные друг друга.
- **`[numthreads(64, 1, 1)]`** — размер одной группы потоков: 64 потока в ряд. Это не "64 инстанса
  видно", это "64 инстанса проверяются параллельно в одной группе" — число выбрано типичным для
  GPU (кратно размеру warp/wavefront на большинстве видеокарт), не имеет отношения к количеству
  видимых объектов.
- **`if (index >= _InstanceCount) return;`** — защита от переполнения: групп потоков всегда
  запускается с округлением вверх, поэтому последняя группа почти всегда "лишняя" сверх реального
  количества инстансов.
- **`_Enable*` галочки** — без возможности выключить дистанцию по отдельности, она глушила бы всё,
  что идёт после неё, и изолировать frustum/occlusion друг от друга было бы невозможно (бисекция
  "какая именно стадия виновата" требует уметь выключать их по одной).
- **`_LodMinDistance`** — если у вас нет LOD, всегда 0 и не влияет ни на что. Разбор — [LOD](06-lod.html).

## Тест 1: дистанция

```hlsl
if (_EnableDistance != 0)
{
    float dist = distance(inst.position, _CullOrigin);
    if (dist > _CullRadius || dist < _LodMinDistance) return;
}
```

Сфера с центром в `_CullOrigin` (позиция камеры). Инстанс внутри сферы — проходит дальше, снаружи —
сразу отбрасывается. Ничего не знает про направление взгляда камеры — используется просто как
дешёвый "не рисуем то, что очень далеко" пре-фильтр перед двумя следующими, более дорогими тестами.
При активном LOD этот же тест ещё и решает, в какой LOD-бакет попадёт инстанс — см. [LOD](06-lod.html).

## Тест 2: frustum culling

Реализация — через clip space напрямую, без построения 6 плоскостей пирамиды видимости. Референс —
`ellioman/Indirect-Rendering-With-Compute-Shaders`, проверенная и широко форкнутая реализация:

```hlsl
bool IsInsideFrustum(float3 worldPos, float radius)
{
    float4 clipPos = mul(_ViewProjection, float4(worldPos, 1.0));
    float w = clipPos.w + radius;

    bool inFront = w > 0.0;
    bool withinDepth = clipPos.z <= w;
    bool withinX = clipPos.x >= -w && clipPos.x <= w;
    bool withinY = clipPos.y >= -w && clipPos.y <= w;

    return inFront && withinDepth && withinX && withinY;
}
```

Точка внутри фрустума тогда и только тогда, когда `-w <= x <= w`, `-w <= y <= w`, `z <= w`, `w > 0`
(конвенция clip space Unity: `w` до перспективного деления равен view-space глубине в мировых
единицах). Поэтому `radius` (тоже в мировых единицах) можно просто прибавить к `w` как консервативный
запас на размер инстанса — размерности совпадают не случайно.

**Важная деталь**: `inFront` сравнивается с **расширенным** `w = clipPos.w + radius`, а не с сырым
`clipPos.w`. Если бы было наоборот — как только **пивот** крупного объекта (например, медведь,
вокруг/сквозь которого прошла камера) пересекал плоскость камеры (`clipPos.w <= 0`), весь инстанс
мгновенно пропадал бы — даже если передняя часть его геометрии, в пределах `radius` перед пивотом,
всё ещё была перед камерой и физически видна. Симптом такого бага: "объект пропадает ровно в момент,
когда его pivot/точка скрывается за камерой, хотя полигоны ещё видны".

`radius` для теста — не произвольное число, а `_MeshBoundsRadius * inst.scale`. `_MeshBoundsRadius`
берётся из `_boundsRadiusOverride`, если он `> 0`, иначе автоматически из
`_mesh.bounds.extents.magnitude`. Пересчитывается **каждый кадр**, не кэшируется при старте — чтобы
правки в инспекторе во время Play mode сразу доходили до реального GPU-теста.

C#-сторона передаёт саму `_ViewProjection` (без плоскостей):

```csharp
private Matrix4x4 BuildViewProjMatrix()
{
    Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(_targetCamera.projectionMatrix, true);
    return gpuProjection * _targetCamera.worldToCameraMatrix;
}
```

`GL.GetGPUProjectionMatrix` переводит `camera.projectionMatrix` (условная "Unity"-конвенция) в
реальную конвенцию конкретной GPU-платформы (D3D/Vulkan/Metal по-своему трактуют clip space) — без
этого перевода сравнение с настоящим аппаратным depth-буфером в occlusion-тесте будет систематически
неверным. Этот же метод строит и `_ViewProjection` для frustum-теста, и `_PrevViewProj` для occlusion
— единообразно, одной функцией, чтобы не рассинхронизировать две отдельные конвенции clip space в
одном файле.

## Тест 3: occlusion culling (по depth-буферу прошлого кадра)

Отбраковка того, что перекрыто другой геометрией. Реализован **самостоятельно**, не через фичу URP
(URP-шная версия работает только с `BatchRendererGroup` и `Graphics.RenderMeshIndirect` не видит
вообще). Идея та же: сравнение с depth-буфером **прошлого** кадра, а не текущего — на момент, когда
мы решаем, что рисовать в этом кадре, глубина этого же кадра ещё физически не существует.

```hlsl
bool IsPointOccluded(float3 worldPos)
{
    float4 clipPos = mul(_PrevViewProj, float4(worldPos, 1.0));

    float3 ndc = clipPos.xyz / clipPos.w;
    float2 uv = ndc.xy * 0.5 + 0.5;
    int2 pixelCoord = int2(uv * _ScreenSize);
    float sceneDepth = _CameraDepthTexture.Load(int3(pixelCoord, 0)).r;
    float instanceDepth = ndc.z;

    bool inFrontOfCamera = clipPos.w > 0.0;
    bool withinPrevFrame = uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0;

    bool occludedByDepth = _UsesReversedZ != 0 ? (instanceDepth < sceneDepth) : (instanceDepth > sceneDepth);

    return inFrontOfCamera && withinPrevFrame && occludedByDepth;
}
```

По шагам:

1. **`mul(_PrevViewProj, ...)`** — проецируем мировую позицию **из прошлого кадра**. `_PrevViewProj`
   сохраняется в конце предыдущего вызова culling'а, строится той же `BuildViewProjMatrix()`, что и выше.
2. **`inFrontOfCamera = clipPos.w > 0.0` → иначе считаем видимым** — инстанс был за камерой в прошлом
   кадре (появился только что / камера повернулась). Сравнивать не с чем — fail-open: лучше лишний
   раз нарисовать, чем ошибочно спрятать то, что реально видно.
3. **`withinPrevFrame`** — та же логика: точка была за пределами прошлого кадра по горизонтали/
   вертикали — тоже fail-open.
4. **`_CameraDepthTexture.Load(int3(pixelCoord, 0))`** — точечное чтение по целочисленным координатам
   пикселя, без сэмплера и фильтрации (`Load`, не `Sample`).
5. **Сравнение с учётом reversed-Z**: `SystemInfo.usesReversedZBuffer` говорит, в каком порядке
   хранится глубина на текущей платформе, передаётся флагом `_UsesReversedZ` — не зашито в код,
   иначе на другой платформе тест дал бы противоположный результат.

**Практическое наблюдение** (Vulkan/Linux/NVIDIA): при активном reversed-Z сырой `clipPos.z` **не**
растёт пропорционально глубине — он остаётся близко к near-плоскости почти на всей дистанции
(near-плоскость соответствует `z ≈ w`, far — `z ≈ 0`, и большая часть диапазона "спрессована" у 0 —
это специально для точности float, так работает reversed-Z). Маленькое `z` при большом `w` — это
нормально для reversed-Z, не аномалия, держите в голове при чтении диагностических чисел.

Occlusion-тест не одной точки, а **семи** — центр + 6 точек на гранях bounding box'а, повёрнутых и
отмасштабированных как сам инстанс:

```hlsl
bool IsOccluded(float3 center, float scale, float4 rotation)
{
    float3 extents = _MeshBoundsExtents * scale;

    float3 localOffsets[7] = {
        float3(0, 0, 0),
        float3(0, extents.y, 0), float3(0, -extents.y, 0),
        float3(extents.x, 0, 0), float3(-extents.x, 0, 0),
        float3(0, 0, extents.z), float3(0, 0, -extents.z)
    };

    [unroll]
    for (int i = 0; i < 7; i++)
    {
        float3 samplePos = center + RotateByQuaternion(localOffsets[i], rotation);
        if (!IsPointOccluded(samplePos)) return false; // хоть одна точка видна - инстанс видим
    }

    return true; // все 7 точек скрыты
}
```

Инстанс считается скрытым, только если скрыты **все 7** точек — одной видимой (например, спина
животного торчит над укрытием, а центр тела — нет) достаточно, чтобы не отбрасывать объект целиком.

**Важный нюанс, который стоит понимать, а не просто знать, что он есть**: точка `(0,0,0)` — это
центр/пивот инстанса, точка **внутри** тела меша. Для любого сплошного объекта центр почти всегда
физически "закрыт" собственной ближней поверхностью — это нормальная геометрия, самоокклюзия, а не
баг. Цикл это учитывает: раз центр не прошёл, проверка идёт к 6 точкам-граням. Но вся надёжность
теста в итоге зависит от того, **долетают ли эти 6 точек до настоящей поверхности меша**. Они
смещены на `_MeshBoundsExtents * scale` — и если `_boundsExtentsOverride` не соответствует реальным,
несимметричным пропорциям животного (например, равносторонний куб на явно вытянутое тело), часть или
все 6 точек могут остаться ВНУТРИ меша и тоже самоокклюзироваться — тест на практике начнёт себя
вести почти как "только центр", хотя код формально сэмплирует 7 точек. Симптом — необъяснимое
"мигание"/пропадание объектов, у которых `occludedCount` (см. [Диагностику](04-diagnostics.html))
стабильно 7/7 даже когда объект явно должен быть виден. Лечится настройкой `_boundsExtentsOverride`
под реальную форму меша.

C#-сторона — привязка текстуры и передача остальных параметров:

```csharp
_cullingShader.SetTextureFromGlobal(_cullingKernel, "_CameraDepthTexture", "_CameraDepthTexture");
_cullingShader.SetMatrix("_PrevViewProj", _prevViewProj);
_cullingShader.SetVector("_ScreenSize", new Vector2(_targetCamera.pixelWidth, _targetCamera.pixelHeight));
_cullingShader.SetInt("_UsesReversedZ", SystemInfo.usesReversedZBuffer ? 1 : 0);
```

**`SetTextureFromGlobal`, не `SetTexture`** — важное отличие именно для compute shader'ов. Обычные
материалы автоматически видят глобальные текстуры, заданные через `Shader.SetGlobalTexture`
(так URP публикует `_CameraDepthTexture` каждый кадр). Compute shader — нет, ему нужно явно сказать
"возьми вот эту глобальную текстуру и привяжи вот сюда". `_CameraDepthTexture` будет реально
существовать и обновляться только если у активного URP-ассета включена Depth Texture (в этом
проекте — `Assets/Settings/PC_RPAsset.asset`, `m_RequireDepthTexture: 1`, уже включено, трогать
не нужно).

## Известные ограничения occlusion-теста

- **`_MeshBoundsExtents` должен соответствовать реальным пропорциям меша**, не быть произвольным
  кубом — см. объяснение самоокклюзии центра выше. Это главный подозреваемый для ложных отбраковок
  occlusion-теста у крупных/близких объектов.
- **Глубина прошлого, не текущего кадра.** При резком повороте камеры на краю экрана возможны
  один-два кадра ложноположительных "не отброшен, хотя должен был бы" (fail-open по UV/за-камерой)
  — это осознанный компромисс в пользу "лучше лишний раз нарисовать".
- **`_boundsRadiusOverride`/`_boundsExtentsOverride` общие на все инстансы одного рендерера** — при
  LOD с разными мешами на разные уровни, у каждого бакета сейчас общий радиус/extents (от базового
  `_mesh`) — обычно нормально, т.к. LOD-мешы сохраняют силуэт, но держите в голове.
- **По умолчанию выключен** (`_enableOcclusionCulling = false`) именно из-за истории нестабильности
  выше — включайте осознанно и проверяйте через диагностику, а не как "ещё одну галочку до кучи".

## Критически важный нюанс: чей буфер читает шейдер рендера

Когда culling включён, `SV_InstanceID`/`Instance ID` в шейдере отрисовки нумерует место в буфере
**выживших** (после всех трёх тестов), а не в исходном `_instances.Buffer`. Выживших хранит
`GpuIndirectCuller.Level.VisibleBuffer` — свой на каждый LOD-уровень (или один, если LOD не
настроен, см. [LOD](06-lod.html)). Поэтому перед каждым draw call'ом привязка буфера к материалу
переключается — реальный код из `IndirectMeshRenderer.DispatchAndRenderWithCulling()`:

```csharp
foreach (GpuIndirectCuller.Level level in _culler.Levels)
{
    _propertyBlock.SetBuffer(InstanceBufferId, level.VisibleBuffer);
    RenderParams rparams = BuildRenderParams();
    rparams.forceMeshLod = level.ForceMeshLod;
    Graphics.RenderMeshIndirect(rparams, level.Mesh, level.CommandBuffer);
}
```

Если бы шейдер продолжал читать `_instances.Buffer` (весь список, до фильтрации) при уже
отфильтрованном `instanceCount`, на экране оказались бы просто первые N инстансов исходного
списка — не обязательно те, что реально прошли отбраковку. Шейдер сам не знает и не должен знать,
работает он с полным списком или с отфильтрованным — за него это решает то, какой буфер ему
подсунули с C#-стороны.

## Какой culling тут реально работает — а какой нет

Это принципиально важно не перепутать:

| Механизм | Реализован здесь? | Что учитывает |
|---|---|---|
| **Дистанционный culling** | ✅ Да | Только расстояние по прямой до `_CullOrigin` |
| **Frustum culling** | ✅ Да, собственная реализация | Направление взгляда камеры — тест напрямую в clip space (`-w<=x<=w`, `-w<=y<=w`, `z<=w`), не через 6 плоскостей |
| **Occlusion culling** (что перекрыто другой геометрией) | ✅ Да, собственная реализация, по depth прошлого кадра | Перекрытие другими объектами — 7 точек по объёму инстанса (центр + 6 граней bounding box'а), без Hi-Z |
| **GPU Occlusion Culling — фича URP** (GPU Resident Drawer → GPU Occlusion) | ❌ Не применима в принципе к этой технике | Отдельная встроенная в URP система через `BatchRendererGroup` — `Graphics.RenderMeshIndirect` идёт полностью в обход неё. Включить галочку "GPU Occlusion" в Renderer'е — не даст этим инстансам вообще ничего. Наша собственная occlusion-реализация выше — это НЕ она, а отдельный, самописный аналог той же идеи |
