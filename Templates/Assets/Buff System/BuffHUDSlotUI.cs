using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PG.BuffManagement
{
    /// <summary>
    /// Small slot in the gameplay HUD.
    /// Shows: icon + radial cooldown overlay + countdown text + expiry pulse.
    /// </summary>
    public class BuffHUDSlotUI : MonoBehaviour
    {
        [SerializeField] private Image       _icon;
        [Tooltip("Image Type = Filled, Fill Method = Radial 360. Drains as buff expires.")]
        [SerializeField] private Image       _cooldownOverlay;
        [SerializeField] private TMP_Text    _timerText;
        [SerializeField] private CanvasGroup _canvasGroup;

        [Header("Expiry Warning")]
        [Tooltip("Seconds remaining when pulsing starts")]
        [SerializeField] private float _warningThreshold = 5f;
        [SerializeField] private float _pulseFrequency   = 3f;

        private ActiveBuff _buff;
        public  ActiveBuff  buff => _buff;

        public void SetData(ActiveBuff buff)
        {
            _buff = buff;
            if (buff.data.icon != null)
                _icon.sprite = buff.data.icon;

            _canvasGroup.alpha = 1f;
            Refresh();
        }

        private void Update()
        {
            if (_buff != null) Refresh();
        }

        private void Refresh()
        {
            if (_buff.IsPermanent)
            {
                _timerText.text = "∞";
                if (_cooldownOverlay != null) _cooldownOverlay.fillAmount = 1f;
                _canvasGroup.alpha = 1f;
                return;
            }

            // Timer text — minutes if over 60s
            float t = _buff.remainingTime;
            _timerText.text = t > 60f
                ? $"{Mathf.CeilToInt(t / 60f)}m"
                : $"{Mathf.CeilToInt(t)}s";

            // Radial drain
            if (_cooldownOverlay != null)
                _cooldownOverlay.fillAmount = _buff.NormalizedTime;

            // Pulse alpha when about to expire
            _canvasGroup.alpha = t < _warningThreshold
                ? Mathf.Lerp(0.35f, 1f, Mathf.PingPong(Time.time * _pulseFrequency, 1f))
                : 1f;
        }
    }
}
