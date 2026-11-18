using UnityEngine;

// На префабе постройки: ставится только на указанные поверхности (снег, лёд). Без компонента - на любую
public class BuildSurfaceRequirement : MonoBehaviour
{
    [Tooltip("Слои Terrain, на которые можно ставить (снег). Слой должен преобладать в точке")]
    [SerializeField] private TerrainLayer[] _terrainLayers;
    [Tooltip("Physics Material коллайдеров, на которые можно ставить (лёд)")]
    [SerializeField] private PhysicsMaterial[] _materials;
    [Tooltip("Объекты со SnowSurface тоже считаются снегом")]
    [SerializeField] private bool _allowSnowSurface = true;

    public bool Allows(RaycastHit hit)
    {
        var collider = hit.collider;
        if (collider == null) return false;
        if (_allowSnowSurface && BuildBridge.IsExtraSurface(collider)) return true;
        if (_materials != null && collider.sharedMaterial != null && System.Array.IndexOf(_materials, collider.sharedMaterial) >= 0) return true;
        return collider.TryGetComponent(out Terrain terrain) && System.Array.IndexOf(_terrainLayers, DominantLayer(terrain, hit.point)) >= 0;
    }

    private static TerrainLayer DominantLayer(Terrain terrain, Vector3 point)
    {
        var data = terrain.terrainData;
        var local = point - terrain.transform.position;
        int x = Mathf.FloorToInt(local.x / data.size.x * data.alphamapWidth);
        int z = Mathf.FloorToInt(local.z / data.size.z * data.alphamapHeight);
        if (x < 0 || z < 0 || x >= data.alphamapWidth || z >= data.alphamapHeight) return null;

        var weights = data.GetAlphamaps(x, z, 1, 1);
        int best = -1;
        float bestWeight = 0f;
        for (int i = 0; i < weights.GetLength(2); i++)
        {
            if (weights[0, 0, i] > bestWeight) { bestWeight = weights[0, 0, i]; best = i; }
        }
        return best >= 0 && best < data.terrainLayers.Length ? data.terrainLayers[best] : null;
    }
}
