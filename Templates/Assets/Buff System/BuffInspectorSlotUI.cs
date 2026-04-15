using System.Text;
using PG.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PG.BuffManagement
{
    /// <summary>
    /// Detailed row in the BuffInspectorPanel.
    /// Shows: icon, localized name, all modifier values, remaining time.
    /// </summary>
    public class BuffInspectorSlotUI : MonoBehaviour
    {
        [SerializeField] private Image    _icon;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _modifiersText;
        [SerializeField] private TMP_Text _durationText;
        [SerializeField] private Image    _background;

        [Header("Background Colors")]
        [SerializeField] private Color _buffColor   = new Color(0.13f, 0.22f, 0.13f, 1f);
        [SerializeField] private Color _debuffColor = new Color(0.28f, 0.09f, 0.09f, 1f);

        [Header("Localization Keys")]
        [SerializeField] private string _keyPermanent = "buff_permanent";

        private ActiveBuff _buff;

        public void SetData(ActiveBuff buff)
        {
            _buff = buff;

            if (buff.data.icon != null)
                _icon.sprite = buff.data.icon;

            if (_background != null)
                _background.color = buff.data.isDebuff ? _debuffColor : _buffColor;

            Localize();
            RefreshTimer();
        }

        /// <summary>Called by the panel when language changes.</summary>
        public void Localize()
        {
            if (_buff == null) return;
            _nameText.text      = _buff.data.GetLocalizedName();
            _modifiersText.text = BuildModifiersString(_buff);
        }

        /// <summary>Called every frame by the panel to keep the countdown fresh.</summary>
        public void RefreshTimer()
        {
            if (_buff == null) return;

            if (_buff.IsPermanent)
            {
                var loc = LocalizationSystem.instance;
                _durationText.text = loc != null
                    ? loc.GetLocalizedValue(_keyPermanent, "Permanent")
                    : "Permanent";
                return;
            }

            float t = _buff.remainingTime;
            _durationText.text = t > 60f
                ? $"{Mathf.CeilToInt(t / 60f)}m {Mathf.CeilToInt(t % 60f)}s"
                : $"{Mathf.CeilToInt(t)}s";
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string BuildModifiersString(ActiveBuff buff)
        {
            if (buff.data.modifiers == null || buff.data.modifiers.Length == 0)
                return "";

            var sb = new StringBuilder();
            foreach (var mod in buff.data.modifiers)
            {
                string statName = GetStatLocalizedName(mod.statType);
                string sign     = mod.value >= 0f ? "+" : "";
                string suffix   = mod.modifierType == BuffModifierType.Percent ? "%" : "";
                // Positive on buff = green, negative or debuff = red
                string color    = (mod.value >= 0f) == (!buff.data.isDebuff) ? "#44FF88" : "#FF4444";

                if (sb.Length > 0) sb.Append("   ");
                sb.Append($"<color={color}>{sign}{mod.value:F0}{suffix}</color> {statName}");
            }
            return sb.ToString();
        }

        // Key pattern: "stat_" + enum name lowercased, e.g. "stat_magicdamage"
        private static string GetStatLocalizedName(BuffStatType stat)
        {
            var    loc = LocalizationSystem.instance;
            string key = "stat_" + stat.ToString().ToLower();
            return loc != null ? loc.GetLocalizedValue(key, stat.ToString()) : stat.ToString();
        }
    }
}
