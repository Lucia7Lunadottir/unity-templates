using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.UI.Slider;

namespace PG.MenuManagement
{
    public class SliderWithValue : MonoBehaviour
    {
        [SerializeField] private Slider _slider;
        [SerializeField] private TMP_Text _valueText;
        [SerializeField] private TMP_Text _minValueText;
        [SerializeField] private TMP_Text _maxValueText;
        public SliderEvent onValueChanged;
        public float value
        {
            get => _slider.value;
            set
            {
                _slider.value = value;
                onValueChanged?.Invoke(value);
                UpdateValuesText();
            }
        }
        public float minValue
        {
            get => _slider.minValue;
            set
            {
                _slider.minValue = value;
                UpdateValuesText();
            }
        }
        public float maxValue
        {
            get => _slider.maxValue;
            set
            {
                _slider.maxValue = value;
                UpdateValuesText();
            }
        }

        private void OnValidate()
        {
            if (_slider != null && _valueText != null && _minValueText != null && _maxValueText != null)
            {
                UpdateValuesText();
            }
        }

        private void Awake()
        {
            _slider.onValueChanged.AddListener(onValueChanged.Invoke);
            _slider.onValueChanged.AddListener(UpdateValuesText);
        }
        private void OnDestroy()
        {
            _slider.onValueChanged.RemoveListener(onValueChanged.Invoke);
            _slider.onValueChanged.RemoveListener(UpdateValuesText);
        }
        void UpdateValuesText(float value = 0)
        {
            _valueText.text = _slider.value.ToString();
            _minValueText.text = _slider.minValue.ToString();
            _maxValueText.text = _slider.maxValue.ToString();
        }
    }
}
