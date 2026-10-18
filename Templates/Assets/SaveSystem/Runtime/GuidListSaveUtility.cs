using System;
using System.Collections.Generic;
using System.IO;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace PG.SaveSystem
{
    // One file per scene and owner type for guid-keyed objects; cached in memory and written once on FlushPending()
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

        // Removes the entry when the object leaves the scene for good
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

        // Writes every changed file exactly once
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

        // Call when files changed behind the cache (slot switch, checkpoint restore)
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
            // Nested "State" types are keyed by their declaring type so each owner gets its own file
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
}
