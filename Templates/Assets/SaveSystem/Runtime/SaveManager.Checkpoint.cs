using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PG.SaveSystem
{
    // Checkpoint is a mirror of the slot folder that only moves on an explicit SetCheckpoint(), never on autosave
    public partial class SaveManager
    {
        private const string CheckpointSuffix = "-Checkpoint";
        private const string CheckpointMetaFileName = "checkpoint_meta.json";

        [Serializable]
        private class CheckpointMeta
        {
            public string mainSceneName;
            public string timestampUtc;
        }

        public event Action checkpointSet;

        // SaveManager doesn't load scenes itself; subscribe here with your scene loader
        public static event Action<string> restartSceneRequested;

        public static string GetCheckpointFolderPath(int saveIndexOverride = -1)
        {
            int index = saveIndexOverride >= 0 ? saveIndexOverride : saveIndex;
            string path = Path.Combine(GetSaveFolderPath(), $"Save-{index}{CheckpointSuffix}");
            Storage.RecoverIfNeeded(path);
            return path;
        }

        public bool HasCheckpoint() => Storage.DirectoryExists(GetCheckpointFolderPath());

        public void SetCheckpoint()
        {
            SaveAllSaveablesImmediate();

            string checkpointPath = GetCheckpointFolderPath();
            string tmp = checkpointPath + ".tmp";
            Storage.CopyDirectory(GetCurrentSaveFolderPath(), tmp);
            WriteCheckpointMeta(tmp, SceneManager.GetActiveScene().name);
            Storage.SwapDirectory(tmp, checkpointPath);

            checkpointSet?.Invoke();
        }

        // Replaces the whole slot with the checkpoint and reloads its scene
        public void RestartToCheckpoint()
        {
            if (!HasCheckpoint())
            {
                Debug.LogWarning("[SaveManager] No checkpoint to restart from.");
                return;
            }

            string checkpointPath = GetCheckpointFolderPath();
            CheckpointMeta meta = ReadCheckpointMeta(checkpointPath);

            string liveDir = GetCurrentSaveFolderPath();
            string tmp = liveDir + ".tmp";
            Storage.CopyDirectory(checkpointPath, tmp);
            Storage.SwapDirectory(tmp, liveDir);
            GuidListSaveUtility.ClearCache();

            string sceneName = !string.IsNullOrEmpty(meta?.mainSceneName) ? meta.mainSceneName : SceneManager.GetActiveScene().name;
            RequestSceneReload(sceneName);
        }

        public void ClearCheckpoint()
        {
            AtomicFileIO.DeleteDirectory(GetCheckpointFolderPath());
        }

        // One "Restart" button: checkpoint if present, otherwise reload the current scene
        public void RestartOrReload()
        {
            if (HasCheckpoint())
            {
                RestartToCheckpoint();
            }
            else
            {
                RequestSceneReload(SceneManager.GetActiveScene().name);
            }
        }

        private static void RequestSceneReload(string sceneName)
        {
            if (restartSceneRequested != null)
            {
                restartSceneRequested.Invoke(sceneName);
            }
            else
            {
                SceneManager.LoadScene(sceneName);
            }
        }

        private static void WriteCheckpointMeta(string folderPath, string mainSceneName)
        {
            var meta = new CheckpointMeta { mainSceneName = mainSceneName, timestampUtc = DateTime.UtcNow.ToString("O") };
            string path = Path.Combine(folderPath, CheckpointMetaFileName);
            Storage.WriteAllTextAtomic(path, JsonUtility.ToJson(meta, true));
        }

        private static CheckpointMeta ReadCheckpointMeta(string folderPath)
        {
            string path = Path.Combine(folderPath, CheckpointMetaFileName);
            if (!Storage.FileExists(path)) return null;
            return JsonUtility.FromJson<CheckpointMeta>(Storage.ReadAllText(path));
        }
    }
}
