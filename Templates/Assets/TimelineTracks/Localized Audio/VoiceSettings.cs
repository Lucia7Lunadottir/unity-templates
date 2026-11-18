using System;
using UnityEngine;

namespace PG.TimelineAudio
{
    // Настройки озвучки на весь проект: какие языки озвучены и какой играть, если записи на языке игры нет.
    // Один ассет, лежит в Resources рядом со скриптом - находится сам (Instance); в редакторе создаётся сам, если его нет.
    // Названия языков - как LocalizationSystem.currentLanguage (заголовки колонок CSV локализации).
    public class VoiceSettings : ScriptableObject
    {
        private const string ResourcePath = "Voice Settings";
        private const string AssetFolder = "Assets/Scripts/Systems/Timeline/Localized Audio/Resources";

        [SerializeField] private string[] _languages = { "English", "Russian", "Ukrainian" };
        [Tooltip("Играет, если в катсцене нет записи на языке игры")]
        [SerializeField, VoiceLanguage(allowEmpty: false)] private string _fallbackLanguage = "English";

        public string[] languages => _languages;
        // Не задан или удалён из списка - первый язык списка
        public string fallbackLanguage => IsVoiced(_fallbackLanguage) || _languages.Length == 0 ? _fallbackLanguage : _languages[0];

        public bool IsVoiced(string language) => Array.IndexOf(_languages, language) >= 0;

        private static VoiceSettings s_instance;

        public static VoiceSettings Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                s_instance = Resources.Load<VoiceSettings>(ResourcePath);
#if UNITY_EDITOR
                if (s_instance == null) s_instance = CreateAsset();
#endif
                return s_instance;
            }
        }

#if UNITY_EDITOR
        private static VoiceSettings CreateAsset()
        {
            var settings = CreateInstance<VoiceSettings>();
            System.IO.Directory.CreateDirectory(AssetFolder);
            UnityEditor.AssetDatabase.CreateAsset(settings, AssetFolder + "/" + ResourcePath + ".asset");
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log("VoiceSettings: создан " + AssetFolder + "/" + ResourcePath + ".asset", settings);
            return settings;
        }
#endif
    }

    // Строковое поле с языком озвучки: в инспекторе - выпадающий список из VoiceSettings.
    // allowEmpty - есть пункт «не выбран» (пустая строка)
    public class VoiceLanguageAttribute : PropertyAttribute
    {
        public readonly bool allowEmpty;
        public VoiceLanguageAttribute(bool allowEmpty = true) => this.allowEmpty = allowEmpty;
    }
}
