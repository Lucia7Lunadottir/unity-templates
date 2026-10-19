using UnityEngine;

namespace PG.SwarmNavigation
{
    // Единая точка настройки SwarmNav. Опциональна: без назначенного ассета берутся значения по умолчанию.
    [CreateAssetMenu(menuName = "SwarmNav/Settings")]
    public class SwarmNavSettings : ScriptableObject
    {
        [Header("Агенты")]
        [Tooltip("Ёмкость пула агентов (фиксируется при создании SwarmNav).")]
        [Min(1)] public int MaxAgents = 32768;

        [Header("NavMesh (Unity.AI.Navigation.LowLevel.NavWorld)")]
        public int AgentTypeId = 0;
        public int AreaMask = -1;
        [Tooltip("Окно поиска полигона под точкой (привязка агента и цели к NavMesh).")]
        public Vector3 QueryExtents = new Vector3(2f, 4f, 2f);

        [Tooltip("Если точка спавна дальше QueryExtents от NavMesh, агент притягивается к ближайшей точке в этом окне (иначе стоит на месте, isOnNavMesh = false).")]
        public Vector3 SpawnSnapExtents = new Vector3(10f, 100f, 10f);

        [Header("Пути")]
        [Tooltip("Сколько путей за кадр считают воркеры (остальные агенты ждут следующего кадра).")]
        public int PathRequestsPerFrame = 128;
        [Tooltip("Пока путь агента считается, он идёт к цели по прямой (MoveLocation останавливает у края NavMesh). Выключено - стоит и ждёт путь, как NavMeshAgent.")]
        public bool WalkWhilePathPending = true;
        [Tooltip("Сколько воркеров занимают расчёт путей (0 - все). Меньше воркеров - остальные свободнее для шага и отрисовки, но пути считаются дольше, " +
                 "а любой вызов API агента (SetDestination и т.п.) дожидается идущих путей: замер показал, что 5 воркеров из 20 делают кадр хуже.")]
        [Min(0)] public int PathWorkers = 0;
        [Tooltip("Размер пула узлов NavQueryBuffer на воркер.")]
        public int PathBufferSize = 8192;
        [Tooltip("Движущаяся цель (погоня): путь пересчитывается, когда цель сместилась больше чем на это расстояние...")]
        public float ChaseRepathDistance = 2f;
        [Tooltip("...или больше чем на эту долю расстояния от агента до цели.")]
        [Range(0.01f, 1f)] public float ChaseRepathFraction = 0.15f;
        [Tooltip("Минимум секунд между пересчётами пути на агента: Near / Mid / Far.")]
        public Vector3 RepathCooldown = new Vector3(0.25f, 1f, 3f);

        [Tooltip("Если цель ближе этого расстояния и по NavMesh до неё нет препятствий (Raycast), агент идёт прямо - без расчёта пути. 0 - выключено.")]
        public float DirectMoveRange = 30f;

        [Header("Local Avoidance")]
        public float AvoidanceRadius = 0.6f;
        public float SeparationWeight = 1f;
        public float FlowWeight = 1f;
        // Потолок на число соседей на агента за кадр: без него стоимость растёт как O(N^2), когда толпа стягивается в одну точку.
        public int MaxNeighborsPerAgent = 24;
        [Tooltip("Разведение на GPU (ComputeShader): считает всех агентов любого тира без потолка в 24 соседа. Выключено или нет compute - хэш на CPU.")]
        public bool GpuAvoidance = true;
        [Tooltip("GPU: считать разведение раз в N кадров (результат меняется медленно; больше N - дешевле выгрузка и чтение).")]
        [Min(1)] public int GpuAvoidEvery = 2;
        [Tooltip("GPU: потолок обхода соседей на агента (защита от патологических скоплений).")]
        public int GpuMaxNeighbors = 96;
        [Tooltip("Толпа у общей цели: агент, коснувшийся уже прибывшего из своей группы, встаёт на месте (диск растёт наружу). Выключено - все лезут в точку.")]
        public bool ArrivalJam = true;
        [Tooltip("Личная цель: правило 'коснулся прибывшего' действует только ближе этого расстояния до цели.")]
        public float PersonalJamRadius = 6f;
        [Tooltip("Разводить и дальних (Far) агентов (только CPU-хэш; на GPU разводятся все). Выключено - Far идут сквозь друг друга, зато в хэш соседей не попадают (дешевле).")]
        public bool SeparateFarAgents = false;

        [Tooltip("Far-агенты (вне Near/Mid) идут вдоль углов пути без MoveLocation, высота - интерполяция по углам, привязка к полигону пересчитывается при выходе из Far. " +
                 "Сильно дешевле шага по NavMesh; выключено - Far шагают через MoveLocation, как остальные.")]
        public bool FarDeadReckoning = true;

        [Header("Переход по NavMeshLink")]
        public float LinkSpeedMultiplier = 1f;

        [Header("LOD")]
        public SwarmLodSettings Lod = SwarmLodSettings.Default;
        // Тег объекта (обычно камеры), вокруг которого считаются тиры Near/Mid/Far; без фокус-точки все агенты Near (полное качество). Пусто - не регистрировать.
        public string LodFocusTag = "MainCamera";
        [Tooltip("Рисовать в Scene view кольца NearRadius (зелёное) и MidRadius (жёлтое) вокруг фокуса.")]
        public bool ShowLodGizmos = true;

        [Header("Transform")]
        [Tooltip("Сглаживание рывков у Mid/Far: показанная позиция догоняет настоящую на скорости агента * это число. 0 - выключено (рывки).")]
        [Min(0f)] public float LodSmoothing = 1.3f;
        [Tooltip("TransformAccessArray.desiredJobCount: на сколько частей делится запись Transform (-1 - по умолчанию).")]
        public int TransformJobCount = 128;

        public float SpatialHashBucketSize => AvoidanceRadius * 2f;

        public static SwarmNavSettings CreateDefault()
        {
            SwarmNavSettings settings = CreateInstance<SwarmNavSettings>();
            settings.name = "SwarmNavSettings (default)";
            settings.hideFlags = HideFlags.DontSave;
            return settings;
        }
    }
}
