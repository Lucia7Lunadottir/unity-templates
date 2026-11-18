using System.Collections.Generic;
using UnityEngine;

namespace PG.Rendering
{
    // Один уровень детализации для GpuIndirectCuller: свой меш, своя дистанция отсечения.
    // Material - опционально: null значит "взять материал батча" (IndirectRenderBatch.Material),
    // задан - использовать вместо него (нужно для импостора на дальнем уровне, у него свой шейдер).
    // MinDistance задаётся явно вызывающим кодом (не выводится автоматически из соседних уровней) -
    // это позволяет нескольким уровням (например, нескольким "частям" одного LOD - см.
    // TerrainIndirectRendererBase.LodLevel.Parts) делить один и тот же диапазон дистанции.
    public struct MeshLevel
    {
        public Mesh Mesh;
        public Material Material;
        public float MinDistance;
        public float MaxDistance;
        public int ForceMeshLod;
        // false по умолчанию - все существующие места, создающие MeshLevel, продолжают отбрасывать
        // тень как раньше без единой правки. Единственное место, которое ставит true - импостор
        // в AppendImpostorLevel (плоский billboard, отбрасывающий тень которого не выглядит
        // осмысленно, а с 4 каскадами теней в URP-ассете это ещё и самый массовый по числу
        // инстансов уровень батча - см. обсуждение производительности).
        public bool DisableShadows;
    }

    public struct CullingSettings
    {
        public bool EnableDistance;
        public bool EnableFrustum;
        public bool EnableOcclusion;
        public bool InvertOcclusion;
        public float CullRadius;
        public int DebugInstanceIndex; // -1 = выключено
    }

    // GPU-driven culling (дистанция -> frustum -> occlusion) + LOD-роутинг поверх произвольного
    // instance-буфера. Не знает, как инстансы сгенерированы и как рисуются - только решает, кто и
    // в каком LOD-уровне виден в этом кадре. Использовать в любом скрипте, не только
    // IndirectMeshRenderer: см. Docs/03-culling.html и Docs/06-lod.html.
    public class GpuIndirectCuller : System.IDisposable
    {
        public readonly struct Level
        {
            public readonly Mesh Mesh;
            public readonly Material Material; // null = материал батча/рендерера
            public readonly int ForceMeshLod;
            public readonly bool DisableShadows;
            public readonly GraphicsBuffer VisibleBuffer;
            public readonly GraphicsBuffer CommandBuffer;
            internal readonly float MinDistance;
            internal readonly float MaxDistance;
            // Bounds ЭТОГО меша, не общие на батч - у импостора (плоский billboard-квад) bounds
            // могут быть в разы больше/меньше, чем у обычного LOD0 (например, у настоящего дерева
            // extents.magnitude ~0.85, у его impostor-меша ~10) - если для frustum-теста всех
            // уровней использовать bounds одного (базового) меша батча, инстансы дальнего уровня
            // на границе кадра ложно считаются "вне frustum" и пропадают (см. Docs/03-culling.html).
            internal readonly float BoundsRadius;
            internal readonly Vector3 BoundsExtents;
            internal readonly Vector3 BoundsCenter; // центр bounds относительно корня - у дерева середина кроны, а не земля

            internal Level(Mesh mesh, Material material, int forceMeshLod, bool disableShadows, float minDistance, float maxDistance,
                GraphicsBuffer visibleBuffer, GraphicsBuffer commandBuffer,
                float boundsRadiusOverride = 0f, Vector3 boundsExtentsOverride = default)
            {
                Mesh = mesh;
                Material = material;
                ForceMeshLod = forceMeshLod;
                DisableShadows = disableShadows;
                MinDistance = minDistance;
                MaxDistance = maxDistance;
                VisibleBuffer = visibleBuffer;
                CommandBuffer = commandBuffer;
                Vector3 meshExtents = mesh != null ? mesh.bounds.extents : Vector3.zero;
                BoundsCenter = mesh != null ? mesh.bounds.center : Vector3.zero;
                BoundsExtents = boundsExtentsOverride != Vector3.zero ? boundsExtentsOverride : meshExtents;
                BoundsRadius = boundsRadiusOverride > 0f ? boundsRadiusOverride : BoundsExtents.magnitude;
            }
        }

        // Массив, не IReadOnlyList<Level> - foreach по IReadOnlyList/IEnumerable (интерфейсу)
        // аллоцирует boxed enumerator НА КАЖДЫЙ вызов (классическая C#-ловушка), а foreach по
        // конкретному массиву использует struct-enumerator без аллокаций. IndirectRenderBatch.Render()
        // вызывает foreach по Levels каждый Update() каждого батча - раньше это было прямым
        // источником GC Alloc каждый кадр (см. обсуждение производительности/Profiler).
        public Level[] Levels => _levels;

        // Вид камеры для отсечения
        private struct View
        {
            public Vector3 Position;
            public Matrix4x4 ViewProj;
            public Vector4[] Planes;
            public Vector3 ShadowSweep;
        }

