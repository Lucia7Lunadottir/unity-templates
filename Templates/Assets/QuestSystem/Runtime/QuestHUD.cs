using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PG.QuestSystem
{
    /// Displays one HUD row ("? Quest Title") per active quest.
    /// Attach to a VerticalLayoutGroup panel on the HUD canvas.
    public class QuestHUD : MonoBehaviour
    {
        [SerializeField] private Transform     _container;
        [SerializeField] private QuestHUDEntry _entryPrefab;

        private readonly List<QuestHUDEntry> _entries = new List<QuestHUDEntry>();

        private void OnEnable()
        {
            Init();
        }

        async void Init()
        {
            await Task.Delay(30);
            if (QuestManager.instance == null) return;
            QuestManager.instance.questGiven     += OnQuestGiven;
            QuestManager.instance.questCompleted += OnQuestCompleted;
            QuestManager.instance.objectiveUpdated += OnObjectiveUpdated;
        }
        private void OnDisable()
        {
            if (QuestManager.instance == null) return;
            QuestManager.instance.questGiven     -= OnQuestGiven;
            QuestManager.instance.questCompleted -= OnQuestCompleted;
            QuestManager.instance.objectiveUpdated -= OnObjectiveUpdated;
        }

        private async void Start()
        {
            await Task.Delay(30);
            // Populate with quests that were already active before this scene loaded.
            if (QuestManager.instance == null) return;
            foreach (var rq in QuestManager.instance.GetActiveQuests())
                AddEntry(rq.data);
        }

        private void OnQuestGiven(QuestData quest)     => AddEntry(quest);
        private void OnQuestCompleted(QuestData quest) => RemoveEntry(quest);
        

        private void OnObjectiveUpdated(QuestData quest, int index)
        {
            foreach (var entry in _entries)
            {
                if (entry.Quest == quest)
                {
                    entry.Refresh();
                    break;
                }
            }
        }

        
        
        private void AddEntry(QuestData quest)
        {
            var entry = Instantiate(_entryPrefab, _container);
            entry.Setup(quest);
            _entries.Add(entry);
        }

        private void RemoveEntry(QuestData quest)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Quest != quest) continue;
                Destroy(_entries[i].gameObject);
                _entries.RemoveAt(i);
                return;
            }
        }
    }
}
