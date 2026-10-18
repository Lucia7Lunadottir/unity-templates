using System;
using System.IO;
using UnityEngine;

namespace PG.SaveSystem
{
    // Remembers the last gameplay scene of a slot so Continue can load it
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

        private static void WriteLastSceneName(string sceneName)
        {
            var meta = new SessionMeta { lastSceneName = sceneName };
            string path = Path.Combine(GetCurrentSaveFolderPath(), SessionMetaFileName);
            Storage.WriteAllTextAtomic(path, JsonUtility.ToJson(meta));
        }
    }
}
