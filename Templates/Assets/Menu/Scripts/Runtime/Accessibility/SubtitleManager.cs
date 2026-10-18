using TMPro;
using UnityEngine;

namespace PG.Accessibility
{
    public class SubtitleManager : MonoBehaviour
    {
        public enum SubtitleSize
        {
            VerySmall = 36,
            Small = 42,
            Medium = 56,
            Large = 72,
            VeryLarge = 80
        }

        [SerializeField] protected GameObject _choiceButtonPrefab;
        [SerializeField] private TMP_Text[] subtitleTexts;
        [SerializeField] private TMP_Dropdown _subtitleDropdown;

        public const string SUBTITLES_SAVE_VALUE = "SubtitlesSize";

        private void Start()
        {
            if (_subtitleDropdown != null)
            {
                _subtitleDropdown.onValueChanged.AddListener(OnDropdownValueChanged);
            }

            Load();
        }

        private void OnDestroy()
        {
            if (_subtitleDropdown != null)
            {
                _subtitleDropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
            }
        }

        public void OnDropdownValueChanged(int index)
        {
            SubtitleSize selectedSize = IndexToSubtitleSize(index);
            SetSubtitleFontSize((float)selectedSize);
            Save();
        }

        public void SetSubtitleFontSize(float size)
        {
            if (subtitleTexts.Length > 0)
            {
                foreach (var subtitleText in subtitleTexts)
                {
                    if (subtitleText != null)
                    {
                        subtitleText.fontSize = size;
                    }
                }
            }

            if (_choiceButtonPrefab != null)
            {
                TMP_Text choiceText = _choiceButtonPrefab.GetComponentInChildren<TMP_Text>();
                if (choiceText != null)
                {
                    choiceText.fontSize = size;
                }
            }
        }

        public void Save()
        {

            PlayerPrefs.SetInt(SUBTITLES_SAVE_VALUE, (int)IndexToSubtitleSize(_subtitleDropdown.value));
            PlayerPrefs.Save();
        }

        public void Load()
        {
            int savedSize = PlayerPrefs.GetInt(SUBTITLES_SAVE_VALUE, (int)SubtitleSize.Large);

            SetSubtitleFontSize(savedSize);

            if (_subtitleDropdown != null)
            {
                int dropdownIndex = SubtitleSizeToDropdownIndex((SubtitleSize)savedSize);
                _subtitleDropdown.SetValueWithoutNotify(dropdownIndex);
            }
        }

        private SubtitleSize IndexToSubtitleSize(int index)
        {
            return index switch
            {
                0 => SubtitleSize.VerySmall,
                1 => SubtitleSize.Small,
                2 => SubtitleSize.Medium,
                3 => SubtitleSize.Large,
                4 => SubtitleSize.VeryLarge,
                _ => SubtitleSize.Large
            };
        }

        private int SubtitleSizeToDropdownIndex(SubtitleSize size)
        {
            return size switch
            {
                SubtitleSize.VerySmall => 0,
                SubtitleSize.Small => 1,
                SubtitleSize.Medium => 2,
                SubtitleSize.Large => 3,
                SubtitleSize.VeryLarge => 4,
                _ => 3
            };
        }
    }
}
