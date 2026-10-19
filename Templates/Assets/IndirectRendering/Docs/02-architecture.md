# Архитектура

Как это устроено внутри — буферы, зачем нужен MaterialPropertyBlock, как расставляются и двигаются
инстансы, путь одного инстанса от буфера до пикселя.

## Зачем вообще нужен RenderMeshIndirect

Обычный рендер в Unity — это одна пара (Transform, MeshRenderer) на один видимый объект. Даже
`Graphics.RenderMeshInstanced` требует, чтобы CPU каждый кадр знал точное количество инстансов
и передавал его в вызов явным числом.

`RenderMeshIndirect` убирает это ограничение: **сколько инстансов рисовать — решает не CPU, а GPU**,
через данные, которые лежат в буфере на видеокарте. CPU-код просто говорит "вот меш, вот буфер
с аргументами отрисовки, дуй" — и не знает (и не должен знать), сколько объектов реально нарисуется.

> **На этом факте строится GPU-driven culling**: compute shader сам решает, сколько инстансов
> видно, и переписывает `instanceCount` в буфере аргументов без единого readback на CPU. В этом
> проекте это реализовано, но **опционально** — включается галочкой `_useComputeCulling`, по
> умолчанию выключено. Без него `instanceCount` прописывается один раз руками и дальше не меняется
> — тоже рабочий вариант, просто без отбраковки невидимых инстансов. Полный разбор —
> [GPU-culling](03-culling.html).

## Синтаксис `Graphics.RenderMeshIndirect`

Прежде чем разбирать буферы под него — сама сигнатура метода (Unity Scripting API,
`UnityEngine.Graphics`):

```csharp
public static void RenderMeshIndirect(
    ref RenderParams rparams,
    Mesh mesh,
    GraphicsBuffer commandBuffer,
    int commandCount = 1,
    int startCommand = 0
);
```

