# ComputeShader: как писать свои

Это не разбор `IndirectCulling.compute` (тот разбор — на странице [GPU-culling](03-culling.html)).
Это отдельная страница про саму технологию: как устроен `.compute`-файл, что означают все эти
`[numthreads]`/`SV_DispatchThreadID`/`Dispatch`, какие типы буферов бывают — чтобы вы могли написать
СВОЙ compute shader с нуля, а не только читать чужой. Если "Основы" ([00-basics.html](00-basics.html))
ещё не читали — сначала туда, там объясняется, чем compute shader принципиально отличается от
обычного (рисующего) шейдера.

## Анатомия `.compute`-файла

```hlsl
#pragma kernel CSMain

RWStructuredBuffer<float> _Numbers;

[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    _Numbers[id.x] = _Numbers[id.x] * 2.0;
}
```

По кусочкам:

- **`#pragma kernel CSMain`** — объявляет, что функция `CSMain` ниже это **kernel** — отдельная
  входная точка, которую можно вызвать (dispatch'нуть) из C#. В одном `.compute`-файле может быть
  несколько kernel'ов (несколько `#pragma kernel` + несколько функций) — например, отдельно "тест
  видимости" и отдельно "движение", в одном файле, вызываемые независимо.
- **`RWStructuredBuffer<float> _Numbers;`** — объявление буфера на уровне файла (аналог полей класса
  в C#) — это "переменная", в которую C#-сторона подставит конкретный `GraphicsBuffer` перед
  запуском. Разбор типов буферов — [ниже](#какие-буферы-можно-объявлять).
- **`[numthreads(64, 1, 1)]`** — говорит GPU "запускай эту функцию ГРУППАМИ по 64 потока в ряд" (два
  других числа — Y и Z, здесь не используются, `1` значит "не задействовано"). Подробнее —
  [ниже](#потоки-группы-потоков-и-numthreads).
- **`void CSMain(uint3 id : SV_DispatchThreadID)`** — сама функция. `SV_DispatchThreadID` — это
  "семантика" (специальное имя, которое GPU понимает без вашего участия) — сюда GPU автоматически
  подставляет уникальный номер потока, который выполняет именно эту копию функции. Ваш код читает
  `id.x` (а если бы `numthreads` было двумерным/трёхмерным — ещё и `id.y`/`id.z`) и понимает, какую
  именно порцию данных ему обрабатывать.

## Потоки, группы потоков, и `[numthreads]`

GPU не запускает функцию "просто миллион раз подряд" — потоки организованы в **группы** фиксированного
размера, и размер группы (`numthreads`) вы задаёте сами, в самом коде шейдера (это должно быть
известно на этапе компиляции — нельзя передать его как переменную из C#).

`[numthreads(64, 1, 1)]` значит: каждая группа — это 64 потока подряд по оси X (и по одному по Y/Z —
то есть фактически одномерная группа). Число `64` не случайно: оно кратно размеру warp/wavefront
(блока потоков, которые физически выполняются GPU синхронно, обычно 32 на NVIDIA или 64 на AMD) — на
большинстве видеокарт это эффективный размер группы. `32` или `256` тоже частые практические выборы;
`numthreads(1,1,1)` технически скомпилируется, но future ГПУ будет использовать одно ядро за раз —
крайне неэффективно.

C# запускает не потоки напрямую, а **группы** потоков:

```csharp
_shader.Dispatch(kernel, threadGroupsX, threadGroupsY, threadGroupsZ);
```

Итоговое число запущенных потоков по оси X = `threadGroupsX * numthreads.x`. Отсюда — практический
паттерн "обработать N элементов массива", который использует весь этот проект:

```csharp
int threadGroups = Mathf.CeilToInt(_instanceCount / 64f);
_shader.Dispatch(kernel, threadGroups, 1, 1);
```

С округлением ВВЕРХ (`CeilToInt`), не вниз — потому что `_instanceCount` почти никогда не делится
на 64 без остатка. Пример с реальными числами: `_instanceCount = 1000`. `1000 / 64 = 15.625`,
округляем вверх до `16` групп. Итого запускается `16 * 64 = 1024` потока — на **24 больше**, чем
реальных инстансов. Эти лишние 24 потока получат `id.x` от `1000` до `1023` — индексы, которых в
массиве не существует. Поэтому **первая строка тела любого такого kernel'а всегда должна быть
защитой от переполнения**:

```hlsl
uint index = id.x;
if (index >= _InstanceCount) return;
```

Без неё лишние потоки читали/писали бы память за пределами буфера — неопределённое поведение,
не всегда сразу заметный краш.

## Какие буферы можно объявлять

| Тип | Смысл | Пример использования в этом проекте |
|---|---|---|
| `StructuredBuffer<T>` | Только чтение | `_AllInstances` в culling-шейдере — читает, никогда не пишет |
| `RWStructuredBuffer<T>` | Чтение и запись | Понадобится для [Техники 3 движения на GPU](02-architecture.html#техника-3-движение-прямо-на-gpu-без-участия-cpu-вообще) - шейдер читает старую позицию и пишет новую в тот же элемент |
| `AppendStructuredBuffer<T>` | Только запись, со встроенным атомарным счётчиком, `.Append(x)` дописывает в конец | `_VisibleInstances` - безопасно из тысяч потоков одновременно, GPU сам гарантирует отсутствие гонок |
| `ConsumeStructuredBuffer<T>` | Обратное Append - `.Consume()` атомарно забирает и удаляет последний элемент | Не используется в этом проекте, но существует как парный к Append тип - полезен для очередей задач на GPU |
| `Texture2D<T>` | Чтение текстуры | `_CameraDepthTexture` - читается через `.Load(int3(pixel, mip))`, без сэмплера |
| `RWTexture2D<T>` | Чтение и запись текстуры | Не используется в этом проекте, но существует - например, для compute-шейдеров, которые сами рисуют в текстуру (постобработка, симуляции) |

Обычные (не буферные) переменные — `float`/`int`/`float3`/`float4x4` и т.п. — тоже объявляются
на уровне файла и заполняются с C#-стороны через `SetFloat`/`SetInt`/`SetVector`/`SetMatrix`, как в
примере выше с `_InstanceCount`.

## C#-сторона: минимальный набор вызовов

```csharp
[SerializeField] private ComputeShader _shader;
private int _kernel;

void Setup()
{
    _kernel = _shader.FindKernel("CSMain"); // имя должно совпадать с #pragma kernel
}

void Dispatch(GraphicsBuffer buffer, int count)
{
    _shader.SetBuffer(_kernel, "_Numbers", buffer);   // буферы - с указанием kernel'а
    _shader.SetInt("_Count", count);                   // обычные переменные - без kernel'а
    _shader.Dispatch(_kernel, Mathf.CeilToInt(count / 64f), 1, 1);
}
```

`FindKernel` ищет по строковому имени — опечатка не поймается компилятором C#, вылезет только в
рантайме (`ArgumentException`). `SetBuffer` требует номер kernel'а (один файл может содержать
несколько kernel'ов с разными наборами привязанных буферов), а обычные переменные (`SetInt` и т.п.)
— нет, они общие на весь compute shader asset.

## Полный минимальный пример: удвоить массив чисел

Отдельно от всего проекта — самый маленький возможный рабочий compute shader, чтобы увидеть все
кусочки вместе, без culling/frustum/occlusion-сложности:

`Double.compute`:
```hlsl
#pragma kernel CSMain

RWStructuredBuffer<float> _Numbers;
uint _Count;

[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= _Count) return;
    _Numbers[id.x] *= 2.0;
}
```

C#:
```csharp
public ComputeShader shader;

void Run()
{
    float[] data = { 1, 2, 3, 4, 5 };
    var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, sizeof(float));
    buffer.SetData(data);

    int kernel = shader.FindKernel("CSMain");
    shader.SetBuffer(kernel, "_Numbers", buffer);
    shader.SetInt("_Count", data.Length);
    shader.Dispatch(kernel, Mathf.CeilToInt(data.Length / 64f), 1, 1);

    buffer.GetData(data); // синхронное чтение обратно - см. предупреждение в Диагностике
    // data теперь { 2, 4, 6, 8, 10 }
    buffer.Dispose();
}
```

Это тот же самый набор шагов, что в реальном culling-коде этого проекта
(`GpuIndirectCuller.Dispatch()`/`DispatchLevel()`) — просто без трёх тестов видимости внутри. Если
этот маленький пример понятен, разница с настоящим `IndirectCulling.compute` — только в том, что
происходит ВНУТРИ тела kernel'а, не в механике вызова.

## Частые грабли, специфичные для compute shader'ов

- **`numthreads` должен быть известен на этапе компиляции** — нельзя передать размер группы как
  переменную из C#, только буквальные числа в самом `.hlsl`.
- **Layout буфера должен побайтово совпадать между HLSL-структурой и C#-структурой** — см.
  [Instance-буфер](02-architecture.html#1-instance-буфер-graphicsbuffertargetstructured), это касается
  любого своего buffer'а, не только `InstanceData`.
- **Нельзя динамически выбирать тип буфера** — если объявили `StructuredBuffer<T>` (только чтение),
  в этот же буфер нельзя писать из того же kernel'а; нужен отдельный `RWStructuredBuffer<T>`.
- **Платформенные различия компиляторов HLSL** (Vulkan/glslang против D3D/DXC) — отдельно разобраны
  в [Граблях](05-gotchas.html#известные-грабли) (namespace в reflected-HLSL, несколько `return` в
  одной функции) — те же ограничения действуют и в `.compute`-файлах.
