using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

namespace PG.Rendering
{
    // GPU-driven вариант раскида "луч вниз -> инстанс" (см. GrassRenderer.BuildInstancesByRaycastScatter)
    // поверх Unity 6 UnifiedRayTracing (аппаратный backend или compute-fallback - работает без
    // RTX). Все лучи трассируются разом на GPU вместо цикла Physics.Raycast на CPU.
    // Нормаль поверхности UnifiedRT::Hit не отдаёт напрямую (только hitDistance/instanceID/
    // primitiveIndex/barycentrics) - восстанавливается здесь на CPU из mesh.normals по
    // instanceID -> (mesh, submesh triangles, localToWorld), см. ResolveNormal. Итоговая сборка
    // InstanceData (scale/yaw/align-to-normal/slope-limit) - в GrassRenderer.ComposeInstance,
    // общая с CPU-путём.
    // "Restrict to own hierarchy" не нужен отдельным параметром - acceleration structure строится
    // ТОЛЬКО из переданных renderer'ов (уже отфильтрованных по иерархии/слою вызывающим кодом).
    public sealed class GpuRaycastScatterer : IDisposable
    {
        public readonly struct ScatterHit
        {
            public readonly Vector3 Position;
            public readonly Vector3 Normal;
            public readonly int CandidateIndex; // для детерминированных scale/yaw на стороне вызывающего (тот же индекс, что ушёл в шейдер)

            public ScatterHit(Vector3 position, Vector3 normal, int candidateIndex)
            {
                Position = position;
                Normal = normal;
                CandidateIndex = candidateIndex;
            }
        }

        // Layout обязан побайтово совпадать с RawHit в GrassGpuScatter.urtshader.
        [StructLayout(LayoutKind.Sequential)]
        private struct RawHit
        {
            public Vector3 Position;
            public Vector2 Barycentrics;
            public uint InstanceID;
            public uint PrimitiveIndex;
            public uint CandidateIndex;
        }
        private const int RawHitStride = 8 * sizeof(float); // 3 position + 2 barycentrics + 3 uint

        private readonly struct InstanceGeometry
        {
            public readonly Vector3[] Normals;
            public readonly int[] Triangles; // GetTriangles(submesh) - уже локальные для этого сабмеша индексы во ВСЕЙ Normals
            public readonly Matrix4x4 LocalToWorld;

            public InstanceGeometry(Vector3[] normals, int[] triangles, Matrix4x4 localToWorld)
            {
                Normals = normals;
                Triangles = triangles;
                LocalToWorld = localToWorld;
            }
        }

        public static bool IsSupported =>
            RayTracingContext.IsBackendSupported(RayTracingBackend.Hardware) ||
            RayTracingContext.IsBackendSupported(RayTracingBackend.Compute);

        private static readonly int RayCountId = Shader.PropertyToID("_RayCount");
        private static readonly int RayOriginYId = Shader.PropertyToID("_RayOriginY");
        private static readonly int RayDistanceId = Shader.PropertyToID("_RayDistance");
        private static readonly int AreaMinId = Shader.PropertyToID("_AreaMin");
        private static readonly int AreaSizeId = Shader.PropertyToID("_AreaSize");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int OutputId = Shader.PropertyToID("_Output");

        private readonly RayTracingResources _resources;
        private readonly RayTracingContext _context;
        private readonly IRayTracingShader _shader;
        private readonly IRayTracingAccelStruct _accelStruct;
        private readonly Dictionary<uint, InstanceGeometry> _instanceGeometry = new();

        // shader - импортированный .urtshader (GrassGpuScatter), передаётся ссылкой.
        // allowedMaterials - пусто значит без фильтра; иначе САБМЕШИ с материалом не из списка
        // (например, скала) вообще не попадают в acceleration structure - луч их физически не
        // видит, точнее и дешевле, чем отбрасывать попадания постфактум.
        public GpuRaycastScatterer(RayTracingShader shader, IReadOnlyList<Renderer> surfaceRenderers,
            IReadOnlyList<Material> allowedMaterials)
        {
            _resources = new RayTracingResources();
            //_resources.Load();

            RayTracingBackend backend = RayTracingContext.IsBackendSupported(RayTracingBackend.Hardware)
                ? RayTracingBackend.Hardware
                : RayTracingBackend.Compute;
            _context = new RayTracingContext(backend, _resources);
            _shader = _context.CreateRayTracingShader(shader);
            _accelStruct = _context.CreateAccelerationStructure(new AccelerationStructureOptions());

            uint instanceId = 0;
            foreach (Renderer renderer in surfaceRenderers)
            {
                if (renderer is not MeshRenderer || !renderer.TryGetComponent(out MeshFilter meshFilter) || meshFilter.sharedMesh == null)
                    continue;

                Mesh mesh = meshFilter.sharedMesh;
                Material[] materials = renderer.sharedMaterials;
                Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (allowedMaterials.Count > 0 && (sub >= materials.Length || !allowedMaterials.Contains(materials[sub])))
                        continue;

                    uint id = instanceId++;
                    var desc = new MeshInstanceDesc(mesh, sub) { localToWorldMatrix = localToWorld, instanceID = id };
                    _accelStruct.AddInstance(desc);
                    _instanceGeometry[id] = new InstanceGeometry(mesh.normals, mesh.GetTriangles(sub), localToWorld);
                }
            }
        }

