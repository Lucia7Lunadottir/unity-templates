using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PG.Localization.Editor
{
    // The table open in the editor: edits, undo/redo and saving
    internal class LocalizationEditModel
    {
        private const int MaxUndo = 100;

        public readonly TextAsset asset;
        public readonly string path;
        public LocalizationTable table { get; private set; }
        public bool dirty { get; private set; }

        private readonly bool _bom;
        private readonly string _newline;
        private readonly List<LocalizationTable> _undo = new List<LocalizationTable>();
        private readonly List<LocalizationTable> _redo = new List<LocalizationTable>();

        public LocalizationEditModel(TextAsset asset)
        {
            this.asset = asset;
            path = AssetDatabase.GetAssetPath(asset);
            byte[] bytes = File.ReadAllBytes(path);
            _bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string text = new UTF8Encoding(false).GetString(bytes, _bom ? 3 : 0, bytes.Length - (_bom ? 3 : 0));
            _newline = text.Contains("\r\n") ? "\r\n" : "\n";
            table = LocalizationTable.Parse(text, asset.name);
        }

        // Puts back edits that were not saved when the scripts were reloaded
        public void RestoreUnsaved(string csv)
        {
            table = LocalizationTable.Parse(csv, asset.name);
            dirty = true;
        }

        public bool canUndo => _undo.Count > 0;
        public bool canRedo => _redo.Count > 0;

        // Call before every change so it can be undone
        public void Record()
        {
            _undo.Add(table.Clone());
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            _redo.Clear();
            dirty = true;
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            _redo.Add(table);
            table = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            dirty = true;
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            _undo.Add(table);
            table = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            dirty = true;
            return true;
        }

        public LocalizationTable.Row AddRow(string key)
        {
            Record();
            string unique = key;
            for (int i = 1; table.rows.Any(r => r.key == unique); i++) unique = $"{key}_{i}";
            var row = new LocalizationTable.Row { key = unique, values = Enumerable.Repeat("", table.languages.Count).ToArray() };
            table.rows.Add(row);
            return row;
        }

        public void DeleteRow(LocalizationTable.Row row)
        {
            Record();
            table.rows.Remove(row);
        }

        public void AddLanguage(string language)
        {
            Record();
            table.AddLanguage(language);
            foreach (var row in table.rows) row.values[table.languages.Count - 1] = "";
        }

        public void RemoveLanguage(int index)
        {
            Record();
            table.RemoveLanguage(index);
        }

        public static bool IsMissing(LocalizationTable.Row row, int language) => string.IsNullOrEmpty(row.values[language]);

        public int MissingCount(int language) => table.rows.Count(r => IsMissing(r, language));

        // Keys that appear more than once here or also exist in another table (the first one wins in the game)
        public HashSet<string> DuplicateKeys()
        {
            var counts = new Dictionary<string, int>();
            foreach (var row in table.rows) counts[row.key] = counts.TryGetValue(row.key, out int c) ? c + 1 : 1;
            var result = new HashSet<string>(counts.Where(p => p.Value > 1).Select(p => p.Key));
            foreach (LocalizationTable other in LocalizationEditorData.tables)
            {
                if (other.name == table.name) continue;
                foreach (var row in other.rows)
                    if (counts.ContainsKey(row.key)) result.Add(row.key);
            }
            return result;
        }

        public void Save()
        {
            File.WriteAllText(path, table.ToCSV(_newline), new UTF8Encoding(_bom));
            dirty = false;
            AssetDatabase.ImportAsset(path);
        }
    }
}
