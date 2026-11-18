using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PG.Localization.Editor
{
    // Finds where a key is used: assets with a text field whose value is exactly the key
    public static class LocalizationUsages
    {
        public struct Usage
        {
            public string path;
            public int line;
            public string field;
        }

        private static readonly string[] Extensions = { ".prefab", ".unity", ".asset", ".playable", ".dialoguegraph", ".storygraph" };

        public static List<Usage> Find(string key)
        {
            var files = new List<string>();
            foreach (string root in new[] { "Assets", "Packages" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string normalized = file.Replace('\\', '/');
                    if (normalized.StartsWith("Assets/Plugins/") || !Extensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                    files.Add(normalized);
                }
            }

            var found = new ConcurrentBag<Usage>();
            int done = 0;
            var task = Task.Run(() => Parallel.ForEach(files, file =>
            {
                ScanFile(file, key, found);
                Interlocked.Increment(ref done);
            }));

            try
            {
                while (!task.IsCompleted)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Find usages", $"{key}  ({done}/{files.Count})", files.Count == 0 ? 1f : (float)done / files.Count))
                        break;
                    Thread.Sleep(30);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return found.OrderBy(u => u.path, StringComparer.Ordinal).ThenBy(u => u.line).ToList();
        }

        private static void ScanFile(string path, string key, ConcurrentBag<Usage> found)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { return; }
            if (!text.StartsWith("%YAML") || !text.Contains(key)) return;

            string listField = "";
            int lineNumber = 0;
            foreach (string raw in text.Split('\n'))
            {
                lineNumber++;
                string line = raw.Trim();
                bool listItem = line.StartsWith("- ");
                if (listItem) line = line.Substring(2).Trim();

                int colon = line.IndexOf(':');
                string field = colon > 0 ? line.Substring(0, colon) : null;
                string value = colon > 0 ? line.Substring(colon + 1).Trim() : line;

                if (!listItem && field != null && value.Length == 0) listField = field;
                value = Unquote(value);
                if (value != key) continue;
                found.Add(new Usage { path = path, line = lineNumber, field = listItem && field == null ? listField : field ?? listField });
            }
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
                return value.Substring(1, value.Length - 2);
            return value;
        }
    }

    // List of the places found by LocalizationUsages
    public class LocalizationUsagesWindow : EditorWindow
    {
        private string _key;
        private List<LocalizationUsages.Usage> _usages = new List<LocalizationUsages.Usage>();

        public static void Show(string key)
        {
            var usages = LocalizationUsages.Find(key);
            var window = GetWindow<LocalizationUsagesWindow>(true, "Usages", true);
            window._key = key;
            window._usages = usages;
            window.minSize = new Vector2(520, 240);
            window.Rebuild();
            window.Show();
        }

        private void CreateGUI() => Rebuild();

        private void Rebuild()
        {
            var root = rootVisualElement;
            root.Clear();
            if (_key == null) return;

            root.Add(new Label($"Key '{_key}': {_usages.Count} place(s) in {_usages.Select(u => u.path).Distinct().Count()} asset(s)")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 8, paddingTop = 6, paddingBottom = 4 }
            });
            root.Add(new Label("Exact value matches in prefabs, scenes, ScriptableObjects, Timeline and dialogue assets. Plugins are skipped.")
            {
                style = { paddingLeft = 8, paddingBottom = 4, opacity = 0.7f, whiteSpace = WhiteSpace.Normal }
            });

            var list = new ListView
            {
                itemsSource = _usages,
                fixedItemHeight = 20,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.All,
                style = { flexGrow = 1 },
                makeItem = () => new Label { style = { unityTextAlign = TextAnchor.MiddleLeft, paddingLeft = 8 } },
                bindItem = (element, i) =>
                {
                    var u = _usages[i];
                    ((Label)element).text = $"{u.path}   ·   {u.field}   (line {u.line})";
                }
            };
            list.selectionChanged += _ =>
            {
                if (list.selectedIndex < 0) return;
                var asset = AssetDatabase.LoadMainAssetAtPath(_usages[list.selectedIndex].path);
                if (asset != null) EditorGUIUtility.PingObject(asset);
            };
            list.itemsChosen += _ =>
            {
                if (list.selectedIndex < 0) return;
                var asset = AssetDatabase.LoadMainAssetAtPath(_usages[list.selectedIndex].path);
                if (asset != null) AssetDatabase.OpenAsset(asset);
            };
            root.Add(list);
        }
    }
}
