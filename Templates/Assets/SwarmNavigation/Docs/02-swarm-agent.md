# SwarmAgent: просто, как NavMeshAgent

`SwarmAgent` - компонент-обёртка над общим `SwarmCrowd`. Он спрятал `Tick`, пути, LOD и разведение - остаётся
`SetDestination`, как у `NavMeshAgent`. Подготовка сцены - `01-quickstart.md`.

## 1. Минимум

1. Запеките `NavMeshSurface`.
2. Киньте `SwarmAgent` на `GameObject`.
3. Зовите `SetDestination`.

```csharp
agent.SetDestination(new Vector3(10f, 0f, 5f));      // неподвижная точка
agent.SetDestination(player.transformHandle);        // погоня: позиция читается каждый кадр сама
```

`SetDestination(Vector3)` можно звать сколько угодно раз: старая цель заменяется, путь считается заново.
`SetDestination(TransformHandle)` достаточно позвать один раз.

## 2. Поля и методы

| Поле/метод | Смысл |
|---|---|
| `SetDestination(Vector3 pos)` | Идти к неподвижной точке. Точка привязывается к ближайшей точке NavMesh в окне `QueryExtents`. |
| `SetDestination(TransformHandle target)` | Погоня за движущейся целью (игрок, враг). Позиция читается раз в кадр на всех, кто гонится за одним `Transform`. |
| `speed` | Скорость, м/с. |
| `stoppingDistance` | На каком расстоянии от цели считать, что дошли (по умолчанию 0,5). |
| `remainingDistance` | Расстояние по XZ до цели (обновляется на шагах агента; для дальних агентов - раз в N кадров). |
| `hasDestination` | `true`, пока цель не достигнута. После прибытия к **неподвижной** цели становится `false`. У погони остаётся `true`: агент возобновит движение, когда цель отойдёт. |
| `pathStatus` | `None`, `Pending` (путь считается), `Complete`, `Partial` (цель недостижима), `Invalid` (агент или цель вне NavMesh). |
| `pathPending` | То же, что `pathStatus == Pending`. |
| `isOnNavMesh` | Привязан ли агент к полигону NavMesh. |
| `isStopped` | `true` - стоит, цель и путь сохраняются. |
| `Stop()` | То же, что `isStopped = true`. |
| `ResetPath()` | Снять цель совсем (`hasDestination` станет `false`), в отличие от `Stop()`. |
| `Warp(Vector3 pos)` | Телепорт: `Transform` и внутреннее состояние сразу, с привязкой к NavMesh. Цель сохраняется, путь пересчитывается. |
| `angularSpeed` | Скорость поворота, градусов/сек. |
| `acceleration` | Как у `NavMeshAgent`: разгон и торможение у цели, м/с² (по умолчанию 8). `0` - скорость сразу полная, как было раньше. |
| `radius` | Как у `NavMeshAgent`: два агента расходятся на сумму радиусов. `0` - половина `AvoidanceRadius` из настроек. |
| `baseOffset` | Смещение Y модели над поверхностью NavMesh (для куба с пивотом в центре - половина высоты). |
| `velocity` | Фактическая скорость: направление шага * текущая скорость (с учётом разгона). |
| `updatePosition`, `updateRotation` | `false` - `Transform` двигает кто-то другой (RootMotion). См. `SyncPosition`/`Move`. |
| `SyncPosition(pos)`, `Move(offset)` | Для RootMotion (раздел 6 в `05-advanced.md`). `SyncPosition` - как `NavMeshAgent.nextPosition`: путь не пересчитывает. |

## 3. Путь считается не мгновенно

`SetDestination` лишь записывает цель. Путь считают воркеры в фоне после шага агентов, поэтому:

- сразу после вызова `pathStatus == Pending`; пока путь не готов, агент идёт к цели по прямой (`WalkWhilePathPending`, `MoveLocation` остановит у края NavMesh), а когда путь придёт, поправит маршрут;
- со следующего кадра он идёт по пути (цели ближе `DirectMoveRange` и без стен - сразу напрямую);
- за кадр считается не больше `PathRequestsPerFrame` путей (по умолчанию 128): если 5000 агентов получили цель
  одновременно, они стартуют порциями, а не все в один кадр.

Это так же, как у `NavMeshAgent` (`pathPending`), только без стоимости на главном потоке.

## 4. Недостижимая цель, острова и NavMeshLink

NavMesh не обязан быть связным. Если цель в другой несвязанной части:

- без `NavMeshLink` `pathStatus` станет `Partial`: агент дойдёт до ближайшей достижимой точки и остановится
  (`hasDestination == false`, `pathStatus == Partial`) - как `NavMeshAgent` с `PathPartial`;
- с `NavMeshLink` между частями путь `Complete`: агент идёт к входу связи, **прыгает** по прямой до выхода
  (скорость `speed * LinkSpeedMultiplier`) и дальше продолжает путь.

Плавающие острова - это просто ещё одна часть NavMesh: если `NavMeshSurface` запекает их (объём запекания
охватывает остров), агенты ходят по ним так же, как по земле. Высота над островом берётся у поверхности NavMesh
в точке агента, поэтому слои «земля под островом» и «поверхность острова» не путаются: агент привязан к тому
полигону, на котором стоит.

## 5. Погоня

```csharp
foreach (SwarmAgent enemy in enemies) enemy.SetDestination(player.transformHandle);
```

- Позиция игрока читается один раз за кадр на всех, кто гонится за ним.
- Пока игрок далеко, агент идёт по пути. Путь пересчитывается, когда игрок сместился больше чем на
  `max(ChaseRepathDistance, ChaseRepathFraction * расстояние до игрока)` и прошёл `RepathCooldown` (Near 0,25 с,
  Mid 1 с, Far 3 с). Дальний агент идёт к устаревшей точке и обновляет её реже - это и держит нагрузку.
- Когда игрок ближе `DirectMoveRange` (30 м) и до него нет стен, агент идёт напрямую по `Raycast` по NavMesh.
- `stoppingDistance` - дистанция остановки у игрока (дистанция удара).

## 6. Толпа

Тот же компонент на каждом `GameObject`. Расход на агента - запись в массивы и один элемент
`TransformAccessArray`; `Update()` нет ни у кого. Обход соседей включён по умолчанию; чтобы агенты проходили
сквозь друг друга, поставьте `SeparationWeight = 0`.

## 7. Что проверено

`SwarmAgentApiTest` (рядом со скриптами SwarmNav) на живой сцене проверяет: прибытие и `remainingDistance`,
высоту агента относительно NavMesh с учётом `baseOffset`, `isStopped`, `ResetPath`, `Warp`, недостижимую цель
(`Partial`), агента вне NavMesh (`Invalid`, без исключений), удаление соседнего агента. Результат последнего прогона -
в разделе 7 `07-performance.md`.
