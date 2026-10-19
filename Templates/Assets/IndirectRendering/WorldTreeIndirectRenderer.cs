using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PG.Rendering
{
    // Прототип: вместо одного IndirectRenderBatch на прототип НА КАЖДЫЙ тайл террейна (как в
    // TerrainTreeIndirectRenderer), один батч на ВИД дерева на ВСЮ сцену - инстансы со всех тайлов
    // с одинаковым TreePrototype.prefab сливаются в один GraphicsBuffer/Dispatch/Render. Тайл
    // перестаёт быть единицей рендера, остаётся только единицей данных (сколько деревьев где растёт).
    // Не трогает TerrainTreeIndirectRenderer - тот остаётся выключенным на тайлах, это отдельный путь.
    public class WorldTreeIndirectRenderer : MonoBehaviour
    {
        [SerializeField] private Camera _targetCamera;
        [SerializeField] private ComputeShader _cullingShader;
        [SerializeField] private float _cullRadius = 500f;
        [SerializeField] private bool _enableDistanceCulling = true;
        [SerializeField] private bool _enableFrustumCulling = true;
        [SerializeField] private bool _enableOcclusionCulling = true;
        [SerializeField] private bool _disableNativeTerrainTrees = true;

        private readonly List<IndirectRenderBatch> _batches = new();
        private readonly Dictionary<Terrain, float> _originalTreeDistances = new();
        private bool CullingReady => _cullingShader != null && _targetCamera != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Object.FindAnyObjectByType<WorldTreeIndirectRenderer>() != null) return;
            if (Terrain.activeTerrains.Length < 2) return; // однотайловым сценам мерджить нечего

            // Неактивный GameObject: AddComponent на активном объекте вызывает OnEnable СРАЗУ,
            // до того как ниже успеет назначиться _cullingShader - компонент тут же выключил бы
            // сам себя (видели это в первом прогоне). Собираем поля, активируем в конце - тогда
            // OnEnable увидит уже настроенный компонент.
            var go = new GameObject("~WorldTreeIndirectRenderer");
            go.SetActive(false);
            var renderer = go.AddComponent<WorldTreeIndirectRenderer>();
#if UNITY_EDITOR
            // Прототип: шейдер грузится напрямую по пути для теста в редакторе. В билде так нельзя -
            // UnityEditor недоступен вне редактора; для прод-версии шейдер должен быть полем
            // сериализуемого ассета настроек или явной ссылкой, не AssetDatabase.
            // По GUID IndirectCulling.compute: путь разный в Assets и в установленном пакете
            renderer._cullingShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                UnityEditor.AssetDatabase.GUIDToAssetPath("a769792e3c827a10ba7e2b91d457c1e3"));
#endif
            if (renderer._cullingShader == null)
                Debug.LogWarning("WorldTreeIndirectRenderer: _cullingShader не найден автоматически - " +
                                  "назначь вручную в инспекторе.", renderer);
            go.SetActive(true);
        }

        private void OnEnable()
        {
            if (_targetCamera == null) _targetCamera = Camera.main;
            if (!CullingReady)
            {
                Debug.LogWarning("WorldTreeIndirectRenderer: нет _cullingShader или камеры - выключаюсь.", this);
                enabled = false;
                return;
            }

            BuildMergedBatches();
            Debug.Log($"WorldTreeIndirectRenderer: {_batches.Count} видов, " +
                      $"{CountTotalInstances()} инстансов всего слито из {Terrain.activeTerrains.Length} тайлов.", this);
        }

        private int CountTotalInstances()
        {
            int total = 0;
            foreach (IndirectRenderBatch b in _batches) total += b.Instances.Count;
            return total;
        }

        private void BuildMergedBatches()
        {
            // prefab -> все инстансы этого вида со всех тайлов сразу в мировых координатах
            var byPrefab = new Dictionary<GameObject, List<InstanceData>>();

            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                TerrainData data = terrain.terrainData;
                Vector3 origin = terrain.transform.position;
                Vector3 size = data.size;
                TreePrototype[] prototypes = data.treePrototypes;

                foreach (TreeInstance t in data.treeInstances)
                {
                    if (t.prototypeIndex < 0 || t.prototypeIndex >= prototypes.Length) continue;
                    GameObject prefab = prototypes[t.prototypeIndex].prefab;
                    if (prefab == null) continue;

                    if (!byPrefab.TryGetValue(prefab, out List<InstanceData> list))
                        byPrefab[prefab] = list = new List<InstanceData>();

                    list.Add(new InstanceData
                    {
                        Position = origin + Vector3.Scale(t.position, size),
                        Scale = (t.widthScale + t.heightScale) * 0.5f,
                        Rotation = Quaternion.Euler(0f, t.rotation * Mathf.Rad2Deg, 0f)
                    });
                }

                if (_disableNativeTerrainTrees)
                {
                    _originalTreeDistances[terrain] = terrain.treeDistance;
                    terrain.treeDistance = 0f;
                }
            }

            foreach (var kvp in byPrefab)
            {
                MeshLevel[] levels = BuildLevelsFromPrefab(kvp.Key, _targetCamera, _cullRadius);
                if (levels == null || levels.Length == 0) continue;

                InstanceData[] instances = kvp.Value.ToArray();
                Bounds bounds = ComputeBounds(instances);
                _batches.Add(new IndirectRenderBatch(levels[0].Mesh, levels[0].Material, instances, bounds,
                    _cullingShader, levels));
            }
        }

        // Использует общий путь базового класса: все Renderer'ы каждого LOD и все дочерние объекты
        // префаба, размер - из LODGroup.size либо автоматически по bounds всех Renderer'ов.
        // Префабы без LODGroup тоже поддерживаются (один уровень из всех Renderer'ов).
        private static MeshLevel[] BuildLevelsFromPrefab(GameObject prefab, Camera camera, float cullRadius)
        {
            MeshLevel[] levels = TerrainIndirectRendererBase.BuildMeshLevels(prefab, null, camera, cullRadius);
            if (levels == null || levels.Length == 0) return null;

            TerrainIndirectRendererBase.ExtendLastLodToRadius(levels, cullRadius);
            return levels;
        }

        private static Bounds ComputeBounds(InstanceData[] instances)
        {
            if (instances.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            Bounds b = new Bounds(instances[0].Position, Vector3.zero);
            foreach (InstanceData inst in instances) b.Encapsulate(inst.Position);
            b.Expand(50f); // запас под радиус крон/импостора
            return b;
        }

        private void Update()
        {
            var settings = new CullingSettings
            {
                EnableDistance = _enableDistanceCulling,
                EnableFrustum = _enableFrustumCulling,
                EnableOcclusion = _enableOcclusionCulling,
                CullRadius = _cullRadius,
                DebugInstanceIndex = -1
            };

            foreach (IndirectRenderBatch batch in _batches)
                batch.Render(_targetCamera, true, settings, IndirectLightingSettings.Default);
        }

        private void OnDisable()
        {
            foreach (IndirectRenderBatch batch in _batches) batch.Dispose();
            _batches.Clear();

            foreach (var kvp in _originalTreeDistances)
                if (kvp.Key != null) kvp.Key.treeDistance = kvp.Value;
            _originalTreeDistances.Clear();
        }
    }
}
