using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

namespace PG.SwarmNavigation
{
    // Постоянный тест нагрузки - кидается на пустой GameObject в сцене, жмёте Play, и всё. Сам спавнит agentCount
    // настоящих кубов с SwarmAgent, раздаёт им случайные цели на NavMesh и раз в cycleSeconds меняет (порциями по
    // кадрам). Один Update() на весь тест, не на агента. Раз в reportEvery кадров пишет в консоль "SWT ..." с
    // перцентилями времени ожидания джобов навигации (p50/p90/p99): целый FPS сцены для оценки навигации не годится.
    [DefaultExecutionOrder(1000)]
    public class SwarmCrowdStressTest : MonoBehaviour
    {
        [SerializeField] private SwarmNavSettings settings;

        [Header("Толпа")]
        [SerializeField] private int agentCount = 20000;
        [SerializeField] private float agentScale = 0.5f;
        [SerializeField] private float agentSpeed = 3.5f;
        [SerializeField] private int goalPoolSize = 2000;
        [Tooltip("Раз в сколько секунд агенты получают новые цели (порциями по кадрам).")]
        [SerializeField] private float cycleSeconds = 14f;

        [Tooltip("Погоня: первый агент (крупный) бродит между случайными целями, все остальные гонятся за ним.")]
        [SerializeField] private bool chaseMode;

        [Header("Отчёт")]
        [SerializeField] private int reportEvery = 120;
        [Tooltip("Замер по стадиям кадра навигации (без перекрытия с остальным кодом).")]
        [SerializeField] private bool profileStages;

        public static string LastReport = "";
        public static bool ForceProfile; // для запуска через eval до AddComponent
        public static bool ForceChase;
        public static System.Action<SwarmNavSettings> SettingsHook;   // для запуска через eval: подмена настроек до создания агентов
        public static int ForceAgentCount;


        private SwarmAgent[] _agents;
        private Vector3[] _goals;
        private System.Random _rng;
        private int _nextAssign;
        private int _frames;
        private double _frameMsSum;
        private readonly List<float> _waits = new List<float>(256);