        // Раскидывает до count кандидатных точек по XZ внутри area, луч вниз из area.max.y +
        // rayStartMargin. Возвращает только успешные попадания (не обязательно count штук) - как
        // и CPU-путь, промахи просто отбрасываются. Синхронный (ждёт GPU) - приемлемо, разовый
        // бейк, не по кадрам.
        public ScatterHit[] Scatter(int count, Bounds area, float rayStartMargin, int seed)
        {
            if (count <= 0) return Array.Empty<ScatterHit>();

            var resultBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, count, RawHitStride);
            resultBuffer.SetCounterValue(0);
            var countBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));

            try
            {
                GraphicsBuffer scratchBuffer =
                    RayTracingHelper.CreateScratchBufferForBuildAndDispatch(_accelStruct, _shader, (uint)count, 1, 1);
                try
                {
                    var cmd = new CommandBuffer();
                    _accelStruct.Build(cmd, scratchBuffer);

                    _shader.SetAccelerationStructure(cmd, "_AccelStruct", _accelStruct);
                    _shader.SetIntParam(cmd, RayCountId, count);
                    _shader.SetFloatParam(cmd, RayOriginYId, area.max.y + rayStartMargin);
                    _shader.SetFloatParam(cmd, RayDistanceId, area.size.y + rayStartMargin * 2f);
                    _shader.SetVectorParam(cmd, AreaMinId, area.min);
                    _shader.SetVectorParam(cmd, AreaSizeId, area.size);
                    _shader.SetIntParam(cmd, SeedId, seed);
                    _shader.SetBufferParam(cmd, OutputId, resultBuffer);

                    _shader.Dispatch(cmd, scratchBuffer, (uint)count, 1, 1);
                    cmd.CopyCounterValue(resultBuffer, countBuffer, 0);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                finally
                {
                    scratchBuffer?.Dispose();
                }

                var countReadback = new uint[1];
                countBuffer.GetData(countReadback);
                int hitCount = Mathf.Min((int)countReadback[0], count);
                if (hitCount == 0) return Array.Empty<ScatterHit>();

                var rawHits = new RawHit[hitCount];
                resultBuffer.GetData(rawHits, 0, 0, hitCount);

                var result = new ScatterHit[hitCount];
                for (int i = 0; i < hitCount; i++)
                {
                    RawHit raw = rawHits[i];
                    Vector3 normal = ResolveNormal(raw.InstanceID, raw.PrimitiveIndex, raw.Barycentrics);
                    result[i] = new ScatterHit(raw.Position, normal, (int)raw.CandidateIndex);
                }
                return result;
            }
            finally
            {
                resultBuffer.Dispose();
                countBuffer.Dispose();
            }
        }

        // Барицентрики - стандартная DXR-конвенция: x/y - веса вершин 1/2 треугольника, вершина 0
        // получает 1-x-y (та же конвенция, что в официальном примере Unity - см. use в
        // trace-camera-rays-full-sample.html: float3(uv.x, uv.y, 1.0 - uv.x - uv.y)).
        private Vector3 ResolveNormal(uint instanceID, uint primitiveIndex, Vector2 barycentrics)
        {
            if (!_instanceGeometry.TryGetValue(instanceID, out InstanceGeometry geo) || geo.Normals.Length == 0)
                return Vector3.up;

            int t = (int)primitiveIndex * 3;
            if (t + 2 >= geo.Triangles.Length) return Vector3.up;

            Vector3 n0 = geo.Normals[geo.Triangles[t]];
            Vector3 n1 = geo.Normals[geo.Triangles[t + 1]];
            Vector3 n2 = geo.Normals[geo.Triangles[t + 2]];
            float w0 = 1f - barycentrics.x - barycentrics.y;
            Vector3 localNormal = n0 * w0 + n1 * barycentrics.x + n2 * barycentrics.y;

            Vector3 worldNormal = geo.LocalToWorld.MultiplyVector(localNormal);
            return worldNormal.sqrMagnitude > 0.0001f ? worldNormal.normalized : Vector3.up;
        }

        public void Dispose()
        {
            // IRayTracingShader не реализует IDisposable - его ресурсы владеет RayTracingContext
            // (см. официальный пример ShootCameraRays.cs: OnDestroy освобождает только
            // accelStruct и context, не shader).
            _accelStruct?.Dispose();
            _context?.Dispose();
        }
    }
}
