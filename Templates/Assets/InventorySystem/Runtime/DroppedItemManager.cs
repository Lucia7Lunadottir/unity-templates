using PG.InteractSystem;
using PG.InventoryManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

/// <summary>
/// Unified tracker for both dropped and placed world items. Static — no instance, no scene
/// placement needed. Self-attaches to every loaded scene (main scene and every additive
/// SubScene chunk) via SceneManager events, so a new chunk just works with zero setup.
/// Which chunk a drop belongs to is resolved by raycasting down from the drop position onto
/// whatever ground/geometry is under it — that geometry already lives in the right chunk scene.
/// </summary>
public static partial class DroppedItemManager
{
    private const float ThrowForce = 3f;

    [AutoStaticsCleanup] private static Dictionary<Scene, List<DroppedItemTag>> _bySceneTags = new();
    [AutoStaticsCleanup] private static Dictionary<Scene, Transform> _containers = new();
    // Сцены, изменённые без записи (save: false) - пишутся SaveDirty() вместе с игрой
    [AutoStaticsCleanup] private static HashSet<Scene> _dirty = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Bootstrap()
    {
        // RuntimeInitializeOnLoadMethod re-fires every Play session in this project (Domain
        // Reload is disabled) — unsubscribe first so repeated sessions don't stack handlers.
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SceneManager.sceneUnloaded -= HandleSceneUnloaded;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;

        _bySceneTags.Clear();
        _containers.Clear();
        _dirty.Clear();
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => LoadScene(scene);

    private static void HandleSceneUnloaded(Scene scene)
    {
        // Несохранённое в выгружаемом чанке иначе пропало бы
        if (_dirty.Remove(scene)) SaveScene(scene);
        // Data is already on disk (every drop/place/pickup saves immediately) — this just
        // stops the dictionaries from growing forever across many chunk load/unload cycles.
        _bySceneTags.Remove(scene);
        _containers.Remove(scene);
    }

    // ── Dropped item (from inventory "Drop" button) ──────────────────────────

    public static void SpawnDrop(Item item, Vector3 position)
    {
        ItemContainer itemContainer = Inventory.instance?.itemContainer;
        if (itemContainer == null) return;
        if (item.dropPrefabs == null || item.dropPrefabs.Length == 0) return;

        int prefabIdx = Random.Range(0, item.dropPrefabs.Length);
        Item.DropEntry entry = item.dropPrefabs[prefabIdx];
        if (entry.prefab == null) return;

        int itemId = itemContainer.GetItemID(item);
        if (itemId < 0) return;

        Vector3 spawnPos = position + Vector3.up * entry.halfHeight;
        Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject go = UnityEngine.Object.Instantiate(entry.prefab, spawnPos, spawnRot);
        go.transform.localScale = Vector3.one * (entry.isRandomScale ? Random.Range(entry.scale, entry.maxScale) : entry.scale);

        if (go.TryGetComponent(out Rigidbody rb))
        {
            Vector3 throwDir = (Camera.main.transform.forward + Vector3.up * 0.3f).normalized;
            rb.AddForce(throwDir * ThrowForce, ForceMode.Impulse);
        }

        Scene scene = ResolveSceneForPosition(spawnPos);
        PlaceInScene(go, scene, itemId, prefabIdx, isPlaced: false);
        SaveScene(scene);
    }

    // ── Предмет, положенный в Build Mode: ровно в точку и с поворотом, без броска ──

    public static GameObject PlaceDrop(Item item, Vector3 position, Quaternion rotation, bool save = true)
    {
        ItemContainer itemContainer = Inventory.instance?.itemContainer;
        if (itemContainer == null || item == null || item.dropPrefabs == null || item.dropPrefabs.Length == 0) return null;
        Item.DropEntry entry = item.dropPrefabs[0];
        if (entry.prefab == null) return null;
        int itemId = itemContainer.GetItemID(item);
        if (itemId < 0) return null;

        GameObject go = UnityEngine.Object.Instantiate(entry.prefab, position, rotation);
        go.transform.localScale = Vector3.one * entry.scale;
        Scene scene = ResolveSceneForPosition(position);
        PlaceInScene(go, scene, itemId, prefabIndex: 0, isPlaced: false);
        SaveOrMark(scene, save);
        return go;
    }

    // ── Placed item (from inventory "Place" button via InventoryInteraction) ─

    public static void RegisterPlaced(GameObject go, PlaceableItem item, bool save = true)
    {
        ItemContainer itemContainer = Inventory.instance?.itemContainer;
        if (itemContainer == null) return;

        int itemId = itemContainer.GetItemID(item);
        if (itemId < 0) return;

        Scene scene = ResolveSceneForPosition(go.transform.position);
        PlaceInScene(go, scene, itemId, prefabIndex: 0, isPlaced: true);
        SaveOrMark(scene, save);
    }

    // ── Called by PlaceableWorldObject.Pickup() and LootItem.interacted ──────

    public static void OnItemPickedUp(GameObject go) => OnItemPickedUp(go, true);

    public static void OnItemPickedUp(GameObject go, bool save)
    {
        if (!go.TryGetComponent(out DroppedItemTag tag)) return;

        Scene scene = go.scene;
        Unregister(scene, tag);
        SaveOrMark(scene, save);
    }

    // Записать всё, что менялось с save: false (сохранение игры)
    public static void SaveDirty()
    {
        foreach (Scene scene in _dirty)
            if (scene.isLoaded) SaveScene(scene);
        _dirty.Clear();
    }

    // ── Called by TelekinesisSystem after Drop()/OnPlace() ───────────────────
    // Телекинез двигает только Transform/Rigidbody, не GameObject.scene — если игрок
    // унёс предмет в другой чанк и там его оставил, нужно перепривязать guid-запись.

    public static void RehomeIfNeeded(GameObject go)
    {
        if (!go.TryGetComponent(out DroppedItemTag tag)) return;

        Scene oldScene = go.scene;
        Scene newScene = ResolveSceneForPosition(go.transform.position);
        if (newScene == oldScene) return;

        Unregister(oldScene, tag);
        SceneManager.MoveGameObjectToScene(go, newScene);
        go.transform.SetParent(GetOrCreateContainer(newScene), worldPositionStays: true);
        Register(newScene, tag);

        SaveScene(oldScene);
        SaveScene(newScene);
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    public static Scene ResolveSceneForPosition(Vector3 position)
    {
        if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f))
        {
            return hit.collider.gameObject.scene;
        }
        return SceneManager.GetActiveScene();
    }

