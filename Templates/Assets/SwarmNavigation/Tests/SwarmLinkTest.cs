using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace PG.SwarmNavigation
{
    // Проверка NavMeshLink на ядре SwarmNav: берётся изолированная часть NavMesh (остров), агент на основной сетке.
    // Без связи цель на острове недостижима (Partial), со связью агент должен дойти по ней и оказаться на острове.
    // Итог - одна запись "SLT" в консоли. Повесьте на пустой GameObject и нажмите Play (нужен остров в NavMesh).
    public class SwarmLinkTest : MonoBehaviour
    {
        [SerializeField] private float timeout = 120f;

        private IEnumerator Start()
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            int[] comp = Components(tri, out int main, out int island);
            if (island < 0) { Debug.Log("SLT: в NavMesh нет изолированных частей - проверка невозможна"); yield break; }

            // Центр острова и ближайшая к нему точка основной сетки -> концы связи.
            Vector3 islandSum = Vector3.zero; int islandTris = 0;
            for (int t = 0; t < comp.Length; t++)
            {
                if (comp[t] != island) continue;
                islandSum += Centroid(tri, t);
                islandTris++;
            }
            Vector3 islandCenter = islandSum / islandTris;
            Vector3 islandPoint = NavMesh.SamplePosition(islandCenter, out NavMeshHit ih, 20f, NavMesh.AllAreas) ? ih.position : islandCenter;

            float bestDistance = float.MaxValue; Vector3 mainEdge = Vector3.zero;
            for (int t = 0; t < comp.Length; t++)
            {
                if (comp[t] != main) continue;
                Vector3 c = Centroid(tri, t);
                float d = (c - islandCenter).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; mainEdge = c; }
            }
            mainEdge = NavMesh.SamplePosition(mainEdge, out NavMeshHit mh, 20f, NavMesh.AllAreas) ? mh.position : mainEdge;

            // Старт агента: основная сетка, 60-150 м от места связи.
            Vector3 start = mainEdge;
            for (int t = 0; t < comp.Length; t++)
            {
                if (comp[t] != main) continue;
                Vector3 c = Centroid(tri, t);
                float d = Vector3.Distance(c, mainEdge);
                if (d > 60f && d < 150f && NavMesh.SamplePosition(c, out NavMeshHit sh, 5f, NavMesh.AllAreas)) { start = sh.position; break; }
            }

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "SLT_Agent";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = start + Vector3.up * 0.5f;
            SwarmAgent agent = go.AddComponent<SwarmAgent>();
            agent.baseOffset = 0.5f;
            agent.speed = 8f;

            // 1. Без связи.
            agent.SetDestination(islandPoint);
            yield return new WaitForSeconds(0.5f);
            SwarmPathStatus before = agent.pathStatus;
            agent.ResetPath();
            yield return null;

            // 2. Создаём связь и ждём, пока NavWorld её подхватит.
            GameObject linkObject = new GameObject("SLT_Link");
            linkObject.transform.position = (mainEdge + islandPoint) * 0.5f;
            NavMeshLink link = linkObject.AddComponent<NavMeshLink>();
            link.startPoint = linkObject.transform.InverseTransformPoint(mainEdge);
            link.endPoint = linkObject.transform.InverseTransformPoint(islandPoint);
            link.width = 1.5f;
            link.bidirectional = true;
            link.agentTypeID = 0;
            link.area = 0;
            link.UpdateLink();
            for (int f = 0; f < 10; f++) yield return null;

            // 3. Со связью.
            agent.SetDestination(islandPoint);
            yield return new WaitForSeconds(0.5f);
            SwarmPathStatus after = agent.pathStatus;
            float t0 = Time.time;
            while (agent.hasDestination && Time.time - t0 < timeout) yield return null;

            Vector3 end = go.transform.position - Vector3.up * 0.5f;
            float distance = Vector3.Distance(new Vector3(end.x, 0f, end.z), new Vector3(islandPoint.x, 0f, islandPoint.z));
            bool onIsland = distance < 4f && Mathf.Abs(end.y - islandPoint.y) < 3f;
            Debug.Log($"SLT: без связи pathStatus {before} (ожидается Partial); со связью pathStatus {after} (ожидается Complete или Pending); " +
                      $"дошёл за {Time.time - t0:F1} с, до острова {distance:F1} м по XZ, высота {end.y:F1} (остров {islandPoint.y:F1}) -> {(onIsland ? "ПРОШЛО" : "НЕ ПРОШЛО")}; " +
                      $"остров {islandTris} тр., связь {Vector3.Distance(mainEdge, islandPoint):F1} м");
            Destroy(linkObject);
            Destroy(go);
        }

        private static Vector3 Centroid(NavMeshTriangulation tri, int t) =>
            (tri.vertices[tri.indices[t * 3]] + tri.vertices[tri.indices[t * 3 + 1]] + tri.vertices[tri.indices[t * 3 + 2]]) / 3f;

        // Компоненты связности NavMesh по общим вершинам: основная (самая большая) и самый большой остров.
        private static int[] Components(NavMeshTriangulation tri, out int main, out int island)
        {
            int triCount = tri.indices.Length / 3;
            var weld = new Dictionary<(int, int, int), int>();
            int[] vid = new int[tri.vertices.Length];
            for (int i = 0; i < vid.Length; i++)
            {
                Vector3 v = tri.vertices[i];
                (int, int, int) key = (Mathf.RoundToInt(v.x * 100f), Mathf.RoundToInt(v.y * 100f), Mathf.RoundToInt(v.z * 100f));
                if (!weld.TryGetValue(key, out int id)) { id = weld.Count; weld[key] = id; }
                vid[i] = id;
            }

            int[] parent = new int[weld.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int t = 0; t < triCount; t++)
            {
                int a = vid[tri.indices[t * 3]], b = vid[tri.indices[t * 3 + 1]], c = vid[tri.indices[t * 3 + 2]];
                parent[Find(a)] = Find(b);
                parent[Find(b)] = Find(c);
            }

            int[] comp = new int[triCount];
            var sizes = new Dictionary<int, int>();
            for (int t = 0; t < triCount; t++)
            {
                comp[t] = Find(vid[tri.indices[t * 3]]);
                sizes[comp[t]] = sizes.TryGetValue(comp[t], out int sz) ? sz + 1 : 1;
            }

            main = -1; island = -1;
            foreach (var kv in sizes) if (main < 0 || kv.Value > sizes[main]) main = kv.Key;
            foreach (var kv in sizes) if (kv.Key != main && (island < 0 || kv.Value > sizes[island])) island = kv.Key;
            return comp;
        }
    }
}
