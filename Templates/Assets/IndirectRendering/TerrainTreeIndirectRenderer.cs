using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace PG.Rendering
{
    // Берёт деревья Terrain'а (TerrainData.treeInstances) и рисует их через
    // Graphics.RenderMeshIndirect вместо встроенного рендера деревьев. Один IndirectRenderBatch
    // на TreePrototype - у разных прототипов разные меши/материалы. Docs/index.html.
    //
    // Один компонент висит на одном тайле Terrain'а - деревьев на сцене обычно несколько тайлов.
    // Если у прототипов на разных тайлах совпадает итоговый (mesh, material) (см. ResolveBase-
    // MeshMaterial ниже), их инстансы сливаются в ОДИН общий батч/Draw Call на весь мир вместо
    // одного на тайл - см. BatchKey/_contributions/_mergedBatches.
    public partial class TerrainTreeIndirectRenderer : TerrainIndirectRendererBase
    {
        [SerializeField] private bool _disableTerrainTrees = true;

        [Header("Ротация")]
        [SerializeField] private float _rotationOffsetDegrees = 0f;
        [SerializeField] private bool _invertRotation = false; // на случай, если знак угла у Terrain обратный

        private float _originalTreeDistance;

        // Ключ слияния: базовый (mesh, material) + меши/материалы всей LOD-цепочки. Батч рисует levels первого
        // вклада, поэтому прототипы с одинаковым стволом, но разной листвой/LOD сливаться не должны.
        // Дистанции не сравниваются - они зависят от среднего масштаба деревьев тайла, а тайлы должны сливаться.
        private readonly struct BatchKey : System.IEquatable<BatchKey>
        {
            public readonly Mesh Mesh;
            public readonly Material Material;
            private readonly MeshLevel[] _levels;
            private readonly int _levelsHash;

            public BatchKey(Mesh mesh, Material material, MeshLevel[] levels)
            {
                Mesh = mesh;
                Material = material;
                _levels = levels ?? System.Array.Empty<MeshLevel>();
                var hash = new System.HashCode();
                foreach (MeshLevel l in _levels) hash.Add(System.HashCode.Combine(l.Mesh, l.Material, l.ForceMeshLod, l.DisableShadows));
                _levelsHash = hash.ToHashCode();
            }

            public bool Equals(BatchKey other)
            {
                if (Mesh != other.Mesh || Material != other.Material || _levelsHash != other._levelsHash || _levels.Length != other._levels.Length) return false;
                for (int i = 0; i < _levels.Length; i++)
                {
                    MeshLevel a = _levels[i], b = other._levels[i];
                    if (a.Mesh != b.Mesh || a.Material != b.Material || a.ForceMeshLod != b.ForceMeshLod || a.DisableShadows != b.DisableShadows) return false;
                }
                return true;
            }
            public override bool Equals(object obj) => obj is BatchKey other && Equals(other);
            public override int GetHashCode() => System.HashCode.Combine(Mesh, Material, _levelsHash);
        }

        // Вклад одного тайла в общий батч по BatchKey. Levels (LOD-цепочка) у всех вкладов одного
        // ключа в норме идентичны - детерминированно выводятся из одного и того же mesh/material
        // (ResolveLevels/AppendImpostorLevel не зависят от тайла), поэтому при слиянии levels
        // берутся у первого вклада, а не пересчитываются заново.
        private sealed class Contribution
        {
            public readonly TerrainTreeIndirectRenderer Owner;
            public readonly InstanceData[] Data;
            public readonly MeshLevel[] Levels;
            public readonly Bounds Bounds;

            public Contribution(TerrainTreeIndirectRenderer owner, InstanceData[] data, MeshLevel[] levels, Bounds bounds)
            {
                Owner = owner;
                Data = data;
                Levels = levels;
                Bounds = bounds;
            }
        }

        // Общее на ВСЕ тайлы состояние - здесь и происходит слияние поперёк тайлов, а не только
        // внутри одного компонента. [AutoStaticsCleanup]: без Domain Reload между Play-сессиями
        // (Unity.Scripting.LifecycleManagement) эти статики иначе унаследовали бы GPU-буферы и
        // ссылки на выгруженные тайлы прошлой сессии - см. тот же паттерн в SaveDataObjectsContainer.
        [AutoStaticsCleanup]
        private static readonly Dictionary<BatchKey, List<Contribution>> _contributions = new();
        [AutoStaticsCleanup]
        private static readonly Dictionary<BatchKey, IndirectRenderBatch> _mergedBatches = new();
        [AutoStaticsCleanup]
        private static readonly List<TerrainTreeIndirectRenderer> _activeInstances = new();

        // Ключи, под которыми СВОИ вклады зарегистрировал именно этот тайл - нужны в OnDisable/
        // при пересборке, чтобы убрать ровно их, не трогая вклады остальных тайлов того же ключа.
        private readonly List<BatchKey> _ownedBatchKeys = new();

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!enabled) return;
            _activeInstances.Add(this);
            if (!_disableTerrainTrees) return;
            _originalTreeDistance = _terrain.treeDistance;
            _terrain.treeDistance = 0f;
        }

        protected override void BuildBatches()
        {
            // Защита от повторного вызова (например, будущий OnValidate -> RebuildBatches) -
            // без этого старые вклады этого тайла остались бы в _contributions задвоенными.
            UnregisterContributions();

            TerrainData terrainData = _terrain.terrainData;
            TreeInstance[] instances = terrainData.treeInstances;
            TreePrototype[] prototypes = terrainData.treePrototypes;

            var byPrototype = new List<TreeInstance>[prototypes.Length];
            foreach (TreeInstance instance in instances)
            {
                int p = instance.prototypeIndex;
                if (p < 0 || p >= prototypes.Length) continue;
                (byPrototype[p] ??= new List<TreeInstance>()).Add(instance);
            }

            Vector3 terrainOrigin = _terrain.transform.position;
            Vector3 terrainSize = terrainData.size;

            for (int p = 0; p < prototypes.Length; p++)
            {
                List<TreeInstance> list = byPrototype[p];
                if (list == null || list.Count == 0) continue;

                GameObject prefab = prototypes[p].prefab;

                // Берём ВСЕ Renderer'ы префаба (в т.ч. дочерних объектов), а не первый MeshFilter:
                // базовый меш/материал - первая часть первого (самого подробного) LOD-уровня.
                // Локальные Transform'ы детей запечены в меши (см. ExtractLodLevelsFromPrefab),
                // поэтому отдельный поворот меша больше не нужен.
                Mesh fallbackMesh = null;
                Material fallbackMaterial = null;
                LodLevel[] prefabLevels = ExtractLodLevelsFromPrefab(prefab, _targetCamera, _cullRadius, GetOverride(p).LodSizeOverride);
                if (prefabLevels.Length > 0 && prefabLevels[0].Parts.Length > 0)
                {
                    fallbackMesh = prefabLevels[0].Parts[0].Mesh;
                    fallbackMaterial = prefabLevels[0].Parts[0].Material;
                }

                // Override не трогает TreePrototype.prefab - чтобы подставить свою LOD-цепочку
                // (с шейдером, читающим _InstanceBuffer) без изменения настоящих деревьев Terrain'а.
                PrototypeOverride over = GetOverride(p);
                (Mesh mesh, Material material) = ResolveBaseMeshMaterial(over, fallbackMesh, fallbackMaterial);

                if (mesh == null)
                {
                    Debug.LogWarning($"TerrainTreeIndirectRenderer: у TreePrototype[{p}] ({prefab?.name}) нет меша и нет override - пропущен.", this);
                    continue;
                }
                if (material == null)
                {
                    Debug.LogWarning($"TerrainTreeIndirectRenderer: у TreePrototype[{p}] ({prefab?.name}) нет материала и нет override - пропущен.", this);
                    continue;
                }

                // Подмена шейдера на Indirect-аналог (см. _indirectShaderConversions/ResolveIndirectMaterial
                // в базовом классе) - сама, в памяти, при каждом входе в Play; обычный материал прототипа
                // (с обычным Lit-шейдером) руками под Indirect переделывать не нужно.
                material = ResolveIndirectMaterial(material);

                // Полная комбинация: масштаб КОРНЯ префаба x масштаб ДОЧЕРНИХ объектов на пути к
                // Renderer'у x масштаб из самого TERRAIN (TreeInstance.widthScale/heightScale, см.
                // instanceScale ниже). Первые два - ResolveRendererScale/AccumulateLocalScale ниже,
                // честное перемножение localScale по цепочке (не lossyScale - тот через разложение
                // мировой матрицы на поворотах между разномасштабными звеньями искажается/задваивается).
                // Не зависит от BakeIntoRootSpace/mesh.isReadable (которая печёт transform в сам меш и
                // тихо пропускает его, если Read/Write Enabled выключен у меша) - считается прямо из
                // Transform'ов, а не из геометрии. ScaleMultiplier для деревьев ОТКЛЮЧЁН полностью -
                // не читается ни здесь, ни в ResolveLevels (см. overForLevels ниже) - размер только
                // из реальных данных префаба/Terrain, инспектор никак не участвует.
                float rendererScale = ResolveRendererScale(prefab);
                float scaleMultiplier = rendererScale;
                var data = new InstanceData[list.Count];
                float instanceScaleSum = 0f;
                for (int i = 0; i < list.Count; i++)
                {
                    TreeInstance t = list[i];
                    float angleDeg = t.rotation * Mathf.Rad2Deg * (_invertRotation ? -1f : 1f) + _rotationOffsetDegrees;
                    //float instanceScale = (t.widthScale + t.heightScale) * 0.5f; // widthScale/heightScale однородный - усредняем
                    float instanceScale = t.widthScale; // widthScale/heightScale однородный - усредняем
                    instanceScaleSum += instanceScale;
                    data[i] = new InstanceData
                    {
                        Position = terrainOrigin + Vector3.Scale(t.position, terrainSize),
                        Scale = instanceScale * scaleMultiplier,
                        Rotation = Quaternion.Euler(0f, angleDeg, 0f)
                    };
                }

                // Меш обычно авторски маленький, реальный размер даёт widthScale/heightScale (и теперь
                // rendererScale) - LOD-дистанции (посчитанные от локального размера меша) должны
                // знать про этот же множитель, иначе автоопределение из LODGroup даёт дистанции для
                // меша размером в несколько миллиметров. См. комментарий у ResolveLevels.
                float averageInstanceScale = instanceScaleSum / list.Count * rendererScale;

                // ResolveLevels сам домножает дистанции на over.ScaleMultiplier, если он > 0 - раз
                // поле отключено выше, передаём копию с принудительным 0, чтобы LOD-дистанции не
                // разошлись с реальным (авто) масштабом инстансов.
                PrototypeOverride overForLevels = over;
                overForLevels.ScaleMultiplier = 0f;

                Bounds bounds = ComputeTerrainBounds(terrainOrigin, terrainSize, mesh.bounds.extents.magnitude);
                MeshLevel[] levels = ResolveLevels(prefab, mesh, overForLevels, _cullRadius, averageInstanceScale);
                levels = ApplyIndirectMaterials(levels); // до импостора - у того уже свой Indirect-материал
                levels = AppendImpostorLevel(levels, over, _cullRadius);

                Debug.Log($"TerrainTreeIndirectRenderer[{p}] {prefab?.name}: AutoScale(root*child)={rendererScale:0.###} (ScaleMultiplier отключён), " +
                          string.Join(" | ", System.Array.ConvertAll(levels, l => $"{l.Mesh?.name}[{l.MinDistance:0.#}-{l.MaxDistance:0.#}]")), this);

                RegisterContribution(new BatchKey(mesh, material, levels), data, levels, bounds);
            }
        }

        // Масштаб "корень x все промежуточные дети x сам Renderer" - перемножаем localScale КАЖДОГО
        // уровня вручную (а не берём Transform.lossyScale одним вызовом): lossyScale считается через
        // разложение полной мировой матрицы и на поворотах между разномасштабными звеньями цепочки
        // (shear) может "съехать" и задвоиться/исказиться - для честного юниформного приближения
        // надёжнее просто перемножить localScale каждого звена, как сделала бы ручная подстановка.
        // Если в префабе несколько Renderer'ов (несколько частей LOD) - усредняем их цепочки между
        // собой тоже, на случай если масштаб применён не ко всем одинаково.
        // Все дети с непустым локальным трансформом запекаются (меш читаемый) - см. BakeIntoRootSpace
        private static bool ChildTransformsBaked(GameObject prefab)
        {
            Transform root = prefab.transform;
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                if (!IsNearlyIdentity(toRoot) && !filter.sharedMesh.isReadable) return false;
            }
            return true;
        }

        private static float ResolveRendererScale(GameObject prefab)
        {
            if (prefab == null) return 1f;

            // Трансформы детей (поворот -90/90 из Blender, масштаб) запечены в меши - масштаб только корня,
            // иначе масштаб ребёнка умножится второй раз (кедр синего леса был вдвое больше)
            if (ChildTransformsBaked(prefab)) return prefab.transform.localScale.y;

            Renderer firstRenderer = prefab.GetComponentInChildren<Renderer>();
            return firstRenderer.transformHandle.lossyScale.y;
            
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return 1f;

            float sum = 0f;
            foreach (Renderer renderer in renderers)
                sum += AccumulateLocalScale(renderer.transformHandle, prefab.transformHandle);
            float average = sum / renderers.Length;
            return average > 0f ? average : 1f;
        }

        // Перемножает localScale от node вверх по parent-цепочке включительно до root. x/y/z внутри
        // одного звена усредняем - InstanceData.Scale один float на весь инстанс, неоднородность по
        // осям в него всё равно не поместится.
        private static float AccumulateLocalScale(TransformHandle node, TransformHandle root)
        {
            float scale = 1f;
            for (TransformHandle t = node; t != null; t = t.parent)
            {
                Vector3 s = t.localScale;
                scale *= (s.x + s.y + s.z) / 3f;
                if (t == root) break;
            }
            return scale > 0f ? scale : 1f;
        }

        private void RegisterContribution(BatchKey key, InstanceData[] data, MeshLevel[] levels, Bounds bounds)
        {
            if (!_contributions.TryGetValue(key, out List<Contribution> list))
                _contributions[key] = list = new List<Contribution>();
            list.Add(new Contribution(this, data, levels, bounds));
            _ownedBatchKeys.Add(key);
            RebuildMergedBatch(key);
        }

        private void UnregisterContributions()
        {
            if (_ownedBatchKeys.Count == 0) return;

            var affectedKeys = new HashSet<BatchKey>(_ownedBatchKeys);
            _ownedBatchKeys.Clear();

            foreach (BatchKey key in affectedKeys)
            {
                if (_contributions.TryGetValue(key, out List<Contribution> list))
                {
                    list.RemoveAll(c => c.Owner == this);
                    if (list.Count == 0) _contributions.Remove(key);
                }
                RebuildMergedBatch(key);
            }
        }

        // Пересобирает общий GPU-батч ключа из ВСЕХ текущих вкладов (всех тайлов, у которых есть
        // прототип с этим mesh/material) - старый батч распускается, новый строится на
        // конкатенации InstanceData всех вкладов. Вызывается на каждую регистрацию/снятие вклада -
        // деревьев по миру немного (не десятки тысяч тайлов), так что пересборка батча целиком
        // дешевле, чем городить инкрементальное дозаполнение GraphicsBuffer.
        private static void RebuildMergedBatch(BatchKey key)
        {
            if (_mergedBatches.TryGetValue(key, out IndirectRenderBatch stale))
            {
                stale.Dispose();
                _mergedBatches.Remove(key);
            }

            if (!_contributions.TryGetValue(key, out List<Contribution> list) || list.Count == 0)
                return;

            int totalCount = 0;
            foreach (Contribution c in list) totalCount += c.Data.Length;

            var merged = new InstanceData[totalCount];
            Bounds bounds = list[0].Bounds;
            int offset = 0;
            foreach (Contribution c in list)
            {
                System.Array.Copy(c.Data, 0, merged, offset, c.Data.Length);
                offset += c.Data.Length;
                bounds.Encapsulate(c.Bounds);
            }

            Contribution first = list[0];
            ComputeShader cullingShader = first.Owner.CullingReady ? first.Owner._cullingShader : null;
            _mergedBatches[key] = new IndirectRenderBatch(key.Mesh, key.Material, merged, bounds, cullingShader, first.Levels);
        }

        // Батчи общие на все тайлы - рендерить их должен только ОДИН компонент за кадр, иначе
        // каждый активный тайл рисовал бы их заново (N раз вместо 1). "Ведущий" - первый в
        // _activeInstances; при его отключении эстафету на следующем кадре сам подхватывает
        // следующий оставшийся тайл - отдельного состояния для этого не держим.
        private void LateUpdate()
        {
            if (_activeInstances.Count == 0 || _activeInstances[0] != this || _targetCamera == null) return;

            var settings = new CullingSettings
            {
                EnableDistance = _enableDistanceCulling,
                EnableFrustum = _enableFrustumCulling,
                EnableOcclusion = _enableOcclusionCulling,
                CullRadius = _cullRadius,
                DebugInstanceIndex = -1
            };
            foreach (IndirectRenderBatch batch in _mergedBatches.Values)
                batch.Render(_targetCamera, _restrictRenderToTargetCamera, settings, _lighting);
        }

        protected override void OnDisable()
        {
            UnregisterContributions();
            _activeInstances.Remove(this);
            base.OnDisable();
            if (_disableTerrainTrees && _terrain != null)
                _terrain.treeDistance = _originalTreeDistance;
        }

        protected override int GetPrototypeCount() => _terrain.terrainData.treePrototypes.Length;
        protected override GameObject GetPrototypePrefab(int index) => _terrain.terrainData.treePrototypes[index].prefab;
    }
}