    private static Transform GetOrCreateContainer(Scene scene)
    {
        if (_containers.TryGetValue(scene, out Transform existing) && existing != null)
        {
            return existing;
        }

        var containerGo = new GameObject("~DroppedItems");
        SceneManager.MoveGameObjectToScene(containerGo, scene);
        _containers[scene] = containerGo.transform;
        return containerGo.transform;
    }

    private static void PlaceInScene(GameObject go, Scene scene, int itemId, int prefabIndex, bool isPlaced)
    {
        if (go.scene != scene)
        {
            SceneManager.MoveGameObjectToScene(go, scene);
        }
        go.transform.SetParent(GetOrCreateContainer(scene), worldPositionStays: true);

        var tag = go.AddComponent<DroppedItemTag>();
        tag.itemId = itemId;
        tag.prefabIndex = prefabIndex;
        tag.isPlaced = isPlaced;
        tag.placedDay = CurrentDay();
        Register(scene, tag);

        // Dropped LootItems notify us automatically via event.
        // Placed objects call OnItemPickedUp() manually from PlaceableWorldObject.Pickup().
        if (!isPlaced && go.TryGetComponent(out LootItem loot))
        {
            loot.interacted += () => OnItemPickedUp(go);
        }
    }

    // Игровой день для записи (0 - смены дня в сцене нет)
    public static int CurrentDay() => InventoryBridge.CurrentDay();

    private static void Register(Scene scene, DroppedItemTag tag)
    {
        if (!_bySceneTags.TryGetValue(scene, out List<DroppedItemTag> list))
        {
            list = new List<DroppedItemTag>();
            _bySceneTags[scene] = list;
        }
        list.Add(tag);
    }

    internal static void Unregister(Scene scene, DroppedItemTag tag)
    {
        if (_bySceneTags.TryGetValue(scene, out List<DroppedItemTag> list))
        {
            list.Remove(tag);
        }
    }

    private static string GetFilePath(Scene scene) => System.IO.Path.Combine(SaveManager.GetCurrentSaveFolderPath(), $"{scene.name}_DroppedItems.json");

    private static void SaveOrMark(Scene scene, bool save)
    {
        if (save) { SaveScene(scene); _dirty.Remove(scene); }
        else _dirty.Add(scene);
    }

