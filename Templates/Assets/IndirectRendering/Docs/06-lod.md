# LOD: несколько уровней детализации по дистанции

Реализовано поверх той же архитектуры буферов — не новая система, а расширение существующей.
Требует включённого [GPU-culling](03-culling.html) (`_useComputeCulling`) — LOD-роутинг сделан
поверх Теста 1 (дистанция), без culling'а нет дешёвого способа решить на GPU, в какой бакет
детализации попадает инстанс.

## Идея в двух словах

`RenderParams.camera` мы уже видели — у `RenderParams` (Unity 6) есть ещё поле `forceMeshLod`,
которое заставляет `Graphics.RenderMeshIndirect` рисовать конкретный, явно указанный уровень
детализации меша. Ограничение: это поле — на весь `RenderParams`, то есть на весь draw call целиком,
**не на отдельный инстанс внутри него**. Один вызов `RenderMeshIndirect` = один уровень LOD для
ВСЕХ инстансов, которые он рисует.

Значит, чтобы разные инстансы одного рендерера рисовались с разной детализацией, нужно **несколько
отдельных draw call'ов** — по одному на уровень, каждый со своим списком "каких именно инстансов
сюда, в этот уровень, включить". Это ровно то, что уже даёт архитектура буферов из
[Три буфера, которые нужно понимать](02-architecture.html#три-буфера-которые-нужно-понимать):
свой `AppendStructuredBuffer` (список выживших) + свой буфер аргументов на каждый уровень, вместо
одного общего.

## Настройка: `_lodLevels`

Массив `LodLevel` в инспекторе, пустой по умолчанию (LOD выключен, поведение как раньше — один
`_mesh`, один draw call). Каждый элемент:

| Поле | Смысл |
|---|---|
| `maxDistance` | Инстанс попадает в этот уровень, если расстояние до камеры `<= maxDistance` (и больше `maxDistance` предыдущего уровня по порядку). Массив сортируется по возрастанию автоматически — порядок ввода в инспекторе не важен. |
| `meshOverride` | **Надёжный вариант.** Отдельный, самостоятельный `Mesh` для этого уровня — свой упрощённый low-poly меш и т.п. Если задан — `meshLodIndex` игнорируется. |
| `meshLodIndex` | Индекс встроенного Mesh LOD внутри базового `_mesh` (см. ниже), выбирается через `RenderParams.forceMeshLod`. Используется, только если `meshOverride` не задан. |

Последний уровень массива (с самым большим `maxDistance`) одновременно становится общим пределом
"дальше вообще не рисовать" — при активном LOD поле `_cullRadius` для самого культинга не
используется (каждый бакет получает свой диапазон дистанции вместо одного общего радиуса).

## Два способа получить меш для уровня

### Способ 1 — свой Mesh на уровень (`meshOverride`, рекомендуется)

Простой и предсказуемый: для каждого LOD-уровня — отдельный, полностью самостоятельный `Mesh`
asset (например, экспортированный из Blender с меньшим числом полигонов, или через любой LOD-
генератор). У каждого своя собственная геометрия (`indexCountPerInstance`/`startIndex`/
`baseVertexIndex` в буфере аргументов считаются с нуля, из ЭТОГО меша) — никакой зависимости от
того, как устроен базовый `_mesh`.

### Способ 2 — встроенный Mesh LOD (`meshLodIndex`)

Unity 6.5 умеет "запекать" несколько уровней детализации ПРЯМО В один Mesh asset — при импорте
модели включите **Model Import Settings → Mesh LODs → Generate mesh LODs**, Unity сам сгенерирует
упрощённые версии и сохранит их в индексном буфере того же меша. Тогда не нужны отдельные Mesh
asset'ы на каждый уровень — один `_mesh`, разные `meshLodIndex` на разных уровнях `_lodLevels`,
выбор конкретного LOD идёт через `RenderParams.forceMeshLod` при рендере этого бакета.

> **Обновление**: раньше здесь было предупреждение, что `IndirectDrawIndexedArgs.indexCountPerInstance`/
> `startIndex` считались по LOD0 (`mesh.GetIndexCount(0)`) независимо от `forceMeshLod` - и это
> действительно было багом. `Mesh.GetIndexCount(int, int)`/`GetIndexStart(int, int)` имеют
> перегрузку с явным `meshLod` (подтверждено по исходникам `UnityEngine.CoreModule` этой версии
> редактора) - `GpuIndirectCullerHelpers.BuildCommandBuffer` теперь передаёт `ForceMeshLod` туда же,
> так что индексный диапазон командного буфера соответствует тому LOD, который реально выберет
> `RenderParams.forceMeshLod` при отрисовке. `GetBaseVertex` LOD-перегрузки не имеет - не нужна,
> вершинный буфер общий на все LOD одного меша, меняется только диапазон индексов. Считаем путь
> надёжным; `meshOverride` (способ 1) остаётся более простым вариантом, если проще держать
> отдельные Mesh asset'ы, чем настраивать Mesh LOD при импорте.

## Как это устроено внутри

Сама логика LOD-бакетов живёт не в `IndirectMeshRenderer`, а в переиспользуемом
[`GpuIndirectCuller`](03-culling.html) — том же классе, что делает дистанцию/frustum/occlusion.
`IndirectMeshRenderer.BuildLevels()` только переводит Inspector-массив `LodLevel[]` в
GPU-agnostic `MeshLevel[]` (сортирует по дистанции, решает `meshOverride` или `meshLodIndex`):

```csharp
private MeshLevel[] BuildLevels()
{
    if (_lodLevels.Length == 0)
        return new[] { new MeshLevel { Mesh = _mesh, MaxDistance = _cullRadius, ForceMeshLod = 0 } };

    var sorted = (LodLevel[])_lodLevels.Clone();
    Array.Sort(sorted, (a, b) => a.maxDistance.CompareTo(b.maxDistance));

    var levels = new MeshLevel[sorted.Length];
    for (int i = 0; i < sorted.Length; i++)
    {
        bool useOverride = sorted[i].meshOverride != null;
        levels[i] = new MeshLevel
        {
            Mesh = useOverride ? sorted[i].meshOverride : _mesh,
            MaxDistance = sorted[i].maxDistance,
            ForceMeshLod = useOverride ? 0 : sorted[i].meshLodIndex
        };
    }
    return levels;
}
```

Этот массив передаётся в конструктор `GpuIndirectCuller`, который сам строит по одному
`Level` (публичная readonly-структура внутри `GpuIndirectCuller`) на каждый `MeshLevel` — со
своим `VisibleBuffer`/`CommandBuffer`:

```csharp
_levels = new Level[levels.Count];
float prevMax = 0f;
for (int i = 0; i < levels.Count; i++)
{
    MeshLevel level = levels[i];
    _levels[i] = new Level(level.Mesh, level.ForceMeshLod, prevMax, level.MaxDistance,
        new GraphicsBuffer(GraphicsBuffer.Target.Append, instanceCount, InstanceData.Stride),
        GpuIndirectCullerHelpers.BuildCommandBuffer(level.Mesh, instanceCount));
    prevMax = level.MaxDistance;
}
```

`GpuIndirectCuller.Dispatch()` при нескольких уровнях не делает один compute-dispatch, а по разу
на каждый `Level` — тот же самый kernel `CSMain`, но каждый раз с разными `_LodMinDistance`/`_CullRadius`:

```csharp
foreach (Level level in _levels)
    DispatchLevel(level, camera, settings, viewProjection);
```

Дистанция (Тест 1, самый дешёвый — см. [GPU-culling](03-culling.html#тест-1-дистанция)) в этой
конфигурации одновременно служит и culling'ом, и роутером — определяет, в какой конкретно уровень
попадёт инстанс. Дороже пути без LOD в N раз по инстансам (N = число уровней), но N обычно 2-4, а
инстансов на каждый отдельный уровень всегда МЕНЬШЕ полного `_instanceCount` (они не пересекаются).

Рендер — на стороне `IndirectMeshRenderer.DispatchAndRenderWithCulling()`, тоже по разу на уровень,
читая `_culler.Levels`:

```csharp
foreach (GpuIndirectCuller.Level level in _culler.Levels)
{
    _propertyBlock.SetBuffer(InstanceBufferId, level.VisibleBuffer);
    RenderParams rparams = BuildRenderParams();
    rparams.forceMeshLod = level.ForceMeshLod;
    Graphics.RenderMeshIndirect(rparams, level.Mesh, level.CommandBuffer);
}
```

Один и тот же `_propertyBlock` переиспользуется на все уровни — переключение буфера прямо перед
каждым `RenderMeshIndirect` безопасно: вызов синхронный по CPU-стороне, GPU видит property block
таким, каким он был в момент issue'а конкретного draw call'а.

## Требования и предупреждения

- **LOD требует `_useComputeCulling`.** Если `_lodLevels` заполнен, а culling не готов (нет
  `_cullingShader`/камеры/галочки) — LOD молча выключается, рисуется только базовый `_mesh`, в
  консоль пишется предупреждение.
- **LOD требует `_enableDistanceCulling`.** Если его выключить при активном LOD — все бакеты
  получат ВСЕ инстансы одновременно (наложение геометрии друг на друга), потому что дистанция — это
  и есть механизм роутинга по бакетам. Предупреждение в консоль при `OnEnable`.
- **`_boundsRadiusOverride`/`_boundsExtentsOverride` общие на все уровни** — считаются один раз от
  базового `_mesh`, не пересчитываются отдельно на `meshOverride`-мешы уровней. Обычно нормально
  (LOD-мешы сохраняют силуэт/пропорции оригинала), но если ваши "уровни" — это на деле совсем
  разные по форме proxy-объекты, radius/extents могут не подойти для дальних уровней.
- **Диагностика (`_debugInstanceIndex`) при LOD печатает данные последнего за кадр бакета**, не
  всех сразу — см. [Диагностика](04-diagnostics.html#_debuginstanceindex-построчный-разбор-одного-инстанса).
