using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PG.SaveSystem
{
    // Saves a transform per scene into the current slot
    public class PositionSaver : MonoBehaviour, ISaveable
    {
        [System.Serializable]
        public class ScenePositionData
        {
            public string sceneName;
            public Vector3 position;
            public Quaternion rotation;
        }

        [System.Serializable]
        public class SaveData
        {
            public List<ScenePositionData> scenes = new List<ScenePositionData>();
        }

        [Tooltip("Player/character transform")]
        public Transform targetCharacter;

        [SerializeField] private string _saveFileName = "player_positions.json";
        private string _saveFilePath;

        private void Awake()
        {
            _saveFilePath = Path.Combine(SaveManager.GetCurrentSaveFolderPath(), _saveFileName);
            Load();
        }

        public void Load()
        {
            SaveData saveData = LoadFromFile();
            if (saveData == null || saveData.scenes == null || saveData.scenes.Count == 0)
                return;

            string currentSceneName = SceneManager.GetActiveScene().name;
            ScenePositionData currentSceneData = saveData.scenes.Find(x => x.sceneName == currentSceneName);

            if (currentSceneData != null)
            {
                targetCharacter.position = currentSceneData.position;
                targetCharacter.rotation = currentSceneData.rotation;
                Physics.SyncTransforms();
            }
        }

        public void Save()
        {
            if (targetCharacter == null) return;
            WritePosition(targetCharacter.position, targetCharacter.rotation);
        }

        public void SavePosition(Transform point)
        {
            if (point == null) return;
            WritePosition(point.position, point.rotation);
        }

        private void WritePosition(Vector3 position, Quaternion rotation)
        {
            SaveData data = LoadFromFile();
            string currentScene = SceneManager.GetActiveScene().name;

            ScenePositionData entry = data.scenes.Find(x => x.sceneName == currentScene);
            if (entry == null)
            {
                entry = new ScenePositionData { sceneName = currentScene };
                data.scenes.Add(entry);
            }
            entry.position = position;
            entry.rotation = rotation;

            AtomicFileIO.WriteAllTextAtomic(_saveFilePath, JsonUtility.ToJson(data, true));
        }

        private SaveData LoadFromFile()
        {
            if (File.Exists(_saveFilePath))
            {
                SaveData loadedData = JsonUtility.FromJson<SaveData>(File.ReadAllText(_saveFilePath));
                return loadedData ?? new SaveData();
            }
            return new SaveData();
        }
    }
}
