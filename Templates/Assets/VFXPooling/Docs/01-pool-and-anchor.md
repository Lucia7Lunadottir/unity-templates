# Пул и пустышка

## Пул объектов

Создать `GameObject` с `ParticleSystem` (`Instantiate`) и уничтожить его (`Destroy`) дорого: выделение памяти,
регистрация в сцене, потом сборщик мусора. **Пул** - заранее созданный запас: вместо `Instantiate` объект берут из
запаса (`Get`), вместо `Destroy` - кладут обратно (`Release`). `VFXPool` использует готовый `ObjectPool<T>` из Unity:

```csharp
var pool = new ObjectPool<ParticleSystem>(
    createFunc: () => CreateVFX(vfx),          // запас кончился - создать новый
    actionOnGet: (vfx) => OnGetVFX(vfx),       // выдать: включить объект
    actionOnRelease: (vfx) => OnReleaseVFX(vfx)); // вернуть: очистить частицы, выключить
```

**Прогрев.** `ObjectPool` создаёт объекты лениво, поэтому первый удар платил бы за `Instantiate`. Поле `prewarm` в
`VFXPrefabsContainer` говорит, сколько экземпляров создать при старте:

```csharp
for (int i = 0; i < count; i++) created.Add(pool.Get());
foreach (var vfx in created) pool.Release(vfx);
```

## Пустышка задаёт позу и размер

`VFXPoolElement` ставится туда, где раньше лежала `ParticleSystem`, с тем же положением, поворотом и масштабом.
`Play()` берёт эффект из пула и копирует её мировую позу:

```csharp
bool useLossyScale = vfx.main.scalingMode != ParticleSystemScalingMode.Local;
TransformSetter.SetTransform(vfx.transformHandle, position, rotation,
    useLossyScale ? anchor.lossyScale : anchor.localScale);
```

`lossyScale` - итоговый масштаб с учётом всех родителей (медведь со scale 2 -> эффект вдвое больше, как было у
дочерней системы). Для систем с `Scaling Mode = Local` родители и раньше не влияли, поэтому берётся `localScale`.

**Follow.** Экземпляр из пула лежит под объектом `VFXPool`, а не под врагом, поэтому сам за лапой не поедет. Пустышка
с включённым `_follow` попадает в список, и `VFXPool.LateUpdate` копирует её позу каждый кадр, пока эффект играет.
`LateUpdate` - потому что кости Animator к этому моменту уже обновлены. Конвертер включает `_follow` у петель и у
эффектов с `Simulation Space = Local` (частицы, которые должны двигаться вместе с источником).

## Частицы на поверхности меша

`Blood` и `Red Eyes` рождают частицы на поверхности меша зверя: модуль Shape с типом Skinned Mesh Renderer ссылается
на рендерер тела или глаз. Префаб пула - ассет, а ассет не может ссылаться на объект на сцене или внутри другого
префаба, поэтому ссылка при сохранении теряется, и Unity пишет `is trying to spawn on a mesh with zero surface area`.

Рендерер хранит пустышка (`_shapeRenderer`), и пул подставляет его в Shape экземпляра **до** `Play()`, иначе первый
выброс частиц ушёл бы в пустоту. У VAT-двойника тела - обычный `MeshRenderer`, тип Shape переключается сам:

```csharp
if (renderer is SkinnedMeshRenderer skinned) { shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer; shape.skinnedMeshRenderer = skinned; }
else if (renderer is MeshRenderer meshRenderer) { shape.shapeType = ParticleSystemShapeType.MeshRenderer; shape.meshRenderer = meshRenderer; }
```

Конвертер заполняет `_shapeRenderer` сам, а `VATTwinSetup` переводит его на одноимённый рендерер двойника.

## Номер запуска: чужой эффект не остановить

Пустышка запоминает экземпляр, который ей выдали. Но эффект без петли пул забирает сам по таймеру
(`duration` в контейнере) и может тут же выдать его другому волку. Если первая пустышка потом вызовет `Stop()` по старой
ссылке, она погасит **чужой** эффект. Это известная ошибка «ABA»: ссылка та же, а объект за ней уже другой.

Решение - номер запуска (как поколение у `SwarmNavHandle`): при каждом `Get` экземпляр получает новый номер, пустышка
хранит пару «экземпляр + номер» и действует, только если номер совпал:

```csharp
public bool IsPlaying()
{
    return _vfx != null && _playId != 0 && VFXPool.instance.GetPlayId(_vfx) == _playId;
}
```

## Один таймер возврата на экземпляр

У экземпляра бывает два способа вернуться: таймер `duration` и `StopVFX` (дождаться, пока частицы догорят). Раньше
`StopVFX` запускал второй таймер, не отменив первый, и экземпляр возвращался в пул **дважды** - второй раз уже будучи
выданным кому-то ещё. Теперь новый таймер отменяет старый:

```csharp
if (activeVFXCoroutines.TryGetValue(vfx, out Coroutine previous) && previous != null)
    StopCoroutine(previous);
activeVFXCoroutines[vfx] = coroutine;
```

## Мелочи

- `_stopWithDisable` (конвертер включает всегда): выключили врага - его эффект сразу вернулся в пул, как пропадали дочерние частицы.
- Повторный `Play()` петли сначала останавливает прежний экземпляр, чтобы тот не остался играть без хозяина.
- `ParticleDamage` берёт `Camera.main` заново, если камера уничтожена: пул переживает смену сцены, а камера нет.
- Без `VFXPool` на сцене `Play()` пишет предупреждение, а не падает.
