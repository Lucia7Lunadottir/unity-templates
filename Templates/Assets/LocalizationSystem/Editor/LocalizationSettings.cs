using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PG.Localization.Editor
{
    // Project-wide localization settings stored in ProjectSettings/LocalizationSettings.asset
    [FilePath("ProjectSettings/LocalizationSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class LocalizationSettings : ScriptableSingleton<LocalizationSettings>
    {
        [SerializeField] private List<TextAsset> _tables = new List<TextAsset>();
        [SerializeField] private string _previewLanguage = "";
        [SerializeField] private bool _initialized;

        public IReadOnlyList<TextAsset> tables => _tables;

        public string previewLanguage
        {
            get => _previewLanguage;
            set
            {
                if (_previewLanguage == value) return;
                _previewLanguage = value;
                Save(true);
                LocalizationEditorData.NotifyChanged();
            }
        }

        // Picks up the CSV tables already in the project the first time the settings are used
        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            AddTablesFromFolder("Assets");
        }

        public void AddTable(TextAsset table)
        {
            if (table == null || _tables.Contains(table)) return;
            _tables.Add(table);
            Changed();
        }

        public void RemoveTable(TextAsset table)
        {
            if (_tables.Remove(table)) Changed();
        }

        public int AddTablesFromFolder(string folder)
        {
            int added = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".csv", System.StringComparison.OrdinalIgnoreCase) || !LooksLikeTable(path)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset != null && !_tables.Contains(asset))
                {
                    _tables.Add(asset);
                    added++;
                }
            }
            _tables = _tables.Where(t => t != null).OrderBy(t => AssetDatabase.GetAssetPath(t)).ToList();
            Changed();
            return added;
        }

        private static bool LooksLikeTable(string path)
        {
            using (var reader = new System.IO.StreamReader(path))
            {
                string header = reader.ReadLine();
                return header != null && header.TrimStart('﻿').StartsWith("key", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        private void Changed()
        {
            Save(true);
            LocalizationEditorData.NotifyChanged();
        }
    }
}
