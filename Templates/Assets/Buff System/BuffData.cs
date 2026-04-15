using PG.Localization;
using UnityEngine;

namespace PG.BuffManagement
{
    [CreateAssetMenu(menuName = "PG/Buff/Buff Data")]
    public class BuffData : ScriptableObject
    {
        [Header("Identity")]
        [field: SerializeField] public string buffName    { get; private set; }
        [SerializeField] private string _nameKey; // e.g. "buff_warmth_name", "buff_damage_name"
        [field: SerializeField] public Sprite icon        { get; private set; }
        [field: SerializeField] public bool   isDebuff    { get; private set; }

        /// <summary>Returns the localized buff name, falling back to buffName if key is missing.</summary>
        public string GetLocalizedName()
        {
            if (LocalizationSystem.instance != null && !string.IsNullOrEmpty(_nameKey))
                return LocalizationSystem.instance.GetLocalizedValue(_nameKey, buffName);
            return buffName;
        }

        [Header("Duration")]
        [Tooltip("-1 = permanent until manually removed (e.g. equipped clothing)")]
        [field: SerializeField] public float duration { get; private set; } = 60f;

        [Header("Stacking")]
        [field: SerializeField] public bool canStack         { get; private set; }
        [field: SerializeField] public int  maxStacks        { get; private set; } = 1;
        [Tooltip("Resets duration timer when the same buff is applied again")]
        [field: SerializeField] public bool refreshOnReapply { get; private set; } = true;

        [Header("Modifiers")]
        [field: SerializeField] public BuffModifier[] modifiers { get; private set; }

        public bool IsPermanent => duration < 0f;
    }
}
