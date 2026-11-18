using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace PG.Localization.Editor
{
    [CustomPropertyDrawer(typeof(LocalizedKeyAttribute))]
    public class LocalizedKeyDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return new Label($"{property.displayName}: [LocalizedKey] works on string fields only");

            string tableFilter = ((LocalizedKeyAttribute)attribute).table;
            var root = new VisualElement();

            var keyRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 1 } };
            var keyField = new TextField(property.displayName) { style = { flexGrow = 1, marginTop = 0, marginBottom = 0 } };
            keyField.BindProperty(property);
            keyField.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            var searchButton = new Button
            {
                tooltip = "Search keys",
                style = { width = 22, height = 18, flexShrink = 0, marginLeft = 2, marginRight = 0, marginTop = 0, marginBottom = 0, paddingLeft = 2, paddingRight = 2, paddingTop = 0, paddingBottom = 0 }
            };
            searchButton.Add(new Image { image = EditorGUIUtility.IconContent("Search Icon").image, scaleMode = ScaleMode.ScaleToFit, style = { width = 14, height = 14, alignSelf = Align.Center } });
            keyRow.Add(keyField);
            keyRow.Add(searchButton);
            root.Add(keyRow);

            var translationRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 0, marginBottom = 1 } };
            var spacer = new VisualElement();
            spacer.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            spacer.style.flexShrink = 0;
            var translationLabel = new Label { style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal, unityTextAlign = TextAnchor.MiddleLeft, fontSize = 12, marginLeft = 0, marginTop = 0, marginBottom = 0, paddingTop = 0, paddingBottom = 0 } };
            var languagePopup = new PopupField<string>(LocalizationEditorData.Languages(), LocalizationEditorData.PreviewLanguage())
            {
                style = { width = 150, minWidth = 110, height = 20, flexShrink = 0, marginLeft = 4, marginRight = 0, marginTop = 0, marginBottom = 0, fontSize = 12 }
            };
            languagePopup.tooltip = "Preview language";
            var createButton = new Button { text = "+ Create", tooltip = "Add this key to a localization table", style = { height = 20, flexShrink = 0, marginLeft = 4, marginRight = 0, marginTop = 0, marginBottom = 0, display = DisplayStyle.None } };
            translationRow.Add(spacer);
            translationRow.Add(translationLabel);
            translationRow.Add(createButton);
            translationRow.Add(languagePopup);
            root.Add(translationRow);

            void Refresh()
            {
                string language = LocalizationEditorData.PreviewLanguage();
                string key = property.stringValue;
                createButton.style.display = !string.IsNullOrEmpty(key) && !LocalizationEditorData.HasKey(key, tableFilter) ? DisplayStyle.Flex : DisplayStyle.None;
                if (string.IsNullOrEmpty(key))
                {
                    translationLabel.text = "—";
                    translationLabel.tooltip = "";
                    translationLabel.style.color = new StyleColor(StyleKeyword.Null);
                    translationLabel.style.unityFontStyleAndWeight = FontStyle.Normal;
                }
                else if (LocalizationEditorData.TryGetTranslation(key, language, tableFilter, out string text))
                {
                    translationLabel.text = text;
                    translationLabel.tooltip = $"{language} (double-click to open in the Localization Editor)";
                    translationLabel.style.color = new Color(0.62f, 0.78f, 0.95f);
                    translationLabel.style.unityFontStyleAndWeight = FontStyle.Normal;
                }
                else
                {
                    translationLabel.text = key;
                    translationLabel.tooltip = $"No translation for this key in {language}";
                    translationLabel.style.color = new Color(0.95f, 0.7f, 0.3f);
                    translationLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                }
            }

            createButton.clicked += () => ShowCreateMenu(property.stringValue, tableFilter);

            searchButton.clicked += () =>
            {
                var dropdown = new LocalizedKeyDropdown(tableFilter, LocalizationEditorData.PreviewLanguage(), key =>
                {
                    property.serializedObject.Update();
                    property.stringValue = key;
                    property.serializedObject.ApplyModifiedProperties();
                });
                dropdown.Show(searchButton.worldBound);
            };

            languagePopup.RegisterValueChangedCallback(e => LocalizationSettings.instance.previewLanguage = e.newValue);
            translationLabel.RegisterCallback<ClickEvent>(e =>
            {
                if (e.clickCount == 2 && !string.IsNullOrEmpty(property.stringValue)) LocalizationEditorWindow.OpenAt(property.stringValue);
            });

            void OnDataChanged()
            {
                languagePopup.choices = LocalizationEditorData.Languages();
                languagePopup.SetValueWithoutNotify(LocalizationEditorData.PreviewLanguage());
                Refresh();
            }

            keyField.RegisterValueChangedCallback(_ => Refresh());
            root.TrackPropertyValue(property, _ => Refresh());
            root.RegisterCallback<AttachToPanelEvent>(_ => LocalizationEditorData.changed += OnDataChanged);
            root.RegisterCallback<DetachFromPanelEvent>(_ => LocalizationEditorData.changed -= OnDataChanged);

            Refresh();
            return root;
        }

        private const string LastCreateTablePref = "PG.Localization.Editor.LastCreateTable";

        // Creates the key in the only matching table, or asks which table to use
        private static void ShowCreateMenu(string key, string tableFilter)
        {
            var tables = LocalizationSettings.instance.tables.Where(t => t != null && (string.IsNullOrEmpty(tableFilter) || t.name == tableFilter)).ToList();
            if (tables.Count == 0)
            {
                EditorUtility.DisplayDialog("Create key", "There is no table to add the key to. Add a CSV table in Project Settings > Localization.", "OK");
                return;
            }
            void Create(TextAsset table)
            {
                EditorPrefs.SetString(LastCreateTablePref, AssetDatabase.GetAssetPath(table));
                LocalizationEditorWindow.CreateKey(key, table);
            }
            if (tables.Count == 1)
            {
                Create(tables[0]);
                return;
            }
            string last = EditorPrefs.GetString(LastCreateTablePref, "");
            var menu = new GenericMenu();
            foreach (TextAsset table in tables.OrderByDescending(t => AssetDatabase.GetAssetPath(t) == last).ThenBy(t => t.name))
            {
                TextAsset t = table;
                menu.AddItem(new GUIContent((AssetDatabase.GetAssetPath(t) == last ? "Last used: " : "") + t.name), false, () => Create(t));
            }
            menu.ShowAsContext();
        }
    }
}
