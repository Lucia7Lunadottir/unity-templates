using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace PG.SwarmNavigation
{
    // Разведение агентов на GPU (SwarmAvoidance.compute): раз в кадр позиции и флаги уходят в GraphicsBuffer, три
    // диспатча строят uniform grid и считают толчок/давление/касания на каждого агента, результат возвращается на CPU
    // через AsyncGPUReadback. Результат запаздывает на 1-3 кадра - при скоростях агентов это доли метра.
    //
    // Результат приходит в колбэк readback, копируется в промежуточный массив (его джобы не читают), а в Latest - в Poll(),
    // когда джобы не идут. Свои NativeArray в API readback не передаются: он требует массивы без защиты джобов.
    public sealed class SwarmAvoidanceGpu : System.IDisposable
    {
        public const uint FlagActive = 1u;
        public const uint FlagArrived = 2u;
        private const int MaxPending = 3;

        private readonly ComputeShader _shader;
        private readonly int _kernelClear, _kernelBuild, _kernelSolve;
        private readonly uint _tableMask;
        private GraphicsBuffer _agents, _heads, _next, _result;
        private NativeArray<float4> _latest;     // то, что читают джобы: меняется только в Poll(), когда джобы не идут
        private NativeArray<float4> _staging;    // сюда копирует колбэк readback (джобы его не читают)
        private int _stagingCount;
        private int _pending;
        private bool _disposed;

        // Последний готовый результат: x,y - толчок по XZ, z - давление (соседей в радиусе), w - касаний прибывших своей группы.
        public NativeArray<float4> Latest => _latest;

        public static SwarmAvoidanceGpu TryCreate(int maxAgents)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback) return null;

            ComputeShader shader = Resources.Load<ComputeShader>("SwarmAvoidance");
            if (shader == null)
            {
                Debug.LogWarning("SwarmNav: не найден SwarmAvoidance.compute в Resources - разведение на CPU.");
                return null;
            }
            return new SwarmAvoidanceGpu(shader, maxAgents);
        }

        private SwarmAvoidanceGpu(ComputeShader shader, int maxAgents)
        {
            _shader = shader;
            _kernelClear = shader.FindKernel("CSClear");
            _kernelBuild = shader.FindKernel("CSBuild");
            _kernelSolve = shader.FindKernel("CSSolve");

            uint table = 65536;
            while (table < (uint)maxAgents * 2u) table <<= 1;
            _tableMask = table - 1;

            _agents = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxAgents, 16);
            _heads = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)table, 4);
            _next = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxAgents, 4);
            _result = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxAgents, 16);

            _latest = new NativeArray<float4>(maxAgents, Allocator.Persistent);
            _staging = new NativeArray<float4>(maxAgents, Allocator.Persistent);
        }

        // Переносит последний пришедший результат в Latest. Вызывать, когда джобы не идут: Latest читают только они.
        public void Poll()
        {
            if (_stagingCount <= 0) return;

            NativeArray<float4>.Copy(_staging, 0, _latest, 0, _stagingCount);
            _stagingCount = 0;
        }

        private void OnReadback(AsyncGPUReadbackRequest request)
        {
            _pending--;
            if (_disposed || request.hasError) return;

            NativeArray<float4> data = request.GetData<float4>();
            NativeArray<float4>.Copy(data, 0, _staging, 0, math.min(data.Length, _staging.Length));
            _stagingCount = math.min(data.Length, _staging.Length);
        }

        // Загружает позиции/флаги, считает разведение на GPU и ставит readback результата.
        public void Dispatch(NativeArray<float4> agents, int count, float avoidanceRadius, float contactDistance, int maxNeighbors)
        {
            if (_disposed || count <= 0) return;

            _agents.SetData(agents, 0, 0, count);

            _shader.SetInt("AgentCount", count);
            _shader.SetInt("TableMask", (int)_tableMask);
            _shader.SetFloat("CellSize", avoidanceRadius);
            _shader.SetFloat("Radius", avoidanceRadius);
            _shader.SetFloat("ContactDistance", contactDistance);
            _shader.SetInt("MaxNeighbors", maxNeighbors);
            _shader.SetFloat("MaxHeightDelta", 2f);

            _shader.SetBuffer(_kernelClear, "Heads", _heads);
            _shader.Dispatch(_kernelClear, (int)((_tableMask + 256u) / 256u), 1, 1);

            int groupsX = (count + 255) / 256;
            _shader.SetBuffer(_kernelBuild, "Heads", _heads);
            _shader.SetBuffer(_kernelBuild, "Next", _next);
            _shader.SetBuffer(_kernelBuild, "Agents", _agents);
            _shader.Dispatch(_kernelBuild, groupsX, 1, 1);

            _shader.SetBuffer(_kernelSolve, "Heads", _heads);
            _shader.SetBuffer(_kernelSolve, "Next", _next);
            _shader.SetBuffer(_kernelSolve, "Agents", _agents);
            _shader.SetBuffer(_kernelSolve, "Result", _result);
            _shader.Dispatch(_kernelSolve, groupsX, 1, 1);

            // Не копим очередь: если прошлые запросы ещё не вернулись, этот кадр без readback.
            if (_pending >= MaxPending) return;

            _pending++;
            AsyncGPUReadback.Request(_result, count * 16, 0, OnReadback);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            AsyncGPUReadback.WaitAllRequests();
            _agents?.Dispose();
            _heads?.Dispose();
            _next?.Dispose();
            _result?.Dispose();
            if (_latest.IsCreated) _latest.Dispose();
            if (_staging.IsCreated) _staging.Dispose();
        }
    }
}
