using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PG.Localization.Editor
{
    // Parsed tables for editor tools, rebuilt when the settings or a CSV file change
    public static class LocalizationEditorData
    {
        public static event Action changed;

        private static List<LocalizationTable> _tables;

        public static IReadOnlyList<LocalizationTable> tables
        {
            get
            {
                if (_tables == null) Load();
                return _tables;
            }
        }

        public static void NotifyChanged()
        {
            _tables = null;
            changed?.Invoke();
        }

        private static void Load()
        {
            var settings = LocalizationSettings.instance;
            settings.EnsureInitialized();
            _tables = new List<LocalizationTable>();
            foreach (TextAsset asset in settings.tables)
                if (asset != null) _tables.Add(LocalizationTable.Parse(asset.text, asset.name));
        }

        public static List<string> Languages()
        {
            return tables.SelectMany(t => t.languages).Distinct().ToList();
        }

        // The language chosen for previews, or the first one available
        public static string PreviewLanguage()
        {
            var languages = Languages();
            string chosen = LocalizationSettings.instance.previewLanguage;
            return languages.Contains(chosen) || languages.Count == 0 ? chosen : languages[0];
        }

        public static IEnumerable<LocalizationTable> TablesFor(string tableFilter)
        {
            return string.IsNullOrEmpty(tableFilter) ? tables : tables.Where(t => t.name == tableFilter);
        }

        public static bool HasKey(string key, string tableFilter)
        {
            return !string.IsNullOrEmpty(key) && TablesFor(tableFilter).Any(t => t.rows.Any(r => r.key == key));
        }

        // False when the key is missing or has no text in this language
        public static bool TryGetTranslation(string key, string language, string tableFilter, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key)) return false;
            foreach (LocalizationTable table in TablesFor(tableFilter))
            {
                int index = table.LanguageIndex(language);
                if (index < 0) continue;
                foreach (LocalizationTable.Row row in table.rows)
                {
                    if (row.key != key) continue;
                    string text = row.values[index];
                    if (string.IsNullOrEmpty(text)) continue;
                    value = text;
                    return true;
                }
            }
            return false;
        }
    }

    internal class LocalizationTablesPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Concat(deleted).Concat(moved).Any(p => p.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
                LocalizationEditorData.NotifyChanged();
        }
    }
}
