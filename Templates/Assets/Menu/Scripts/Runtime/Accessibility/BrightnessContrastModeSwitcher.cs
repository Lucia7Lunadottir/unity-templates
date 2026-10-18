using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace PG.Accessibility
{
    public sealed class BrightnessContrastModeSwitcher : MonoBehaviour
    {
        [SerializeField] private Material _fullscreenMaterial;
        public const string BRIGHTNESS_SAVE_VALUE = "Brightness";
        public const string CONTRAST_SAVE_VALUE = "Contrast";
        public const string BRIGHTNESS_PARAMETER = "_Brightness";
        public const string CONTRAST_PARAMETER = "_Contrast";

        [SerializeField] private Slider _brightnessSlider;
        [SerializeField] private Slider _contrastSlider;

        private void Awake()
        {
            if (_fullscreenMaterial == null)
            {
                Debug.LogError("[BrightnessContrastModeSwitcher] Material is not assigned.");
                enabled = false;
                return;
            }

            _brightnessSlider.onValueChanged.AddListener(SetBrightnessIntensity);
            _contrastSlider.onValueChanged.AddListener(SetContrastIntensity);

            Load();
        }

        private void OnDestroy()
        {
            if (_brightnessSlider != null) _brightnessSlider.onValueChanged.RemoveListener(SetBrightnessIntensity);
            if (_contrastSlider != null) _contrastSlider.onValueChanged.RemoveListener(SetContrastIntensity);
        }

        public void SetBrightnessIntensity(float value)
        {
            if (_fullscreenMaterial == null) return;

            float clampedValue = Mathf.Clamp(value, -5, 5);
            _fullscreenMaterial.SetFloat(BRIGHTNESS_PARAMETER, clampedValue);
            Save();
        }
        public void SetContrastIntensity(float value)
        {
            if (_fullscreenMaterial == null) return;

            float clampedValue = Mathf.Clamp(value, -0.5f, 1.5f);
            _fullscreenMaterial.SetFloat(CONTRAST_PARAMETER, clampedValue);
            Save();
        }

        public void Save()
        {
            PlayerPrefs.SetFloat(BRIGHTNESS_SAVE_VALUE, _brightnessSlider.value);
            PlayerPrefs.SetFloat(CONTRAST_SAVE_VALUE, _contrastSlider.value);
            PlayerPrefs.Save();
        }

        public void Load()
        {
            float savedBrightnessIntensity = PlayerPrefs.GetFloat(BRIGHTNESS_SAVE_VALUE, 1f);
            float savedContrastIntensity = PlayerPrefs.GetFloat(CONTRAST_SAVE_VALUE, 1f);

            _brightnessSlider.SetValueWithoutNotify(savedBrightnessIntensity);
            _contrastSlider.SetValueWithoutNotify(savedContrastIntensity);

            SetBrightnessIntensity(savedBrightnessIntensity);
            SetContrastIntensity(savedContrastIntensity);
        }

    }
}
