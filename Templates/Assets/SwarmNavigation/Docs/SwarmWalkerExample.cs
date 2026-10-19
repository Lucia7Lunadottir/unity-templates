using PG.SwarmNavigation;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

// Шаблон "своего скрипта" поверх SwarmNav без SwarmAgent: толпа обычных GameObject с Animator.
// - блуждание: K групп со случайными целями (общая цель на группу, у каждого агента свой путь NavWorld);
// - цикл идёт/стоит на агента (пауза = SetStopped);
// - SwarmNav сам двигает агентов по NavMesh (высота и стены от поверхности), скрипт только пишет Transform и анимацию;
// - анимация считается от ФАКТИЧЕСКОГО сдвига, а не от Velocity.
// Тикает SwarmNav ТОЛЬКО этот скрипт - не вешайте рядом другой, который тоже вызывает Tick.
public class SwarmWalkerExample : MonoBehaviour
{
    [Header("SwarmNav")]
    [SerializeField] private SwarmNavSettings settings;

    [Header("Crowd")]
    [SerializeField] private GameObject prefab;          // с Animator и float-параметром "Speed"
    [SerializeField] private int count = 200;
    [SerializeField] private float spread = 40f;
    [SerializeField] private int seed = 1;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private float stoppingDistance = 1f;
    [SerializeField] private float turnSpeedDegrees = 240f;
    [SerializeField] private Vector2 moveDurationRange = new Vector2(3f, 7f);
    [SerializeField] private Vector2 pauseDurationRange = new Vector2(1f, 3f);

    [Header("Goals")]
    [SerializeField, Range(1, 64)] private int goalGroups = 6;
    [SerializeField] private float goalRepickSeconds = 8f;

    [Header("Animation")]
    [SerializeField] private string speedParameter = "Speed";
    [SerializeField] private float animSmoothing = 10f;

    private class Agent
    {
        public TransformHandle Body;
        public Animator Anim;
        public SwarmNavHandle Handle;
        public int Group;
        public float Clock;          // время внутри цикла идёт/стоит
        public float MoveTime;
        public float PauseTime;
        public bool Paused;
        public float3 LastPosition;
        public float SmoothedSpeed;  // 0..1 для blend tree
    }

    private SwarmNav _nav;
    private int _focusId;
    private System.Random _rng;
    private Agent[] _agents;
    private float3[] _goals;
    private float[] _goalTimers;
    private int _speedHash;

    private void Start()
    {
        if (prefab == null)
        {
            Debug.LogError("SwarmWalkerExample: назначьте prefab.");
            enabled = false;
            return;
        }

        if (settings == null) settings = SwarmNavSettings.CreateDefault();
        _rng = new System.Random(seed);
        _speedHash = Animator.StringToHash(speedParameter);
        _nav = new SwarmNav(settings, count);

        // Без focus-точки ВСЕ агенты - Far (обновляются раз в несколько кадров).
        _focusId = _nav.RegisterFocusPoint(FocusPosition());

        _goals = new float3[goalGroups];
        _goalTimers = new float[goalGroups];
        for (var g = 0; g < goalGroups; g++)
        {
            _goals[g] = RandomNavMeshPoint();
            _goalTimers[g] = NextRepickInterval();
        }

        _agents = new Agent[count];
        for (var i = 0; i < count; i++)
        {
            var handle = _nav.RegisterAgent(RandomNavMeshPoint(), moveSpeed, stoppingDistance);
            if (!_nav.IsHandleValid(handle)) continue;

            var spawn = _nav.GetAgentPosition(handle);
            var go = Instantiate(prefab, (Vector3)spawn, Quaternion.identity, transform);
            var agent = new Agent
            {
                Body = go.transformHandle,
                Anim = go.GetComponentInChildren<Animator>(),
                Handle = handle,
                Group = _rng.Next(goalGroups),
                MoveTime = Mathf.Lerp(moveDurationRange.x, moveDurationRange.y, (float)_rng.NextDouble()),
                PauseTime = Mathf.Lerp(pauseDurationRange.x, pauseDurationRange.y, (float)_rng.NextDouble()),
                LastPosition = spawn
            };
            agent.Clock = (float)_rng.NextDouble() * (agent.MoveTime + agent.PauseTime); // расфазировка

            // Агентов с одним ключом (1 + группа) - одна общая цель, пути у каждого свои.
            _nav.SetSharedGoal(handle, _goals[agent.Group], 1 + agent.Group);
            _agents[i] = agent;
        }
    }

