using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PG.QuestSystem
{
    /// Single quest row in the journal quest list (left panel).
    public class QuestEntryUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text   _titleText;
        [SerializeField] private Image      _icon;
        [SerializeField] private GameObject _completedMark; // strikethrough or checkmark overlay
        [SerializeField] private Button     _button;

        public QuestData Quest { get; private set; }

        public void Setup(QuestData quest, bool isCompleted, Action<QuestData> onSelected)
        {
            Quest = quest;
            _titleText.text = quest.GetTitle();

            if (_icon != null)
            {
                _icon.enabled = quest.icon != null;
                if (quest.icon != null) _icon.sprite = quest.icon;
            }

            if (_completedMark != null)
                _completedMark.SetActive(isCompleted);

            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => onSelected?.Invoke(quest));
        }
    }
}
