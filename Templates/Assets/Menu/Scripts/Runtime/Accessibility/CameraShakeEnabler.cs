#if PG_CINEMACHINE
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

namespace PG.Accessibility
{
    public sealed class CameraShakeEnabler : MonoBehaviour
    {
        [SerializeField] private Slider _enableSlider;
        [SerializeField] private CinemachineBasicMultiChannelPerlin _cinemachineBasicMultiChannelPerlin;
        public const string SHAKE_SAVE_VALUE = "CameraShake";

        private void Awake()
        {
            _enableSlider.onValueChanged.AddListener(SetShakeEnable);
            Load();
        }

        private void OnDestroy()
        {
            if (_enableSlider != null) _enableSlider.onValueChanged.RemoveListener(SetShakeEnable);
        }

        public void SetShakeEnable(float value)
        {
            if (_cinemachineBasicMultiChannelPerlin != null)
            {
                _cinemachineBasicMultiChannelPerlin.enabled = _enableSlider.value > 0;
            }

            Save();
        }

        public void Save()
        {
            PlayerPrefs.SetFloat(SHAKE_SAVE_VALUE, _enableSlider.value);
            PlayerPrefs.Save();
        }

        public void Load()
        {
            float savedShakeEnable = PlayerPrefs.GetFloat(SHAKE_SAVE_VALUE, 1f);

            _enableSlider.SetValueWithoutNotify(savedShakeEnable);
            SetShakeEnable(savedShakeEnable);
        }
    }
}
#endif