    private void Update()
    {
        var dt = Time.deltaTime;
        if (dt <= 0f) return;

        // 1. Цели групп переставляются по таймеру (с разбросом, чтобы группы были несинхронны).
        for (var g = 0; g < goalGroups; g++)
        {
            _goalTimers[g] -= dt;
            if (_goalTimers[g] > 0f) continue;
            _goals[g] = RandomNavMeshPoint();
            _nav.UpdateSharedGoalPosition(1 + g, _goals[g]);
            _goalTimers[g] = NextRepickInterval();
        }

        // 2. Фокус LOD (камера) и ОДИН Tick на весь SwarmNav.
        _nav.UpdateFocusPoint(_focusId, FocusPosition());
        _nav.Tick(dt);

        // 3. Применяем результат к каждому агенту.
        var maxTurn = turnSpeedDegrees * dt;
        for (var i = 0; i < _agents.Length; i++)
        {
            var a = _agents[i];
            if (a == null) continue;

            a.Clock += dt;
            var cycle = a.MoveTime + a.PauseTime;
            if (a.Clock >= cycle) a.Clock -= cycle;
            var paused = a.Clock >= a.MoveTime;
            if (paused != a.Paused)
            {
                a.Paused = paused;
                _nav.SetStopped(a.Handle, paused);
            }

            float3 position = _nav.GetAgentPosition(a.Handle);
            float3 steer = _nav.GetSteeringDirection(a.Handle);    // направление шага, длина 0..1

            var flat = new Vector3(steer.x, 0f, steer.z);
            if (flat.sqrMagnitude > 1e-4f)
            {
                var targetRot = Quaternion.LookRotation(flat, Vector3.up);
                a.Body.rotation = Quaternion.RotateTowards(a.Body.rotation, targetRot, maxTurn);
            }
            a.Body.position = (Vector3)position;

            // Анимация от фактического сдвига: упёрся в соседа/стоит на паузе - скорость падает сама.
            var actualSpeed = math.distance(a.LastPosition.xz, position.xz) / dt;
            a.LastPosition = position;
            var target01 = Mathf.Clamp01(actualSpeed / moveSpeed);
            a.SmoothedSpeed = Mathf.Lerp(a.SmoothedSpeed, target01, 1f - Mathf.Exp(-animSmoothing * dt));
            if (a.Anim != null) a.Anim.SetFloat(_speedHash, a.SmoothedSpeed);
        }
    }

    private void OnDestroy() => _nav?.Dispose();

    // ---- helpers ----

    private float3 FocusPosition()
    {
        var cam = Camera.main;
        return cam != null ? (float3)cam.transformHandle.position : (float3)transformHandle.position;
    }

    private float NextRepickInterval() =>
        Mathf.Max(0.5f, goalRepickSeconds) * (0.5f + (float)_rng.NextDouble());

    // Случайная точка в квадрате вокруг этого объекта, притянутая к ближайшей точке NavMesh.
    private float3 RandomNavMeshPoint()
    {
        var center = transformHandle.position;
        var p = new Vector3(
            center.x + (float)(_rng.NextDouble() * 2.0 - 1.0) * spread, center.y,
            center.z + (float)(_rng.NextDouble() * 2.0 - 1.0) * spread);

        return NavMesh.SamplePosition(p, out var hit, 50f, NavMesh.AllAreas) ? (float3)hit.position : (float3)center;
    }
}
