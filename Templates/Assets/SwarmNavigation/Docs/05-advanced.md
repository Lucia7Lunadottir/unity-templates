# Расширенные темы

## 1. SwarmNav напрямую: агенты без GameObject

`SwarmNav` не знает про `Transform`. Он хранит позиции в массиве `Agents` и считает кадр по вашему вызову. Это путь для
`RenderMeshIndirect` и VAT-толпы с десятками тысяч агентов.

```csharp
SwarmNav nav = new SwarmNav(SwarmNavSettings.CreateDefault(), 50000);
int focus = nav.RegisterFocusPoint(camera.position);            // фокус LOD (без него все Near)

SwarmNavHandle h = nav.RegisterAgent(spawnPoint, speed: 3.5f, stoppingDistance: 1f);
nav.SetDestination(h, goal);                       // личная цель
nav.SetSharedGoal(h, playerPos, groupKey: 1);      // общая цель группы (погоня, сбор у точки)

// каждый кадр:
nav.UpdateFocusPoint(focus, camera.position);
nav.UpdateSharedGoalPosition(1, playerPos);        // сдвинуть общую цель
nav.Tick(Time.deltaTime);                          // Schedule + Complete
NativeArray<SwarmAgentState> agents = nav.Agents;  // Position на NavMesh, Velocity - направление шага
```

`nav.Agents` дожидается джобов, если они идут. Позиция уже посчитана и стоит на поверхности NavMesh: ваш джоб
только пишет матрицы инстансов (и, например, плавно вращает по `Velocity`). Живой пример - `SwarmNavDemo.cs`
(`Assets/Scripts/Experimental`) и `VATCrowdIndirectRendererNav` (интеграция с VAT-толпой).

Освобождайте `nav.Dispose()` в `OnDestroy`. Один `Tick` на `SwarmNav`: не вызывайте его из двух скриптов.

### 1.1. Overlap с остальным кадром

`Tick` синхронный. Если хотите, чтобы воркеры считали параллельно с вашим кодом, используйте пару:

```csharp
void Update()   { nav.ScheduleTick(Time.deltaTime); }   // поставить джобы
void LateUpdate() { nav.Complete(); /* читать nav.Agents */ }
```

Любой метод `SwarmNav` между ними сам дожидается джобов (`Sync`), поэтому порядок безопасен, но ждать придётся при
первом обращении.

### 1.2. AgentsGpu: те же агенты в GraphicsBuffer

```csharp
GraphicsBuffer gpu = nav.EnableGpuBuffer();   // раскладка SwarmAgentState, stride = UnsafeUtility.SizeOf<SwarmAgentState>()
```

