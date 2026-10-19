using System.Collections.Generic;
using UnityEngine;

namespace PG.Rendering
{
    // Берёт траву Terrain'а (detail layers) и рисует её через Graphics.RenderMeshIndirect вместо
    // встроенного рендера травы. Меш-прототипы (usePrototypeMesh) берут меш/материал из своего
    // GameObject'а; текстурные билборды своего меша не имеют - им нужен override в инспекторе
    // (см. _prototypeOverrides / TerrainPrototypeOverrideEditorBase). Один IndirectRenderBatch
    // на DetailPrototype. Docs/index.html.
    public class TerrainGrassIndirectRenderer : TerrainIndirectRendererBase
    {
        [SerializeField] private bool _disableTerrainDetails = true;
        [SerializeField] private int _maxInstancesPerLayer = 200000; // защита от гигантских detail-карт
        [SerializeField] private int _maxTotalInstances = 300000; // защита от суммы по всем слоям сразу
        [SerializeField, Range(0f, 1f)] private float _densityMultiplier = 1f;

        private float _originalDetailObjectDistance;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!enabled || !_disableTerrainDetails) return;
            _originalDetailObjectDistance = _terrain.detailObjectDistance;
            _terrain.detailObjectDistance = 0f;
        }

        protected override void BuildBatches()
        {
            TerrainData terrainData = _terrain.terrainData;
            DetailPrototype[] prototypes = terrainData.detailPrototypes;
            Vector3 terrainOrigin = _terrain.transformHandle.position;
            Vector3 terrainSize = terrainData.size;
            int mapWidth = terrainData.detailWidth;
            int mapHeight = terrainData.detailHeight;
            float cellSizeX = terrainSize.x / mapWidth;
            float cellSizeZ = terrainSize.z / mapHeight;
            var rng = new System.Random(12345); // фиксированный seed - детерминированный разброс/поворот/масштаб
            int totalInstances = 0;

            for (int layer = 0; layer < prototypes.Length; layer++)
            {
                if (totalInstances >= _maxTotalInstances)
                {
                    Debug.LogWarning($"TerrainGrassIndirectRenderer: достигнут _maxTotalInstances ({_maxTotalInstances}) - " +
                                      $"слои {layer}..{prototypes.Length - 1} пропущены.", this);
                    break;
                }

                DetailPrototype proto = prototypes[layer];
                MeshFilter meshFilter = proto.usePrototypeMesh && proto.prototype != null
                    ? proto.prototype.GetComponentInChildren<MeshFilter>()
                    : null;
                MeshRenderer renderer = meshFilter != null ? meshFilter.GetComponent<MeshRenderer>() : null;

                // Override не трогает DetailPrototype - можно подставить LOD-цепочку даже для
                // текстурных билбордов (usePrototypeMesh == false), у которых своего меша нет.
                PrototypeOverride over = GetOverride(layer);
                (Mesh protoMesh, Material material) = ResolveBaseMeshMaterial(over, meshFilter?.sharedMesh, renderer?.sharedMaterial);

                if (protoMesh == null)
                {
                    Debug.LogWarning($"TerrainGrassIndirectRenderer: у DetailPrototype[{layer}] нет меша и нет override - пропущен.", this);
                    continue;
                }
                if (material == null)
                {
                    Debug.LogWarning($"TerrainGrassIndirectRenderer: у DetailPrototype[{layer}] нет материала и нет override - пропущен.", this);
                    continue;
                }

                // Подмена шейдера на Indirect-аналог (см. _indirectShaderConversions/ResolveIndirectMaterial
                // в базовом классе) - сама, в памяти, при каждом входе в Play.
                material = ResolveIndirectMaterial(material);

                float scaleMultiplier = over.ScaleMultiplier > 0f ? over.ScaleMultiplier : 1f;
                int[,] density = terrainData.GetDetailLayer(0, 0, mapWidth, mapHeight, layer);
                var data = new List<InstanceData>();
                int layerCap = Mathf.Min(_maxInstancesPerLayer, _maxTotalInstances - totalInstances);

                long rawTotal = 0;
                for (int yy = 0; yy < mapHeight; yy++)
                for (int xx = 0; xx < mapWidth; xx++)
                    rawTotal += density[yy, xx];

                if (rawTotal == 0) continue;

// Density масштабирует долю от layerCap (бюджета), а НЕ долю от rawTotal (нарисованной
// плотности) - на реальных terrain'ах rawTotal может быть на два-три порядка больше cap'а
// (здесь ~145 млн против cap ~200 тыс), и старая формула keepProb = min(density, cap/rawTotal)
// упиралась в cap уже при density ~0.001 - весь слайдер выше этого визуально ничего не менял,
// то есть Density был сломан почти на всём диапазоне. Теперь density=1 = "залить весь layerCap",
// density=0 = 0 инстансов, промежуточные значения - линейная (точнее, квадратичная - см. ниже)
// доля БЮДЖЕТА, а не доля от того, что покрашено кистью.
// Квадрат, а не сам _densityMultiplier - линейная шкала визуально почти не отличима на
// середине слайдера (глаз воспринимает разреженное случайное поле травы как "всё ещё много"
// почти до самого низа), квадратичная даёт заметное на глаз изменение уже с середины.
                float effectiveDensity = _densityMultiplier * _densityMultiplier;
                long targetCount = (long)(effectiveDensity * layerCap);
                float keepProb = Mathf.Clamp01((float)targetCount / rawTotal);

                for (int y = 0; y < mapHeight; y++)
                {
                    for (int x = 0; x < mapWidth; x++)
                    {
                        int count = density[y, x];
                        for (int i = 0; i < count; i++)
                        {
                            if (rng.NextDouble() >= keepProb) continue;

                            float worldX = terrainOrigin.x + (x + (float)rng.NextDouble()) * cellSizeX;
                            float worldZ = terrainOrigin.z + (y + (float)rng.NextDouble()) * cellSizeZ;
                            float worldY = terrainOrigin.y + _terrain.SampleHeight(new Vector3(worldX, 0f, worldZ));

                            data.Add(new InstanceData
                            {
                                Position = new Vector3(worldX, worldY, worldZ),
                                Scale = Mathf.Lerp(proto.minWidth, proto.maxWidth, (float)rng.NextDouble()) * scaleMultiplier,
                                Rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f)
                            });
                        }
                    }
                }

                if (data.Count == 0) continue;
                totalInstances += data.Count;

                // minWidth/maxWidth - тот же множитель авторского размера меша, что уже применён
                // выше к Scale инстансов; LOD-дистанции должны знать про него так же, как трава/
                // деревья - см. комментарий у ResolveLevels.
                float averageInstanceScale = (proto.minWidth + proto.maxWidth) * 0.5f * scaleMultiplier;

                Bounds bounds = ComputeTerrainBounds(terrainOrigin, terrainSize, protoMesh.bounds.extents.magnitude);
                MeshLevel[] levels = ResolveLevels(proto.prototype, protoMesh, over, _cullRadius, averageInstanceScale);
                levels = ApplyIndirectMaterials(levels); // до импостора - у того уже свой Indirect-материал
                levels = AppendImpostorLevel(levels, over, _cullRadius);

                Debug.Log($"TerrainGrassIndirectRenderer[{layer}]: ScaleMultiplier={over.ScaleMultiplier}, " +
                          string.Join(" | ", System.Array.ConvertAll(levels, l => $"{l.Mesh?.name}[{l.MinDistance:0.#}-{l.MaxDistance:0.#}]")), this);

                _batches.Add(new IndirectRenderBatch(protoMesh, material, data.ToArray(), bounds,
                    CullingReady ? _cullingShader : null, levels));
            }
        }

        // BuildBatches (и с ним _densityMultiplier) вызывается только из OnEnable - без этого правка
        // слайдера в инспекторе во время Play визуально ничего не меняла, пока компонент не
        // выключишь/включишь заново.
        private void OnValidate()
        {
            if (!Application.isPlaying || !enabled || _terrain == null) return;
            RebuildBatches();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_disableTerrainDetails && _terrain != null)
                _terrain.detailObjectDistance = _originalDetailObjectDistance;
        }

        protected override int GetPrototypeCount() => _terrain.terrainData.detailPrototypes.Length;

        protected override GameObject GetPrototypePrefab(int index)
        {
            DetailPrototype proto = _terrain.terrainData.detailPrototypes[index];
            return proto.usePrototypeMesh ? proto.prototype : null; // текстурный билборд - своего префаба с LODGroup нет
        }
    }
}
