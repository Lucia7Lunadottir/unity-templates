using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

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

public class PositionSaver : MonoBehaviour, ISaveable
{
    [Tooltip("Player/character transform")]
    public Transform targetCharacter;

    [SerializeField] private string _saveFileName = "player_positions.json";
    private string _saveFilePath;

    private void Awake()
    {
        _saveFilePath = Path.Combine(SaveManager.GetCurrentSaveFolderPath(), _saveFileName);
        Load();
    }

    // private void OnEnable()
    // {
    //     // Subscribe to the event to update the position on every scene transition
    //     SceneManager.sceneLoaded += OnSceneLoaded;
    // }

    // private void OnDisable()
    // {
    //     SceneManager.sceneLoaded -= OnSceneLoaded;
    // }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Load();
    }

    public void Load()
    {
        SaveData saveData = LoadFromFile();
        
        // Protection against an empty file or missing data
        if (saveData == null || saveData.scenes == null || saveData.scenes.Count == 0) 
            return;

        // Look for data SPECIFICALLY for the scene that has just opened
        string currentSceneName = SceneManager.GetActiveScene().name;
        ScenePositionData currentSceneData = saveData.scenes.Find(x => x.sceneName == currentSceneName);

        if (currentSceneData != null)
        {
            // Update the transform position and rotation
            targetCharacter.position = currentSceneData.position;
            targetCharacter.rotation = currentSceneData.rotation;

            // Force physics engine synchronization
            Physics.SyncTransforms();
            
            Debug.Log($"[PositionSaver] Position loaded for scene: {currentSceneName}");
        }
    }

    public void Save()
    {
        if (targetCharacter == null) return;

        SaveData data = LoadFromFile();
        string currentScene = SceneManager.GetActiveScene().name;

        // Check if we have already visited and saved this scene before
        ScenePositionData existingEntry = data.scenes.Find(x => x.sceneName == currentScene);

        if (existingEntry != null)
        {
            existingEntry.position = targetCharacter.position;
            existingEntry.rotation = targetCharacter.rotation;
        }
        else
        {
            ScenePositionData newEntry = new ScenePositionData
            {
                sceneName = currentScene,
                position = targetCharacter.position,
                rotation = targetCharacter.rotation
            };
            data.scenes.Add(newEntry);
        }

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(_saveFilePath, json);
    }

    private SaveData LoadFromFile()
    {
        if (File.Exists(_saveFilePath))
        {
            string json = File.ReadAllText(_saveFilePath);
            SaveData loadedData = JsonUtility.FromJson<SaveData>(json);
            return loadedData ?? new SaveData();
        }
        return new SaveData();
    }
    
    
    public void SavePosition(Transform point)
    {

        SaveData data = LoadFromFile();
        string currentScene = SceneManager.GetActiveScene().name;

        // Check if we have already visited and saved this scene before
        ScenePositionData existingEntry = data.scenes.Find(x => x.sceneName == currentScene);

        if (existingEntry != null)
        {
            existingEntry.position = point.position;
            existingEntry.rotation = point.rotation;
        }
        else
        {
            ScenePositionData newEntry = new ScenePositionData
            {
                sceneName = currentScene,
                position = point.position,
                rotation = point.rotation
            };
            data.scenes.Add(newEntry);
        }

        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(_saveFilePath, json);
    }
}