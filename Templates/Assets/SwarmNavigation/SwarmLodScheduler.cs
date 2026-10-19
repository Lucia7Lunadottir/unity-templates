using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace PG.SwarmNavigation
{
    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct SwarmLodSettings
    {
        [Tooltip("Метры от фокуса (камера с тегом из LodFocusTag). Внутри этого радиуса агенты обновляются КАЖДЫЙ кадр.")]
        public float NearRadius;
        [Tooltip("Метры от фокуса. Между NearRadius и MidRadius - Mid, дальше - Far.")]
        public float MidRadius;
        [Tooltip("Mid обновляется раз в N кадров (1 = каждый кадр).")]
        [Min(1)] public int MidUpdateEveryNFrames;
        [Tooltip("Far обновляется раз в N кадров (1 = каждый кадр). Чем больше N, тем заметнее скачки движения.")]
        [Min(1)] public int FarUpdateEveryNFrames;

        public static SwarmLodSettings Default => new SwarmLodSettings
        {
            NearRadius = 60f,
            MidRadius = 150f,
            MidUpdateEveryNFrames = 2,
            FarUpdateEveryNFrames = 4
        };
    }

    // Near - каждый кадр; Mid/Far - раз в N кадров ЦЕЛИКОМ по тиру (frame-modulo-per-tier), не по
    // таймеру на агента - O(1) проверка "обновляется ли тир в этом кадре", без per-agent состояния
    // сверх самого Tier (уже есть в SwarmAgentState). Тараканья дешевизна: далёким агентам визуальный
    // "пропуск кадра" не заметен, экономия реальна - см. Context в плане.
    public class SwarmLodScheduler : System.IDisposable
    {
        public SwarmLodSettings Settings { get; set; }

        private NativeList<float3> _focusPositions;
        private NativeList<int> _focusIds;

        public SwarmLodScheduler(SwarmLodSettings settings)
        {
            Settings = settings;
            _focusPositions = new NativeList<float3>(4, Allocator.Persistent);
            _focusIds = new NativeList<int>(4, Allocator.Persistent);
        }

        public void RegisterFocusPoint(int focusId, float3 worldPos) => UpdateFocusPoint(focusId, worldPos);

        public void UpdateFocusPoint(int focusId, float3 worldPos)
        {
            for (int i = 0; i < _focusIds.Length; i++)
            {
                if (_focusIds[i] != focusId) continue;
                _focusPositions[i] = worldPos;
                return;
            }
            _focusIds.Add(focusId);
            _focusPositions.Add(worldPos);
        }

        public void UnregisterFocusPoint(int focusId)
        {
            for (int i = 0; i < _focusIds.Length; i++)
            {
                if (_focusIds[i] != focusId) continue;
                _focusIds.RemoveAtSwapBack(i);
                _focusPositions.RemoveAtSwapBack(i);
                return;
            }
        }

        // Раз в кадр: пишет Tier прямо в SwarmAgentState (брутфорс по маленькому списку фокус-точек -
        // их единицы, полноценный spatial hash здесь избыточен, см. план).
        public JobHandle ScheduleTierUpdates(NativeArray<SwarmAgentState> agents, int agentCount, JobHandle dependency)
        {
            TierJob job = new TierJob
            {
                Agents = agents,
                FocusPositions = _focusPositions.AsArray(),
                NearRadius = Settings.NearRadius,
                MidRadius = Settings.MidRadius
            };
            return job.Schedule(agentCount, 64, dependency);
        }

        public bool ShouldUpdateTier(SwarmLodTier tier, int frameNumber)
        {
            switch (tier)
            {
                case SwarmLodTier.Near:
                    return true;
                case SwarmLodTier.Mid:
                    return Settings.MidUpdateEveryNFrames <= 1 || frameNumber % Settings.MidUpdateEveryNFrames == 0;
                default:
                    return Settings.FarUpdateEveryNFrames <= 1 || frameNumber % Settings.FarUpdateEveryNFrames == 0;
            }
        }

        public void Dispose()
        {
            if (_focusPositions.IsCreated) _focusPositions.Dispose();
            if (_focusIds.IsCreated) _focusIds.Dispose();
        }

        [BurstCompile]
        private struct TierJob : IJobParallelFor
        {
            public NativeArray<SwarmAgentState> Agents;
            [ReadOnly] public NativeArray<float3> FocusPositions;
            public float NearRadius;
            public float MidRadius;

            public void Execute(int index)
            {
                SwarmAgentState agent = Agents[index];

                // Свободный слот (далеко от мира) всегда Far; без фокус-точки живые агенты считаются Near (полное качество).
                if (agent.Position.x > 1e5f)
                {
                    agent.Tier = SwarmLodTier.Far;
                    Agents[index] = agent;
                    return;
                }

                if (FocusPositions.Length == 0)
                {
                    agent.Tier = SwarmLodTier.Near;
                    Agents[index] = agent;
                    return;
                }

                float nearestDistSq = float.MaxValue;
                for (int f = 0; f < FocusPositions.Length; f++)
                {
                    float distSq = math.distancesq(agent.Position, FocusPositions[f]);
                    if (distSq < nearestDistSq) nearestDistSq = distSq;
                }

                if (nearestDistSq <= NearRadius * NearRadius) agent.Tier = SwarmLodTier.Near;
                else if (nearestDistSq <= MidRadius * MidRadius) agent.Tier = SwarmLodTier.Mid;
                else agent.Tier = SwarmLodTier.Far;

                Agents[index] = agent;
            }
        }
    }
}
