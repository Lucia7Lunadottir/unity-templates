using System.Collections.Generic;
using UnityEngine;
using PG.Localization;

namespace PG.QuestSystem
{
    [CreateAssetMenu(menuName = "PG/Quest/Quest")]
    public class QuestData : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique string ID used for save/load. Must be unique across all quests.")]
        public string questID;

        [Header("Display")]
        public string title;
        [SerializeField] private string _titleKey;
        [TextArea(2, 4)] public string description;
        [SerializeField] private string _descriptionKey;
        public Sprite icon;

        [Header("Objectives")]
        public List<QuestObjectiveData> objectives = new List<QuestObjectiveData>();

        public string GetTitle()
        {
            if (!string.IsNullOrEmpty(_titleKey) && LocalizationSystem.instance != null)
                return LocalizationSystem.instance.GetLocalizedValue(_titleKey, title);
            return title;
        }

        public string GetDescription()
        {
            if (!string.IsNullOrEmpty(_descriptionKey) && LocalizationSystem.instance != null)
                return LocalizationSystem.instance.GetLocalizedValue(_descriptionKey, description);
            return description;
        }
    }

    [System.Serializable]
    public class QuestObjectiveData
    {
        public string description;
        [SerializeField] private string _descriptionKey;
        [Min(1)] public int requiredCount = 1;

        public string GetDescription()
        {
            if (!string.IsNullOrEmpty(_descriptionKey) && LocalizationSystem.instance != null)
                return LocalizationSystem.instance.GetLocalizedValue(_descriptionKey, description);
            return description;
        }
    }
}