    private static void SaveScene(Scene scene)
    {
        if (!_bySceneTags.TryGetValue(scene, out List<DroppedItemTag> tags)) return;

        var data = new SaveData
        {
            drops = tags
                .Where(t => t != null)
                .Select(t => new DropRecord
                {
                    itemId = t.itemId,
                    prefabIndex = t.prefabIndex,
                    isPlaced = t.isPlaced,
                    placedDay = t.placedDay,
                    position = t.transform.position,
                    rotation = t.transform.rotation,
                    scale = t.transform.localScale
                })
                .ToList()
        };

        AtomicFileIO.WriteAllTextAtomic(GetFilePath(scene), JsonUtility.ToJson(data));
    }

    private static void LoadScene(Scene scene)
    {
        ItemContainer itemContainer = Inventory.instance?.itemContainer;
        if (itemContainer == null) return;

        string path = GetFilePath(scene);
        if (!SaveManager.Storage.FileExists(path)) return;

        SaveData data = JsonUtility.FromJson<SaveData>(SaveManager.Storage.ReadAllText(path));
        int count = data?.drops?.Count ?? 0;
        if (count == 0) return;

        var handles = new NativeArray<TransformHandle>(count, Allocator.TempJob);
        var positions = new NativeArray<float3>(count, Allocator.TempJob);
        var rotations = new NativeArray<quaternion>(count, Allocator.TempJob);
        var scales = new NativeArray<float3>(count, Allocator.TempJob);

        var pending = new List<(GameObject go, int itemId, int prefabIndex, bool isPlaced, int placedDay)>(count);
        int validCount = 0;

        foreach (DropRecord record in data.drops)
        {
            if (record.itemId < 0 || record.itemId >= itemContainer.items.Length) continue;

            Item item = itemContainer.items[record.itemId];
            GameObject go;

            if (record.isPlaced)
            {
                if (item is not PlaceableItem placed || placed.worldPrefab == null) continue;
                go = UnityEngine.Object.Instantiate(placed.worldPrefab);
                if (go.TryGetComponent(out PlaceableWorldObject pwo)) pwo.sourceItem = placed;
            }
            else
            {
                if (item.dropPrefabs == null || record.prefabIndex >= item.dropPrefabs.Length) continue;
                Item.DropEntry entry = item.dropPrefabs[record.prefabIndex];
                if (entry.prefab == null) continue;
                go = UnityEngine.Object.Instantiate(entry.prefab);
            }

            if (go.TryGetComponent(out Rigidbody rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetParent(GetOrCreateContainer(scene), worldPositionStays: true);

            handles[validCount] = go.transform.transformHandle;
            positions[validCount] = record.position;
            rotations[validCount] = record.rotation;
            scales[validCount] = record.scale;
            // Записи до появления дня установки: отсчёт с текущего дня
            pending.Add((go, record.itemId, record.prefabIndex, record.isPlaced, record.placedDay > 0 ? record.placedDay : CurrentDay()));
            validCount++;
        }

        if (validCount > 0)
        {
            ApplyTransformsBurst.Apply(ref handles, ref positions, ref rotations, ref scales, validCount);
        }

        handles.Dispose();
        positions.Dispose();
        rotations.Dispose();
        scales.Dispose();

        foreach (var entry in pending)
        {
            var tag = entry.go.AddComponent<DroppedItemTag>();
            tag.itemId = entry.itemId;
            tag.prefabIndex = entry.prefabIndex;
            tag.isPlaced = entry.isPlaced;
            tag.placedDay = entry.placedDay;
            Register(scene, tag);

            if (!entry.isPlaced && entry.go.TryGetComponent(out LootItem loot))
            {
                GameObject capturedGo = entry.go;
                loot.interacted += () => OnItemPickedUp(capturedGo);
            }
        }
    }

    [Serializable]
    private struct DropRecord
    {
        public int itemId;
        public int prefabIndex;
        public bool isPlaced;
        public int placedDay;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    [Serializable]
    private class SaveData
    {
        public List<DropRecord> drops = new();
    }

    [BurstCompile]
    private static class ApplyTransformsBurst
    {
        [BurstCompile]
        public static void Apply(
            ref NativeArray<TransformHandle> handles,
            ref NativeArray<float3> positions,
            ref NativeArray<quaternion> rotations,
            ref NativeArray<float3> scales,
            int count)
        {
            for (int i = 0; i < count; i++)
            {
                TransformHandle handle = handles[i];
                handle.position = positions[i];
                handle.rotation = rotations[i];
                handle.localScale = scales[i];
            }
        }
    }
}

// Added via AddComponent at spawn/load time — never placed manually in the inspector.
public class DroppedItemTag : MonoBehaviour
{
    public int itemId;
    public int prefabIndex;
    public bool isPlaced;
    public int placedDay;

    private void OnDestroy() => DroppedItemManager.Unregister(gameObject.scene, this);
}
