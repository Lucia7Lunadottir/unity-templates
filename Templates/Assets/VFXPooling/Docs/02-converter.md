# Перевод ParticlesInvoker на пул

`VFXPoolConverter` (`VFX Pooling/Editor/VFXPoolConverter.cs`) делает перевод автоматически.

## Как запустить

- **Объект на сцене или в Prefab Mode:** ПКМ по компоненту `ParticlesInvoker` -> **Convert To VFX Pool** (с Undo).
- **Префабы в Project:** выделить один или несколько `.prefab` -> ПКМ -> **VFX Pool -> Convert ParticlesInvoker In Selected Prefabs**.

Отчёт пишется в Console строкой `[VFX Pool]`.

## Что происходит с каждым эффектом списка

1. **Префаб пула.** Копия эффекта сохраняется в `Assets/Prefabs/VFX/Pooled/<Враг>/<Имя>.prefab` с нулевым
   `Transform` (позу и размер даст пустышка). У всех систем внутри выключается `Play On Awake` (иначе при выдаче
   эффект мигнёт в старой точке до установки позы), а `Stop Action = Destroy` меняется на `None` (иначе пул потерял бы
   экземпляр).
2. **Контейнер.** В `VFXPrefabsContainer` добавляется `<Враг>/<Имя>`: длительность = задержка + длительность +
   время жизни частиц (максимум по всем системам), петля - если она есть хоть у одной системы, прогрев 2.
3. **Пустышка.** На место системы ставится `<Имя> (VFX)` с `VFXPoolElement`: тот же родитель, порядок, позиция,
   поворот, масштаб. `_follow` - для петель и `Local Simulation Space`, `_stopWithDisable` - всегда.
4. **UnityEvent перенаправляется.** Вызов `ParticleSystem.Play()` из UnityEvent (например, `HealthMob._damageEvent`
   у единорогов запускает `Blood`) переписывается на `VFXPoolElement.Play()` пустышки: обе функции без аргументов,
   поэтому событие продолжает работать.
5. **Оригинал удаляется**, если на него больше никто не ссылается. Если ссылается (у единорогов внутри `Attack` лежит
   `Spawn Attack`, на который указывает `UnicornAIUnit.magicProjectilePrefab`), оригинал остаётся, а в отчёте будет
   строка «оригинал оставлен».
6. `ParticlesInvoker` заменяется на `VFXPoolElementInvoker` с тем же порядком и именами - индексы в
   `ParticlesActivator`/`VATParticlesActivator` не меняются.

Проверка ссылок обходит все сериализуемые поля компонентов объекта (`SerializedObject` + `property.Next(true)`) и
ищет ссылку на систему, её `GameObject` или детей. `Transform` пропускается: родитель хранит список детей
(`m_Children`), это не использование эффекта.

## Что переведено (2026-10-18)

Wolf Realistic, Bear, Bear_VAT, Lava Monster, Shadow Unicorn и варианты (Fire, Poison, Poison with metal horn).
116 фоновых «Shadow Unicorn without AI Variant» на сцене Snow World подхватили перевод базового Shadow Unicorn сами.

**Не переведено намеренно:**

| Что | Почему |
|---|---|
| `PLAYER` / `Lieris V3`, `Laser` | на игроке много связанных систем - **не трогать** |
| Vanitas Metal Horn Fire Shadow Unicorn Variant, Storm Tower Unicorn Boss | боссы: переводить только вручную и осторожно |
| `ShadowRoom.unity` | `ParticlesInvoker` читают ноды Story Graph (`ParticleSystemInvokeNode`/`StopNode`). Story Graph - отдельная сборка `PG.StoryGraph` и `VFXPool` не видит |
| Эффекты баффов, `ShadowPegasusUnit.attackParticleSystem` и другие поля `ParticleSystem` в коде | нужен перевод самого кода на `VFXPoolElement` |

## VAT-двойники: триггеры атак и пустышки

У VAT-версии зверя нет костей, поэтому ни триггеров атак (`Damage Triggers/...`, их по имени включает
`VATColliderEnableBehaviour`), ни пустышек эффектов у неё не было. `VATTwinSetup` (`AI/Editor/VATTwinSetup.cs`)
переносит их с обычной версии: ПКМ по `VATAnimator` -> **Copy Combat Rig From Animator Prefab...** -> выбрать префаб
обычного зверя.

| Что копируется | Как |
|---|---|
| Коллайдеры (триггеры атак с `Damager`, коллайдер тела с `DamagableLinker`) | та же поза относительно корня; группа у корня (`Damage Triggers`, `Armature`) создаётся с тем же именем |
| Пустышки `VFXPoolElement` | так же; список `VFXPoolElementInvoker` заполняется по тем же индексам (пустые индексы двойника) |
| Ссылки на корень/компоненты исходника | переводятся на двойника (`DamagableLinker._damagableObject` -> корень двойника) |

Объекты с костей (коллайдер тела, Red Eyes медведя) встают в позу из префаба и **не двигаются с анимацией**.
Чтобы следовали, запеките сокеты (кости лап, головы) в VAT Baker и добавьте этим объектам `VATSocketLink`.
Уже есть у двойника объект с тем же именем - он пропускается, поэтому команду можно повторять.

Сделано: `Bear` -> `Bear_VAT`, `Wolf Realistic` -> `Wolf Realistic_VAT`.

## Откат

Префаб-ассеты меняются без Undo. Копии до конвертации лежали в бэкапе сессии; надёжный откат - git
(`git checkout -- <префаб>`), но у Wolf/Bear/Shadow Unicorn до конвертации уже были незакоммиченные изменения.
