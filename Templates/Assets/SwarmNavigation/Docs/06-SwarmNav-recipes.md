# SwarmNav простыми словами

Шпаргалка «хочу X -> делай так». Объяснения - в остальных страницах.

## Шесть слов, которые встретятся

| Слово | Что это |
|---|---|
| **NavMesh / `NavWorld`** | Запечённая навигационная сетка Unity и её API для джобов. SwarmNav берёт у неё пути, высоту и стены. |
| **Хэндл** (`SwarmNavHandle`) | Номер агента внутри `SwarmNav`. Нужен, если работаете без `SwarmAgent`. |
| **Путь** | Список углов от агента до цели (кратчайший, по NavMesh). Считается в фоне. |
| **LOD / тир** | Near/Mid/Far по расстоянию до камеры: дальние шагают реже. |
| **Прямой ход** | Если до цели близко и нет стен, агент идёт напрямую, без пути. |
| **Приземление** | Прибытие по `NavMeshLink`: агент прыгнул и снова стоит на NavMesh. |

## Рецепт 0. Подготовка (один раз)

1. Запеките `NavMeshSurface`.
2. Повесьте тег `MainCamera` на камеру (фокус LOD).
3. Больше ничего: `SwarmAgent` сам создаёт всё нужное.

## Рецепт 1. Один персонаж идёт в точку

```csharp
agent.SetDestination(point);
```

## Рецепт 2. Один персонаж идёт за объектом

```csharp
agent.stoppingDistance = 1.5f;                       // дистанция остановки у цели
agent.SetDestination(player.transformHandle);        // достаточно один раз
```

## Рецепт 3. Толпа тысяч `GameObject`

Тот же компонент на каждом объекте (в корне сцены, не под одним родителем). `PathRequestsPerFrame` ограничивает,
сколько путей считается за кадр: остальные агенты стартуют порциями.

## Рецепт 4. Остановить / сбросить / телепортировать

```csharp
agent.isStopped = true;       // стоит, цель сохраняется
agent.ResetPath();            // цели больше нет (hasDestination == false)
agent.Warp(newPosition);      // телепорт с привязкой к NavMesh
```

## Рецепт 5. Дошёл ли агент

```csharp
if (!agent.hasDestination) { /* дошёл до неподвижной цели */ }
if (agent.remainingDistance <= agent.stoppingDistance) { /* у цели (в том числе в погоне) */ }
if (agent.pathStatus == SwarmPathStatus.Partial) { /* цель недостижима */ }
```

## Рецепт 6. Агент не дошёл, цель на другом острове

Нужен `NavMeshLink` между частями NavMesh. Без него `pathStatus == Partial`: агент дойдёт до ближайшей достижимой
точки и остановится. Плавающий остров - просто ещё одна часть NavMesh, пока его запекает `NavMeshSurface`.

## Рецепт 7. Патруль у каждого свой, погоня за игроком, атака

См. `SwarmPatrolChaseExample.cs` и `08-behaviour-and-lod.md`, раздел 2: один менеджер на всю толпу, один `Update()`.

## Рецепт 8. 50 000 животных без GameObject (RenderMeshIndirect)

`SwarmNav` напрямую: `RegisterAgent`, `SetSharedGoal`/`SetDestination`, `Tick`, чтение `nav.Agents` и запись
инстансов в своём джобе. Пример - `SwarmNavDemo.cs`; `AgentsGpu` отдаёт те же данные в GraphicsBuffer (`05-advanced.md`, раздел 1).

## Рецепт 9. Агенты проходят сквозь друг друга / слипаются

- Сквозь: `SeparationWeight` слишком мал или `SeparateFarAgents = false` (дальние не разводятся).
- Слипаются в точке: увеличьте `AvoidanceRadius` или `MaxNeighborsPerAgent`; для погони поставьте `stoppingDistance`
  побольше, чтобы они вставали кольцом.

## Рецепт 10. Дальние агенты дрожат

Mid/Far шагают раз в N кадров. `LodSmoothing > 0` сглаживает их в кадре камеры (`03-swarm-crowd.md`, раздел 5);
сократите `MidUpdateEveryNFrames`/`FarUpdateEveryNFrames` (дороже), если нужно ещё плавнее. Для `RenderMeshIndirect`
сглаживание нужно делать в своём джобе записи инстансов.

## Рецепт 11. Посмотреть, что происходит

`SwarmNavDebugOverlay` на любой объект; `ShowLodGizmos` в настройках; для времени по стадиям -
`SwarmNav.ProfileStages = true`.

## Рецепт 12. Агент висит в воздухе или в земле

Проверьте `baseOffset` и пивот модели (для куба с центральным пивотом - половина высоты) и что NavMesh запечён
на нужной поверхности.

## Если не работает

| Симптом | Причина |
|---|---|
| Агент стоит, `isOnNavMesh == false` | Заспавнен дальше `QueryExtents` от NavMesh. |
| `pathStatus == Invalid` | Цель или агент вне NavMesh. |
| `pathStatus == Partial`, агент останавливается рано | Цель недостижима без `NavMeshLink`. |
| Агент стартует с задержкой | Путь считается в фоне, порциями по `PathRequestsPerFrame`. |
| «NavWorld недоступен» в консоли | Нет запечённого NavMesh при создании `SwarmNav`. |
| Дальние рывками | `LodSmoothing = 0`, либо вне кадра камеры (там сглаживания нет намеренно). |
