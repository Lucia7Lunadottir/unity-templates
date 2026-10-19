# Быстрый старт

Навигация SwarmNav работает на запечённом **NavMesh** Unity. Своей сетки, бейка высот и компонента-поверхности
больше нет: `SwarmNav` спрашивает `NavWorld`, где поверхность, куда можно пройти и как попасть в нужную точку.

## 1. Что нужно на сцене

1. **NavMesh.** Запеките `NavMeshSurface` как обычно (пакет `AI Navigation`). Земля, Terrain-тайлы, плавающие
   острова - всё, что попало в NavMesh, попадает и в SwarmNav. Размер и качество запекания - ваши настройки Surface.
2. **Мосты и лестницы между разорванными частями NavMesh** - обычный `NavMeshLink`. Агенты проходят по нему сами
   (раздел 4 в `02-swarm-agent.md`).
3. **Камера с тегом `MainCamera`** - фокус LOD. Без неё все агенты считаются Near (полное качество, без прореживания).
4. **Агенты** - `GameObject` с компонентом `SwarmAgent`.

Больше ничего: ни `SwarmNavSurface`, ни ассета сетки, ни настроек на агентах.

## 2. Первый агент

```csharp
using PG.SwarmNavigation;
using UnityEngine;

public class MyNpc : MonoBehaviour
{
    [SerializeField] private SwarmAgent agent;   // тот же GameObject или дочерний
    [SerializeField] private Transform target;

    private void Start()
    {
        agent.SetDestination(target.position);   // неподвижная точка
        // agent.SetDestination(target.transformHandle);   // погоня: позиция читается каждый кадр сама
    }
}
```

`SwarmAgent` при `Awake` регистрируется в общем `SwarmCrowd` (создаёт его, если это первый агент) и получает хэндл.
Один драйвер на сцену ставит джобы кадра в `Update()` и дожидается их в `LateUpdate()`.

## 3. SwarmNavSettings (необязательно)

Без ассета берутся значения по умолчанию (`SwarmNavSettings.CreateDefault()`). Чтобы поменять - создайте ассет
(`Create > SwarmNav > Settings`) и назначьте его первому `SwarmAgent` (`Settings`) или через
`agent.Configure(settings)` в коде. Настройки читаются один раз при создании `SwarmNav`: побеждает ассет первого агента.

| Поле | Смысл |
|---|---|
| `MaxAgents` | Ёмкость пула. Фиксируется при создании: поднимите заранее, если агентов будет больше (по умолчанию 32768). |
| `AgentTypeId`, `AreaMask`, `QueryExtents` | Тип агента NavMesh, маска областей и окно поиска полигона под точкой (`2 x 4 x 2`). |
| `PathRequestsPerFrame` | Сколько путей за кадр считают воркеры (по умолчанию 128). Остальные агенты ждут следующего кадра; пока путь не готов, они идут к цели по прямой (`WalkWhilePathPending`). |
| `WalkWhilePathPending` | Пока путь считается, агент идёт к цели по прямой (`MoveLocation` остановит у края NavMesh). Выключено - стоит и ждёт, как `NavMeshAgent`. |
| `PathWorkers` | Сколько воркеров считают пути (0 - все). Меньшее число замедляет пути и не ускоряет кадр: любой вызов API агента дожидается идущих путей. |
| `SpawnSnapExtents` | Если точка спавна дальше `QueryExtents` от NavMesh, агент притягивается к ближайшей точке в этом окне (по умолчанию 10 x 100 x 10 м); иначе стоит, `isOnNavMesh = false`. |
| `PathBufferSize` | Пул узлов `NavQueryBuffer` на воркер. Увеличьте, если на очень длинных путях часть запросов не завершается. |
| `ChaseRepathDistance`, `ChaseRepathFraction`, `RepathCooldown` | Когда пересчитывать путь за движущейся целью (раздел 3 в `08-behaviour-and-lod.md`). |
| `DirectMoveRange` | Если цель ближе и до неё нет стен (`Raycast`), агент идёт напрямую, без пути. `0` - выключено. |
| `AvoidanceRadius`, `SeparationWeight`, `FlowWeight` | Обход соседей. `AvoidanceRadius` - личное пространство агента, **не меньше его размера**: для куба 1 м ставьте >= 1,05. |
| `GpuAvoidance`, `GpuMaxNeighbors`, `GpuAvoidEvery` | Разведение на GPU (ComputeShader): считает всех агентов любого тира; `GpuAvoidEvery` - раз в сколько кадров (по умолчанию 2). Без compute-шейдеров или при выключении - хэш на CPU. |
| `ArrivalJam`, `PersonalJamRadius` | Толпа у общей цели: агент, коснувшийся уже прибывшего из своей группы, встаёт (диск растёт наружу). Для личной цели - только ближе `PersonalJamRadius`. |
| `MaxNeighborsPerAgent`, `SeparateFarAgents` | Только CPU-вариант разведения (хэш): потолок соседей и разведение Far-агентов. |
| `LinkSpeedMultiplier` | Скорость прыжка по `NavMeshLink` относительно `speed`. |
| `Lod`, `LodFocusTag`, `ShowLodGizmos` | LOD по расстоянию (раздел 3 в `08-behaviour-and-lod.md`). |
| `FarDeadReckoning` | Far-агенты идут вдоль углов пути без `MoveLocation` (цена `MoveLocation` растёт с пройденным расстоянием, а Far шагает раз в N кадров сразу на N шагов). Привязка к полигону пересчитывается при выходе из Far. |
| `LodSmoothing` | Сглаживание рывков у дальних агентов в кадре (раздел 5 в `03-swarm-crowd.md`). `0` - выключено. |
| `TransformJobCount` | На сколько частей делится запись `Transform` (по умолчанию 128). |

## 4. Проверка, что всё работает

- Повесьте `SwarmNavDebugOverlay` (папка `Docs`) на любой объект и нажмите Play: HUD покажет число агентов, тиров,
  путей за кадр и ожидание джобов.
- `Tests/SwarmAgentApiTest` прогоняет публичный API `SwarmAgent` на вашей сцене и пишет
  в консоль одну запись `SAT` со списком проверок.
- `Tests/SwarmCrowdStressTest` спавнит N кубов, раздаёт им цели и пишет в консоль `SWT` с перцентилями ожидания навигации.

## 5. Если агент не двигается

1. `agent.isOnNavMesh` равен `false` - агент заспавнен вне NavMesh (дальше `QueryExtents` от поверхности).
2. `agent.pathStatus == Invalid` - цель или агент вне NavMesh. `Partial` - цель недостижима, агент дойдёт до
   ближайшей достижимой точки.
3. `agent.isStopped == true` или `speed == 0`.
4. В консоли «NavWorld недоступен» - на сцене нет запечённого NavMesh на момент создания `SwarmNav`.
