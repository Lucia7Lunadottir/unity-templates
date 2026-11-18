using System.Threading.Tasks;
using UnityEngine;

namespace PG.Localization
{
    public class LocalizationSaver : MonoBehaviour
    {
        [SerializeField] private string _localizationParameter = "Language";
        private bool _subscribed;
        private void OnEnable()
        {
            Init();
        }
        async void Init()
        {
            await Task.Yield();
            if (!isActiveAndEnabled || LocalizationSystem.instance == null) return;
            LocalizationSystem.instance.localizationChanged += OnLocalizationChanged;
            _subscribed = true;
        }
        private void OnDisable()
        {
            if (_subscribed && LocalizationSystem.instance != null)
                LocalizationSystem.instance.localizationChanged -= OnLocalizationChanged;
            _subscribed = false;
        }
        void OnLocalizationChanged(string language)
        {
            PlayerPrefs.SetString(_localizationParameter, language);
        }
    }
}
