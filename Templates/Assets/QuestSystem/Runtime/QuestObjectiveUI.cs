using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PG.QuestSystem
{
    /// Single objective row inside the quest journal detail view.
    public class QuestObjectiveUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text _descriptionText;
        [SerializeField] private TMP_Text _progressText;   // shown only when requiredCount > 1
        [SerializeField] private Image    _checkmark;       // activated when objective is done

        public void Setup(QuestObjectiveData data, int currentCount)
        {
            bool done = currentCount >= data.requiredCount;

            _descriptionText.text = data.GetDescription();

            if (_progressText != null)
            {
                bool showProgress = data.requiredCount > 1;
                _progressText.gameObject.SetActive(showProgress);
                if (showProgress)
                    _progressText.text = $"{currentCount}/{data.requiredCount}";
            }

            if (_checkmark != null)
                _checkmark.gameObject.SetActive(done);
        }
    }
}
