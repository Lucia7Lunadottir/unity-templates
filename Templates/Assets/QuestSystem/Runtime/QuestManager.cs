using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PG.QuestSystem
{
    public class QuestManager : MonoBehaviour, ISaveable
    {
        public static QuestManager instance { get; private set; }

        [Header("Data")]
        [SerializeField] private QuestContainer _questContainer;

        [Header("Save")]
        [SerializeField] private string _fileName = "Quests.json";
        private string _filePath;

        private readonly List<RuntimeQuest> _activeQuests    = new List<RuntimeQuest>();
        private readonly List<RuntimeQuest> _completedQuests = new List<RuntimeQuest>();

        // Events
        public event System.Action<QuestData>       questGiven;
        public event System.Action<QuestData, int>  objectiveUpdated;  // quest, objectiveIndex
        public event System.Action<QuestData>       questCompleted;

        // ── Save data ─────────────────────────────────────────────────────────

        [System.Serializable]
        private class SaveData
        {
            public List<RuntimeQuestSave> activeQuests    = new List<RuntimeQuestSave>();
            public List<string>           completedIDs    = new List<string>();
        }

        [System.Serializable]
        private class RuntimeQuestSave
        {
            public string questID;
            public int[]  objectiveCounts;
        }

        private SaveData _saveData = new SaveData();

        // ── Unity lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            //if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            //transform.SetParent(null);
            //DontDestroyOnLoad(gameObject);
            _filePath = Path.Combine(Application.persistentDataPath, _fileName);
        }

        private void Start()
        {
            Load();
        }

        // ── Public API ─────────────────────────────────────────────────────────

        public void GiveQuest(QuestData quest)
        {
            if (quest == null || IsQuestGiven(quest)) return;
            var rq = new RuntimeQuest(quest);
            _activeQuests.Add(rq);
            questGiven?.Invoke(quest);
            Save();
        }

        /// <param name="amount">How much progress to add to this objective (default 1).</param>
        public void CompleteObjective(QuestData quest, int objectiveIndex, int amount = 1)
        {
            var rq = GetRuntimeQuest(quest);
            if (rq == null || rq.isCompleted) return;
            if (objectiveIndex < 0 || objectiveIndex >= rq.objectiveCounts.Length) return;

            int required = quest.objectives[objectiveIndex].requiredCount;
            rq.objectiveCounts[objectiveIndex] =
                Mathf.Min(rq.objectiveCounts[objectiveIndex] + amount, required);

            objectiveUpdated?.Invoke(quest, objectiveIndex);

            if (AreAllObjectivesComplete(rq, quest))
                CompleteQuest(quest);
            else
                Save();
        }

        /// Force-completes a quest regardless of objective progress.
        public void CompleteQuest(QuestData quest)
        {
            var rq = GetRuntimeQuest(quest);
            if (rq == null) return;

            rq.isCompleted = true;
            _activeQuests.Remove(rq);
            _completedQuests.Add(rq);

            questCompleted?.Invoke(quest);
            Save();
        }

        public bool IsQuestActive(QuestData quest)   => GetRuntimeQuest(quest) != null;
        public bool IsQuestComplete(QuestData quest) => FindCompleted(quest) != null;
        public bool IsQuestGiven(QuestData quest)    => IsQuestActive(quest) || IsQuestComplete(quest);

        public RuntimeQuest GetRuntimeQuest(QuestData quest)
        {
            if (quest == null) return null;
            for (int i = 0; i < _activeQuests.Count; i++)
                if (_activeQuests[i].data == quest) return _activeQuests[i];
            return null;
        }

        public List<RuntimeQuest> GetActiveQuests()    => _activeQuests;
        public List<RuntimeQuest> GetCompletedQuests() => _completedQuests;

        // ── ISaveable ─────────────────────────────────────────────────────────

        public void Save()
        {
            _saveData.activeQuests.Clear();
            foreach (var rq in _activeQuests)
            {
                _saveData.activeQuests.Add(new RuntimeQuestSave
                {
                    questID        = rq.data.questID,
                    objectiveCounts = (int[])rq.objectiveCounts.Clone()
                });
            }

            _saveData.completedIDs.Clear();
            foreach (var rq in _completedQuests)
                _saveData.completedIDs.Add(rq.data.questID);

            File.WriteAllText(_filePath, JsonUtility.ToJson(_saveData));
        }

        public void Load()
        {
            if (!File.Exists(_filePath)) return;
            if (_questContainer == null) { Debug.LogWarning("QuestManager: QuestContainer not assigned!"); return; }

            try
            {
                JsonUtility.FromJsonOverwrite(File.ReadAllText(_filePath), _saveData);

                _activeQuests.Clear();
                foreach (var saved in _saveData.activeQuests)
                {
                    var data = _questContainer.GetQuest(saved.questID);
                    if (data == null) { Debug.LogWarning($"QuestManager: Quest '{saved.questID}' not found in container."); continue; }

                    var rq = new RuntimeQuest(data);
                    // Restore objective progress (guard against count mismatch)
                    for (int i = 0; i < rq.objectiveCounts.Length && i < saved.objectiveCounts.Length; i++)
                        rq.objectiveCounts[i] = saved.objectiveCounts[i];
                    _activeQuests.Add(rq);
                }

                _completedQuests.Clear();
                foreach (var id in _saveData.completedIDs)
                {
                    var data = _questContainer.GetQuest(id);
                    if (data == null) continue;
                    var rq = new RuntimeQuest(data);
                    rq.isCompleted = true;
                    _completedQuests.Add(rq);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"QuestManager: Load failed — {e.Message}");
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private RuntimeQuest FindCompleted(QuestData quest)
        {
            if (quest == null) return null;
            for (int i = 0; i < _completedQuests.Count; i++)
                if (_completedQuests[i].data == quest) return _completedQuests[i];
            return null;
        }

        private static bool AreAllObjectivesComplete(RuntimeQuest rq, QuestData data)
        {
            if (data.objectives.Count == 0) return true;
            for (int i = 0; i < data.objectives.Count; i++)
                if (rq.objectiveCounts[i] < data.objectives[i].requiredCount) return false;
            return true;
        }
    }

    // ── Runtime quest state (not serialized directly) ──────────────────────────

    public class RuntimeQuest
    {
        public QuestData data;
        public int[]     objectiveCounts;
        public bool      isCompleted;

        public RuntimeQuest(QuestData data)
        {
            this.data       = data;
            objectiveCounts = new int[data.objectives.Count];
        }
    }
}
