using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace PG.SwarmNavigation
{
    // Проверка публичного API SwarmAgent на живой сцене: путь, прибытие, Stop, ResetPath, Warp, недостижимая цель,
    // агент вне NavMesh. Повесьте на пустой GameObject и нажмите Play; итог - одна запись "SAT" в консоли.
    public class SwarmAgentApiTest : MonoBehaviour
    {
        [SerializeField] private SwarmNavSettings settings;
        [SerializeField] private float speed = 6f;
        [SerializeField] private float baseOffset = 0.5f;

        private readonly StringBuilder _log = new StringBuilder();
        private int _failed;

        private IEnumerator Start()
        {
            if (settings == null) settings = SwarmNavSettings.CreateDefault();

            Vector3 start = RandomPoint(Vector3.zero, 1000f, 10f);
            SwarmAgent agent = Spawn("SAT_Agent", start);

            // 1. Путь и прибытие.
            Vector3 goal = RandomPoint(start, 60f, 25f);
            agent.SetDestination(goal);
            Check("после SetDestination hasDestination", agent.hasDestination);
            Check("после SetDestination pathPending или Complete", agent.pathPending || agent.pathStatus == SwarmPathStatus.Complete);
            float t = 0f;
            while (agent.hasDestination && t < 40f) { t += Time.deltaTime; yield return null; }
            float finalDistance = Vector3.Distance(Flat(agent.transform.position), Flat(goal));
            Check($"дошёл за {t:F1} с, остаток {finalDistance:F2} м (stoppingDistance {agent.stoppingDistance})", !agent.hasDestination && finalDistance <= agent.stoppingDistance + 1.5f);
            Check("pathStatus Complete после прибытия", agent.pathStatus == SwarmPathStatus.Complete);

            // 2. Агент стоит на поверхности NavMesh с учётом baseOffset.
            Vector3 p = agent.transform.position;
            bool onMesh = NavMesh.SamplePosition(p - Vector3.up * baseOffset, out NavMeshHit hit, 1f, NavMesh.AllAreas);
            float dy = onMesh ? Mathf.Abs(hit.position.y - (p.y - baseOffset)) : float.NaN;
            Check($"высота = NavMesh + baseOffset (расхождение {dy:F3} м)", onMesh && dy < 0.15f);

            // 3. Stop / isStopped.
            Vector3 far = RandomPoint(p, 80f, 40f);
            agent.SetDestination(far);
            yield return new WaitForSeconds(1f);
            agent.isStopped = true;
            yield return new WaitForSeconds(0.4f);   // показанная позиция дальнего агента ещё догоняет настоящую (LodSmoothing)
            Vector3 stopped = agent.transform.position;
            yield return new WaitForSeconds(1f);
            Check("isStopped: позиция не меняется", Vector3.Distance(stopped, agent.transform.position) < 0.05f);
            agent.isStopped = false;
            yield return new WaitForSeconds(1f);
            Check("isStopped = false: снова идёт", Vector3.Distance(stopped, agent.transform.position) > 0.5f);

            // 4. ResetPath.
            agent.ResetPath();
            Check("ResetPath: hasDestination = false", !agent.hasDestination);
            yield return new WaitForSeconds(0.4f);
            Vector3 afterReset = agent.transform.position;
            yield return new WaitForSeconds(0.5f);
            Check("ResetPath: агент остановился", Vector3.Distance(afterReset, agent.transform.position) < 0.3f);

            // 5. Warp.
            Vector3 warpTarget = RandomPoint(p, 200f, 100f);
            agent.Warp(warpTarget + Vector3.up * baseOffset);
            yield return null;
            Check($"Warp: позиция {Vector3.Distance(Flat(agent.transform.position), Flat(warpTarget)):F2} м от цели", Vector3.Distance(Flat(agent.transform.position), Flat(warpTarget)) < 1.5f);
            Check("Warp: isOnNavMesh", agent.isOnNavMesh);

            // 6. Недостижимая цель: путь Partial, агент доходит до ближайшей достижимой точки и останавливается.
            if (TryFindUnreachable(agent.transform.position, out Vector3 unreachable))
            {
                agent.SetDestination(unreachable);
                yield return new WaitForSeconds(0.3f);
                SwarmPathStatus status = agent.pathStatus;
                Check($"недостижимая цель: pathStatus {status} (ожидается Partial или Pending)", status == SwarmPathStatus.Partial || status == SwarmPathStatus.Pending);
                t = 0f;
                float nextSample = 0f;
                while (agent.hasDestination && t < 120f)
                {
                    t += Time.deltaTime;
                    if (t >= nextSample)
                    {
                        nextSample += 10f;
                        _log.AppendLine($"    t={t:F0} pos={agent.transform.position} цель={unreachable} остаток={agent.remainingDistance:F1} статус={agent.pathStatus} v={agent.velocity}");
                    }
                    yield return null;
                }
                Check($"недостижимая цель: остановился за {t:F1} с (цель {Vector3.Distance(Flat(agent.transform.position), Flat(unreachable)):F0} м по прямой в конце), pathStatus {agent.pathStatus}", !agent.hasDestination && agent.pathStatus == SwarmPathStatus.Partial);
            }
            else
            {
                _log.AppendLine("  (недостижимая цель: на сцене нет изолированных областей NavMesh - пропуск)");
            }

            // 7. Агент вне NavMesh: без исключений, isOnNavMesh = false, pathStatus Invalid.
            SwarmAgent lost = Spawn("SAT_Lost", new Vector3(50000f, 0f, 50000f));
            Check("вне NavMesh: isOnNavMesh = false", !lost.isOnNavMesh);
            lost.SetDestination(start);
            yield return new WaitForSeconds(0.3f);
            Check($"вне NavMesh: pathStatus {lost.pathStatus} (ожидается Invalid)", lost.pathStatus == SwarmPathStatus.Invalid);

            // 8. Удаление агента не ломает остальных.
            Destroy(lost.gameObject);
            yield return null;
            Check("после Destroy другого агента первый жив", agent != null && agent.isOnNavMesh);

            Debug.Log($"SAT {(_failed == 0 ? "ВСЁ ПРОШЛО" : _failed + " ОШИБОК")}\n{_log}");
            Destroy(agent.gameObject);
        }

        private SwarmAgent Spawn(string objectName, Vector3 position)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = objectName;
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one;
            go.transform.position = position + Vector3.up * baseOffset;

            SwarmAgent agent = go.AddComponent<SwarmAgent>();
            agent.Configure(settings);
            agent.baseOffset = baseOffset;
            agent.speed = speed;
            return agent;
        }

        private void Check(string what, bool ok)
        {
            if (!ok) _failed++;
            _log.AppendLine($"  {(ok ? "OK  " : "FAIL")} {what}");
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // Случайная точка NavMesh на расстоянии minDistance..radius от center.
        private static Vector3 RandomPoint(Vector3 center, float radius, float minDistance)
        {
            for (int i = 0; i < 200; i++)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 30f, NavMesh.AllAreas) && Vector3.Distance(Flat(hit.position), Flat(center)) >= minDistance)
                {
                    return hit.position;
                }
            }
            return center;
        }

        // Ближайшая к from точка NavMesh, до которой нет пути (изолированный остров): дальние идти до ответа слишком долго.
        private static bool TryFindUnreachable(Vector3 from, out Vector3 point)
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            NavMeshPath path = new NavMeshPath();
            bool found = false;
            point = default;
            float best = float.MaxValue;
            for (int i = 0; i < 1500; i++)
            {
                int t = Random.Range(0, tri.indices.Length / 3) * 3;
                Vector3 candidate = (tri.vertices[tri.indices[t]] + tri.vertices[tri.indices[t + 1]] + tri.vertices[tri.indices[t + 2]]) / 3f;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas)) continue;

                NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, path);
                float distance = Vector3.Distance(Flat(from), Flat(hit.position));
                if (distance < best && path.status == NavMeshPathStatus.PathPartial)
                {
                    best = distance;
                    point = hit.position;
                    found = true;
                }
            }
            return found;
        }
    }
}
