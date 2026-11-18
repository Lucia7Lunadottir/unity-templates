using System;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PG.Localization.Editor
{
    // Searchable list of keys; the search matches the key and the translation text
    internal class LocalizedKeyDropdown : AdvancedDropdown
    {
        private readonly string _tableFilter;
        private readonly string _language;
        private readonly Action<string> _onPick;

        // Unity recomputes item ids from the names, so each item carries its own key
        private class KeyItem : AdvancedDropdownItem
        {
            public readonly string key;

            public KeyItem(string name, string key) : base(name)
            {
                this.key = key;
            }
        }

        public LocalizedKeyDropdown(string tableFilter, string language, Action<string> onPick) : base(new AdvancedDropdownState())
        {
            _tableFilter = tableFilter;
            _language = language;
            _onPick = onPick;
            minimumSize = new Vector2(460, 420);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Localization keys");
            root.AddChild(new KeyItem("(none)", ""));

            foreach (LocalizationTable table in LocalizationEditorData.TablesFor(_tableFilter))
            {
                var group = new AdvancedDropdownItem(table.name);
                int languageIndex = table.LanguageIndex(_language);
                foreach (LocalizationTable.Row row in table.rows)
                {
                    string text = languageIndex >= 0 ? row.values[languageIndex] : null;
                    string label = string.IsNullOrEmpty(text) ? row.key : $"{row.key}    {Shorten(text)}";
                    group.AddChild(new KeyItem(label, row.key));
                }
                root.AddChild(group);
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is KeyItem keyItem) _onPick(keyItem.key);
        }

        private static string Shorten(string text)
        {
            text = text.Replace('\n', ' ');
            return text.Length > 70 ? text.Substring(0, 70) + "…" : text;
        }
    }
}
