using System;
using UnityEditor;
using UnityEngine;

namespace PG.TimelineAudio
{
    // Выпадающий список языков из VoiceSettings вместо ввода текста.
    // Первый пункт - «не выбран» (пустая строка: у VoiceTrack это запасной язык); язык не из списка показывается с пометкой
    [CustomPropertyDrawer(typeof(VoiceLanguageAttribute))]
    public class VoiceLanguageDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var settings = VoiceSettings.Instance;
            if (property.propertyType != SerializedPropertyType.String || settings == null || settings.languages.Length == 0)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var languages = settings.languages;
            bool allowEmpty = ((VoiceLanguageAttribute)attribute).allowEmpty;
            string value = property.stringValue;
            bool unknown = !string.IsNullOrEmpty(value) && Array.IndexOf(languages, value) < 0;
            int first = allowEmpty ? 1 : 0;

            var options = new System.Collections.Generic.List<GUIContent>();
            if (allowEmpty) options.Add(new GUIContent("— не выбран (запасной: " + settings.fallbackLanguage + ")"));
            foreach (var language in languages) options.Add(new GUIContent(language));
            if (unknown) options.Add(new GUIContent(value + " (нет в Voice Settings)"));

            int index = unknown ? options.Count - 1
                : string.IsNullOrEmpty(value) ? (allowEmpty ? 0 : -1)
                : Array.IndexOf(languages, value) + first;

            using (new EditorGUI.PropertyScope(position, label, property))
            {
                EditorGUI.BeginChangeCheck();
                int chosen = EditorGUI.Popup(position, label, index, options.ToArray());
                if (EditorGUI.EndChangeCheck() && chosen >= 0 && chosen < first + languages.Length)
                    property.stringValue = chosen < first ? "" : languages[chosen - first];
            }
        }
    }
}