| Параметр | Тип | Что это |
|---|---|---|
| `rparams` | `RenderParams` | Материал, bounds, тени, камера и т.д. — передаётся по `ref`, то есть метод МОЖЕТ изменить структуру на стороне вызывающего (на практике не делает, но это не `in`/`readonly`-параметр, помните об этом при чтении чужого кода) |
| `mesh` | `Mesh` | Какой меш рисовать — один на весь вызов, один и тот же для всех инстансов в нём |
| `commandBuffer` | `GraphicsBuffer` | Буфер аргументов отрисовки, `Target.IndirectArguments` — см. [ниже](#2-буфер-аргументов-отрисовки-graphicsbuffertargetindirectarguments) |
| `commandCount` | `int`, по умолчанию `1` | Сколько КОМАНД выполнить из `commandBuffer` за этот вызов — да, можно упаковать НЕСКОЛЬКО разных draw-команд в один буфер и запустить их все одним вызовом (в этом проекте всегда `1`: один вызов = одна команда = один меш) |
| `startCommand` | `int`, по умолчанию `0` | С какой команды в `commandBuffer` начать, если их там несколько |

Официальные правила/ограничения из документации Unity:

- **Работает только на платформах с поддержкой compute shader'ов** — если платформа их не
  поддерживает, метод недоступен в принципе, не только "работает медленно".
- **Формат `commandBuffer` завязан на структуру `GraphicsBuffer.IndirectDrawIndexedArgs`** — именно
  её поля (`indexCountPerInstance`, `instanceCount` и т.д., см.
  [Буфер аргументов](#2-буфер-аргументов-отрисовки-graphicsbuffertargetindirectarguments)) должны
  лежать в буфере в ожидаемом порядке — реальная бинарная раскладка может отличаться по платформам,
  поэтому обязательно собирайте буфер через саму структуру `IndirectDrawIndexedArgs`, а не руками
  считанными байтами.
- **`RenderParams.worldBounds`** — Unity использует его, чтобы отбраковать/отсортировать всю
  геометрию этого вызова КАК ОДНО ЦЕЛОЕ, до всякого GPU-culling'а внутри (см.
  [Известные грабли](05-gotchas.html#известные-грабли)).
- **Официальный шаблон вершинного шейдера** для этой техники — три штуки: `#define
  UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs`, `#include "UnityIndirect.cginc"`,
  `InitIndirectDrawArgs(0)` в начале вершинной функции. **В этом проекте их нет** — `IndirectCube.shader`
  читает `SV_InstanceID` напрямую и без них, и это реально работает. Разница в том, что этот проект
  всегда вызывает `RenderMeshIndirect` с `commandCount = 1` (одна команда за вызов, без упаковки
  нескольких draw'ов в один буфер) — судя по всему, официальный шаблон нужен именно для случая
  `commandCount > 1`, чтобы шейдер мог правильно посчитать номер инстанса С УЧЁТОМ того, из какой
  именно команды буфера он пришёл. Если когда-нибудь понадобится `commandCount > 1` — добавьте эти
  три строки и проверьте, меняется ли что-то в номерах инстансов.

## Синтаксис `RenderParams`

Первый параметр `RenderMeshIndirect` — `RenderParams` (`UnityEngine`, `struct`). У неё один
конструктор:

```csharp
public RenderParams(Material material);
```

Конструктор задаёт только `material` — все остальные поля структуры получают тип-значения-по-умолчанию
C# (`0`/`false`/`null` для соответствующих типов), и их нужно проставлять отдельно, через
object-initializer или присваиванием после создания:

```csharp
var rparams = new RenderParams(material)
{
    worldBounds = someBounds,
    camera = someCamera
    // остальные поля - по необходимости, ниже полный список
};
```

Полный список полей (Unity 6):

| Поле | Тип | Что это |
|---|---|---|
| `material` | `Material` | Материал для рендера — единственное, что задаёт конструктор |
| `worldBounds` | `Bounds` | Мировые границы геометрии — для culling/сортировки всего вызова как единого целого |
| `layer` | `int` | Layer объекта (как `GameObject.layer`) |
| `camera` | `Camera` | Какая камера рисует. `null` (значение по умолчанию!) — рисует **на все камеры разом**, см. [Известные грабли](05-gotchas.html#известные-грабли) |
| `shadowCastingMode` | `ShadowCastingMode` | Отбрасывает ли геометрия тени |
| `receiveShadows` | `bool` | Принимает ли геометрия тени от других объектов |
| `motionVectorMode` | `MotionVectorGenerationMode` | Режим генерации motion vectors (для TAA/motion blur и т.п.) |
| `reflectionProbeUsage` | `ReflectionProbeUsage` | Как использовать reflection probes |
| `lightProbeUsage` | `LightProbeUsage` | Как использовать light probes |
| `renderingLayerMask` | `uint` | Маска rendering layers (для URP/HDRP light layers и подобного) |
| `rendererPriority` | `int` | Приоритет рендерера |
| `instanceID` | `int` | ID для picking'а в Scene view редактора; `0` (по умолчанию) — объект нельзя выбрать кликом |
| `matProps` | `MaterialPropertyBlock` | Переопределения свойств материала на этот вызов — см. [MaterialPropertyBlock](#materialpropertyblock-зачем-он-тут) |
| `lightProbeProxyVolume` | `LightProbeProxyVolume` | LPPV-компонент, если используется |
| `forceMeshLod` | `int` | Принудительный уровень запечённого Mesh LOD — см. [LOD](06-lod.html) |
| `meshLodSelectionBias` | `float` | Смещение при автоматическом выборе Mesh LOD (не актуально здесь — этот проект всегда задаёт LOD явно через `forceMeshLod`, не полагается на автовыбор) |
| `overrideSceneCullingMask` / `sceneCullingMask` | `bool` / `ulong` | Только для редактора — переопределение маски видимости сцены |

**Что из этого реально используется в этом проекте** (`IndirectMeshRenderer.BuildRenderParams()`,
показан целиком в разделе [MaterialPropertyBlock](#materialpropertyblock-зачем-он-тут) ниже):
`material` (через конструктор), `worldBounds`, `shadowCastingMode`, `receiveShadows`, `matProps`,
`camera`, и `forceMeshLod` — но только в LOD-пути (см. [LOD](06-lod.html)), не в общем
`BuildRenderParams()`. Остальные поля не заданы явно — значит, используются их дефолтные
type-значения C# (`0`/`false` для соответствующих enum'ов и типов); если вам для своей задачи важно
конкретное поведение одного из них — проверьте, что означает конкретно `0`/`false` для его типа
(например, для `ShadowCastingMode` `0` — это `Off`), не полагайтесь на "наверное, разумно по
умолчанию".

## Три буфера, которые нужно понимать

### 1. Instance-буфер (`GraphicsBuffer.Target.Structured`)

Это ядро всей техники, и оно не специфично для этого проекта — в ЛЮБОМ вашем `RenderMeshIndirect`-коде
вам понадобится ровно то же самое: обычный `GraphicsBuffer` с типом `Structured`, куда с CPU кладётся
массив ваших собственных данных инстансов (позиция/масштаб/поворот — или что угодно ещё, это всегда
своя структура под вашу задачу):

```csharp
var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, stride);
buffer.SetData(data);
```

`data.Length` — сколько элементов. `stride` — размер ОДНОГО элемента в байтах (для структуры из
3 float на позицию + 1 float на масштаб + 4 float на кватернион поворота — `sizeof(float) * 8` =
32 байта). Буфер хранит их в GPU-памяти одним плоским куском, `data.Length * stride` байт всего.

**Это самый частый источник багов в этой механике**: HLSL (`StructuredBuffer<T>` в шейдере) и C#
(структура на CPU-стороне) описывают один и тот же байтовый layout полностью независимо друг от
друга — компилятор никак не проверяет, что они совпадают. Переставили поле местами в одном описании
и забыли во втором — получите тихо съехавшие/развёрнутые данные без единой ошибки компиляции или в
консоли. Поэтому на C#-структуре явно нужен `[StructLayout(LayoutKind.Sequential)]` — без него CLR
формально не обязан хранить поля в порядке объявления (на практике почти всегда хранит, но
полагаться на "почти всегда" в GPU-коде не стоит). В этом правиле нет исключений независимо от
проекта — держите в голове для любого своего буфера, не только `InstanceData`.

Конкретно (реальный код из `InstanceData.cs`):

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct InstanceData
{
    public Vector3 Position;   // байты 0..11  (3 float)
    public float Scale;        // байты 12..15 (1 float)
    public Quaternion Rotation; // байты 16..31 (4 float)
}
```

И зеркальная HLSL-структура — поля в ТОМ ЖЕ порядке, теми же по размеру типами:

```hlsl
struct InstanceData
{
    float3 position; // байты 0..11
    float scale;      // байты 12..15
    float4 rotation;  // байты 16..31
};
```

Если в HLSL-версии, например, поменять местами `scale` и `position` (или забыть один из них при
правке в будущем) — компиляция обеих сторон пройдёт без единой ошибки, но C# будет писать позицию
инстанса в байты 0..11, а шейдер будет читать эти же байты 0..11 как что-то другое (в примере выше
— начало `scale`, дальше поплывёт всё) — отсюда "тихо съехавшие данные" в описании ошибки: кубы
разлетаются по случайным местам без единого сообщения в консоли, потому что с точки зрения
компилятора всё синтаксически верно с обеих сторон.

**Как это организовано именно в этом проекте** (необязательно повторять в своём): владение этим
буфером + такая структура (`InstanceData { Vector3 Position; float Scale; Quaternion Rotation; }`,
`InstanceData.Stride = 32`) вынесены в отдельный переиспользуемый класс `InstanceBuffer`
(`InstanceBuffer.cs`) — не завязанный на `IndirectMeshRenderer`, конструктор которого делает ровно
два вызова выше:

```csharp
public InstanceBuffer(InstanceData[] data)
{
    _data = data;
    Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, InstanceData.Stride);
    Buffer.SetData(data);
}
```

> **Не перепутайте с `_InstanceBuffer`** (с подчёркиванием, внутри HLSL-кода) — это два РАЗНЫХ
> имени про разное: `InstanceBuffer` (класс без подчёркивания) — C#-обёртка вокруг `GraphicsBuffer`
> в этом проекте; `_InstanceBuffer` (с подчёркиванием) — имя свойства шейдера (строка, которую
> шейдер использует для чтения буфера, см. [Путь одного инстанса](#путь-одного-инстанса-от-буфера-до-пикселя)
> ниже). Они связаны (одно передаётся в другое через `MaterialPropertyBlock.SetBuffer`), но это не
> один и тот же идентификатор, и в другом проекте класс с таким именем может вообще не существовать
> — а вот имя шейдерного свойства вы вольны назвать как угодно, оно ни от чего не зависит.

**Это самый частый источник багов в этой механике**: HLSL (`StructuredBuffer<InstanceData>` в
шейдере) и C# (`struct InstanceData` в скрипте) описывают один и тот же байтовый layout полностью
независимо друг от друга — компилятор никак не проверяет, что они совпадают. Переставили поле
местами в одном описании и забыли во втором — получите тихо съехавшие/развёрнутые данные без единой
ошибки компиляции или в консоли. Поэтому на C#-структуре явно стоит
`[StructLayout(LayoutKind.Sequential)]` — без него CLR формально не обязан хранить поля в порядке
объявления (на практике почти всегда хранит, но полагаться на "почти всегда" в GPU-коде не стоит).

### 2. Буфер аргументов отрисовки (`GraphicsBuffer.Target.IndirectArguments`)

Тоже универсальная часть API, не специфичная для этого проекта. Это **не** данные инстансов — это
буквально аргументы вызова `DrawIndexedInstanced` на видеокарте, той самой low-level команды, которую
в обычном рендере Unity формирует сама, а тут вы обязаны собрать вручную:

```csharp
var buffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
    GraphicsBuffer.IndirectDrawIndexedArgs.size);
buffer.SetData(new[]
{
    new GraphicsBuffer.IndirectDrawIndexedArgs
    {
        indexCountPerInstance = mesh.GetIndexCount(0),
        instanceCount = (uint)instanceCount,
        startIndex = mesh.GetIndexStart(0),
        baseVertexIndex = mesh.GetBaseVertex(0),
        startInstance = 0
    }
});
```

| Поле | Смысл |
|---|---|
| `indexCountPerInstance` | сколько индексов рисовать на один инстанс (обычно = все индексы меша) |
| `instanceCount` | сколько инстансов рисовать. Без culling'а задаётся один раз с CPU и не меняется; с GPU-driven culling'ом — переписывается каждый кадр через `GraphicsBuffer.CopyCount` (см. [GPU-culling](03-culling.html)) |
| `startIndex` | смещение в index buffer меша (нужно для submesh'ей) |
| `baseVertexIndex` | смещение в vertex buffer меша |
| `startInstance` | с какого инстанса начинать (обычно 0) |

Один буфер аргументов = один draw call = одна пара (mesh, material). Для нескольких LOD-уровней
нужен свой такой буфер на каждый уровень — см. [LOD](06-lod.html).

**Как это организовано именно в этом проекте**: построение такого буфера вынесено в переиспользуемый
статический метод `GpuIndirectCullerHelpers.BuildCommandBuffer(Mesh mesh, int instanceCount)` (в
точности код выше, обёрнутый в функцию) — общий и для пути без culling'а, и для каждого LOD-уровня
отдельно.

### 3. Меш

Обычный `Mesh` — источник вершин/индексов, ничего специального. В демо, если поле `_mesh` не
назначено в инспекторе, скрипт сам временно создаёт `GameObject.CreatePrimitive(PrimitiveType.Cube)`,
забирает его `sharedMesh` и уничтожает временный объект — это разовая операция в `OnEnable`,
не имеет отношения к тому, что рисуется каждый кадр.

## MaterialPropertyBlock: зачем он тут

Изначально буфер привязывался через `_material.SetBuffer(...)`. Это работает, но ломается в
конкретный момент: если два `IndirectMeshRenderer` используют **один и тот же** Material asset,
`SetBuffer` перезаписывает шейдерное свойство на самом asset'е — и оба компонента будут рисовать
данные того, кто вызвал `SetBuffer` последним за кадр.

`MaterialPropertyBlock` — официальный механизм Unity для ровно этой ситуации: "один Material, но у
каждого вызова отрисовки свои значения свойств поверх него". Минимальный общий паттерн, без
привязки к конкретному проекту:

```csharp
var propertyBlock = new MaterialPropertyBlock();
propertyBlock.SetBuffer(Shader.PropertyToID("_InstanceBuffer"), yourGraphicsBuffer);

var rparams = new RenderParams(material) { matProps = propertyBlock };
Graphics.RenderMeshIndirect(rparams, mesh, commandBuffer);
```

В этом проекте те же самые вызовы разнесены по местам (кэш `Shader.PropertyToID` в статическое поле
`InstanceBufferId`, сборка `RenderParams` в приватный метод `BuildRenderParams()`), но суть
идентична. Вот он целиком, вместе с местом, откуда его реально вызывают (`IndirectMeshRenderer.cs`):

```csharp
private RenderParams BuildRenderParams() => new(_material)
{
    worldBounds = _bounds,
    shadowCastingMode = ShadowCastingMode.On,
    receiveShadows = true,
    matProps = _propertyBlock,
    camera = _restrictRenderToTargetCamera ? _targetCamera : null
};

private void Update()
{
    if (_culler != null)
    {
        DispatchAndRenderWithCulling(); // свой путь, с culling'ом - см. GPU-culling
        return;
    }

    Graphics.RenderMeshIndirect(BuildRenderParams(), _mesh, _legacyCommandBuffer);
}
```

Поля `RenderParams`, если не встречали их раньше: `worldBounds` — грубые границы для встроенного
Unity-culling'а (см. [Известные грабли](05-gotchas.html#известные-грабли), "Bounds всегда
приблизительные"), `shadowCastingMode`/`receiveShadows` — обычные настройки теней, `matProps` — тот
самый `MaterialPropertyBlock` с буфером, `camera` — кому конкретно рисовать (см.
[Камера: почему RenderMeshIndirect рисует "лишний раз"](05-gotchas.html#камера-почему-rendermeshindirect-рисует-лишний-раз)).
Путь БЕЗ culling'а (`_culler == null`) — самый короткий возможный вызов `RenderMeshIndirect` во всём
проекте, ради него стоит начинать читать код, если культинг вас пока не интересует.

Материал теперь общий (`_sharedDefaultMaterial`, статик на класс) — Unity не создаёт под капотом
`Material (Instance)`-копию, как это происходит при первом обращении к `renderer.material` в
обычном рабочем процессе. Property Block живёт на стороне компонента и переопределяет буфер только
для конкретного вызова отрисовки, материал остаётся один физический asset на сколько угодно
компонентов.

**Побочный эффект, который стоит держать в голове**: MaterialPropertyBlock ломает совместимость
с SRP Batcher (Unity не может закэшировать GPU-константы материала, если они переопределяются
per-draw). Для одного indirect-вызова на всю "поляну" объектов это не имеет значения — SRP Batcher
батчит *разные* материалы/объекты, а тут и так один draw call на всё.

## Как расставляются объекты

Вся расстановка происходит один раз в `GenerateInstanceData()` (вызывается из `OnEnable`), не
каждый кадр — позже данные просто лежат в GPU-буфере и никак не пересчитываются.

```csharp
var rng = new System.Random(12345);
for (int i = 0; i < _instanceCount; i++)
{
    Vector3 offset = new Vector3(
        (float)(rng.NextDouble() * 2.0 - 1.0) * _spread,
        0f,
        (float)(rng.NextDouble() * 2.0 - 1.0) * _spread);
    ...
    data[i].Position = transformHandle.position + offset;
}
```

По шагам:

- **Фиксированный seed (`12345`)** — расстановка детерминированная: при каждом Play кубы окажутся
  ровно в тех же местах. Это осознанный выбор для отладки (сравнивать кадр к кадру, не гадать,
  баг это или просто новая случайность), а не случайная константа. Хотите другую расстановку при
  каждом запуске — замените на `new System.Random()` без аргумента (тогда seed берётся из системных
  часов).
- **Область разброса** — квадрат `2*_spread × 2*_spread` по осям X/Z с центром в
  `transformHandle.position`, `Y` всегда `0` относительно этого центра (кубы стоят на одной
  плоскости, не летают и не проваливаются).
- **`transformHandle.position`** — это позиция GameObject'а, на котором висит компонент, но
  прочитанная через `TransformHandle` (Unity 6.3+, полноценно с 6.5), а не через классический
  `Transform`. `TransformHandle` — unmanaged struct, совместимый с Burst, с тем же набором операций
  (`position`, `rotation`, `TransformPoint`, `GetChild` и т.д.), но без накладных расходов managed
  API. Практическое следствие: **центр "поляны" кубов берётся из позиции GameObject'а, но только
  один раз, в момент запуска** (см. следующий раздел про то, что будет, если подвинуть объект уже
  во время Play).
- **Масштаб** — `Mathf.Lerp(_minScale, _maxScale, random 0..1)`, равномерно по всем трём осям
  (кубы остаются кубами, не вытягиваются).

### Как это заменить на реальные данные (например, деревья террейна)

Вся генерация — случайные позиции/масштаб/поворот — живёт **только** внутри цикла в
`GenerateInstanceData()`. Всё остальное в системе (буферы, culling, шейдер) работает с готовым
`InstanceData[]` и понятия не имеет, откуда взялись числа — случайные они или настоящие.

Значит, чтобы вместо случайной "поляны" рисовать, например, реальные деревья террейна, меняется
**только** тело цикла — не буферы, не шейдер, не culling:

```csharp
TerrainData terrainData = terrain.terrainData;
var data = new InstanceData[terrainData.treeInstanceCount];
for (int i = 0; i < terrainData.treeInstanceCount; i++)
{
    TreeInstance tree = terrainData.GetTreeInstance(i);
    // TreeInstance.position - нормализованные 0..1 координаты внутри террейна, не мировые
    Vector3 worldPos = terrain.transform.position + Vector3.Scale(tree.position, terrainData.size);

    data[i] = new InstanceData
    {
        Position = worldPos,
        Scale = tree.widthScale, // или heightScale - зависит от того, что для вас "масштаб"
        Rotation = Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f)
    };
}
```

Это набросок направления, не готовый компонент — `TreeInstance.rotation` в Unity хранится в
радианах (не в градусах, как остальной API), а `widthScale`/`heightScale` — это два разных числа,
не одно, так что для настоящего дерева вам почти наверняка понадобится не `float Scale`, а
`Vector3 Scale` в `InstanceData` (и, соответственно, в HLSL-структуре — не забудьте про правило
про совпадающий layout из раздела про instance-буфер). Но сама архитектура — C# заполняет массив,
остальное не меняется — рассчитана именно на такую замену источника данных.

## Как менять позицию инстансов — общая техника, не два конкретных метода

Вся расстановка из раздела выше происходит **один раз**, в `OnEnable` → `GenerateInstanceData()`.
После этого позиции лежат в GPU-буфере и никак сами не пересчитываются. Отсюда частый источник
путаницы: **если подвинуть GameObject со скриптом во время Play, кубы за ним не последуют** —
`transformHandle.position` был прочитан один раз, в момент создания буфера, и всё.

Важно понимать не просто "какие два метода тут есть", а откуда они вообще берутся: раз данные
инстанса — это байты в `GraphicsBuffer`, "подвинуть инстанс" технически всегда означает "записать в
буфер новые байты по нужному смещению". Дальше — вопрос ОТКУДА эти байты берутся и КАК ЧАСТО
записываются, и вариантов ровно три, каждый со своим честным компромиссом. Ниже — все три, не
только те два, что реализованы в демо-компоненте.

### Техника 1: точечная запись с CPU (`GraphicsBuffer.SetData`, один элемент)

```csharp
GraphicsBuffer.SetData(data, index, index, 1);
```

Сигнатура `SetData(T[] source, int sourceStart, int destStart, int count)` — "возьми `count`
элементов из массива `source`, начиная с `sourceStart`, и запиши их в буфер на GPU, начиная со
смещения `destStart`".

Почему это реально трогает только один элемент, а не весь буфер — дело в том, что сам GPU-буфер это
плоский кусок байт, без понятия "элемент" внутри себя (это понятие есть только в вашем C#/HLSL-коде,
через `stride`). `SetData` переводит (индекс, count) в конкретный **диапазон байт** и копирует ровно
его, ничего больше. Для этого проекта `stride = 32` байта (`InstanceData.Stride`), значит вызов
`SetData(data, 42, 42, 1)` копирует ровно байты `[42*32, 43*32)` = `[1344, 1376)` — 32 байта из
позиции 42 массива `data` в те же 32 байта буфера на GPU. Байты до `1344` и после `1376` в этом
вызове вообще не упоминаются — ни читаются, ни перезаписываются, ни сравниваются, GPU-драйвер их
просто не видит в рамках этого конкретного вызова. Поэтому "не трогает остальные `_instanceCount - 1`
инстансов" — не оптимизация "под капотом", а прямое следствие того, ЧТО именно вы попросили скопировать.

**Когда уместно**: событийные, нечастые изменения одного-двух инстансов — игрок взаимодействовал с
конкретным объектом, скрипт телепортировал одно животное. **Когда НЕ уместно**: если вам нужно
двигать ТЫСЯЧИ инстансов каждый кадр — да, технически можно вызвать это в цикле по всем инстансам
каждый `Update()`, но тогда вы получаете тысячи отдельных вызовов `SetData` за кадр, каждый со своим
API-оверхедом на CPU-стороне — то, ради чего вообще затевался GPU-driven рендер (минимум работы на
CPU), при таком использовании теряется. В этом проекте цепочка вызовов — `IndirectMeshRenderer.
SetInstancePosition(index, pos)` → `InstanceBuffer.SetPosition(index, pos)` (в `InstanceBuffer.cs`)
→ вот этот самый `Buffer.SetData(_data, index, index, 1)` — три строчки, ни одна не делает больше,
чем описано выше, для нечастых точечных правок.

### Техника 2: полная перезапись с CPU (`GraphicsBuffer.SetData`, весь массив)

```csharp
GraphicsBuffer.SetData(data); // весь InstanceData[] целиком
```

Без смещений — весь массив разом. **Когда уместно**: редкое, но массовое изменение — все инстансы
сразу должны сдвинуться/пересчитаться (например, "поляна" целиком переехала вслед за объектом).
**Когда НЕ уместно**: каждый кадр для большого `_instanceCount` — весь буфер перезаливается целиком,
и перед этим CPU должен сначала в цикле на C#-стороне пересчитать все элементы массива — двойная
работа, которую GPU-driven архитектура как раз должна была устранить. Цепочка вызовов в этом проекте
— `IndirectMeshRenderer.RecenterToTransform()` → `InstanceBuffer.Offset(delta)` → `Buffer.SetData(_data)`
целиком — намеренно НЕ вызывается сама по себе каждый кадр, только по явному разовому событию.

> После `RecenterToTransform()` не забывайте, что `RenderParams.worldBounds` тоже пересчитывается
> (`_bounds.center` обновляется внутри метода) — иначе Unity может отбраковать всю "поляну" по
> старым, неактуальным границам.

### Техника 3: движение прямо на GPU, без участия CPU вообще

Это техника, которой в демо-компоненте этого проекта НЕТ, но именно она — правильный ответ на
"как двигать тысячи инстансов каждый кадр" в духе всей этой архитектуры: **не гонять данные через
CPU каждый кадр вообще**, а посчитать новые позиции ПРЯМО на GPU, отдельным compute shader'ом,
который читает И ПИШЕТ тот же самый `_instances.Buffer` (реальный `GraphicsBuffer` внутри `InstanceBuffer`) на месте.

Разница с culling-шейдером ([GPU-culling](03-culling.html)) в одной детали объявления буфера:

```hlsl
// Culling-шейдер ТОЛЬКО читает исходные данные:
StructuredBuffer<InstanceData> _AllInstances;

// Шейдер движения ЧИТАЕТ И ПИШЕТ один и тот же буфер:
RWStructuredBuffer<InstanceData> _AllInstances;
```

`RWStructuredBuffer` (read-write) — то же самое хранилище, но с правом записи. Дальше — обычный
compute shader, по потоку на инстанс, который вычисляет новую позицию и **пишет её обратно в тот же
элемент буфера**, откуда только что прочитал:

```hlsl
#pragma kernel CSMove

RWStructuredBuffer<InstanceData> _AllInstances;
uint _InstanceCount;
float _DeltaTime;
float _WindSpeed;

[numthreads(64, 1, 1)]
void CSMove(uint3 id : SV_DispatchThreadID)
{
    uint index = id.x;
    if (index >= _InstanceCount) return;

    InstanceData inst = _AllInstances[index];
    inst.position.x += _WindSpeed * _DeltaTime; // любая логика движения - ветер, гравитация, путь
    _AllInstances[index] = inst;
}
```

C#-сторона каждый кадр передаёт только несколько чисел (не данные инстансов!) и один раз диспатчит:

```csharp
_movementShader.SetBuffer(_moveKernel, "_AllInstances", _instances.Buffer);
_movementShader.SetInt("_InstanceCount", _instanceCount);
_movementShader.SetFloat("_DeltaTime", Time.deltaTime);
_movementShader.SetFloat("_WindSpeed", _windSpeed);
_movementShader.Dispatch(_moveKernel, Mathf.CeilToInt(_instanceCount / 64f), 1, 1);
```

CPU тут не трогает ни один байт данных инстансов — ни разу не читает и не пишет `InstanceData[]` на
своей стороне. Вся тысяча (или миллион) обновлений позиций считается параллельно на GPU, за один
dispatch. Это и есть тот самый принцип "GPU решает, а не CPU", который уже применён к отбраковке
([GPU-culling](03-culling.html)) — применённый теперь к самому движению.

**Практические следствия, если будете писать такой шейдер сами**:
- Один и тот же `GraphicsBuffer` (`_instances.Buffer`) можно спокойно передавать И в шейдер движения
  (как `RWStructuredBuffer`, на запись), И в culling-шейдер (как `StructuredBuffer`, на чтение) — это
  один физический буфер на GPU, разные шейдеры просто по-разному объявляют доступ к нему.
- Порядок в `Update()` имеет значение: если движение должно повлиять на то, что попадёт в этот же
  кадр culling'а — диспатчите шейдер движения ДО диспатча culling'а, не после.
- Никакой синхронной блокировки/`GetData()` тут не нужно (в отличие от диагностики в
  [Диагностике](04-diagnostics.html)) — данные остаются на GPU от записи до следующего чтения тем же
  или другим шейдером, CPU в этот обмен не вовлечён вообще.

### Как выбрать технику

| Сценарий | Техника |
|---|---|
| Одно/несколько событийных изменений в кадр (не каждый кадр) | 1 — точечная запись |
| Редкое массовое изменение всех сразу (не каждый кадр) | 2 — полная перезапись |
| Много инстансов, движение каждый кадр (анимация, физика, ветер, путь) | 3 — шейдер движения на GPU |

### Откуда брать индекс инстанса, и как на него посмотреть

Все техники выше требуют индекс — `SetInstancePosition(index, ...)`, `_AllInstances[index]` в
шейдере. Индекс — это НЕ идентичность/имя инстанса, ничего похожего на GUID или ссылку на объект.
Это буквально "какая по счёту позиция в массиве" — число от `0` до `_instanceCount - 1`, и только
то, что вы сами туда положили в этой позиции, там и лежит.

**Если вы сами создали инстанс** — индекс у вас уже есть, это была переменная цикла в момент
создания (`for (int i = 0; i < _instanceCount; i++) data[i] = ...`). Дальше просто сохраните это
число там, где храните остальное состояние вашей игровой сущности — например, в обычном C#-классе
"мой логический медведь" (не `GameObject` — в этой технике инстансы физически не являются
GameObject'ами, значит и обвязку для них нужно писать свою, тем же принципом "без GameObject на
каждый инстанс"):

```csharp
public class BearAgent
{
    public int InstanceIndex; // индекс в GPU-буфере IndirectMeshRenderer
    public float Health;
    // ... остальное состояние вашей игровой логики
}
```

**Если индекс неизвестен заранее** — например, нужно найти "ближайшего к точке инстанса" или
"инстанс под курсором" — у инстансов нет `Collider`'ов (GameObject'ов нет физически), поэтому
`Physics.Raycast` их не увидит. Для небольшого/умеренного количества инстансов (десятки тысяч,
поиск не каждый кадр, а по событию — клик, редкий AI-запрос) достаточно обычного линейного перебора
по CPU-стороне буфера:

```csharp
int FindClosestIndex(InstanceBuffer instances, Vector3 point)
{
    int closest = -1;
    float closestDistSq = float.MaxValue;
    for (int i = 0; i < instances.Count; i++)
    {
        float distSq = (instances.Get(i).Position - point).sqrMagnitude;
        if (distSq < closestDistSq) { closestDistSq = distSq; closest = i; }
    }
    return closest;
}
```

Если такой поиск нужен каждый кадр и инстансов реально много (сотни тысяч+) — линейный перебор на
CPU станет узким местом, и понадобится собственная пространственная структура (сетка/grid, kd-tree)
или GPU-picking (рендер индексов вместо цвета в отдельный буфер, чтение под курсором) — за рамками
этого демо, но это тот случай, когда наивный перебор перестаёт годиться.

**Посмотреть, что реально лежит по конкретному индексу** — `InstanceBuffer.Get(index)` возвращает
данные без обращения к GPU (буфер хранит копию массива на CPU-стороне специально для этого). Но
здесь есть ловушка, если вы используете Технику 3 (движение на GPU): та CPU-копия заполняется только
при создании буфера/через `SetPosition`/`Offset` — если позиции меняются ПРЯМО НА GPU через свой
compute shader, CPU-копия про это ничего не узнаёт и начинает врать. Для честного чтения текущих
данных в этом случае нужен настоящий readback (`GraphicsBuffer.GetData`, синхронный, с задержкой
кадра-двух) — тот же приём, что уже используется для диагностики culling'а, см.
[Диагностику](04-diagnostics.html).

**Посмотреть индексы визуально, в Scene view** — уже есть гизмо `_debugShowAllFrustumStatus`
([Диагностика](04-diagnostics.html)), но по умолчанию подписывает индексом только КРАСНЫЕ
(отбракованные) сферы — иначе тысяча подписей друг на друге нечитаема. Если нужно увидеть индекс
ЛЮБОГО инстанса, а не только отбракованных — в `IndirectMeshRenderer.OnDrawGizmos()` уберите условие
`if (!inside)` перед вызовом `UnityEditor.Handles.Label(...)`, оставив только сам вызов (учитывайте,
что подписи тысяч инстансов сразу забьют Scene view — включайте на небольшом `_instanceCount` или
временно).

## Поворот инстансов

Добавлен `Quaternion Rotation` в `InstanceData` — поворот вокруг вертикальной оси (Y), диапазон
настраивается полями `_minYRotation`/`_maxYRotation` (по умолчанию 0°..360°, то есть полностью
случайный поворот на месте):

```csharp
float yAngle = Mathf.Lerp(_minYRotation, _maxYRotation, (float)rng.NextDouble());
data[i].Rotation = Quaternion.Euler(0f, yAngle, 0f);
```

Почему именно Y, а не произвольный 3D-поворот: это стандартный паттерн для "объектов, стоящих на
земле" (деревья, камни, трава) — им не нужно заваливаться на бок, только поворачиваться вокруг
вертикали. Сам механизм ниже — универсальный кватернион, так что при желании ничто не мешает
генерировать `Quaternion.Euler(x, y, z)` с произвольными углами по всем осям.

В шейдере кватернион нужно применить к вершине **до** масштабирования и переноса, а также отдельно
к нормали (иначе освещение будет вести себя так, будто куб не повернулся):

```hlsl
float3 RotateByQuaternion(float3 v, float4 q)
{
    return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
}

float3 rotatedPositionOS = RotateByQuaternion(IN.positionOS, inst.rotation);
float3 positionWS = rotatedPositionOS * inst.scale + inst.position;
OUT.normalWS = RotateByQuaternion(IN.normalOS, inst.rotation);
```

Это стандартная формула поворота вектора кватернионом (без перехода через матрицу 3×3) — дешевле
на GPU, чем собирать полную rotation-матрицу ради одного умножения.

## Путь одного инстанса от буфера до пикселя

1. GPU получает команду "нарисуй `instanceCount` копий меша" из буфера аргументов.
2. Для инстанса номер `i` в vertex shader аппаратно устанавливается `SV_InstanceID = i` — это
   чистый Direct3D/Vulkan механизм, он не имеет отношения к Unity-макросам вроде
   `UNITY_SETUP_INSTANCE_ID` (те нужны только для *классического* чекбокса "Enable GPU Instancing"
   и работают иначе — через per-instance-property массивы, а не произвольный буфер).
3. Шейдер читает `_InstanceBuffer[IN.instanceID]` — получает позицию, масштаб и поворот именно
   этого инстанса.
4. Вершина поворачивается кватернионом, масштабируется и сдвигается на мировую позицию инстанса —
   всё вручную, в самом шейдере.
5. `TransformWorldToHClip(positionWS)` — сразу переводим в clip space, **минуя** `unity_ObjectToWorld`.

Пункт 5 — специфика ручного indirect-рендера: у вызова `RenderMeshIndirect` нет привязанного
`Transform`, поэтому `unity_ObjectToWorld` для него не имеет смысла (в этом демо он не используется
вообще внутри шейдера). Расстановка кубов относительно `transformHandle.position` (см. раздел выше)
происходит на CPU при заполнении буфера, а не через стандартный механизм трансформации в шейдере —
это два разных места, не перепутайте при отладке.