После этого буфер обновляется в конце каждого `Complete()`. Его можно привязать к материалу `RenderMeshIndirect` или
читать ComputeShader'ом (позиция, направление, скорость, тир). Раскладка структуры: `float3 Position; float3 Velocity;
float Speed; byte Tier; byte Generation;` плюс выравнивание до 4 байт (30 байт данных -> `SizeOf` = 32); в HLSL объявляйте
такой же stride, например `float3, float3, float, uint` (тир и поколение лежат в младших байтах последнего слова).

## 2. Анимация ходьбы

- **Animator на GameObject.** `SwarmAgent.velocity.magnitude` (или фактический сдвиг за кадр) -> параметр `Speed`.
  Фактический сдвиг надёжнее: агент, упёршийся в соседа, перестаёт «идти ногами».
- **VAT-толпа (`VATCrowdIndirectRendererNav`).** Walk-параметр = `lengthsq(Velocity) > 0.0025` (в джобе
  `SwarmIntegrateJob`). Агент без цели имеет нулевую `Velocity` и стоит.

## 3. События «дошёл до цели»

Событий нет: опросите `hasDestination` (для неподвижной цели станет `false` по прибытии) или `remainingDistance <=
stoppingDistance`. Один менеджер, опрашивающий массив агентов, дешевле `Update()` на агента: пример -
`SwarmPatrolChaseExample.cs`.

## 4. Высота

Высоту даёт NavMesh: `Position.y` - это высота поверхности под агентом, `baseOffset` добавляет смещение модели
(половина высоты куба с центральным пивотом). Отдельного бейка высот, `Height Source` и объёмов больше нет.
Если агент «в воздухе» или «в земле»: проверьте `baseOffset`, пивот модели и что NavMesh запечён на нужной поверхности.

## 5. Отладка

| Что смотреть | Где |
|---|---|
| Счётчики: активные агенты, тиры, стоящие, путей за кадр, ожидание джобов | `SwarmNavDebugOverlay` (HUD) |
| Позиции и направления агентов в Scene View | `SwarmNavDebugOverlay` (гизмо, цвет по тиру) |
| Поля агента по ПКМ | `SwarmNavDebugOverlay` (инспектор в Game View) |
| На что уходит время кадра | `SwarmNav.ProfileStages = true`, затем `StageMs` / `StageNames` (раздел 3 в `07-performance.md`) |
| Радиусы LOD | `ShowLodGizmos` в настройках: зелёное кольцо - Near, жёлтое - Mid |
| Статус пути агента | `agent.pathStatus` |

## 6. RootMotion: Transform двигает Animator

```csharp
agent.updatePosition = false;     // позицию не пишет SwarmCrowd
agent.updateRotation = false;
// в OnAnimatorMove:
agent.SyncPosition(transform.position);          // сообщить SwarmNav, где агент на самом деле
transform.position = agent.Move(animator.deltaPosition);   // смещение с учётом NavMesh и стен
```

`Move` применяет смещение через `NavWorld.MoveLocation`: агент не выйдет за границу NavMesh, и возвращается
позиция на поверхности. Поворот при `updateRotation = false` остаётся за вами.

`SyncPosition` сдвигает привязку агента к полигону тем же `MoveLocation` и **не трогает путь** (как
`NavMeshAgent.nextPosition`). Телепорт с пересчётом пути - это `Warp`. Раньше `SyncPosition` работал как телепорт и каждый
кадр заново заказывал путь: у агента с root motion `pathStatus` навсегда оставался `Pending`.

**Где вызывать.** `OnAnimatorMove` выполняется после `Update`, когда джобы SwarmNav уже идут, и первый вызов API в нём
дожидается их (`Complete`) - параллельность кадра теряется. Лучше копить `animator.deltaPosition` в `OnAnimatorMove` и
прикладывать в своём `Update` до драйвера SwarmNav (так делает `WolfAI`, см. `09-ai-integration.md`).

## 7. Реакция на удар (взрыв, частица)

```csharp
SwarmNav.Instance.ApplyImpulseAt(hitPoint, radius: 3f, strength: 2f);
```

Агенты в радиусе получают толчок прочь от точки, с затуханием по расстоянию и с учётом NavMesh (через `MoveLocation`).
Метод перебирает всех агентов, поэтому подходит для редких событий, а не для вызова каждым столкновением частицы.

## 8. Известные ограничения

- **API агента ждёт основную цепочку кадра, но не пути.** Первый вызов `SwarmAgent`/`SwarmNav`, который трогает данные агента (`SetDestination`, `remainingDistance`, `Warp` и т.п.), дожидается джобов шага (LOD, Steer, Move, запись `Transform`); остальные вызовы кадра бесплатны. Фоновые пути с массивами агентов не работают (у них своя копия запроса), поэтому API их не ждёт. Результат пути вступает в силу в начале следующего кадра; если цель или позиция агента менялись после запроса (`Serial`), устаревший результат отбрасывается.
- **Пути длиннее 24 углов усекаются.** Агент доходит до последнего угла и просит новый путь.
- **Результат GPU-разведения запаздывает на 1-3 кадра.**
- **Far-агенты** идут строго по углам пути и не учитывают толчок соседей (`FarDeadReckoning`).
- **Прыжок по `NavMeshLink`** - прямая между концами связи, без дуги и анимации.
- **Предел для `GameObject`.** Двигающиеся `GameObject` упираются в обработку `Transform` самим Unity (около 17-20 FPS на 21 000 в Editor, режим энергосбережения); больше - только `RenderMeshIndirect`.
- **Замеры сделаны в Editor.** Значения для билда не снимались.

## 9. Частые грабли

- **Агент не двигается.** `agent.isOnNavMesh == false` (спавн вне NavMesh), `pathStatus == Invalid`, `isStopped`.
- **Много `SetDestination` за один кадр.** Пути считаются порциями по `PathRequestsPerFrame`: остальные стартуют позже.
  Увеличьте бюджет, если задержка старта заметна и на воркерах есть запас.
- **Агенты под одним родителем.** Запись `Transform` делится по иерархиям: тысячи под одним родителем считаются в один поток.
- **Домен Reload отключён.** Статические данные переживают Stop/Play. `SwarmNavRuntime` сам сбрасывается при старте
  Play (`RuntimeInitializeOnLoadMethod`).
- **Свой `SwarmNav` и `SwarmAgent` вместе.** Это два независимых набора агентов (у каждого свой пул, свои джобы).
  `SwarmNav.Instance` указывает на последний созданный `SwarmNav`; храните свою ссылку.
- **Данные агента из другого потока.** `SwarmNav` не потокобезопасен: все вызовы только с главного потока.
