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

        private readonly ComputeShader _shader;
        private readonly int _kernel;
        private readonly GraphicsBuffer _instanceBuffer;
        private readonly int _instanceCount;
        private readonly Level[] _levels;

        private readonly GraphicsBuffer _debugClipPos;
        private readonly GraphicsBuffer _debugOcclusion;
        private readonly GraphicsBuffer _debugOccludedMask;
        private readonly Vector4[] _debugClipPosReadback = new Vector4[1];
        private readonly Vector4[] _debugOcclusionReadback = new Vector4[1];
        private readonly uint[] _debugOccludedMaskReadback = new uint[1];
        private float _debugLogTimer;

        private Matrix4x4 _prevViewProj;
        private bool _hasPrevViewProj;

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
            _debugOcclusion = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(float) * 4);
            _debugOccludedMask = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
        }

        public void Dispatch(Camera camera, CullingSettings settings)
        {
            if (!_hasPrevViewProj)
            {
                _prevViewProj = GpuIndirectCullerHelpers.BuildViewProjMatrix(camera);
                _hasPrevViewProj = true;
            }

            Matrix4x4 viewProjection = GpuIndirectCullerHelpers.BuildViewProjMatrix(camera);
            foreach (Level level in _levels)
                DispatchLevel(level, camera, settings, viewProjection);

            LogDebugInstanceIfDue(settings.DebugInstanceIndex);
            _prevViewProj = GpuIndirectCullerHelpers.BuildViewProjMatrix(camera);
        }

        public void Dispose()
        {
            foreach (Level level in _levels)
            {
                level.VisibleBuffer?.Dispose();
                level.CommandBuffer?.Dispose();
            }
            _debugClipPos?.Dispose();
            _debugOcclusion?.Dispose();
            _debugOccludedMask?.Dispose();
        }

        private void DispatchLevel(Level level, Camera camera, CullingSettings settings, Matrix4x4 viewProjection)
        {
            level.VisibleBuffer.SetCounterValue(0);

            _shader.SetBuffer(_kernel, "_AllInstances", _instanceBuffer);
            _shader.SetBuffer(_kernel, "_VisibleInstances", level.VisibleBuffer);
            _shader.SetTextureFromGlobal(_kernel, "_CameraDepthTexture", "_CameraDepthTexture");
            _shader.SetInt("_InstanceCount", _instanceCount);
            _shader.SetVector("_CullOrigin", camera.transform.position);
            _shader.SetFloat("_CullRadius", level.MaxDistance);
            _shader.SetFloat("_LodMinDistance", level.MinDistance);
            _shader.SetFloat("_MeshBoundsRadius", level.BoundsRadius);
            _shader.SetVector("_MeshBoundsExtents", level.BoundsExtents);
            _shader.SetMatrix("_ViewProjection", viewProjection);
            _shader.SetMatrix("_PrevViewProj", _prevViewProj);
            _shader.SetVector("_ScreenSize", new Vector2(camera.pixelWidth, camera.pixelHeight));
            _shader.SetInt("_UsesReversedZ", SystemInfo.usesReversedZBuffer ? 1 : 0);
            _shader.SetInt("_EnableDistance", settings.EnableDistance ? 1 : 0);
            _shader.SetInt("_EnableFrustum", settings.EnableFrustum ? 1 : 0);
            _shader.SetInt("_EnableOcclusion", settings.EnableOcclusion ? 1 : 0);
            _shader.SetInt("_InvertOcclusion", settings.InvertOcclusion ? 1 : 0);
            _shader.SetBuffer(_kernel, "_DebugClipPos", _debugClipPos);
            _shader.SetBuffer(_kernel, "_DebugOcclusion", _debugOcclusion);
            _shader.SetBuffer(_kernel, "_DebugOccludedMask", _debugOccludedMask);
            _shader.SetInt("_DebugInstanceIndex", Mathf.Max(settings.DebugInstanceIndex, 0));

            _shader.Dispatch(_kernel, Mathf.CeilToInt(_instanceCount / 64f), 1, 1);
            GraphicsBuffer.CopyCount(level.VisibleBuffer, level.CommandBuffer, sizeof(uint));
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

            _debugOcclusion.GetData(_debugOcclusionReadback);
            Vector4 o = _debugOcclusionReadback[0];
            bool withinPrevFrame = o.x >= 0f && o.x <= 1f && o.y >= 0f && o.y <= 1f;
            bool occludedByDepth = SystemInfo.usesReversedZBuffer ? o.w < o.z : o.w > o.z;
            Debug.Log($"GpuIndirectCuller instance[{debugInstanceIndex}] occlusion: uv=({o.x:F3}, {o.y:F3}), " +
                      $"sceneDepth={o.z:F5}, instanceDepth={o.w:F5}, withinPrevFrame={withinPrevFrame}, " +
                      $"occludedByDepth={occludedByDepth}");

            _debugOccludedMask.GetData(_debugOccludedMaskReadback);
            uint mask = _debugOccludedMaskReadback[0];
            int occludedCount = 0;
            for (int bit = 0; bit < 7; bit++)
                if ((mask & (1u << bit)) != 0) occludedCount++;
            Debug.Log($"GpuIndirectCuller instance[{debugInstanceIndex}] occlusion 7 точек: " +
                      $"occludedCount={occludedCount}/7 (mask={System.Convert.ToString((int)mask, 2).PadLeft(7, '0')}, " +
                      $"биты 0..6 = центр, +Y, -Y, +X, -X, +Z, -Z)");
        }
    }
}
