using TMPro;
using UnityEngine;

namespace PG.Localization
{
    public class LocalizeText : MonoBehaviour
    {
        [SerializeField] private TMP_Text _textObject;
        [LocalizedKey]
        [SerializeField] private string _key;
        public string key
        {
            get => _key;
            set
            {
                _key = value;
            }
        }

        [ContextMenu("Get Cache")]
        private void GetCache() => TryGetComponent(out _textObject);
        [ContextMenu("Get Key Cache")]
        private void GetKeyCache() => key = _textObject.text;
        private void Start()
        {
            Localize();
        }
        public void Localize()
        {
            if (_textObject == null || LocalizationSystem.instance == null) return;
            _textObject.text = LocalizationSystem.instance.GetLocalizedValue(_key, _textObject.text);
        }
        public string GetLocalizedValue()
        {
            if (_textObject == null || LocalizationSystem.instance == null) return _key;
            return LocalizationSystem.instance.GetLocalizedValue(_key, _textObject.text);
        }
    }
}
