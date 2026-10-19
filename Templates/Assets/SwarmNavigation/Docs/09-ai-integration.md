# Подключение ИИ игры

Эта страница о том, как обычный ИИ из `Assets/Scripts/AI` (волк, медведь, олень, белка) работает со `SwarmAgent` и
`VATAnimator`, не зная, что именно висит на объекте. Код: `AIBody.cs`, `BaseEnemyAI.cs`, `AIManager.cs`, `WolfAI.cs`,
`BearAI.cs`, `WildlifeAI.cs`.

## 1. Тело ИИ: одна точка входа

ИИ раньше напрямую звал `NavMeshAgent` и `Animator`. Теперь между ними два маленьких класса из `AIBody.cs`:

| Класс | Что выбирает | Что даёт ИИ |
|---|---|---|
| `AINavigation` | `SwarmAgent`, если он есть и включён, иначе `NavMeshAgent` | `SetDestination`, `Chase`, `ResetPath`, `hasPath`, `remainingDistance`, `isPathPartial`, `reachedPartialEnd`, `SteeringTarget`, `Move` |
| `AIAnimation` | `VATAnimator`, если он есть, иначе `Animator` | `SetFloat` (с damp), `SetBool`, `SetTrigger`, `SetInteger`, `SetBoolOrTrigger`, `HasRootMotion` |

Это приём **адаптер**: у двух классов с разным API (`NavMeshAgent` и `SwarmAgent`) появляется общая «розетка», и код ИИ
пишется один раз. Пример из `BearAI`:

```csharp
nav.speed = _chaseSpeed;
nav.Chase(targetTransform);   // SwarmAgent сам следит за движущейся целью
```

Если рядом со `SwarmAgent` есть `NavMeshAgent`, `AINavigation` его выключает (иначе Unity считал бы агента второй раз) и
переносит `radius` с учётом масштаба объекта, если у `SwarmAgent` свой радиус не задан.

**Боссы не затронуты.** В `BaseEnemyAI` поля `nav` и `anim` ленивые:

```csharp
protected AINavigation nav => _nav ??= new AINavigation(gameObject);
```

`??=` создаёт объект при первом обращении. Боссы (`VanitasBoss`, `KingDiceBossAI` и др.) к `nav` не обращаются, поэтому
у них ничего не создаётся и ничего не выключается.

## 2. Порядок кадра: почему ИИ раньше SwarmNav

Драйвер SwarmNav в `Update` ставит джобы кадра, а в `LateUpdate` их дожидается. Любой вызов API агента, пока джобы
идут, вынужден их дождаться (`Complete`). Поэтому:

- `AIManager` помечен `[DefaultExecutionOrder(-50)]`: `Tick()` всех ИИ выполняется **до** драйвера и читает данные,
  готовые с прошлого кадра, ничего не ожидая;
- root motion волка копится в `OnAnimatorMove` / `OnVATAnimatorMove` и прикладывается в следующем `Tick()`
  (`ApplyMotion`), а не сразу: `OnAnimatorMove` идёт после `Update`, когда джобы уже запущены;
- цепочка «отскок -> лай -> обход с фланга» у волка - состояние внутри `Tick()`, а не корутина: корутины
  продолжаются после `Update`, и их вызовы навигации тоже останавливали бы джобы.

Это общий принцип многопоточного кадра: **главный поток пишет данные до того, как отдал их воркерам, и читает после
того, как воркеры закончили**. Всё, что лезет в данные посередине, превращает параллельную работу в последовательную.

## 3. Видимость цели: игрок в доме

Раньше враг решал атаковать только по дистанции, поэтому бил сквозь стену дома. Теперь `AIManager` раз в
`updateRateFrames` кадров бросает лучи «глаза врага -> игрок» **одним пакетом**:

```csharp
_sightCommands.Add(new RaycastCommand(eye, toTarget / distance, query, distance));
...
RaycastCommand.ScheduleBatch(_sightCommands.AsArray(), _sightHits.AsArray(), 8).Complete();
```

`RaycastCommand` - запрос луча как данные. `ScheduleBatch` раздаёт их воркерам по 8 штук, и физика считает лучи
параллельно, вместо N вызовов `Physics.Raycast` подряд на главном потоке. Попадание в `Default` или `Ground`
(стены, крыша, скалы, террейн) значит, что цель не видна. Слои игрока и врагов в маску не входят.

ИИ включает проверку сам (`UsesLineOfSight => true`) и использует её так:

| Где | Правило |
|---|---|
| `DistanceToSensedTarget()` | Игрок не виден дольше `loseSightTime` - для врага его нет, враг уходит в патруль. |
| Атака волка и медведя | Только если игрок виден прямо сейчас и по высоте в пределах `maxAttackHeight` (не летит, не на крыше). |
| `WildlifeAI` | Пугается только видимого игрока. |
| Пегасы (`ShadowPegasusAI` + `FlightJob`) | Свой пакет лучей; без видимости не замечают, не пикируют, а начатое пике прерывают отлётом без атакующих частиц; через `_loseSightTime` возвращаются к патрулю. |

## 4. Недостижимая цель

Если путь до игрока частичный (`isPathPartial`: игрок в воздухе, на крыше), медведь доходит до края пути
(`reachedPartialEnd`), встаёт и смотрит на игрока. Погоня остаётся назначенной: когда путь снова станет полным,
он побежит сам.

## 5. Разгон и радиус

У `NavMeshAgent` есть `acceleration` и `radius`, у `SwarmAgent` их раньше не было: скорость сразу была полной (медведь
со `_chaseSpeed = 35` «телепортировался»), а разводились все на одинаковые 0,6 м. Теперь:

- `acceleration` - текущая скорость тянется к целевой не быстрее `acceleration * dt`, а у цели ограничена
  `sqrt(2 * acceleration * d)`. Это формула равнозамедленного движения: с такой скоростью агент успевает остановиться
  ровно за оставшиеся `d` метров (так же устроен `autoBraking` у `NavMeshAgent`);
- `radius` - пара агентов расходится на `r1 + r2`. На GPU радиус едет в свободных битах флагов агента
  (биты 2..7, шаг 0,1 м), поэтому буфер не вырос.

## 6. Анимация

| Зверь | Что двигает модель | Параметр скорости |
|---|---|---|
| Волк | root motion (`Animator`, или `VATAnimator` в режиме `Manual`); без него - сам ИИ (`_walkSpeed`/`_runSpeed`) | `Run`, `InputZ` |
| Медведь | `SwarmAgent` | `Move` по фактической скорости (`_moveParamScale`: 1 для Animator, 2 для VAT-графа) |
| Олень, белка | `SwarmAgent` | `Locomotion` (VAT: 1 - шаг, 2 - бег), у Animator оленя `Run` + `Move` |

У VAT нет `StateMachineBehaviour`, поэтому конец атаки сообщает `VATAttackStateNotifier` (VAT-копия
`AttackStateNotifier`). Если в графе его нет, ИИ ждёт возврата VAT-графа в состояние, из которого началась атака
(`WaitVatAttackEnd`, не дольше 4 с).
