using UnityEngine;
using UnityEngine.Rendering;

namespace PG.Rendering
{
    // Один рендер-батч: меш + материал + свои InstanceBuffer/GpuIndirectCuller. Нужен, когда на
    // сцене несколько прототипов с разными мешами (деревья/трава Terrain'а) - по батчу на прототип.
    // Docs/index.html.
    public class IndirectRenderBatch : System.IDisposable
    {
        private static readonly int InstanceBufferId = Shader.PropertyToID("_InstanceBuffer");
        private static readonly int UseInstanceBufferId = Shader.PropertyToID("_UseInstanceBuffer");

        public readonly Mesh Mesh;
        public readonly Material Material;
        public readonly InstanceBuffer Instances;
        public readonly GpuIndirectCuller Culler; // null, если культинг выключен для этого батча

        private readonly Bounds _bounds;
        private readonly MaterialPropertyBlock _propertyBlock;
        private readonly GraphicsBuffer _legacyCommandBuffer; // используется, только если Culler == null

        public IndirectRenderBatch(Mesh mesh, Material material, InstanceData[] data, Bounds bounds,
            ComputeShader cullingShader, MeshLevel[] levels,
            float boundsRadiusOverride = 0f, Vector3 boundsExtentsOverride = default)
        {
            Mesh = mesh;
            Material = material;
            _bounds = bounds;
            Instances = new InstanceBuffer(data);
            _propertyBlock = new MaterialPropertyBlock();
            _propertyBlock.SetBuffer(InstanceBufferId, Instances.Buffer);
            // Boolean-property на Blackboard графа (_UseInstanceBuffer) + Branch-нода после
            // Apply Instance Transform - см. обсуждение с пользователем. Пока в графах не добавлено,
            // этот SetFloat просто ни на что не влияет (unknown property), безопасно лежит здесь заранее.
            _propertyBlock.SetFloat(UseInstanceBufferId, 1f);

            if (cullingShader != null)
                Culler = new GpuIndirectCuller(cullingShader, Instances.Buffer, data.Length, levels,
                    boundsRadiusOverride, boundsExtentsOverride);
            else
                _legacyCommandBuffer = GpuIndirectCullerHelpers.BuildCommandBuffer(mesh, data.Length);
        }

        public void Render(Camera targetCamera, bool restrictToTargetCamera, CullingSettings settings,
            IndirectLightingSettings lighting)
        {
            var rparams = new RenderParams(Material)
            {
                worldBounds = _bounds,
                matProps = _propertyBlock,
                camera = restrictToTargetCamera ? targetCamera : null
            };
            lighting.Apply(ref rparams);

            if (Culler == null)
            {
                Graphics.RenderMeshIndirect(rparams, Mesh, _legacyCommandBuffer);
                return;
            }

            Culler.Dispatch(targetCamera, settings);
            foreach (GpuIndirectCuller.Level level in Culler.Levels)
            {
                _propertyBlock.SetBuffer(InstanceBufferId, level.VisibleBuffer);
                rparams.matProps = _propertyBlock;
                rparams.material = level.Material != null ? level.Material : Material;
                rparams.forceMeshLod = level.ForceMeshLod;
                // level.DisableShadows (импостор - см. AppendImpostorLevel) переопределяет
                // lighting.ShadowCastingMode ТОЛЬКО для этого уровня - rparams переиспользуется
                // между итерациями, поэтому ветка else обязательна, иначе следующий уровень
                // унаследовал бы Off от предыдущего импостора.
                rparams.shadowCastingMode = level.DisableShadows ? ShadowCastingMode.Off : lighting.ShadowCastingMode;
                Graphics.RenderMeshIndirect(rparams, level.Mesh, level.CommandBuffer);
            }
        }

        public void Dispose()
        {
            Instances?.Dispose();
            Culler?.Dispose();
            _legacyCommandBuffer?.Dispose();
        }
    }
}
