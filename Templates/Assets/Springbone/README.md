# PonyPhysics – SpringBone v2

Профессиональная физика хвоста и гривы для Unity.  
**Компоненты на отдельные кости не нужны.**

---

## Требования

| Пакет | Минимальная версия |
|-------|-------------------|
| Unity | 2022.3 LTS |
| Burst | 1.8+ |
| Collections | 2.x |

---

## Установка

Скопируйте `Runtime/` и `Editor/` в `Assets/`.  
`.asmdef` подключит зависимости автоматически.

---

## Иерархия компонентов

```
PonyRoot
  └─ SpringBoneManager        ← ОДИН на всего персонажа

       ├─ Tail_Root
       │    └─ SpringBoneChain  ← ОДИН на цепочку хвоста
       │         └─ Tail_1
       │              └─ Tail_2
       │                   └─ Tail_3
       │                        └─ Tail_Tip
       │
       ├─ Mane_Root
       │    └─ SpringBoneChain  ← ОДИН на цепочку гривы
       │         └─ Mane_1 ...
       │
       ├─ Chest
       │    └─ SpringCollider (Sphere)
       ├─ Neck
       │    └─ SpringCollider (Capsule)
       └─ HindLeg_L
            └─ SpringCollider (Sphere)
```

---

## SpringBoneChain — параметры

| Параметр | Описание |
|----------|----------|
| **Chain Depth** | Глубина обхода дочерних костей. `-1` = до конца |
| **Exclude Bones** | Список костей, которые нужно пропустить (IK-таргеты и т.п.) |
| **Stiffness** | Сила возврата к позе аниматора |
| **Damping** | Гашение скорости колебаний |
| **Elasticity** | Инерция от движения родителя |
| **Gravity Power / Dir** | Сила и направление гравитации |
| **Angle Limit Deg** | Максимальный угол отклонения |
| **Radius** | Радиус коллизии |

### Кривые (AnimationCurve, X: 0=корень → 1=кончик)

| Кривая | Эффект |
|--------|--------|
| **Stiffness Curve** | Корень жёсткий, кончик мягкий (рекомендуется 1 → 0.4) |
| **Damping Curve** | Кончик гасится сильнее |
| **Gravity Curve** | Кончик тяжелее |
| **Radius Curve** | Корень шире, кончик тоньше |

---

## Рекомендуемые значения

### Хвост
```
Stiffness   = 0.12    Damping = 0.10
GravityPower = 0.025  AngleLimit = 70°
subSteps = 2
```

### Грива / чёлка
```
Stiffness   = 0.20    Damping = 0.08
GravityPower = 0.015  AngleLimit = 55°
subSteps = 2
```

---

## API

```csharp
var mgr = GetComponent<SpringBoneManager>();

// Пересобрать цепочки (после изменения иерархии)
mgr.Initialise();

// Сброс симуляции в текущую позу аниматора
mgr.ResetPose();

// Ветер
mgr.windForce = new Vector3(0.4f, 0f, 0.1f);
```

---

## Как это работает

```
LateUpdate (после Animator)
│
├─ RefreshColliders()      обновить worldCenter/worldRadius коллайдеров
├─ UploadInputs()          матрицы + параметры с кривыми → NativeArray
│
└─ × subSteps:
     ├─ IntegrateJob        Burst | Verlet + spring + gravity + wind + angle limit
     └─ ResolveCollisions() main thread | вытолкнуть точки из коллайдеров
│
└─ ApplyResults()          tip position → Quaternion на каждую кость
```

---

## Лицензия

MIT
