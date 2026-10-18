using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace PG.SaveSystem.Editor
{
    public class GuidSaveManagerWindow : EditorWindow
    {
        private const string AllTypesLabel = "All";

        private ScrollView _container;
        private Label _statusLabel;
        private DropdownField _typeFilter;
        private Toggle _onlyMissingToggle;

        private List<ISaveableGuid> _filtered = new();

        [MenuItem("Tools/PG/Guid Save Manager")]
        public static void OpenWindow()
        {
            var window = GetWindow<GuidSaveManagerWindow>("GUID Save Manager");
            window.minSize = new Vector2(450, 300);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            // Toolbar / Header
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.marginBottom = 4;
            toolbar.style.marginTop = 4;

            var refreshButton = new Button(RefreshList) { text = "Refresh List" };
            refreshButton.style.flexGrow = 1;

            var generateAllButton = new Button(GenerateMissingGuids) { text = "Generate Missing GUIDs" };
            generateAllButton.style.flexGrow = 1;

            toolbar.Add(refreshButton);
            toolbar.Add(generateAllButton);
            root.Add(toolbar);

            // Filters
            var filterRow = new VisualElement();
            filterRow.style.flexDirection = FlexDirection.Row;
            filterRow.style.marginBottom = 8;
            filterRow.style.alignItems = Align.Center;

            _typeFilter = new DropdownField(new List<string> { AllTypesLabel }, 0);
            _typeFilter.style.flexGrow = 1;
            _typeFilter.RegisterValueChangedCallback(_ => RefreshList());
            filterRow.Add(_typeFilter);

            _onlyMissingToggle = new Toggle("Only missing GUID");
            _onlyMissingToggle.style.marginLeft = 8;
            _onlyMissingToggle.RegisterValueChangedCallback(_ => RefreshList());
            filterRow.Add(_onlyMissingToggle);

            root.Add(filterRow);

            // Status info
            _statusLabel = new Label();
            _statusLabel.style.marginBottom = 6;
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(_statusLabel);

            // Scroll view for items
            _container = new ScrollView();
            _container.style.flexGrow = 1;
            root.Add(_container);

            RefreshList();
        }

        private void RefreshList()
        {
            _container.Clear();

            // Find all objects implementing ISaveableGuid in the current scene
            var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include)
                             .OfType<ISaveableGuid>()
                             .ToList();

            RefreshTypeFilterChoices(all);

            string selectedType = _typeFilter.value;
            bool onlyMissing = _onlyMissingToggle.value;

            _filtered = all
                .Where(s => selectedType == AllTypesLabel || GetTypeName(s) == selectedType)
                .Where(s => !onlyMissing || string.IsNullOrEmpty(s.guid))
                .ToList();

            int emptyGuidsInFiltered = _filtered.Count(s => string.IsNullOrEmpty(s.guid));

            foreach (var obj in _filtered)
            {
                _container.Add(CreateRow(obj));
            }

            int totalEmpty = all.Count(s => string.IsNullOrEmpty(s.guid));
            _statusLabel.text = $"Showing: {_filtered.Count}/{all.Count} | Empty in view: {emptyGuidsInFiltered} | Empty total: {totalEmpty}";
        }

        private void RefreshTypeFilterChoices(List<ISaveableGuid> all)
        {
            var typeNames = all.Select(GetTypeName).Distinct().OrderBy(n => n).ToList();
            var choices = new List<string> { AllTypesLabel };
            choices.AddRange(typeNames);

            string previous = _typeFilter.value;
            _typeFilter.choices = choices;
            _typeFilter.value = choices.Contains(previous) ? previous : AllTypesLabel;
        }

        private static string GetTypeName(ISaveableGuid saveable) => saveable.GetType().Name;

        private VisualElement CreateRow(ISaveableGuid saveable)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;
            row.style.paddingBottom = 4;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.3f, 0.3f, 0.3f, 0.4f);

            // Type name
            var typeLabel = new Label(GetTypeName(saveable));
            typeLabel.style.width = 130;
            typeLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(typeLabel);

            // Object reference link
            var objectField = new ObjectField();
            objectField.objectType = typeof(GameObject);
            objectField.value = saveable.gameObject;
            objectField.SetEnabled(false);
            objectField.style.width = 150;
            row.Add(objectField);

            // Guid text field
            var guidField = new TextField();
            guidField.value = saveable.guid;
            guidField.style.flexGrow = 1;
            guidField.style.marginLeft = 4;
            guidField.style.marginRight = 4;

            // Update guid on value change
            guidField.RegisterValueChangedCallback(evt =>
            {
                UpdateGuid(saveable, evt.newValue);
            });
            row.Add(guidField);

            // Generate button for specific item
            var generateButton = new Button(() =>
            {
                if (saveable is Object unityObj)
                {
                    Undo.RecordObject(unityObj, "Generate GUID");
                    saveable.SetGuid();
                    MarkAsDirty(unityObj, saveable.gameObject);
                    guidField.value = saveable.guid;
                }
            })
            {
                text = "Make GUID"
            };
            generateButton.style.width = 90;
            row.Add(generateButton);

            return row;
        }

        private void GenerateMissingGuids()
        {
            foreach (var saveable in _filtered)
            {
                if (string.IsNullOrEmpty(saveable.guid) && saveable is Object unityObj)
                {
                    Undo.RecordObject(unityObj, "Generate Missing GUIDs");
                    saveable.SetGuid();
                    MarkAsDirty(unityObj, saveable.gameObject);
                }
            }

            RefreshList();
        }

        private void UpdateGuid(ISaveableGuid saveable, string newGuid)
        {
            if (saveable is Object unityObj)
            {
                Undo.RecordObject(unityObj, "Change GUID");
                saveable.guid = newGuid;
                MarkAsDirty(unityObj, saveable.gameObject);
            }
        }

        private void MarkAsDirty(Object componentObj, GameObject gameObject)
        {
            EditorUtility.SetDirty(componentObj);

            if (gameObject != null && gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
        }
    }
}
