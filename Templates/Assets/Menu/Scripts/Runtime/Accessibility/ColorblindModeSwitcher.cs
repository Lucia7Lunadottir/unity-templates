using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace PG.Accessibility
{
    // Applies a colorblind LUT through the ColorLookup override of a global Volume
    public sealed class ColorblindModeSwitcher : MonoBehaviour
    {
        [SerializeField] private Volume targetVolume;
        public const string COLORBLIND_SAVE_PARAMETER = "Colorblind";
        public const string COLORBLIND_SAVE_VALUE = "ColorblindSlider";

        [SerializeField] private Texture _protanomaly;
        [SerializeField] private Texture _deuteranomaly;
        [SerializeField] private Texture _tritanomaly;
        [SerializeField] private Texture _protanopia;
        [SerializeField] private Texture _deuteranopia;
        [SerializeField] private Texture _tritanopia;
        [SerializeField] private Texture _achromatopsia;
        [SerializeField] private Texture _blueConeMonochromacy;

        [SerializeField] private TMP_Dropdown _modeDropdown;
        [SerializeField] private Slider _slider;
        private ColorLookup _component;

        private void Awake()
        {
            if (targetVolume == null)
            {
                Debug.LogError("[ColorblindModeSwitcher] Volume is not assigned.");
                enabled = false;
                return;
            }

            if (!targetVolume.profile.TryGet(out _component))
            {
                Debug.LogError("[ColorblindModeSwitcher] Volume profile has no ColorLookup override.");
                enabled = false;
                return;
            }

            _component.active = true;
            _component.texture.overrideState = true;
            _component.contribution.overrideState = true;

            _modeDropdown.onValueChanged.AddListener(SetMode);
            _slider.onValueChanged.AddListener(SetIntensity);

            Load();
        }

        private void OnDestroy()
        {
            if (_modeDropdown != null) _modeDropdown.onValueChanged.RemoveListener(SetMode);
            if (_slider != null) _slider.onValueChanged.RemoveListener(SetIntensity);
        }

        public void SetMode(int modeIndex)
        {
            if (_component == null) return;
            SetMode((ColorblindMode)modeIndex);
        }

        private void DeactivateAll(ColorblindObject[] gameObjects)
        {
            foreach (ColorblindObject colorblindObject in gameObjects)
            {
                if (colorblindObject != null) colorblindObject.gameObject.SetActive(false);
            }
        }

        private void ActivateAll(ColorblindObject[] gameObjects)
        {
            foreach (ColorblindObject colorblindObject in gameObjects)
            {
                if (colorblindObject != null) colorblindObject.gameObject.SetActive(true);
            }
        }

        public void SetMode(ColorblindMode mode)
        {
            if (_component == null) return;

            ColorblindObject[] colorblindObjects = FindObjectsByType<ColorblindObject>(FindObjectsInactive.Include);

            switch (mode)
            {
                case ColorblindMode.None:
                    _component.active = false;
                    DeactivateAll(colorblindObjects);
                    break;
                case ColorblindMode.Protanomaly:
                    ApplyTexture(_protanomaly, colorblindObjects);
                    break;
                case ColorblindMode.Deuteranomaly:
                    ApplyTexture(_deuteranomaly, colorblindObjects);
                    break;
                case ColorblindMode.Tritanomaly:
                    ApplyTexture(_tritanomaly, colorblindObjects);
                    break;
                case ColorblindMode.Protanopia:
                    ApplyTexture(_protanopia, colorblindObjects);
                    break;
                case ColorblindMode.Deuteranopia:
                    ApplyTexture(_deuteranopia, colorblindObjects);
                    break;
                case ColorblindMode.Tritanopia:
                    ApplyTexture(_tritanopia, colorblindObjects);
                    break;
                case ColorblindMode.Achromatopsia:
                    ApplyTexture(_achromatopsia, colorblindObjects);
                    break;
                case ColorblindMode.BlueConeMonochromacy:
                    ApplyTexture(_blueConeMonochromacy, colorblindObjects);
                    break;
            }

            Save();
        }

        private void ApplyTexture(Texture lutTexture, ColorblindObject[] objectsToActivate)
        {
            _component.texture.value = lutTexture;
            _component.active = true;
            ActivateAll(objectsToActivate);
        }

        public void SetIntensity(float value)
        {
            if (_component == null) return;

            float clampedValue = Mathf.Clamp01(value);
            _component.contribution.value = clampedValue;
            Save();
        }

        public void Disable() => SetMode(ColorblindMode.None);
        public void SetProtanomaly() => SetMode(ColorblindMode.Protanomaly);
        public void SetDeuteranomaly() => SetMode(ColorblindMode.Deuteranomaly);
        public void SetTritanomaly() => SetMode(ColorblindMode.Tritanomaly);
        public void SetProtanopia() => SetMode(ColorblindMode.Protanopia);
        public void SetDeuteranopia() => SetMode(ColorblindMode.Deuteranopia);
        public void SetTritanopia() => SetMode(ColorblindMode.Tritanopia);
        public void SetAchromatopsia() => SetMode(ColorblindMode.Achromatopsia);
        public void SetBlueConeMonochromacy() => SetMode(ColorblindMode.BlueConeMonochromacy);

        public void Save()
        {
            PlayerPrefs.SetInt(COLORBLIND_SAVE_PARAMETER, _modeDropdown.value);
            PlayerPrefs.SetFloat(COLORBLIND_SAVE_VALUE, _slider.value);
            PlayerPrefs.Save();
        }

        public void Load()
        {
            int savedMode = PlayerPrefs.GetInt(COLORBLIND_SAVE_PARAMETER, 0);
            float savedIntensity = PlayerPrefs.GetFloat(COLORBLIND_SAVE_VALUE, 1f);

            _modeDropdown.SetValueWithoutNotify(savedMode);
            _slider.SetValueWithoutNotify(savedIntensity);

            SetMode(savedMode);
            SetIntensity(savedIntensity);
        }
    }
}
