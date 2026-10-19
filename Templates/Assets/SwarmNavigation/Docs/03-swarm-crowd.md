# SwarmCrowd: мост между SwarmNav и GameObject

`SwarmAgent` ничего не считает сам - он регистрируется в одном общем `SwarmCrowd` и форвардит вызовы по хэндлу.
`SwarmCrowd` хранит всё, что касается именно `Transform`, а навигацию целиком отдаёт `SwarmNav`.

## 1. Что делает SwarmCrowd, а что SwarmNav

| | `SwarmNav` | `SwarmCrowd` |
|---|---|---|
| Пути, шаг по NavMesh, высота, разведение, LOD | да | нет |
| Цели (`SetDestination`), статус пути | да | форвардит |
| `TransformAccessArray` из `TransformHandle` | нет | да |
| Поворот, `baseOffset`, `updatePosition/Rotation` | нет | да |
| Цель-`Transform` (погоня): чтение позиции раз в кадр | нет | да |
| Сглаживание рывков дальних агентов | нет | да |

`SwarmNav` не знает про `Transform` вообще (для `RenderMeshIndirect` он используется напрямую,
`05-advanced.md`), поэтому границу провели так.

## 2. Кадр SwarmCrowd

Драйвер `SwarmCrowdDriver` (один `MonoBehaviour` на сцену, создаётся сам) делает два вызова:

1. **`Update()`** -> `SwarmCrowd.Schedule(dt)`:
   - читает позицию каждой цели погони (один раз на уникальный `Transform`) и передаёт в `SwarmNav`;
   - `SwarmNav.ScheduleTick(dt)` ставит цепочку джобов (`04-how-it-works.md`);
   - после неё ставится `WriteTransformsJob` (`IJobParallelForTransform`), зависящий от джоба шага.
2. **`LateUpdate()`** -> `SwarmCrowd.Complete()` дожидается цепочки.

Между этими вызовами воркеры считают параллельно с остальным кодом игры (`Update` других скриптов, анимации).
Если какой-то скрипт в этом промежутке читает данные агента (`agent.remainingDistance`, `Warp` и т.п.), первое
такое обращение само дожидается джобов (`Sync`), остальные уже бесплатны.

## 3. Как записывается Transform

`TransformAccessArray` строится из `TransformHandle` (`go.transformHandle`), а не из `Transform`: `TransformHandle` -
unmanaged-структура, ей не нужен managed-объект. Запись идёт в `IJobParallelForTransform`:

```csharp
public void Execute(int i, TransformAccess transform)
{
    SwarmAgentState agent = Agents[Slots[i]];
    // позиция - только если изменилась, поворот - плавно к Velocity
    transform.SetPositionAndRotation(pos, rotation);   // одним вызовом, если нужны оба
}
```

Принципы, которые держат эту запись быстрой:

- **Пишем только при изменении.** `LastPositions[i]` хранит то, что записали в прошлый раз; стоящий агент не трогает `Transform`.
- **`SetPositionAndRotation` одним вызовом**, а не два присваивания.
- **Поворот считаем, когда агент сдвинулся**, а не каждый кадр на всех: `RotateTowards` (угол + `slerp`) - самая дорогая
  арифметика в джобе.
- **`TransformAccessArray(capacity, desiredJobCount)`** с `desiredJobCount = 128` (`TransformJobCount`): Unity делит
  работу по иерархиям (корням), а 128 частей раскидывает её по воркерам ровнее.
- **Агенты должны лежать в корне сцены** (или под разными родителями): тысячи агентов под одним родителем
  считаются в один поток.

## 4. Почему два джоба, а не один

Расчёт шага (`SwarmMoveJob`, внутри `NavWorld.MoveLocation`) и запись `Transform` (`WriteTransformsJob`) - два
отдельных джоба. Одним `IJobParallelForTransform` было хуже: на замере `MoveLocation` внутри
`IJobParallelForTransform` работал в ~5 раз медленнее (20-25 мс на 20 000 агентов), чем тот же вызов в обычном
`IJobParallelFor` (≈2 мс), потому что `IJobParallelForTransform` делит работу по иерархиям `Transform`, а не равномерно по
индексам. Разделение вернуло скорость (6,6-7,4 мс вместе до LOD, затем 2-3 мс с LOD).

## 5. Сглаживание рывков у LOD

Mid и Far агенты шагают раз в 2-4 кадра с накопленным `dt`. Позиция меняется скачком, и издалека видно дрожание.
`WriteTransformsJob` для Mid/Far показывает не настоящую позицию, а догоняющую: она приближается к настоящей на
скорости агента (`speed * LodSmoothing`, по умолчанию 1,3), без скачка. Телепорт, `Warp` и прыжок по `NavMeshLink`
(расстояние больше 8 м) переносятся сразу.

Сглаживание стоит денег: каждый такой агент пишет `Transform` каждый кадр, а не раз в N кадров. Поэтому оно
включено **только для дальних агентов внутри кадра камеры** (6 плоскостей `GeometryUtility.CalculateFrustumPlanes`
для `Camera.main`, запас 3 м). За кадром агент пишется на своём шаге, как без сглаживания. Замер 20 000 агентов:

| Вариант | Ожидание навигации p50 |
|---|---|
| Без сглаживания | ≈2,2 мс |
| Сглаживание всех Mid/Far | 3,8-5 мс |
| Сглаживание только Mid/Far в кадре | 1,6-1,8 мс (в этом прогоне, шум машины ±30%) |

`LodSmoothing = 0` выключает сглаживание. Для `RenderMeshIndirect` сглаживание в `SwarmCrowd` не участвует
(`Transform` там нет); его нужно делать в своём джобе записи инстансов.

## 6. API SwarmCrowd

Прямое использование нужно, если не хочется компонента на каждом `GameObject`:

```csharp
SwarmCrowd crowd = new SwarmCrowd(settings, 20000);
SwarmNavHandle h = crowd.AddAgent(go, speed: 3.5f, stoppingDistance: 0.5f, baseOffset: 0.5f);
crowd.SetDestination(h, new Vector3(10, 0, 5));
// каждый кадр: crowd.Schedule(dt) в Update, crowd.Complete() в LateUpdate
crowd.RemoveAgent(h);
crowd.Dispose();
```

Через `SwarmAgent` это всё делается само; `SwarmNavRuntime` держит общий `SwarmCrowd` и драйвер.
Методы `SwarmCrowd` повторяют `SwarmAgent` (по хэндлу): `SetDestination` (`Vector3`/`TransformHandle`), `ResetPath`,
`SetStopped`, `SetSpeed`, `SetStoppingDistance`, `SetBaseOffset`, `SetAngularSpeed`, `SetUpdatePosition`,
`SetUpdateRotation`, `GetRemainingDistance`, `HasDestination`, `GetPathStatus`, `IsOnNavMesh`, `Warp`, `SyncPosition`, `Move`.