        // Направление солнца для теневого прохода: RenderSettings.sun, иначе первый направленный свет
        private static Light s_sun;

        private static Vector3 SunDirection()
        {
            if (s_sun == null || !s_sun.isActiveAndEnabled) s_sun = RenderSettings.sun;
            if (s_sun == null || !s_sun.isActiveAndEnabled)
                foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                    if (light.type == LightType.Directional) { s_sun = light; break; }
            return s_sun != null ? s_sun.transform.forward : Vector3.down;
        }

        private static readonly Plane[] s_planes = new Plane[6];

        private static View CaptureView(Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, s_planes);
            var planes = new Vector4[6];
            for (int i = 0; i < 6; i++) planes[i] = new Vector4(s_planes[i].normal.x, s_planes[i].normal.y, s_planes[i].normal.z, s_planes[i].distance);
            return new View { Position = camera.transform.position, ViewProj = GpuIndirectCullerHelpers.BuildViewProjMatrix(camera), Planes = planes };
        }

        private readonly ComputeShader _shader;
        private readonly int _kernel;
        private readonly GraphicsBuffer _instanceBuffer;
        private readonly int _instanceCount;
        private readonly Level[] _levels;

        // Отдельные буферы теневого прохода, создаются при первом DispatchShadows
        private GraphicsBuffer[] _shadowVisibleBuffers;
        private GraphicsBuffer[] _shadowCommandBuffers;

        private readonly GraphicsBuffer _debugClipPos;
        private readonly Vector4[] _debugClipPosReadback = new Vector4[1];
        private float _debugLogTimer;

