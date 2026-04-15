using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using PG.MenuManagement;

namespace PG.QuestSystem
{
    /// Quest journal panel. Toggle with the input action assigned in the inspector.
    /// Left panel: scrollable list of active + completed quests.
    /// Right panel: selected quest title, description, and objectives.
    public class QuestMenuPanel : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject _panel;

        [Header("Quest List (left)")]
        [SerializeField] private Transform    _questListContainer;
        [SerializeField] private QuestEntryUI _entryPrefab;
        [SerializeField] private Selectable   _firstSelectable;

        [Header("Detail (right)")]
        [SerializeField] private GameObject      _noQuestSelected;    // "Select a quest" placeholder
        [SerializeField] private GameObject      _questDetail;        // parent of detail widgets
        [SerializeField] private TMP_Text        _detailTitleText;
        [SerializeField] private TMP_Text        _detailDescriptionText;
        [SerializeField] private Image           _detailIcon;
        [SerializeField] private Transform       _objectivesContainer;
        [SerializeField] private QuestObjectiveUI _objectivePrefab;

        [Header("Input")]
        [SerializeField] private InputActionProperty _toggleAction;

        private QuestData _selectedQuest;

        // ── Unity lifecycle ────────────────────────────────────────────────────

        private void OnEnable()
        {
            _toggleAction.action.performed += OnToggle;

            if (QuestManager.instance != null)
            {
                QuestManager.instance.questGiven     += OnListChanged;
                QuestManager.instance.questCompleted += OnListChanged;
                QuestManager.instance.objectiveUpdated += OnObjectiveUpdated;
            }
        }

        private void OnDisable()
        {
            _toggleAction.action.performed -= OnToggle;

            if (QuestManager.instance != null)
            {
                QuestManager.instance.questGiven     -= OnListChanged;
                QuestManager.instance.questCompleted -= OnListChanged;
                QuestManager.instance.objectiveUpdated -= OnObjectiveUpdated;
            }
        }

        // ── Toggle ─────────────────────────────────────────────────────────────

        private void OnToggle(InputAction.CallbackContext ctx)
        {
            if (_panel.activeSelf) ClosePanel();
            else OpenPanel();
        }

        public void OpenPanel()
        {
            if (!UIManager.RequestOpen(_panel)) return;

            _panel.SetActive(true);
            Time.timeScale = 0f;
            Menu.OnChangeCursorVisible(true);
            Pause.isPauseEnable = false;

            RefreshList();
            ShowNoSelection();

            if (_firstSelectable != null)
                _firstSelectable.Select();
        }

        public void ClosePanel()
        {
            UIManager.RegisterClose(_panel);
            _panel.SetActive(false);
            Time.timeScale = 1f;
            Menu.OnChangeCursorVisible(false);
            Pause.isPauseEnable = true;
            EventSystem.current?.SetSelectedGameObject(null);
        }

        // ── List ───────────────────────────────────────────────────────────────

        private void RefreshList()
        {
            foreach (Transform child in _questListContainer)
                Destroy(child.gameObject);

            if (QuestManager.instance == null) return;

            foreach (var rq in QuestManager.instance.GetActiveQuests())
                CreateEntry(rq.data, isCompleted: false);

            foreach (var rq in QuestManager.instance.GetCompletedQuests())
                CreateEntry(rq.data, isCompleted: true);

            // If the selected quest was removed (e.g., completed and re-listed), re-select it
            if (_selectedQuest != null)
                ShowQuestDetail(_selectedQuest);
        }

        private void CreateEntry(QuestData quest, bool isCompleted)
        {
            var entry = Instantiate(_entryPrefab, _questListContainer);
            entry.Setup(quest, isCompleted, SelectQuest);
        }

        // ── Detail ─────────────────────────────────────────────────────────────

        private void SelectQuest(QuestData quest)
        {
            _selectedQuest = quest;
            ShowQuestDetail(quest);
        }

        private void ShowQuestDetail(QuestData quest)
        {
            if (_noQuestSelected != null) _noQuestSelected.SetActive(false);
            if (_questDetail != null)     _questDetail.SetActive(true);

            if (_detailTitleText != null)       _detailTitleText.text = quest.GetTitle();
            if (_detailDescriptionText != null) _detailDescriptionText.text = quest.GetDescription();

            if (_detailIcon != null)
            {
                _detailIcon.enabled = quest.icon != null;
                if (quest.icon != null) _detailIcon.sprite = quest.icon;
            }

            RebuildObjectives(quest);
        }

        private void RebuildObjectives(QuestData quest)
        {
            foreach (Transform child in _objectivesContainer)
                Destroy(child.gameObject);

            if (QuestManager.instance == null) return;

            var rq = QuestManager.instance.GetRuntimeQuest(quest);

            for (int i = 0; i < quest.objectives.Count; i++)
            {
                var row = Instantiate(_objectivePrefab, _objectivesContainer);
                int count = (rq != null && i < rq.objectiveCounts.Length)
                    ? rq.objectiveCounts[i]
                    : quest.objectives[i].requiredCount; // completed quests show full progress
                row.Setup(quest.objectives[i], count);
            }
        }

        private void ShowNoSelection()
        {
            _selectedQuest = null;
            if (_noQuestSelected != null) _noQuestSelected.SetActive(true);
            if (_questDetail != null)     _questDetail.SetActive(false);
        }

        // ── Event callbacks ────────────────────────────────────────────────────

        private void OnListChanged(QuestData _)
        {
            if (_panel.activeSelf) RefreshList();
        }

        private void OnObjectiveUpdated(QuestData quest, int _)
        {
            if (_panel.activeSelf && _selectedQuest == quest)
                RebuildObjectives(quest);
        }
    }
}
