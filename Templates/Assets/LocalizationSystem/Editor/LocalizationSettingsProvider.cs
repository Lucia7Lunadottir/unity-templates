using UnityEditor;
using UnityEngine;

namespace PG.Localization.Editor
{
    // Project Settings > Localization
    internal static class LocalizationSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Localization", SettingsScope.Project)
            {
                label = "Localization",
                keywords = new[] { "localization", "language", "translation", "csv" },
                guiHandler = _ => Draw()
            };
        }

        private static void Draw()
        {
            var settings = LocalizationSettings.instance;
            settings.EnsureInitialized();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tables", EditorStyles.boldLabel);
            TextAsset toRemove = null;
            foreach (TextAsset table in settings.tables)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField(table, typeof(TextAsset), false);
                    if (GUILayout.Button("✕", GUILayout.Width(24))) toRemove = table;
                }
            }
            if (toRemove != null) settings.RemoveTable(toRemove);

            using (new EditorGUILayout.HorizontalScope())
            {
                var added = (TextAsset)EditorGUILayout.ObjectField("Add table", null, typeof(TextAsset), false);
                if (added != null) settings.AddTable(added);
            }
            if (GUILayout.Button("Add all CSV tables from Assets"))
                Debug.Log($"Localization: added {settings.AddTablesFromFolder("Assets")} table(s)");

            if (GUILayout.Button("Open Localization Editor")) LocalizationEditorWindow.Open();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            var languages = LocalizationEditorData.Languages();
            if (languages.Count == 0)
            {
                EditorGUILayout.HelpBox("No languages found. Add a CSV table whose header is: key,Language1,Language2,...", MessageType.Info);
                return;
            }
            int current = Mathf.Max(0, languages.IndexOf(LocalizationEditorData.PreviewLanguage()));
            int chosen = EditorGUILayout.Popup(new GUIContent("Preview language", "Language shown under [LocalizedKey] fields"), current, languages.ToArray());
            if (chosen != current) settings.previewLanguage = languages[chosen];
        }
    }
}
