# SwarmNav

Навигация на десятки тысяч агентов поверх **NavMesh Unity**. Пути, высота поверхности, стены и `NavMeshLink`
берутся у `NavWorld` (`Unity.AI.Navigation.LowLevel`, Unity 6.6) - тот же NavMesh, который вы запекаете через
`NavMeshSurface`, без отдельного бейка. SwarmNav добавляет то, чего у `NavMeshAgent` на тысячах агентов нет:
всё считается Burst-джобами на воркерах, агентов прореживает LOD по камере, толпа обходит друг друга через
spatial hash, а результат либо пишется в `Transform` одним многопоточным джобом, либо отдаётся массивом/буфером для
`RenderMeshIndirect`.

**Если хотите максимально просто, как с `NavMeshAgent`:** запеките `NavMeshSurface` как обычно, киньте `SwarmAgent`
на `GameObject` и зовите `agent.SetDestination(pos)`. Больше ничего назначать не нужно. Есть `speed`,
`stoppingDistance`, `remainingDistance`, `pathStatus`, `isStopped`, `ResetPath`, `Warp` - как у `NavMeshAgent`.
Подробнее - `02-swarm-agent.md`.

**`SwarmAgent` не имеет своего `Update()`.** Это тонкая обёртка над одним общим `SwarmCrowd` на сцену. Весь кадр для
ВСЕХ агентов идёт Burst-джобами, которые ставит один общий драйвер. Подробнее - `03-swarm-crowd.md`.

**Если агентов десятки тысяч и они не `GameObject`** (VAT-толпа, `RenderMeshIndirect`) - берите `SwarmNav` напрямую:
`Agents` (массив состояний) и `AgentsGpu` (тот же массив в `GraphicsBuffer`). Подробнее - `05-advanced.md`.

## Что внутри

| Что | Чем делается |
|---|---|
| Путь до цели | `NavWorld.BeginFindPath/ContinueFindPath/EndFindPath` в Burst-джобе на каждом воркере + funnel (углы кратчайшего пути) |
| Высота и стены | `NavWorld.MoveLocation`: шаг по поверхности NavMesh возвращает и точку на ней, и упор в границу |
| Острова и мосты | сама сетка NavMesh и `NavMeshLink`: агент проходит по связи прыжком вход -> выход |
| Близкая цель | `NavWorld.Raycast`: если до цели нет стен, идёт напрямую, без расчёта пути |
| Погоня | цель-`Transform` читается один раз на группу, путь агента пересчитывается, когда цель заметно сместилась |
| Обход соседей | разведение на GPU (ComputeShader, uniform grid) для всех агентов; без compute - spatial hash на CPU |
| Толпа у одной цели | правило прибытия: коснулся прибывшего из своей группы - встал, диск растёт наружу |
| LOD | тиры Near/Mid/Far по расстоянию до камеры: дальние шагают раз в N кадров |
| Запись в `Transform` | `IJobParallelForTransform` по `TransformAccessArray` из `TransformHandle` |

## С чего начать

| Файл | Что внутри |
|---|---|
| `01-quickstart.md` | Что нужно на сцене, первый агент, настройки `SwarmNavSettings`. |
| `02-swarm-agent.md` | **Начните отсюда.** `SwarmAgent` - как `NavMeshAgent`: цели, статус пути, остановка, телепорт, острова. |
| `03-swarm-crowd.md` | Один `SwarmCrowd` на сцену: как он пишет `Transform`, почему два джоба, сглаживание LOD. |
| `04-how-it-works.md` | Как устроен кадр: данные, порядок джобов, funnel, прямой ход, разведение, синхронизация. |
| `05-advanced.md` | `SwarmNav` напрямую, `RenderMeshIndirect`, `AgentsGpu`, анимация, RootMotion, отладка, грабли. |
| `06-SwarmNav-recipes.md` | Шпаргалка «хочу X -> делай так». |
| `07-performance.md` | Замеры, замер по стадиям, на что уходит время и что оптимизировали. |
| `08-behaviour-and-lod.md` | Патруль/погоня/атака, настройка LOD, `baseOffset`, Domain Reload. |
| `09-ai-integration.md` | Как ИИ игры (`WolfAI`, `BearAI`, `WildlifeAI`) работает со `SwarmAgent` и `VATAnimator`, видимость цели, порядок кадра. |

Готовые файлы (в этой же папке):

| Файл | Что делает |
|---|---|
| `SwarmWalkerExample.cs` | Толпа `GameObject` с `Animator`: блуждание по группам, пауза через `SetStopped`, анимация от фактического сдвига. |
| `SwarmPatrolChaseExample.cs` | Менеджер патруль -> погоня -> атака для массива `SwarmAgent`: один `Update()` на всю толпу. |
| `SwarmNavDebugOverlay.cs` | HUD (агенты, тиры, пути за кадр, ожидание джобов), гизмо, инспектор агента по ПКМ. |
| `../Tests/SwarmCrowdStressTest.cs` | Нагрузочный тест: N кубов с `SwarmAgent`, режим погони, замер по стадиям. |
| `../Tests/SwarmAgentApiTest.cs` | Проверка публичного API `SwarmAgent` на живой сцене. |
| `../Tests/SwarmLinkTest.cs` | Проверка `NavMeshLink`: агент идёт на изолированный остров по связи (написан, на сцене не запускался до конца). |
| `../Tests/TransformMoveBenchmark.cs` | Контрольный замер без навигации: N кубов двигает только `IJobParallelForTransform`. |

## Если коротко

- NavMesh запекается как обычно; бейка своей сетки нет. Тип агента и маска областей - в `SwarmNavSettings`.
- Один `SwarmNav` и один `SwarmCrowd` на сцену, драйвер создаётся сам. `Tick()` для `SwarmAgent` вызывать не нужно.
- Любой метод `SwarmNav`/`SwarmAgent`, который читает или пишет данные агента, сам дожидается джобов кадра.
- Фокус LOD - объект с тегом `MainCamera` (`LodFocusTag`). Без него все агенты считаются Near (полное качество).
- Путь агента, назначенный в этом кадре, начинает выполняться со следующего: пути считаются в фоне после шага.
- Замеры в Editor (режим энергосбережения процессора, 20 000 агентов): ожидание навигации 1,6-5 мс на кадр, с разведением и
  LOD. Потолок для двигающихся `GameObject` (~17-20 FPS на 21 000) задаёт сам Unity, не навигация; для десятков тысяч нужен
  `RenderMeshIndirect`. Подробности и оговорки про Editor/Profiler - `07-performance.md`.
- Domain Reload в проекте отключён: статики переживают Stop/Play (`08-behaviour-and-lod.md`, раздел 5).
