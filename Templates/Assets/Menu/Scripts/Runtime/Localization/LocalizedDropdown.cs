using System;
using System.Collections.Generic;
using PG.Localization;
using TMPro;
using UnityEngine;

namespace PG.MenuManagement
{
    public class LocalizedDropdown : MonoBehaviour
    {
        [SerializeField] private List<string> _optionKeys = new List<string>();
        [SerializeField] private TMP_Dropdown _dropdown;
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            if (LocalizationSystem.instance != null)
            {
                LocalizationSystem.instance.localizationChanged += LocalizationChanged;
            }

            Localize();
        }

        private void OnDestroy()
        {

            if (LocalizationSystem.instance != null)
            {
                LocalizationSystem.instance.localizationChanged -= LocalizationChanged;
            }
        }

        [ContextMenu("Get Cache")]
        void GetCache() => TryGetComponent(out _dropdown);

        [ContextMenu("Get Key Cache")]
        void GetKeyCache()
        {
            _optionKeys.Clear();
            foreach (var key in _dropdown.options)
            {
                _optionKeys.Add(key.text);
            }
        }

        void LocalizationChanged(string localization) => Localize();
        public void Localize()
        {
            if (LocalizationSystem.instance == null)
            {
                return;
            }
            int selectedIndex = _dropdown.value;
            _dropdown.ClearOptions();
            List<string> translatedOptions = new List<string>();
            foreach (var key in _optionKeys)
            {
                translatedOptions.Add(LocalizationSystem.instance.GetLocalizedValue(key, key));
            }
            _dropdown.AddOptions(translatedOptions);
            _dropdown.SetValueWithoutNotify(selectedIndex);
        }
    }
}
