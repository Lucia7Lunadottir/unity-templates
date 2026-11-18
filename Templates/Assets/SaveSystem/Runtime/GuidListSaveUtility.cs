using System;
using System.Collections.Generic;
using System.IO;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

/// Shared read-modify-write helper for guid-keyed world objects (levers, ice blocks, ...).
/// Every instance of a given type shares one file per scene; each call updates only the
/// entry matching its own guid. Same pattern already used by PositionSaver/DroppedItemManager
/// (read whole file -> find-or-create own entry -> write back), generalized so a new object
/// type doesn't need its own hand-rolled file format.
///
/// File name is derived from the state type automatically (no per-component suffix field to
/// set up) — pass a private nested "State" struct/class from the owning component (as Lever
/// and IceObject do) and the file is named after the OWNER, not the generic "State" name, so
/// dozens of object types can each get their own file with zero manual configuration.
///
/// Reads/writes are cached in memory per file path and only actually hit disk once, on
/// FlushPending() — with hundreds of instances sharing one file (e.g. 593 PhysicsPropSaveable
/// in one scene), doing a full read+reserialize+write PER OBJECT made a single save pass
/// O(n^2) and took several seconds. SaveManager.SaveAllSaveablesImmediate() calls
/// FlushPending() once after every ISaveable.Save() has run.
[AutoStaticsCleanup]
public static partial class GuidListSaveUtility
{
    [AutoStaticsCleanup] private static Dictionary<string, object> _cache = new();
    [AutoStaticsCleanup] private static HashSet<string> _dirtyPaths = new();

    public static void SaveEntry<T>(string sceneName, string guid, T state)
    {
        if (string.IsNullOrEmpty(guid))
        {
            Debug.LogWarning($"[GuidListSaveUtility] Empty guid for {typeof(T).Name} in scene '{sceneName}' — skipped.");
            return;
        }

        try
        {
            string path = GetPath(sceneName, typeof(T));
            SaveList<T> list = GetCached<T>(path);

            Entry<T> entry = list.entries.Find(e => e.guid == guid);
            if (entry == null)
            {
                entry = new Entry<T> { guid = guid };
                list.entries.Add(entry);
            }
            entry.state = state;

            _dirtyPaths.Add(path);
        }
        catch (Exception e)
        {
            Debug.LogError($"[GuidListSaveUtility] Save failed ({typeof(T).Name}, guid={guid}): {e}");
        }
    }

    // Убирает запись целиком — нужно, когда объект навсегда покидает сцену (например,
    // унесён телекинезом в другой чанк): без этого в старом файле остался бы мёртвый guid.
    public static void RemoveEntry<T>(string sceneName, string guid)
    {
        if (string.IsNullOrEmpty(guid)) return;

        try
        {
            string path = GetPath(sceneName, typeof(T));
            SaveList<T> list = GetCached<T>(path);
            if (list.entries.RemoveAll(e => e.guid == guid) > 0)
            {
                _dirtyPaths.Add(path);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GuidListSaveUtility] Remove failed ({typeof(T).Name}, guid={guid}): {e}");
        }
    }

    public static bool TryLoadEntry<T>(string sceneName, string guid, out T state)
    {
        state = default;
        if (string.IsNullOrEmpty(guid)) return false;

        try
        {
            string path = GetPath(sceneName, typeof(T));
            SaveList<T> list = GetCached<T>(path);
            Entry<T> entry = list.entries.Find(e => e.guid == guid);
            if (entry == null) return false;

            state = entry.state;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[GuidListSaveUtility] Load failed ({typeof(T).Name}, guid={guid}): {e}");
            return false;
        }
    }

    // Зовётся SaveManager.SaveAllSaveablesImmediate() один раз в конце прохода — пишет на диск
    // ровно по одному разу каждый файл, который реально поменялся с последнего Flush/Clear.
    public static void FlushPending()
    {
        foreach (string path in _dirtyPaths)
        {
            if (_cache.TryGetValue(path, out object list))
            {
                AtomicFileIO.WriteAllTextAtomic(path, JsonUtility.ToJson(list, true));
            }
        }
        _dirtyPaths.Clear();
    }

    // Зовётся, когда файлы на диске могли поменяться в обход этого кэша — например,
    // SaveManager.RestartToCheckpoint() подменяет всю папку слота файлами чекпоинта, или
    // при старте новой сцены/смене слота. Без этого кэш продолжил бы отдавать старые данные.
    public static void ClearCache()
    {
        _cache.Clear();
        _dirtyPaths.Clear();
    }

    private static SaveList<T> GetCached<T>(string path)
    {
        if (_cache.TryGetValue(path, out object cached))
        {
            return (SaveList<T>)cached;
        }

        SaveList<T> list = ReadList<T>(path);
        _cache[path] = list;
        return list;
    }

    private static string GetPath(string sceneName, Type stateType)
    {
        // Nested "State" types (Lever.State, IceObject.State, ...) all share the same bare
        // name ("State") — key off the declaring type instead so each owner gets its own file.
        string typeName = stateType.DeclaringType != null ? stateType.DeclaringType.Name : stateType.Name;
        return Path.Combine(SaveManager.GetCurrentSaveFolderPath(), $"{sceneName}_{typeName}.json");
    }

    private static SaveList<T> ReadList<T>(string path)
    {
        if (!File.Exists(path)) return new SaveList<T>();

        string json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json)) return new SaveList<T>();

        SaveList<T> list = JsonUtility.FromJson<SaveList<T>>(json);
        return list ?? new SaveList<T>();
    }

    [Serializable]
    private class Entry<T>
    {
        public string guid;
        public T state;
    }

    [Serializable]
    private class SaveList<T>
    {
        public List<Entry<T>> entries = new List<Entry<T>>();
    }
}