        private void Awake()
        {
            if (ForceAgentCount > 0) agentCount = ForceAgentCount;
            if (settings == null) settings = SwarmNavSettings.CreateDefault();
            SettingsHook?.Invoke(settings);
            SwarmNavRuntime.MaxAgentsHint = Mathf.Max(SwarmNavRuntime.MaxAgentsHint, agentCount + 64);
            _rng = new System.Random(12345);

            _goals = new Vector3[goalPoolSize];
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            for (int g = 0; g < goalPoolSize; g++) _goals[g] = RandomOnMesh(tri);

            GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = template.GetComponent<MeshFilter>().sharedMesh;
            Material material = template.GetComponent<MeshRenderer>().sharedMaterial;
            Destroy(template);

            _agents = new SwarmAgent[agentCount];
            for (int i = 0; i < agentCount; i++)
            {
                GameObject go = new GameObject("SwarmAgent_" + i, typeof(MeshFilter), typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
                go.transform.localScale = Vector3.one * agentScale;
                go.transform.position = RandomOnMesh(tri) + Vector3.up * (agentScale * 0.5f);

                SwarmAgent agent = go.AddComponent<SwarmAgent>();
                agent.Configure(settings);
                agent.baseOffset = agentScale * 0.5f;
                agent.speed = agentSpeed;
                _agents[i] = agent;
            }

            profileStages |= ForceProfile;
            chaseMode |= ForceChase;
            if (chaseMode) StartChase();
            SwarmNav.Instance.ProfileStages = profileStages;
            Debug.Log($"SWT готов: {agentCount} агентов, пул целей {goalPoolSize}");
        }

        // Агент 0 - цель погони: крупнее и быстрее, остальные идут за его Transform.
        private void StartChase()
        {
            _agents[0].transform.localScale = Vector3.one * agentScale * 3f;
            _agents[0].baseOffset = agentScale * 1.5f;
            _agents[0].speed = agentSpeed * 0.6f; // медленнее преследователей, чтобы они могли догнать
            _agents[0].SetDestination(_goals[_rng.Next(goalPoolSize)]);

            TransformHandle target = _agents[0].gameObject.transformHandle;
            for (int i = 1; i < agentCount; i++) _agents[i].SetDestination(target);
        }

        private void Update()
        {
            if (chaseMode)
            {
                if (!_agents[0].hasDestination) _agents[0].SetDestination(_goals[_rng.Next(goalPoolSize)]);
                return;
            }

            // Порция агентов за кадр так, чтобы полный круг занимал cycleSeconds.
            int perFrame = Mathf.Max(1, Mathf.CeilToInt(agentCount * Time.deltaTime / Mathf.Max(1f, cycleSeconds)));
            for (int n = 0; n < perFrame; n++)
            {
                _agents[_nextAssign].SetDestination(_goals[_rng.Next(goalPoolSize)]);
                _nextAssign = (_nextAssign + 1) % agentCount;
            }
        }

        private void LateUpdate()
        {
            SwarmNav nav = SwarmNav.Instance;
            if (nav == null) return;

            _waits.Add((float)nav.LastWaitMs);
            _frames++;
            _frameMsSum += Time.unscaledDeltaTime * 1000.0;
            if (_frames < reportEvery) return;

            int moving = 0;
            var agents = nav.Agents;
            for (int i = 0; i < nav.HighWaterMark; i++)
            {
                if (math.lengthsq(agents[i].Velocity) > 1e-6f) moving++;
            }

            _waits.Sort();
            float Pct(float q) => _waits[Mathf.Min(_waits.Count - 1, (int)(q * _waits.Count))];
            LastReport = $"SWT N={agentCount} кадр {_frameMsSum / _frames:F2} мс ({1000.0 / (_frameMsSum / _frames):F0} FPS) | " +
                         $"ожидание навигации p50 {Pct(0.5f):F2} p90 {Pct(0.9f):F2} p99 {Pct(0.99f):F2} макс {Pct(1f):F1} мс | " +
                         $"путей/кадр {nav.PathsLastFrame} | идут {moving}";
            if (chaseMode)
            {
                float3 t = agents[0].Position; // слот 0 - цель погони (регистрируется первой)
                var dist = new List<float>(agentCount);
                for (int i = 0; i < nav.HighWaterMark; i++)
                {
                    if (agents[i].Position.x > 1e5f) continue;
                    dist.Add(math.distance(agents[i].Position.xz, t.xz));
                }
                dist.Sort();
                int near30 = 0;
                foreach (float d in dist) if (d <= 30f) near30++;
                LastReport += $" | ПОГОНЯ расстояние до цели p10 {dist[dist.Count / 10]:F0} p50 {dist[dist.Count / 2]:F0} p90 {dist[dist.Count * 9 / 10]:F0} м, ближе 30 м: {near30}";
            }
            if (profileStages)
            {
                var sb = new System.Text.StringBuilder(" | СТАДИИ:");
                for (int k = 0; k < nav.StageMs.Length; k++) sb.Append($" {SwarmNav.StageNames[k]} {nav.StageMs[k]:F2};");
                LastReport += sb.ToString();
            }
            Debug.Log(LastReport);
            _frames = 0;
            _frameMsSum = 0;
            _waits.Clear();
        }

        private void OnDestroy()
        {
            if (_agents == null) return;
            foreach (SwarmAgent agent in _agents) if (agent != null) Destroy(agent.gameObject);
        }

        private Vector3 RandomOnMesh(NavMeshTriangulation tri)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int t = _rng.Next(tri.indices.Length / 3) * 3;
                Vector3 a = tri.vertices[tri.indices[t]], b = tri.vertices[tri.indices[t + 1]], c = tri.vertices[tri.indices[t + 2]];
                float r1 = Mathf.Sqrt((float)_rng.NextDouble()), r2 = (float)_rng.NextDouble();
                Vector3 p = a * (1f - r1) + b * (r1 * (1f - r2)) + c * (r1 * r2);
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 20f, NavMesh.AllAreas)) return hit.position;
            }
            return Vector3.zero;
        }
    }
}
