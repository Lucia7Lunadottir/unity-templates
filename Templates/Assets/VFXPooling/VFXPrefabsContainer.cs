using System.Collections.Generic;
using UnityEngine;

namespace PG.VFXPool
{
    [CreateAssetMenu(fileName = "VFXPrefabsContainer", menuName = "VFX/VFX Prefabs Container")]
    public class VFXPrefabsContainer : ScriptableObject
    {
        [field: SerializeField]
        public VFXData[] vfxPrefabs { get; private set; }

        // Cache dictionary to avoid O(N) allocations and traversals during gameplay
        private Dictionary<string, VFXData> vfxDictionary;

        private void InitializeDictionary()
        {
            if (vfxDictionary != null) return;

            vfxDictionary = new Dictionary<string, VFXData>();
            foreach (var vfx in vfxPrefabs)
            {
                if (!vfxDictionary.ContainsKey(vfx.id))
                {
                    vfxDictionary.Add(vfx.id, vfx);
                }
                else
                {
                    Debug.LogWarning($"Duplicate VFX ID found in container: {vfx.id}");
                }
            }
        }

        public ParticleSystem GetVFXPrefab(string vfxID)
        {
            InitializeDictionary();

            if (vfxDictionary.TryGetValue(vfxID, out VFXData data))
            {
                return data.prefab;
            }
        
            Debug.LogWarning($"VFX with ID '{vfxID}' not found in container!");
            return null;
        }

        public VFXData GetVFXData(string vfxID)
        {
            InitializeDictionary();

            if (vfxDictionary.TryGetValue(vfxID, out VFXData data))
            {
                return data;
            }
        
            Debug.LogWarning($"VFX with ID '{vfxID}' not found in container!");
            return default;
        }

        public float GetVFXDuration(string vfxID)
        {
            InitializeDictionary();

            if (vfxDictionary.TryGetValue(vfxID, out VFXData data))
            {
                return data.duration;
            }
        
            Debug.LogWarning($"VFX with ID '{vfxID}' not found in container!");
            return 3.0f; // Default fallback
        }

        public bool IsVFXLooping(string vfxID)
        {
            InitializeDictionary();

            if (vfxDictionary.TryGetValue(vfxID, out VFXData data))
            {
                return data.isLooping;
            }

            Debug.LogWarning($"VFX with ID '{vfxID}' not found in container!");
            return false;
        }
    }

    [System.Serializable]
    public struct VFXData
    {
        public string id;
        public ParticleSystem prefab;
        public float duration;
        public bool isLooping; // Defines whether this VFX should play continuously
        [Tooltip("Сколько экземпляров создать заранее при старте пула.")]
        public int prewarm;
    }
}