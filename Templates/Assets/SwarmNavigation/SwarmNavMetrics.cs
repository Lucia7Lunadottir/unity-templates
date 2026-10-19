using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;

namespace PG.SwarmNavigation
{
    // Диагностика качества толпы (для тестов и eval): сколько агентов налезает друг на друга, сколько стоит, как далеко от цели.
    // Считается на главном потоке за O(N) через хэш-сетку - вызывать раз в секунды, не каждый кадр.
    public static class SwarmNavMetrics
    {
        public static string Report(SwarmNav nav, float3 goal, float overlapDistance)
        {
            NativeArray<SwarmAgentState> agents = nav.Agents;
            int count = nav.HighWaterMark;

            var cells = new Dictionary<long, List<int>>(count);
            var alive = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                if (agents[i].Position.x > 1e5f) continue;

                alive.Add(i);
                float3 p = agents[i].Position;
                long key = ((long)(int)math.floor(p.x / overlapDistance) << 32) ^ (uint)(int)math.floor(p.z / overlapDistance);
                if (!cells.TryGetValue(key, out List<int> list)) cells[key] = list = new List<int>(4);
                list.Add(i);
            }

            int overlapping = 0, standing = 0, deep = 0;
            float overlapSq = overlapDistance * overlapDistance, deepSq = overlapSq * 0.25f;
            var distances = new List<float>(alive.Count);
            foreach (int i in alive)
            {
                float3 p = agents[i].Position;
                if (math.lengthsq(agents[i].Velocity) < 1e-6f) standing++;
                distances.Add(math.distance(p.xz, goal.xz));

                int cx = (int)math.floor(p.x / overlapDistance), cz = (int)math.floor(p.z / overlapDistance);
                bool hit = false, hitDeep = false;
                for (int dz = -1; dz <= 1 && !hitDeep; dz++)
                {
                    for (int dx = -1; dx <= 1 && !hitDeep; dx++)
                    {
                        long key = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);
                        if (!cells.TryGetValue(key, out List<int> list)) continue;

                        foreach (int j in list)
                        {
                            if (j == i) continue;
                            float3 d = p - agents[j].Position;
                            d.y = 0f;
                            float dSq = math.lengthsq(d);
                            if (dSq < overlapSq) hit = true;
                            if (dSq < deepSq) { hitDeep = true; break; }
                        }
                    }
                }
                if (hit) overlapping++;
                if (hitDeep) deep++;
            }

            distances.Sort();
            int n = math.max(1, distances.Count);
            var sb = new StringBuilder();
            sb.Append($"агентов {alive.Count}, стоят {standing} ({100f * standing / n:F0}%), ");
            sb.Append($"ближе {overlapDistance:F2} м к соседу: {100f * overlapping / n:F1}%, ближе {overlapDistance * 0.5f:F2} м (глубокое наложение): {100f * deep / n:F1}%, ");
            sb.Append($"расстояние до цели p50 {distances[n / 2]:F0} p90 {distances[n * 9 / 10]:F0} макс {distances[n - 1]:F0} м");
            return sb.ToString();
        }
    }
}
