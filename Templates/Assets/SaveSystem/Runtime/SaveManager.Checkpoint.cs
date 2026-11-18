using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

// Двухуровневый сейв: обычный автосейв продолжает писать только в live-папку слота
// (Save-{n}/, без изменений). SetCheckpoint()/RestartToCheckpoint() — явный, авторский
// откат "как в Kingdom Hearts": чекпоинт продвигается только по прямому вызову, никогда
// по таймеру, поэтому 5 минут боя с автосейвами внутри не трогают последний чекпоинт.
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

    // PG.SaveSystem — отдельная asmdef-сборка и не может ссылаться на Michsky.LSS
    // (у Loading Screen Studio своего asmdef нет, он в Assembly-CSharp). Поэтому SaveManager
    // сам не грузит сцену, а просит об этом через событие — подписчик живёт в
    // Assets/Scripts/Menu/CheckpointSceneBridge.cs.
    public static event Action<string> restartSceneRequested;

    public static string GetCheckpointFolderPath(int saveIndexOverride = -1)
    {
        int index = saveIndexOverride >= 0 ? saveIndexOverride : saveIndex;
        string path = Path.Combine(GetSaveFolderPath(), $"Save-{index}{CheckpointSuffix}");
        Storage.RecoverIfNeeded(path);
        return path;
    }

    public bool HasCheckpoint() => Storage.DirectoryExists(GetCheckpointFolderPath());

    // Снимает зеркало текущей live-папки слота в Save-{n}-Checkpoint/. Синхронно догоняет
    // Save() перед копированием, чтобы чекпоинт точно отражал текущих живых ISaveable.
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

    // Откатывает весь live-слот к последнему чекпоинту целиком (файлы, которых не было
    // на момент чекпоинта, уходят вместе со старой папкой) и перезагружает главную сцену —
    // LoadingScreen.LoadScene грузит в Single-режиме, поэтому все чанки выгружаются и
    // пересоздаются сами, живые объекты боя не нужно откатывать руками.
    public void RestartToCheckpoint()
    {
        if (!HasCheckpoint())
        {
            Debug.LogWarning("[SaveManager] Нет чекпоинта для рестарта.");
            return;
        }

        string checkpointPath = GetCheckpointFolderPath();
        CheckpointMeta meta = ReadCheckpointMeta(checkpointPath);

        string liveDir = GetCurrentSaveFolderPath();
        string tmp = liveDir + ".tmp";
        Storage.CopyDirectory(checkpointPath, tmp);
        Storage.SwapDirectory(tmp, liveDir);
        GuidListSaveUtility.ClearCache(); // файлы под ногами подменились — кэш иначе отдал бы старое

        string sceneName = !string.IsNullOrEmpty(meta?.mainSceneName) ? meta.mainSceneName : SceneManager.GetActiveScene().name;
        RequestSceneReload(sceneName);
    }

    // Убирает чекпоинт — CheckpointZone зовёт это на выходе из зоны (опасность миновала,
    // откатываться уже незачем), можно звать и вручную (например, из события "босс побеждён").
    public void ClearCheckpoint()
    {
        AtomicFileIO.DeleteDirectory(GetCheckpointFolderPath());
    }

    // Единая точка для одной кнопки "Restart": если есть чекпоинт — откатываемся к нему,
    // если нет — обычный Restart Scene (перезагрузка текущей сцены с уже сохранённым
    // обычным сейвом, без затрагивания чекпоинта).
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
            Debug.LogWarning($"[SaveManager] Нет подписчика на restartSceneRequested — сцена '{sceneName}' не перезагружена.");
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