        // boundsRadiusOverride/boundsExtentsOverride - ручная поправка ПОВЕРХ автоопределения по
        // mesh.bounds каждого уровня (см. Level.BoundsRadius/BoundsExtents), на случай если у
        // мешей заведомо не совпадающая с реальным силуэтом форма bounds (например, вытянутое
        // тело животного при кубических bounds - см. Docs/03-culling.html). > 0 / != Vector3.zero -
        // применяется ко ВСЕМ уровням одинаково; иначе каждый уровень использует bounds своего меша.
        public GpuIndirectCuller(ComputeShader shader, GraphicsBuffer instanceBuffer, int instanceCount,
            IReadOnlyList<MeshLevel> levels, float boundsRadiusOverride = 0f, Vector3 boundsExtentsOverride = default)
        {
            _shader = shader;
            _kernel = shader.FindKernel("CSMain");
            _instanceBuffer = instanceBuffer;
            _instanceCount = instanceCount;

            _levels = new Level[levels.Count];
            for (int i = 0; i < levels.Count; i++)
            {
                MeshLevel level = levels[i];
                // Graphics.RenderMeshIndirect (в отличие от Graphics.RenderMesh) НЕ поддерживает
                // автовыбор Mesh LOD по расстоянию - форсирует LOD0 всегда, что для forceMeshLod
                // передать -1, что 0 (см. RenderParams.forceMeshLod, доки Unity). А вот
                // Mesh.GetIndexCount(sub, meshLod) ведёт себя иначе: -1 отдаёт размер ВСЕГО
                // объединённого буфера индексов сразу по ВСЕМ запечённым Mesh LOD уровням меша (не
                // "весь меш без LOD", а буквально сумма LOD0+LOD1+LOD2+...), а 0 - честный размер
                // именно LOD0. Раньше command buffer строился под -1 (буфер думал, что индексов
                // намного больше, чем реально принадлежит LOD0), а рендерилось всё равно LOD0 -
                // GPU дочитывал за пределы LOD0 в данные соседних LOD-уровней как в продолжение той
                // же геометрии - обрубленный/мусорный меш. 0 здесь - не "проигнорировать LOD", а
                // "то же самое, что реально нарисует RenderMeshIndirect" - синхронизирует buffer и
                // рендер для ЛЮБОГО меша, с запечёнными Mesh LOD или без (для меша без LOD-данных
                // GetIndexCount(0,0) и GetIndexCount(0,-1) совпадают, так что это всегда безопасно).
                const int renderedMeshLod = 0;
                _levels[i] = new Level(level.Mesh, level.Material, renderedMeshLod, level.DisableShadows, level.MinDistance, level.MaxDistance,
                    new GraphicsBuffer(GraphicsBuffer.Target.Append, instanceCount, InstanceData.Stride),
                    GpuIndirectCullerHelpers.BuildCommandBuffer(level.Mesh, instanceCount, renderedMeshLod),
                    boundsRadiusOverride, boundsExtentsOverride);
            }

            _debugClipPos = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(float) * 4);
        }

        public void Dispatch(Camera camera, CullingSettings settings)
        {
            View view = CaptureView(camera);
            foreach (Level level in _levels)
                DispatchInto(level, level.VisibleBuffer, level.CommandBuffer, level.MaxDistance, camera, settings, view);

            LogDebugInstanceIfDue(settings.DebugInstanceIndex);
        }

        public GraphicsBuffer ShadowVisibleBuffer(int levelIndex) => _shadowVisibleBuffers?[levelIndex];
        public GraphicsBuffer ShadowCommandBuffer(int levelIndex) => _shadowCommandBuffers?[levelIndex];

        // Отбор для теневого прохода: только ближе shadowDistance (дальше URP тени не рисует), без occlusion
        public void DispatchShadows(Camera camera, CullingSettings settings, float shadowDistance)
        {
            if (_shadowVisibleBuffers == null) CreateShadowBuffers();

            settings.EnableOcclusion = false;
            View view = CaptureView(camera);
            // Дерево за кадром отбрасывает тень в кадр - отбор по кадру, протянутому против света на дальность теней
            view.ShadowSweep = SunDirection() * shadowDistance;
            for (int i = 0; i < _levels.Length; i++)
            {
                Level level = _levels[i];
                if (level.DisableShadows || level.MinDistance >= shadowDistance) continue;
                DispatchInto(level, _shadowVisibleBuffers[i], _shadowCommandBuffers[i],
                    Mathf.Min(level.MaxDistance, shadowDistance), camera, settings, view);
            }
        }

        private void CreateShadowBuffers()
        {
            _shadowVisibleBuffers = new GraphicsBuffer[_levels.Length];
            _shadowCommandBuffers = new GraphicsBuffer[_levels.Length];
            for (int i = 0; i < _levels.Length; i++)
            {
                if (_levels[i].DisableShadows) continue;
                _shadowVisibleBuffers[i] = new GraphicsBuffer(GraphicsBuffer.Target.Append, _instanceCount, InstanceData.Stride);
                _shadowCommandBuffers[i] = GpuIndirectCullerHelpers.BuildCommandBuffer(_levels[i].Mesh, _instanceCount, _levels[i].ForceMeshLod);
            }
        }

        public void Dispose()
        {
            foreach (Level level in _levels)
            {
                level.VisibleBuffer?.Dispose();
                level.CommandBuffer?.Dispose();
            }
            if (_shadowVisibleBuffers != null)
            {
                for (int i = 0; i < _levels.Length; i++)
                {
                    _shadowVisibleBuffers[i]?.Dispose();
                    _shadowCommandBuffers[i]?.Dispose();
                }
            }
            _debugClipPos?.Dispose();
        }

        private void DispatchInto(Level level, GraphicsBuffer visibleBuffer, GraphicsBuffer commandBuffer, float maxDistance,
            Camera camera, CullingSettings settings, View view)
        {
            visibleBuffer.SetCounterValue(0);

            _shader.SetBuffer(_kernel, "_AllInstances", _instanceBuffer);
            _shader.SetBuffer(_kernel, "_VisibleInstances", visibleBuffer);
            _shader.SetInt("_InstanceCount", _instanceCount);
            _shader.SetVector("_CullOrigin", view.Position);
            _shader.SetFloat("_CullRadius", maxDistance);
            _shader.SetFloat("_LodMinDistance", level.MinDistance);
            _shader.SetFloat("_MeshBoundsRadius", level.BoundsRadius);
            _shader.SetVector("_MeshBoundsExtents", level.BoundsExtents);
            _shader.SetVector("_MeshBoundsCenter", level.BoundsCenter);
            _shader.SetMatrix("_ViewProjection", view.ViewProj);
            _shader.SetVectorArray("_FrustumPlanes", view.Planes);
            _shader.SetVector("_ShadowSweep", view.ShadowSweep);
            _shader.SetInt("_EnableDistance", settings.EnableDistance ? 1 : 0);
            _shader.SetInt("_EnableFrustum", settings.EnableFrustum ? 1 : 0);
            _shader.SetBuffer(_kernel, "_DebugClipPos", _debugClipPos);
            _shader.SetInt("_DebugInstanceIndex", Mathf.Max(settings.DebugInstanceIndex, 0));

            _shader.Dispatch(_kernel, Mathf.CeilToInt(_instanceCount / 64f), 1, 1);
            GraphicsBuffer.CopyCount(visibleBuffer, commandBuffer, sizeof(uint));
        }

        private void LogDebugInstanceIfDue(int debugInstanceIndex)
        {
            if (debugInstanceIndex < 0) return;
            _debugLogTimer += Time.deltaTime;
            if (_debugLogTimer < 1f) return;
            _debugLogTimer = 0f;

            _debugClipPos.GetData(_debugClipPosReadback);
            Vector4 c = _debugClipPosReadback[0];
            Debug.Log($"GpuIndirectCuller instance[{debugInstanceIndex}]: clipPos.xyz=({c.x:F2}, {c.y:F2}, {c.z:F2}), " +
                      $"w(+radius)={c.w:F2} - внутри frustum'а если |x|<=w и |y|<=w и z<=w и w>0");
        }
    }
}
