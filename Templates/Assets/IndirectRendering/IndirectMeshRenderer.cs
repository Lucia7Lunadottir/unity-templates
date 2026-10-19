using UnityEngine;
using UnityEngine.Rendering;

namespace PG.Rendering
{
    // Демо-компонент для Graphics.RenderMeshIndirect. Сам рендерер тонкий: генерирует данные
    // инстансов, конфигурирует InstanceBuffer/GpuIndirectCuller (оба переиспользуемы отдельно
    // от этого класса) и рисует. Документация - Docs/index.html рядом с этим файлом.
    public class IndirectMeshRenderer : MonoBehaviour
    {
        [System.Serializable]
        public struct LodLevel
        {
            public float maxDistance;
            public Mesh meshOverride; // задан - надёжный путь; не задан - meshLodIndex на базовом _mesh
            public int meshLodIndex; // индекс запечённого Mesh LOD, см. Docs/06-lod.html
        }

        [SerializeField] private Mesh _mesh;
        [SerializeField] private Material _material;
        [SerializeField] private Camera _targetCamera;
        [SerializeField] private bool _restrictRenderToTargetCamera = true;
        [SerializeField] private int _seed = 12345;
        [SerializeField] private int _instanceCount = 1000;
        [SerializeField] private float _spread = 50f;
        [SerializeField] private float _minScale = 0.5f;
        [SerializeField] private float _maxScale = 2f;
        [SerializeField] private float _minYRotation = 0f;
        [SerializeField] private float _maxYRotation = 360f;
        [SerializeField] private bool _useRaycast;
        [SerializeField] private float _alignHeight;

        [Header("GPU-driven culling")]
        [SerializeField] private bool _useComputeCulling = false;
        [SerializeField] private ComputeShader _cullingShader;
        [SerializeField] private float _cullRadius = 40f;
        [SerializeField] private bool _enableDistanceCulling = true;
        [SerializeField] private bool _enableFrustumCulling = true;
        [SerializeField] private bool _enableOcclusionCulling = false; // см. Docs/03-culling.html
        [SerializeField] private bool _invertOcclusion = false;
        [SerializeField] private float _boundsRadiusOverride = 0f;
        [SerializeField] private Vector3 _boundsExtentsOverride = Vector3.zero;

        [Header("Освещение и тени")]
        [SerializeField] private IndirectLightingSettings _lighting = IndirectLightingSettings.Default;

        [Header("Диагностика")]
        [SerializeField] private int _debugInstanceIndex = -1;
        [SerializeField] private bool _debugShowAllFrustumStatus = false;

        [Header("LOD (требует _useComputeCulling)")]
        [SerializeField] private LodLevel[] _lodLevels = System.Array.Empty<LodLevel>();

        private static Material _sharedDefaultMaterial;
        private static readonly int InstanceBufferId = Shader.PropertyToID("_InstanceBuffer");
        private static readonly int UseInstanceBufferId = Shader.PropertyToID("_UseInstanceBuffer");

        private InstanceBuffer _instances;
        private GpuIndirectCuller _culler;
        private GraphicsBuffer _legacyCommandBuffer; // используется, только если культинг выключен
        private MaterialPropertyBlock _propertyBlock;
        private Bounds _bounds;
        private Vector3 _bakedCenter;

        private void OnEnable()
        {
            if (_mesh == null) _mesh = BuildFallbackCubeMesh();
            if (_material == null) _material = GetSharedDefaultMaterial();
            if (_targetCamera == null) _targetCamera = Camera.main;

            _propertyBlock = new MaterialPropertyBlock();
            _instances = new InstanceBuffer(GenerateInstanceData());
            _propertyBlock.SetBuffer(InstanceBufferId, _instances.Buffer);
            // См. IndirectRenderBatch.cs - тот же _UseInstanceBuffer для Blackboard Boolean + Branch.
            _propertyBlock.SetFloat(UseInstanceBufferId, 1f);
            _bakedCenter = transformHandle.position;
            _bounds = ComputeBounds();

            bool cullingReady = _useComputeCulling && _cullingShader != null && _targetCamera != null;
            if (_useComputeCulling && !cullingReady)
                Debug.LogWarning("IndirectMeshRenderer: нет _cullingShader или камеры - culling выключен.", this);
            if (_lodLevels.Length > 0 && !cullingReady)
                Debug.LogWarning("IndirectMeshRenderer: _lodLevels заданы, но LOD требует _useComputeCulling.", this);
            if (_lodLevels.Length > 0 && !_enableDistanceCulling)
                Debug.LogWarning("IndirectMeshRenderer: LOD активен при выключенном _enableDistanceCulling - " +
                                  "все уровни получат все инстансы разом (дистанция и есть роутер по уровням).", this);

            if (cullingReady)
                _culler = new GpuIndirectCuller(_cullingShader, _instances.Buffer, _instanceCount, BuildLevels(),
                    _boundsRadiusOverride, _boundsExtentsOverride);
            else
                _legacyCommandBuffer = GpuIndirectCullerHelpers.BuildCommandBuffer(_mesh, _instanceCount);
        }

        private InstanceData[] GenerateInstanceData()
        {
            var data = new InstanceData[_instanceCount];
            var rng = new System.Random(_seed); // фиксированный seed - детерминированная расстановка
            for (int i = 0; i < _instanceCount; i++)
            {
                Vector3 offset = new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0) * _spread,
                    0f,
                    (float)(rng.NextDouble() * 2.0 - 1.0) * _spread);
                float yAngle = Mathf.Lerp(_minYRotation, _maxYRotation, (float)rng.NextDouble());

