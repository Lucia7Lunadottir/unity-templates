using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PG.QuestSystem
{
    public class QuestHUDEntry : MonoBehaviour
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _objectiveText; // Добавьте это поле в инспекторе
        [SerializeField] private Image    _questIcon;

        public QuestData Quest { get; private set; }

        public void Setup(QuestData quest)
        {
            Quest = quest;
            Refresh();
        }

        public void Refresh()
        {
            if (Quest == null || QuestManager.instance == null) return;

            _titleText.text = Quest.GetTitle();
            
            // Находим текущую активную цель через QuestManager
            var rq = QuestManager.instance.GetRuntimeQuest(Quest);
            if (rq != null && _objectiveText != null)
            {
                _objectiveText.text = GetCurrentObjectiveDescription(rq);
            }

            if (_questIcon != null)
            {
                if (Quest.icon != null) _questIcon.sprite = Quest.icon;
            }
        }

        private string GetCurrentObjectiveDescription(RuntimeQuest rq)
        {
            for (int i = 0; i < Quest.objectives.Count; i++)
            {
                // Если цель еще не достигла нужного количества
                if (rq.objectiveCounts[i] < Quest.objectives[i].requiredCount)
                {
                    var obj = Quest.objectives[i];
                    string progress = obj.requiredCount > 1 
                        ? $" ({rq.objectiveCounts[i]}/{obj.requiredCount})" 
                        : "";
                    return "- " + obj.GetDescription() + progress;
                }
            }
            return "All objectives complete!";
        }
    }
}