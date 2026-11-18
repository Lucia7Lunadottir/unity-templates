using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace PG.Rendering
{
    // Общая часть для TerrainTreeIndirectRenderer и TerrainGrassIndirectRenderer: список
    // IndirectRenderBatch'ей (по одному на прототип), цикл рендера и диспоз. Наследники читают
    // Terrain и заполняют _batches в BuildBatches(). Docs/index.html.
    public abstract partial class TerrainIndirectRendererBase : MonoBehaviour
    {
        // Одна "часть" LOD-уровня: меш + опционально свой материал (не задан - берётся материал
        // прототипа/renderer.sharedMaterial). У одного LOD-уровня может быть несколько частей -
        // как несколько Renderer'ов на один LOD у настоящего LODGroup (например, отдельно ствол
        // и отдельно листва, каждая со своим материалом, но одной дистанцией).
        [System.Serializable]
        public struct MeshMaterialEntry
        {
            public Mesh Mesh;
            public Material Material;
        }

        // Один уровень ручной LOD-цепочки прототипа: список частей (см. выше) + общая для них
        // дистанция отсечения этого уровня.
        [System.Serializable]
        public struct LodLevel
        {
            public MeshMaterialEntry[] Parts;
            public float MaxDistance;
        }

        // Ручная настройка под конкретный индекс прототипа Terrain'а, задаётся вручную в инспекторе
        // (см. Editor/TerrainPrototypeOverrideEditorBase.cs) - НЕ трогает сам Terrain/TreePrototype,
        // так что оригинальные деревья/трава в Paint Trees/Details остаются целыми.
        [System.Serializable]
        public struct PrototypeOverride
        {
            // Пусто - автоопределение LOD из LODGroup прототипа (или просто его меш, если
            // LODGroup нет). Один и больше элементов - ручная цепочка LOD0, LOD1, ... (по
            // возрастанию MaxDistance, порядок в списке не важен - сортируется автоматически),
            // полностью заменяет автоопределение.
            public LodLevel[] LodLevels;

            // Импостор - отдельный дальний уровень LOD со своим мешем/материалом (например,
            // IndirectImpostor.shader). Дистанция, с которой он начинается, НЕ задаётся отдельным
            // числом - вычисляется сама, как MaxDistance последнего LOD-уровня (LodLevels или
            // автоопределённого из LODGroup), чтобы дыра между концом LOD и началом импостора была
            // структурно невозможна - см. AppendImpostorLevel(). Импостор всегда тянется до _cullRadius.
            public Mesh ImpostorMesh;
            public Material ImpostorMaterial;

            // Множитель поверх масштаба инстанса (widthScale/heightScale из TreeInstance) - один
            // на весь прототип, не на LOD-уровень: сам масштаб уже кладётся в InstanceData один раз
            // на инстанс и общий для всех его LOD/impostor'а, менять его по уровням отдельным
            // юниформом в шейдере не нужно. 0 (в т.ч. значение по умолчанию) трактуется как 1
            // (без изменений) - см. использование в TerrainTreeIndirectRenderer/TerrainGrassIndirectRenderer.
            // У деревьев (TerrainTreeIndirectRenderer) это поле ОТКЛЮЧЕНО и не читается вообще -
            // базовый масштаб считается целиком автоматически как "корень префаба x дочерние
            // объекты на пути к Renderer'у x widthScale/heightScale из TreeInstance" (честное
            // перемножение localScale по цепочке - см. ResolveRendererScale/AccumulateLocalScale в
            // TerrainTreeIndirectRenderer), инспектор на размер дерева не влияет.
            public float ScaleMultiplier;

            // Ручная поправка размера для формулы screenRelativeTransitionHeight -> дистанция (см.
            // LodGroupDistance) - используется ВМЕСТО lodGroup.size (один размер на ВСЮ LODGroup,
            // как в самом Unity - см. комментарий у ExtractLodLevelsFromPrefab) для автоопределения
            // (Convert/BuildMeshLevels) этого прототипа, если > 0. Нужна, когда lodGroup.size не
            // задан/неверен, а руками лезть в префаб и жать Recalculate Bounds не хочется. 0 = не
            // трогать, взять lodGroup.size как есть.
            public float LodSizeOverride;
        }

        // Пара "обычный шейдер прототипа -> его Indirect-аналог" (например Simple Nature Snow Lit ->
        // Simple Nature Snow IndirectLit) - см. ResolveIndirectMaterial. Именование пар между обычным
        // и Indirect-шейдером в проекте не единообразно (где-то суффикс, где-то слово вставлено в
        // середину названия), так что сопоставление строкой по имени ненадёжно - сопоставляем по
        // ссылке на сам Shader, задаётся руками один раз в инспекторе.
        [System.Serializable]
        public struct ShaderConversion
        {
            public Shader Source;
            public Shader Indirect;
        }

        [SerializeField] protected Terrain _terrain;
        [SerializeField] protected Camera _targetCamera;
        [SerializeField] protected bool _restrictRenderToTargetCamera = true;
        [SerializeField] protected PrototypeOverride[] _prototypeOverrides = System.Array.Empty<PrototypeOverride>();

        // Таблица замены шейдеров - заполняется руками один раз (пары Source/Indirect шейдеров), а
        // дальше материалы прототипов подменяются САМИ при каждом BuildBatches (OnEnable/Play) - см.
        // ResolveIndirectMaterial. Подмена происходит ТОЛЬКО в памяти (HideFlags.HideAndDontSave),
        // исходный .mat на диске и _prototypeOverrides не трогаются - ничего не ломается в Editor,
        // обычные материалы прототипов (с обычным Lit-шейдером) можно оставлять как есть.
        [SerializeField] protected ShaderConversion[] _indirectShaderConversions = System.Array.Empty<ShaderConversion>();

        [Header("GPU-driven culling")]
        [SerializeField] protected bool _useComputeCulling = true;
        [SerializeField] protected ComputeShader _cullingShader;
        [SerializeField] protected float _cullRadius = 100f;
        [SerializeField] protected bool _enableDistanceCulling = true;
        [SerializeField] protected bool _enableFrustumCulling = true;
        [SerializeField] protected bool _enableOcclusionCulling = false; // см. Docs/03-culling.html

        [SerializeField] protected IndirectLightingSettings _lighting = IndirectLightingSettings.Default;

        protected readonly List<IndirectRenderBatch> _batches = new();

        protected bool CullingReady => _useComputeCulling && _cullingShader != null && _targetCamera != null;

        // Локальный override (инспектор ЭТОГО тайла) побеждает на СВОЁМ тайле и становится
        // источником истины для TreePrototypeOverrideLibrary - остальные тайлы, у которых для
        // этого же префаба локально пусто, получают его через библиотеку сами, без повтора
        // настройки. См. TreePrototypeOverrideLibrary и HasMeaningfulOverride ниже.
        protected PrototypeOverride GetOverride(int prototypeIndex)
        {
            PrototypeOverride local = prototypeIndex >= 0 && prototypeIndex < _prototypeOverrides.Length
                ? _prototypeOverrides[prototypeIndex]
                : default;

            GameObject prefab = prototypeIndex >= 0 && prototypeIndex < GetPrototypeCount()
                ? GetPrototypePrefab(prototypeIndex)
                : null;
            return ResolveOverride(local, prefab);
        }

        // Общее для рантайма и превью: локальный override -> библиотека -> автоматика. Чужое (меши/импостор
        // другого префаба - строки тайла сдвинулись при смене прототипов) отбрасывается, вместо него - авто.
        public static PrototypeOverride ResolveOverride(PrototypeOverride local, GameObject prefab)
        {
            if (prefab == null) return local; // без префаба (текстурный билборд травы) делиться не с кем

            local = Sanitize(local, prefab);
            PrototypeOverride result = local;
            TreePrototypeOverrideLibrary library = TreePrototypeOverrideLibrary.Instance;
            if (library != null)
            {
                if (HasMeaningfulOverride(local)) library.Set(prefab, local);
                else if (library.TryGet(prefab, out PrototypeOverride shared)) result = Sanitize(shared, prefab);
            }
            return WithPrefabImpostor(result, prefab);
        }

        [AutoStaticsCleanup]
        private static readonly Dictionary<GameObject, HashSet<string>> _prefabMeshNames = new();
        [AutoStaticsCleanup]
        private static readonly HashSet<GameObject> _warnedForeign = new();

        private static bool BelongsTo(GameObject prefab, Mesh mesh)
        {
            if (mesh == null) return true;
            if (!_prefabMeshNames.TryGetValue(prefab, out HashSet<string> names))
            {
                names = new HashSet<string>();
                foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null) names.Add(filter.sharedMesh.name);
                _prefabMeshNames[prefab] = names;
            }
            string name = mesh.name;
            if (name.EndsWith("_baked")) name = name[..^"_baked".Length];
            int sub = name.LastIndexOf("_sub", System.StringComparison.Ordinal);
            if (sub > 0) name = name[..sub];
            return names.Contains(name);
        }

        [AutoStaticsCleanup]
        private static readonly HashSet<GameObject> _warnedRawLevels = new();

        // Есть ли в уровнях меш, у которого в префабе непустой локальный трансформ ребёнка относительно корня
        private static bool NeedsBakedMeshes(LodLevel[] levels, GameObject prefab)
        {
            Transform root = prefab.transform;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                if (IsNearlyIdentity(root.worldToLocalMatrix * filter.transform.localToWorldMatrix)) continue;
                foreach (LodLevel level in levels)
                    if (level.Parts != null)
                        foreach (MeshMaterialEntry part in level.Parts)
                            if (part.Mesh == filter.sharedMesh) return true;
            }
            return false;
        }

        private static PrototypeOverride Sanitize(PrototypeOverride over, GameObject prefab)
        {
            bool dropped = false;
            if (over.LodLevels != null)
                foreach (LodLevel level in over.LodLevels)
                    if (level.Parts != null)
                        foreach (MeshMaterialEntry part in level.Parts)
                            if (!BelongsTo(prefab, part.Mesh)) dropped = true;
            if (dropped) over.LodLevels = null;
            // Ручные уровни с сырыми мешами детей, повёрнутых/масштабированных в префабе (Blender -90/90 по X):
            // ручной путь не запекает трансформ и не умножает дистанции - дерево лежит и пропадает дальше пары метров.
            // Такие уровни не годятся - автоматика из LODGroup запекает меши и считает дистанции сама
            if (over.LodLevels != null && NeedsBakedMeshes(over.LodLevels, prefab))
            {
                over.LodLevels = null;
                if (_warnedRawLevels.Add(prefab))
                    Debug.LogWarning($"Ручные LOD-уровни '{prefab.name}' ссылаются на меши с поворотом/масштабом ребёнка - " +
                                     "использую автоматику из LODGroup (меши запекаются).", prefab);
            }
            if (!BelongsTo(prefab, over.ImpostorMesh))
            {
                over.ImpostorMesh = null;
                over.ImpostorMaterial = null;
                dropped = true;
            }
            if (dropped && _warnedForeign.Add(prefab))
                Debug.LogWarning($"Override для '{prefab.name}' содержит меши другого дерева (строки сдвинулись) - " +
                                 "чужое не используется, работает автоматика из LODGroup.", prefab);
            return over;
        }

        // Импостора в override нет - берём Amplify-импостор из LODGroup самого префаба (в памяти на Custom/IndirectImpostor)
        private static PrototypeOverride WithPrefabImpostor(PrototypeOverride over, GameObject prefab)
        {
            if (over.ImpostorMesh != null) return over;
            LODGroup lodGroup = prefab.GetComponentInChildren<LODGroup>();
            if (lodGroup == null) return over;
            foreach (LOD lod in lodGroup.GetLODs())
            {
                if (!IsImpostorLod(lod)) continue;
                foreach (Renderer renderer in lod.renderers)
                {
                    MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
                    Material material = ToIndirectImpostorMaterial(renderer != null ? renderer.sharedMaterial : null);
                    if (filter == null || filter.sharedMesh == null || material == null) continue;
                    over.ImpostorMesh = filter.sharedMesh;
                    over.ImpostorMaterial = material;
                    return over;
                }
            }
            return over;
        }

        [AutoStaticsCleanup]
        private static readonly Dictionary<Material, Material> _impostorMaterialCache = new();

        private static Material ToIndirectImpostorMaterial(Material source)
        {
            if (source == null) return null;
            if (_impostorMaterialCache.TryGetValue(source, out Material cached) && cached != null) return cached;
            Shader shader = Shader.Find("Custom/IndirectImpostor");
            if (shader == null) return null;
            var converted = new Material(shader) { name = source.name + " (Indirect Runtime)", hideFlags = HideFlags.HideAndDontSave };
            converted.CopyPropertiesFromMaterial(source);
            _impostorMaterialCache[source] = converted;
            return converted;
        }

        // "Пусто" - LodLevels не заполнены и импостор не задан, т.е. этот тайл для этого прототипа
        // ничего руками не настраивал (ScaleMultiplier/LodSizeOverride сами по себе не считаются -
        // это тонкая доп. поправка, не полноценная конфигурация, делиться/перетирать ею не нужно).
        private static bool HasMeaningfulOverride(PrototypeOverride over) =>
            (over.LodLevels != null && over.LodLevels.Length > 0) || over.ImpostorMesh != null || over.ImpostorMaterial != null;

        // Если этот компонент висит прямо на GameObject'е самого Terrain'а - забирает его отсюда,
        // а не заставляет тащить ссылку руками в инспекторе.
        [ContextMenu("Find Terrain On This GameObject")]
        private void FindTerrainOnThisGameObject()
        {
            if (!TryGetComponent(out Terrain terrain))
            {
                Debug.LogWarning($"{GetType().Name}: на этом GameObject'е нет компонента Terrain.", this);
                return;
            }
            _terrain = terrain;
            Debug.Log($"{GetType().Name}: _terrain = {terrain.name} (найден через TryGetComponent).", this);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        protected virtual void OnEnable()
        {
            if (_terrain == null) _terrain = Terrain.activeTerrain;
            if (_targetCamera == null) _targetCamera = Camera.main;
            if (_terrain == null)
            {
                Debug.LogWarning($"{GetType().Name}: нет Terrain'а.", this);
                enabled = false;
                return;
            }

            if (_useComputeCulling && !CullingReady)
                Debug.LogWarning($"{GetType().Name}: нет _cullingShader или камеры - culling выключен.", this);

            _tileBounds = ComputeTerrainBounds(_terrain.transform.position, _terrain.terrainData.size, 0f);

            BuildBatches();
            LogBatchSummary();
        }

        private void LogBatchSummary()
        {
            int totalInstances = 0;
            foreach (IndirectRenderBatch batch in _batches) totalInstances += batch.Instances.Count;
            Debug.Log($"{GetType().Name}: {_batches.Count} батчей, {totalInstances} инстансов всего, " +
                      $"culling {(CullingReady ? "включён" : "выключен")}.", this);
        }

        // Bounds ЭТОГО тайла Terrain'а (не отдельных инстансов) - используется в Update() как
        // ранний выход: если ближайшая точка тайла к камере уже дальше _cullRadius, ни один
        // инстанс на нём физически не может пройти distance-culling, и весь GPU-диспатч
        // (Dispatch на каждый LOD-уровень каждого батча) можно пропустить целиком. Без этого все
        // тайлы диспатчат КАЖДЫЙ кадр независимо от видимости - см. Docs/06-lod.html и
        // обсуждение производительности. Считается один раз в OnEnable - позиция/размер тайла не
        // меняются в рантайме.
        private Bounds _tileBounds;

        protected abstract void BuildBatches();

        // Пересборка батчей на лету (например, из OnValidate при правке density/override полей в
        // инспекторе во время Play) - BuildBatches сам по себе только добавляет в _batches, старые
        // GPU-буферы нужно распустить явно, иначе они утекут.
        protected void RebuildBatches()
        {
            foreach (IndirectRenderBatch batch in _batches) batch.Dispose();
            _batches.Clear();
            BuildBatches();
            LogBatchSummary();
        }

        // Сколько прототипов у Terrain'а данного типа (TreePrototype/DetailPrototype) и как достать
        // префаб конкретного индекса - нужно ConvertLodFromTerrainPrototypes ниже, реализуют наследники.
        protected abstract int GetPrototypeCount();
        protected abstract GameObject GetPrototypePrefab(int index);

        // Заполняет _prototypeOverrides[i].LodLevels из LODGroup префаба прототипа (та же логика,
        // что и автоопределение в BuildMeshLevels, только результат не выбрасывается, а сохраняется
        // в override - чтобы дальше руками поправить/добавить импостор в инспекторе, а не тащить
        // каждый Mesh/Material в реорганизуемый список самой. Импостор сюда НЕ входит - на Terrain'е
        // нет данных, откуда его взять (это отдельно запечённый AmplifyImpostors ассет), трогать
        // ImpostorMesh/ImpostorMaterial не будет, если они уже заданы.
        [ContextMenu("Convert LOD From Terrain Prototypes")]
        private void ConvertLodFromTerrainPrototypes()
        {
            if (_terrain == null) _terrain = Terrain.activeTerrain;
            if (_terrain == null)
            {
                Debug.LogWarning($"{GetType().Name}: нет Terrain'а - конвертировать нечего.", this);
                return;
            }

            Camera camera = _targetCamera != null ? _targetCamera : Camera.main;
            if (camera == null)
            {
                Debug.LogWarning($"{GetType().Name}: нет камеры - дистанции LOD посчитать не из чего.", this);
                return;
            }

            int count = GetPrototypeCount();
            var overrides = new PrototypeOverride[count];
            int copyCount = Mathf.Min(_prototypeOverrides?.Length ?? 0, count);
            for (int i = 0; i < copyCount; i++) overrides[i] = _prototypeOverrides[i]; // сохраняем то, что уже настроено (включая Impostor*)

            int convertedCount = 0;
            for (int i = 0; i < count; i++)
            {
                LodLevel[] lodLevels = ExtractLodLevelsFromPrefab(GetPrototypePrefab(i), camera, _cullRadius, overrides[i].LodSizeOverride);
                if (lodLevels.Length == 0) continue;
                overrides[i].LodLevels = lodLevels;
                convertedCount++;
            }

            _prototypeOverrides = overrides;
            Debug.Log($"{GetType().Name}: сконвертировано {convertedCount} из {count} прототипов " +
                      "(LODGroup -> LodLevels). Impostor не трогала - на Terrain'е для него нет данных, " +
                      "задай сам(а) в инспекторе. Дистанции посчитаны по текущей камере/_cullRadius, " +
                      "проверь и поправь руками при необходимости.", this);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        // Все Renderer'ы одного LOD (а также все дочерние объекты префаба) попадают в уровень как
        // отдельные части. Локальный Transform каждого Renderer'а относительно КОРНЯ префаба (смещение,
        // поворот, масштаб дочернего объекта) запекается в копию меша - индиректный рендер не
        // инстанцирует префаб, а в InstanceData есть только позиция/поворот/масштаб всего дерева.
        // Если префаб без LODGroup - все его MeshRenderer'ы образуют один уровень.
        internal static LodLevel[] ExtractLodLevelsFromPrefab(GameObject prefab, Camera camera, float cullRadius, float sizeOverride)
        {
            if (prefab == null) return System.Array.Empty<LodLevel>();

            LODGroup lodGroup = prefab.GetComponentInChildren<LODGroup>();
            if (lodGroup == null)
            {
                MeshMaterialEntry[] all = CollectParts(prefab.GetComponentsInChildren<Renderer>(true), prefab.transform);
                return all.Length == 0
                    ? System.Array.Empty<LodLevel>()
                    : new[] { new LodLevel { Parts = all, MaxDistance = cullRadius } };
            }

            LOD[] lods = lodGroup.GetLODs();
            float worldSize = sizeOverride > 0f ? sizeOverride : ResolveLodGroupWorldSize(prefab, lodGroup, lods);
            float tanHalfFov = camera != null ? Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) : 1f;

            var levels = new List<LodLevel>();
            foreach (LOD lod in lods)
            {
                // Импостор Amplify в LODGroup - не обычный LOD: его заменяет indirect-импостор (AppendImpostorLevel)
                if (IsImpostorLod(lod)) continue;
                MeshMaterialEntry[] parts = CollectParts(lod.renderers, prefab.transform);
                if (parts.Length == 0) continue;

                float maxDistance = LodGroupDistance(worldSize, lod.screenRelativeTransitionHeight, tanHalfFov, cullRadius);
                levels.Add(new LodLevel { Parts = parts, MaxDistance = maxDistance });
            }
            return levels.ToArray();
        }

        private static bool IsImpostorLod(LOD lod)
        {
            if (lod.renderers == null || lod.renderers.Length == 0) return false;
            foreach (Renderer renderer in lod.renderers)
            {
                if (renderer == null) continue;
                Material material = renderer.sharedMaterial;
                if (material == null || material.shader == null || !material.shader.name.Contains("Amplify Impostors")) return false;
            }
            return true;
        }

        // Части из набора Renderer'ов: у каждого Renderer'а - свой меш (с запечённым локальным
        // Transform'ом относительно корня) и материал(ы). Если у Renderer'а несколько материалов
        // (сабмешей) - каждый сабмеш становится отдельной частью со своим материалом.
        private static MeshMaterialEntry[] CollectParts(Renderer[] renderers, Transform root)
        {
            var parts = new List<MeshMaterialEntry>();
            if (renderers == null) return parts.ToArray();

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                if (mesh == null) continue;

                Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.transformHandle.localToWorldMatrix;
                Material[] materials = renderer.sharedMaterials;

                if (materials.Length <= 1 || mesh.subMeshCount <= 1)
                {
                    parts.Add(new MeshMaterialEntry
                    {
                        Mesh = BakeIntoRootSpace(mesh, toRoot),
                        Material = materials.Length > 0 ? materials[0] : null
                    });
                    continue;
                }

                int count = Mathf.Min(materials.Length, mesh.subMeshCount);
                for (int sub = 0; sub < count; sub++)
                {
                    parts.Add(new MeshMaterialEntry
                    {
                        Mesh = BakeIntoRootSpace(ExtractSubMesh(mesh, sub), toRoot),
                        Material = materials[sub]
                    });
                }
            }
            return parts.ToArray();
        }

        // Кэш запечённых мешей: один и тот же (mesh, матрица) с разных тайлов/компонентов даёт ОДИН
        // и тот же экземпляр - иначе слияние батчей по (mesh, material) в TerrainTreeIndirectRenderer
        // перестало бы работать.
        [AutoStaticsCleanup]
        private static readonly Dictionary<(Mesh, Matrix4x4), Mesh> _bakedMeshCache = new();
        [AutoStaticsCleanup]
        private static readonly Dictionary<(Mesh, int), Mesh> _subMeshCache = new();

        private static Mesh BakeIntoRootSpace(Mesh source, Matrix4x4 matrix)
        {
            if (IsNearlyIdentity(matrix)) return source;

            var key = (source, matrix);
            if (_bakedMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            if (!source.isReadable)
            {
                Debug.LogWarning($"Mesh '{source.name}' не включён Read/Write - не могу запечь локальный Transform " +
                                 "дочернего объекта, использую меш как есть (смещение/поворот/масштаб ребёнка потеряются).", source);
                return source;
            }

            Mesh baked = Object.Instantiate(source);
            baked.name = source.name + "_baked";
            baked.hideFlags = HideFlags.HideAndDontSave;

            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector4[] tangents = source.tangents;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;

            for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 t = matrix.MultiplyVector(tangents[i]).normalized;
                tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
            }

            baked.vertices = vertices;
            if (normals.Length == vertices.Length) baked.normals = normals;
            if (tangents.Length == vertices.Length) baked.tangents = tangents;
            baked.RecalculateBounds();

            _bakedMeshCache[key] = baked;
            return baked;
        }

        protected static bool IsNearlyIdentity(Matrix4x4 m)
        {
            Matrix4x4 id = Matrix4x4.identity;
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(m[i] - id[i]) > 1e-4f) return false;
            return true;
        }

        private static Mesh ExtractSubMesh(Mesh source, int subMesh)
        {
            var key = (source, subMesh);
            if (_subMeshCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            if (!source.isReadable) return source; // без Read/Write сабмеш не вырезать - рисуем меш целиком

            Mesh copy = Object.Instantiate(source);
            copy.name = $"{source.name}_sub{subMesh}";
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.subMeshCount = 1;
            copy.SetTriangles(source.GetTriangles(subMesh), 0);
            copy.RecalculateBounds();
            _subMeshCache[key] = copy;
            return copy;
        }

        // Размер LODGroup в мире (как LODGroup.size - наибольшая сторона bounds). Если size == 0
        // (Recalculate Bounds не запускался) - считаем сами по bounds ВСЕХ Renderer'ов LOD0/префаба,
        // включая все дочерние объекты, в пространстве корня префаба (с их смещениями/поворотами/
        // масштабами), а не по одному мешу первого Renderer'а.
        internal static float ResolveLodGroupWorldSize(GameObject prefab, LODGroup lodGroup, LOD[] lods)
        {
            // Сначала настоящие bounds LOD0 (как Recalculate Bounds): LODGroup.size в префабах бывает устаревшим
            // (у Dry - 6 при меше ~1.5), и тогда импостор уезжает за _cullRadius и не включается никогда
            float size = ComputePrefabSize(prefab, lods);
            if (size > 0f) return size;
            return lodGroup.size > 0f ? lodGroup.size : 1f; // совсем нечем оценить размер - 1 хотя бы не схлопывает все дистанции в 0
        }

        private static float ComputePrefabSize(GameObject prefab, LOD[] lods)
        {
            Transform root = prefab.transform;
            Renderer[] source = null;
            if (lods != null && lods.Length > 0 && lods[0].renderers != null && lods[0].renderers.Length > 0)
                source = lods[0].renderers;
            if (source == null) source = prefab.GetComponentsInChildren<Renderer>(true);

            bool has = false;
            Bounds total = default;
            foreach (Renderer renderer in source)
            {
                if (renderer == null) continue;
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                if (mesh == null) continue;

                Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                Bounds b = TransformBounds(mesh.bounds, toRoot);
                if (!has) { total = b; has = true; }
                else total.Encapsulate(b);
            }
            if (!has) return 0f;
            Vector3 sz = total.size;
            return Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z));
        }

        private static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            Vector3 c = b.center, e = b.extents;
            Bounds result = new Bounds(m.MultiplyPoint3x4(c + new Vector3(-e.x, -e.y, -e.z)), Vector3.zero);
            for (int i = 1; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                result.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return result;
        }

        protected static Bounds ComputeTerrainBounds(Vector3 origin, Vector3 size, float meshPadding)
        {
            Vector3 center = origin + size * 0.5f;
            Vector3 extents = size * 0.5f + Vector3.one * (meshPadding + 5f);
            return new Bounds(center, extents * 2f);
        }

        // Кэш подменённых материалов - один раз создали Indirect-копию конкретного исходного
        // материала, дальше при каждом BuildBatches переиспользуем ту же ссылку (а не плодим новую
        // копию каждый OnEnable/RebuildBatches). Ключ - именно исходный материал прототипа, а не
        // индекс/имя - так разные прототипы с одним и тем же материалом получат один и тот же
        // результат, и слияние батчей по (mesh, material) в TerrainTreeIndirectRenderer не сломается.
        [AutoStaticsCleanup]
        private static readonly Dictionary<Material, Material> _indirectMaterialCache = new();
        [AutoStaticsCleanup]
        private static readonly HashSet<Material> _warnedNoConversion = new();

        // Подменяет материал на его Indirect-аналог из _indirectShaderConversions - ТОЛЬКО в памяти
        // (HideFlags.HideAndDontSave), исходный .mat-ассет не трогается. Свойства (текстуры/цвета)
        // копируются по имени через CopyPropertiesFromMaterial - руками переносить не нужно. Если
        // пары для этого шейдера нет в таблице - возвращает материал как есть (с одним предупреждением
        // на материал, не каждый кадр) - рендер в этом случае скорее всего будет выглядеть неверно
        // (обычный шейдер не читает _InstanceBuffer), но не крашит и ничего не ломает.
        protected Material ResolveIndirectMaterial(Material source)
        {
            if (source == null) return null;
            if (_indirectMaterialCache.TryGetValue(source, out Material cached) && cached != null) return cached;

            Shader indirectShader = FindIndirectShader(_indirectShaderConversions, source.shader);
            // Пары нет у этого тайла - берём у любого другого: таблицы у тайлов свои, а кэш общий
            if (indirectShader == null)
                foreach (var other in FindObjectsByType<TerrainIndirectRendererBase>(FindObjectsInactive.Include))
                    if ((indirectShader = FindIndirectShader(other._indirectShaderConversions, source.shader)) != null) break;
            if (indirectShader == null)
            {
                // Промах не кэшируем - иначе тайл без пары навсегда запретил бы подмену остальным
                if (_warnedNoConversion.Add(source))
                    Debug.LogWarning($"{GetType().Name}: для шейдера '{source.shader?.name}' материала '{source.name}' нет " +
                                      "пары в _indirectShaderConversions ни у одного тайла - рендерю материалом как есть.", source);
                return source;
            }

            var converted = new Material(indirectShader) { name = source.name + " (Indirect Runtime)", hideFlags = HideFlags.HideAndDontSave };
            converted.CopyPropertiesFromMaterial(source);
            _indirectMaterialCache[source] = converted;
            return converted;
        }

        private static Shader FindIndirectShader(ShaderConversion[] table, Shader source)
        {
            if (table == null || source == null) return null;
            foreach (ShaderConversion pair in table)
                if (pair.Source == source && pair.Indirect != null) return pair.Indirect;
            return null;
        }

        // Применяет ResolveIndirectMaterial к каждому уровню LOD - вызывать ПОСЛЕ ResolveLevels, но
        // ДО AppendImpostorLevel (у импостора свой уже-Indirect материал, задаётся руками через
        // over.ImpostorMaterial/ImpostorMaterialConverter - гонять его через эту таблицу не нужно).
        protected MeshLevel[] ApplyIndirectMaterials(MeshLevel[] levels)
        {
            for (int i = 0; i < levels.Length; i++)
            {
                MeshLevel level = levels[i];
                if (level.Material != null) level.Material = ResolveIndirectMaterial(level.Material);
                levels[i] = level;
            }
            return levels;
        }

        // Базовый меш/материал прототипа: если задана ручная LodLevels - берём из первой части
        // первого уровня (fallback на fallbackMesh/fallbackMaterial по каждому полю отдельно),
        // иначе - сами fallback'и (обычно meshFilter.sharedMesh/renderer.sharedMaterial прототипа).
        protected static (Mesh mesh, Material material) ResolveBaseMeshMaterial(
            PrototypeOverride over, Mesh fallbackMesh, Material fallbackMaterial)
        {
            MeshMaterialEntry? first = FirstPart(over);
            if (first == null) return (fallbackMesh, fallbackMaterial);
            return (first.Value.Mesh != null ? first.Value.Mesh : fallbackMesh,
                    first.Value.Material != null ? first.Value.Material : fallbackMaterial);
        }

        private static MeshMaterialEntry? FirstPart(PrototypeOverride over)
        {
            if (over.LodLevels == null) return null;
            foreach (LodLevel level in over.LodLevels)
            {
                if (level.Parts == null) continue;
                foreach (MeshMaterialEntry part in level.Parts)
                    if (part.Mesh != null) return part;
            }
            return null;
        }

        // Приоритет: ручная LodLevels (если в ней есть хоть одна валидная часть) -> автоопределение
        // из LODGroup прототипа (BuildMeshLevels, требует CullingReady) -> один уровень на fallbackMesh.
        // Импостор сюда не входит - добавляется отдельно через AppendImpostorLevel. Все части одного
        // LOD-уровня получают ОДИН и тот же диапазон [MinDistance, MaxDistance) - рисуются вместе,
        // как несколько Renderer'ов одного LOD у настоящего LODGroup (например, ствол + листва).
        //
        // Дистанции (заданные руками в LodLevels или посчитанные автоматически в BuildMeshLevels)
        // посчитаны от ЛОКАЛЬНОГО (авторского) размера меша - см. BuildMeshLevels/
        // ExtractLodLevelsFromPrefab, которые больше не умножают на lossyScale префаба: индиректный
        // рендер никогда не инстанцирует префаб и не применяет его Transform, реальный размер на
        // экране задаётся только averageInstanceScale (widthScale/heightScale дерева или
        // minWidth/maxWidth DetailPrototype - то же самое, что уже кладётся в InstanceData.Scale
        // у TerrainTreeIndirectRenderer/TerrainGrassIndirectRenderer). Если меш авторски маленький
        // (см. Docs), а реальный размер целиком берётся из этого множителя - без него все LOD-
        // дистанции посчитаются как для меша размером в несколько миллиметров и свернутся в 0.
        // ScaleMultiplier - ДОПОЛНИТЕЛЬНАЯ ручная поправка поверх этого, а не замена ему.
        // Масштабируем ВСЕ дистанции здесь, а не один раз при вводе - тогда правки ScaleMultiplier
        // в инспекторе применяются сразу.
        //
        // Если импостора нет - последний уровень насильно дотягивается до cullRadius (иначе появится
        // никем не запрошенный "недорисованный хвост"). Если импостор есть - НЕ дотягиваем: место
        // до cullRadius нужно оставить ему, AppendImpostorLevel сам продолжит ровно оттуда, где
        // кончился последний уровень - дыра между ними становится структурно невозможной.
        protected MeshLevel[] ResolveLevels(GameObject prefab, Mesh fallbackMesh, PrototypeOverride over, float cullRadius, float averageInstanceScale = 1f)
        {
            bool isManualLevels = over.LodLevels != null && over.LodLevels.Length > 0;
            float scaleMultiplier = (over.ScaleMultiplier > 0f ? over.ScaleMultiplier : 1f) *
                                     (averageInstanceScale > 0f ? averageInstanceScale : 1f);
            MeshLevel[] levels = System.Array.Empty<MeshLevel>();
            if (isManualLevels)
            {
                var sorted = (LodLevel[])over.LodLevels.Clone();
                System.Array.Sort(sorted, (a, b) => a.MaxDistance.CompareTo(b.MaxDistance));

                var built = new List<MeshLevel>();
                float prevMax = 0f;
                foreach (LodLevel lodLevel in sorted)
                {
                    if (lodLevel.Parts == null) continue;
                    bool anyPartAdded = false;
                    foreach (MeshMaterialEntry part in lodLevel.Parts)
                    {
                        if (part.Mesh == null)
                        {
                            Debug.LogWarning($"{GetType().Name}: в LOD-уровне пустой Mesh - часть пропущена.", this);
                            continue;
                        }
                        built.Add(new MeshLevel
                        {
                            Mesh = part.Mesh,
                            Material = part.Material,
                            MinDistance = prevMax,
                            MaxDistance = lodLevel.MaxDistance,
                            ForceMeshLod = 0
                        });
                        anyPartAdded = true;
                    }
                    if (anyPartAdded) prevMax = lodLevel.MaxDistance;
                }
                levels = built.Count > 0 ? built.ToArray() : System.Array.Empty<MeshLevel>();

                // Ручные LodLevels заданы, но ВСЕ их части оказались битыми (пустой Mesh - например,
                // ссылка стёрлась после переимпорта модели) - не оставляем дерево вообще без
                // базового LOD (только дальний импостор от дистанции 0), откатываемся на
                // автоопределение из LODGroup, как если бы ручных LodLevels не было совсем.
                if (levels.Length == 0)
                {
                    Debug.LogWarning($"{GetType().Name}: ручные LodLevels заданы, но ВСЕ части битые " +
                                      "(пустой Mesh) - откатываюсь на автоопределение из LODGroup префаба.", this);
                    isManualLevels = false;
                }
            }
            if (!isManualLevels && levels.Length == 0)
            {
                levels = CullingReady
                    ? BuildMeshLevels(prefab, fallbackMesh, _targetCamera, cullRadius, over.LodSizeOverride)
                    : SingleLevel(fallbackMesh, cullRadius);
            }

            // ScaleMultiplier/averageInstanceScale домножают дистанции ТОЛЬКО для автоопределения
            // из LODGroup (BuildMeshLevels/lodGroup.size - см. комментарий выше про "меш авторски
            // маленький"). Ручные LodLevels (введены руками или сконвертированы через "Convert LOD
            // From Terrain Prototypes") УЖЕ хранят финальные, реальные метры - их видно и правится
            // прямо в инспекторе. Повторно домножать их - баг: при большом ScaleMultiplier
            // (например, нужном только для реального масштаба рендера, не для LOD) реальные
            // дистанции улетают далеко за cullRadius, обрезаются об него, и все уровни кроме
            // первого схлопываются в один и тот же MinDistance == MaxDistance == cullRadius - т.е.
            // ближний LOD рисуется вообще везде, а дальние/импостор никогда не наступают.
            if (!isManualLevels && scaleMultiplier != 1f)
            {
                for (int i = 0; i < levels.Length; i++)
                {
                    MeshLevel level = levels[i];
                    level.MinDistance = Mathf.Min(level.MinDistance * scaleMultiplier, cullRadius);
                    level.MaxDistance = Mathf.Min(level.MaxDistance * scaleMultiplier, cullRadius);
                    levels[i] = level;
                }
            }

            if (levels.Length > 0 && over.ImpostorMesh == null)
                ExtendLastLodToRadius(levels, cullRadius);
            return levels;
        }

        // Последний LOD может состоять из нескольких частей (Renderer'ов) с одной и той же
        // MaxDistance - дотягиваем до cullRadius ВСЕ его части, а не только последнюю в массиве.
        internal static void ExtendLastLodToRadius(MeshLevel[] levels, float cullRadius)
        {
            if (levels == null || levels.Length == 0) return;
            float lastMax = levels[^1].MaxDistance;
            for (int i = levels.Length - 1; i >= 0 && Mathf.Approximately(levels[i].MaxDistance, lastMax); i--)
            {
                MeshLevel level = levels[i];
                level.MaxDistance = cullRadius;
                levels[i] = level;
            }
        }

        // Строит уровни LOD из LODGroup прототипа (если есть), переводя его
        // screenRelativeTransitionHeight в мировую дистанцию по формуле подобных треугольников:
        // distance = (size * 0.5) / (tan(fov/2) * screenRelativeTransitionHeight). Без LODGroup или
        // без камеры - один уровень на fallbackMesh, как раньше. lodGroup.size - ЛОКАЛЬНЫЙ (авторский)
        // размер префаба, БЕЗ lossyScale: индиректный рендер не инстанцирует префаб и никогда не
        // применяет его Transform, поэтому lossyScale к реальному размеру на экране отношения не
        // имеет - реальный множитель размера (widthScale/heightScale и т.п.) применяет вызывающий
        // ResolveLevels через averageInstanceScale. Не учитывает индивидуальный widthScale/heightScale
        // КОНКРЕТНОГО инстанса (LOD общий на батч, там используется усреднённое значение) -
        // приближение, не точная копия того, как Terrain считал бы LOD для каждого дерева отдельно.
        // Последний уровень НЕ дотягивается до cullRadius здесь - этим управляет вызывающий
        // ResolveLevels (зависит от наличия импостора).
        internal static MeshLevel[] BuildMeshLevels(GameObject prefab, Mesh fallbackMesh, Camera camera, float cullRadius, float sizeOverride = 0f)
        {
            if (prefab == null || camera == null || camera.orthographic)
                return SingleLevel(fallbackMesh, cullRadius);

            // Та же логика, что и у Convert LOD: все Renderer'ы каждого LOD (и все дочерние объекты
            // префаба) -> части уровня. Каждая часть - отдельный MeshLevel с тем же диапазоном.
            LodLevel[] lodLevels = ExtractLodLevelsFromPrefab(prefab, camera, cullRadius, sizeOverride);
            var levels = new List<MeshLevel>();
            float prevMax = 0f;
            foreach (LodLevel lod in lodLevels)
            {
                foreach (MeshMaterialEntry part in lod.Parts)
                {
                    levels.Add(new MeshLevel
                    {
                        Mesh = part.Mesh,
                        Material = part.Material,
                        MinDistance = prevMax,
                        MaxDistance = lod.MaxDistance,
                        ForceMeshLod = 0
                    });
                }
                prevMax = lod.MaxDistance;
            }

            return levels.Count > 0 ? levels.ToArray() : SingleLevel(fallbackMesh, cullRadius);
        }

        // Формула подобных треугольников: screenRelativeTransitionHeight -> мировая дистанция.
        // Обратная от Unity'шной DistanceToRelativeHeight (AutoLOD/LODGroupExtensions.cs, тот же
        // метод, которым LODGroupEditor.cs рисует слайдер в инспекторе LODGroup):
        // relativeHeight = size*0.5/(distance*tanHalfFov) - т.е. distance = size*0.5/(relativeHeight*
        // tanHalfFov), ровно как здесь. Вынесена отдельно, чтобы ConvertLodFromTerrainPrototypes
        // считал так же, как BuildMeshLevels.
        private static float LodGroupDistance(float worldSize, float screenRelativeTransitionHeight, float tanHalfFov, float cullRadius) =>
            Mathf.Min(worldSize * 0.5f / (Mathf.Max(screenRelativeTransitionHeight, 0.0001f) * tanHalfFov), cullRadius);

        private static MeshLevel[] SingleLevel(Mesh mesh, float cullRadius) =>
            mesh != null
                ? new[] { new MeshLevel { Mesh = mesh, MaxDistance = cullRadius, ForceMeshLod = 0 } }
                : System.Array.Empty<MeshLevel>();

        // Добавляет импостор как ещё один, самый дальний уровень LOD - начинается ровно там, где
        // кончается последний уровень levels (уже гарантированно НЕ дотянутый до cullRadius, см.
        // ResolveLevels), и тянется до cullRadius. Дистанция начала не задаётся отдельно - дыра
        // между концом LOD и началом импостора структурно невозможна. Не изменяет levels, если
        // импостор не задан.
        protected MeshLevel[] AppendImpostorLevel(MeshLevel[] levels, PrototypeOverride over, float cullRadius)
        {
            if (over.ImpostorMesh == null) return levels;

            float startDistance = levels.Length > 0 ? levels[^1].MaxDistance : 0f;
            if (startDistance >= cullRadius)
            {
                Debug.LogWarning($"{GetType().Name}: ImpostorMesh задан, но LOD-уровни уже доходят до " +
                                  $"_cullRadius ({cullRadius}) - для импостора не осталось места, он не будет " +
                                  "использован. Уменьши дистанцию последнего LOD-уровня, если он нужен.", this);
                return levels;
            }
            if (!CullingReady)
            {
                Debug.LogWarning($"{GetType().Name}: ImpostorMesh задан, но импостор-уровень требует " +
                                  "_useComputeCulling - без него LOD целиком игнорируется.", this);
                return levels;
            }

            var result = new MeshLevel[levels.Length + 1];
            System.Array.Copy(levels, result, levels.Length);
            result[levels.Length] = new MeshLevel
            {
                Mesh = over.ImpostorMesh,
                Material = over.ImpostorMaterial,
                MinDistance = startDistance,
                MaxDistance = cullRadius,
                ForceMeshLod = 0,
                // Плоский billboard - отбрасывать тень ему нечем, а это обычно самый многочисленный
                // по инстансам уровень батча (дальний диапазон). При 4 каскадах теней в URP-ассете
                // это лишний полный проход по самой массовой геометрии - выключаем точечно здесь,
                // не трогая ShadowCastingMode остальных уровней/URP-ассет.
                DisableShadows = true
            };
            return result;
        }

        private void Update()
        {
            // Гейтинг тайла: если ближайшая точка ЭТОГО тайла к камере уже дальше _cullRadius,
            // ни один инстанс на нём не может пройти distance-тест в компуте - пропускаем Dispatch
            // и Render целиком на ВСЕХ батчах/уровнях этого тайла, а не только на дальних инстансах
            // внутри него. Раньше диспатчились ВСЕ тайлы каждый кадр независимо от дистанции - это
            // и было главным виновником просадки FPS (см. обсуждение производительности).
            if (_targetCamera != null)
            {
                float distSq = _tileBounds.SqrDistance(_targetCamera.transform.position);
                if (distSq > _cullRadius * _cullRadius) return;
            }

            var settings = new CullingSettings
            {
                EnableDistance = _enableDistanceCulling,
                EnableFrustum = _enableFrustumCulling,
                EnableOcclusion = _enableOcclusionCulling,
                CullRadius = _cullRadius,
                DebugInstanceIndex = -1
            };

            foreach (IndirectRenderBatch batch in _batches)
                batch.Render(_targetCamera, _restrictRenderToTargetCamera, settings, _lighting);
        }

        protected virtual void OnDisable()
        {
            foreach (IndirectRenderBatch batch in _batches)
                batch.Dispose();
            _batches.Clear();
        }
    }
}