                Vector3 position = transformHandle.position + offset;
                if (_useRaycast)
                {
                    Ray ray = new Ray(position, Vector3.down);
                    if (Physics.Raycast(ray, out RaycastHit hit))
                    {
                        position = hit.point + (Vector3.up * _alignHeight);
                    }
                }
                data[i] = new InstanceData
                {
                    Position = position,
                    Scale = Mathf.Lerp(_minScale, _maxScale, (float)rng.NextDouble()),
                    Rotation = Quaternion.Euler(0f, yAngle, 0f)
                };
            }
            return data;
        }

        private Bounds ComputeBounds()
        {
            float extent = Mathf.Max(_spread, _maxScale) * 2f + 5f;
            return new Bounds(transformHandle.position, new Vector3(extent, extent, extent));
        }

        private MeshLevel[] BuildLevels()
        {
            if (_lodLevels.Length == 0)
                return new[] { new MeshLevel { Mesh = _mesh, MaxDistance = _cullRadius, ForceMeshLod = 0 } };

            var sorted = (LodLevel[])_lodLevels.Clone();
            System.Array.Sort(sorted, (a, b) => a.maxDistance.CompareTo(b.maxDistance));

            var levels = new MeshLevel[sorted.Length];
            float prevMax = 0f;
            for (int i = 0; i < sorted.Length; i++)
            {
                bool useOverride = sorted[i].meshOverride != null;
                levels[i] = new MeshLevel
                {
                    Mesh = useOverride ? sorted[i].meshOverride : _mesh,
                    MinDistance = prevMax,
                    MaxDistance = sorted[i].maxDistance,
                    ForceMeshLod = useOverride ? 0 : sorted[i].meshLodIndex
                };
                prevMax = sorted[i].maxDistance;
            }
            return levels;
        }

        public void SetInstancePosition(int index, Vector3 worldPosition) => _instances.SetPosition(index, worldPosition);

        public void RecenterToTransform()
        {
            Vector3 delta = transformHandle.position - _bakedCenter;
            if (delta == Vector3.zero) return;
            _instances.Offset(delta);
            _bakedCenter = transformHandle.position;
            _bounds.center = transformHandle.position;
        }

        private void Update()
        {
            if (_culler != null)
            {
                DispatchAndRenderWithCulling();
                return;
            }

            Graphics.RenderMeshIndirect(BuildRenderParams(), _mesh, _legacyCommandBuffer);
        }

        private void DispatchAndRenderWithCulling()
        {
            _culler.Dispatch(_targetCamera, new CullingSettings
            {
                EnableDistance = _enableDistanceCulling,
                EnableFrustum = _enableFrustumCulling,
                EnableOcclusion = _enableOcclusionCulling,
                InvertOcclusion = _invertOcclusion,
                CullRadius = _cullRadius,
                DebugInstanceIndex = _debugInstanceIndex
            });

            foreach (GpuIndirectCuller.Level level in _culler.Levels)
            {
                _propertyBlock.SetBuffer(InstanceBufferId, level.VisibleBuffer);
                RenderParams rparams = BuildRenderParams();
                rparams.forceMeshLod = level.ForceMeshLod;
                Graphics.RenderMeshIndirect(rparams, level.Mesh, level.CommandBuffer);
            }
        }

        private RenderParams BuildRenderParams()
        {
            var rparams = new RenderParams(_material)
            {
                worldBounds = _bounds,
                matProps = _propertyBlock,
                camera = _restrictRenderToTargetCamera ? _targetCamera : null
            };
            _lighting.Apply(ref rparams);
            return rparams;
        }

        private void OnDisable()
        {
            _instances?.Dispose();
            _instances = null;
            _culler?.Dispose();
            _culler = null;
            _legacyCommandBuffer?.Dispose();
            _legacyCommandBuffer = null;
        }

        private void OnDrawGizmos()
        {
            if (_instances == null || _targetCamera == null) return;

            float meshRadius = _boundsRadiusOverride > 0f ? _boundsRadiusOverride : _mesh.bounds.extents.magnitude;
            Matrix4x4 viewProjection = GpuIndirectCullerHelpers.BuildViewProjMatrix(_targetCamera);

            if (_debugShowAllFrustumStatus)
            {
                for (int i = 0; i < _instances.Count; i++)
                {
                    InstanceData inst = _instances.Get(i);
                    bool inside = GpuIndirectCullerHelpers.IsInsideFrustumCpu(viewProjection, inst.Position, meshRadius * inst.Scale);
                    Gizmos.color = inside ? Color.green : Color.red;
                    Gizmos.DrawSphere(inst.Position, Mathf.Max(meshRadius * inst.Scale * 0.5f, 0.3f));
#if UNITY_EDITOR
                    if (!inside)
                        UnityEditor.Handles.Label(inst.Position + Vector3.up * 0.5f, i.ToString());
#endif
                }
            }

            if (_debugInstanceIndex >= 0 && _debugInstanceIndex < _instances.Count)
            {
                InstanceData inst = _instances.Get(_debugInstanceIndex);
                bool inside = GpuIndirectCullerHelpers.IsInsideFrustumCpu(viewProjection, inst.Position, meshRadius * inst.Scale);
                Gizmos.color = inside ? Color.green : Color.red;
                Gizmos.DrawWireSphere(inst.Position, Mathf.Max(meshRadius * inst.Scale, 1f));
            }
        }

        private static Material GetSharedDefaultMaterial()
        {
            if (_sharedDefaultMaterial == null)
                _sharedDefaultMaterial = new Material(Shader.Find("Custom/IndirectCube"));
            return _sharedDefaultMaterial;
        }

        private static Mesh BuildFallbackCubeMesh()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);
            return mesh;
        }
    }
}
