using System;
using System.IO;
using UnityEngine;

// Раньше в сейве не было записи "в какой сцене был игрок" — Continue из главного меню
// мог только грузить сцену, зашитую в саму UI-кнопку, а не ту, где реально остановились.
// Пишется из SaveManager.Awake() (не из глобального события SceneManager.sceneLoaded!) —
// SaveManager живёт только в реальных игровых сценах, а не в Menu, так что вернуться
// в главное меню не портит запись "последней сцены" активного слота.
public partial class SaveManager
{
    private const string SessionMetaFileName = "session_meta.json";

    [Serializable]
    private class SessionMeta
    {
        public string lastSceneName;
    }

    public static string GetLastSceneName(int saveIndexOverride = -1)
    {
        string folder = saveIndexOverride >= 0 ? GetTargetSaveFolderPath(saveIndexOverride, false) : GetCurrentSaveFolderPath();
        string path = Path.Combine(folder, SessionMetaFileName);
        if (!Storage.FileExists(path)) return null;

        SessionMeta meta = JsonUtility.FromJson<SessionMeta>(Storage.ReadAllText(path));
        return meta?.lastSceneName;
    }

    public static void WriteLastSceneName(string sceneName)
    {
        var meta = new SessionMeta { lastSceneName = sceneName };
        string path = Path.Combine(GetCurrentSaveFolderPath(), SessionMetaFileName);
        Storage.WriteAllTextAtomic(path, JsonUtility.ToJson(meta));
    }
}
