using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Row = PG.Localization.LocalizationTable.Row;

namespace PG.Localization.Editor
{
    public class LocalizationEditorWindow : EditorWindow
    {
        private const string LastTablePref = "PG.Localization.Editor.LastTable";
        private const string HiddenPref = "PG.Localization.Editor.HiddenLanguages";
        private const string UnsavedPathKey = "PG.Localization.Editor.UnsavedPath";
        private const string UnsavedCsvKey = "PG.Localization.Editor.UnsavedCsv";
        private static readonly Color MissingTint = new Color(1f, 0.6f, 0.1f, 0.18f);
        private static readonly Color DuplicateColor = new Color(1f, 0.45f, 0.4f);

        private enum Filter { All, Missing, Duplicates }

        private class CellState
        {
            public Action<string> commit;
        }

        private LocalizationEditModel _model;
        private List<Row> _view = new List<Row>();
        private HashSet<string> _duplicates = new HashSet<string>();
        private readonly HashSet<string> _hidden = new HashSet<string>();
        private Filter _filter = Filter.All;
        private bool _saving;

        private ToolbarMenu _tableMenu;
        private ToolbarMenu _filterMenu;
        private ToolbarSearchField _search;
        private MultiColumnListView _list;
        private VisualElement _detail;
        private VisualElement _addLanguageBar;
        private TextField _addLanguageField;
        private Label _status;
        private readonly Dictionary<int, Label> _headerLabels = new Dictionary<int, Label>();
        private readonly Dictionary<int, TextField> _detailFields = new Dictionary<int, TextField>();
        private TextField _detailKeyField;
        private Row _detailRow;
        private Action<string> _languageBarAction;

        [MenuItem("Tools/PG/Localization Editor")]
        public static void Open() => GetWindow<LocalizationEditorWindow>("Localization");

        // Opens the table that contains the key and selects it
        public static void OpenAt(string key)
        {
            var window = GetWindow<LocalizationEditorWindow>("Localization");
            window.SelectKey(key);
        }

        // Adds the key to the table, saves it when nothing else was pending, and puts the cursor into the preview language
        public static void CreateKey(string key, TextAsset table)
        {
            var window = GetWindow<LocalizationEditorWindow>("Localization");
            window.CreateKeyInTable(key, table);
        }

        private void CreateKeyInTable(string key, TextAsset table)
        {
            if (_list == null) CreateGUI();
            if (_model == null || _model.asset != table)
            {
                if (!ConfirmLeave()) return;
                LoadTable(table);
            }
            bool wasClean = !_model.dirty;
            if (!_model.table.rows.Any(r => r.key == key))
            {
                _model.AddRow(key);
                hasUnsavedChanges = true;
            }
            if (wasClean) Save();
            else ShowNotification(new GUIContent($"'{key}' added, save the table to keep it"));
            SelectKey(key);
            _detail.schedule.Execute(() =>
            {
                string preview = LocalizationEditorData.PreviewLanguage();
                int language = _model == null ? -1 : _model.table.languages.IndexOf(preview);
                if (_detailFields.TryGetValue(language, out var field)) field.Focus();
            }).ExecuteLater(150);
        }

        private void OnEnable()
        {
            saveChangesMessage = "The localization table has unsaved changes.";
            foreach (string language in EditorPrefs.GetString(HiddenPref, "").Split('|'))
                if (language.Length > 0) _hidden.Add(language);
            LocalizationEditorData.changed += OnDataChanged;
            AssemblyReloadEvents.beforeAssemblyReload += StashUnsaved;
        }

        private void OnDisable()
        {
            LocalizationEditorData.changed -= OnDataChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= StashUnsaved;
        }

        // Unsaved edits live in memory, so keep them across a script reload
        private void StashUnsaved()
        {
            if (_model == null || !_model.dirty) return;
            SessionState.SetString(UnsavedPathKey, _model.path);
            SessionState.SetString(UnsavedCsvKey, _model.table.ToCSV());
        }

        public override void SaveChanges()
        {
            Save();
            base.SaveChanges();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            var toolbar = new Toolbar();
            _tableMenu = new ToolbarMenu { text = "Table", tooltip = "Choose a table" };
            toolbar.Add(_tableMenu);
            toolbar.Add(new ToolbarButton(NewTable) { text = "New", tooltip = "Create a new CSV table" });
            toolbar.Add(new ToolbarButton(Save) { text = "Save", tooltip = "Save (Ctrl+S)" });
            toolbar.Add(new ToolbarButton(Reload) { text = "Reload", tooltip = "Discard changes and read the file again" });
            toolbar.Add(new ToolbarSpacer { style = { width = 8 } });
            _search = new ToolbarSearchField { tooltip = "Search keys and translations (Ctrl+F)", style = { width = 240, flexShrink = 1 } };
            _search.RegisterValueChangedCallback(_ => ApplyFilter());
            toolbar.Add(_search);
            _filterMenu = new ToolbarMenu { text = "Show: All" };
            foreach (Filter filter in Enum.GetValues(typeof(Filter)))
            {
                Filter f = filter;
                _filterMenu.menu.AppendAction(f.ToString(), _ => { _filter = f; _filterMenu.text = "Show: " + f; ApplyFilter(); },
                    _ => _filter == f ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            toolbar.Add(_filterMenu);
            var languagesMenu = new ToolbarMenu { text = "Languages" };
            languagesMenu.RegisterCallback<PointerDownEvent>(_ => BuildLanguagesMenu(languagesMenu), TrickleDown.TrickleDown);
            toolbar.Add(languagesMenu);
            toolbar.Add(new ToolbarSpacer { style = { flexGrow = 1 } });
            toolbar.Add(new ToolbarButton(AddKey) { text = "+ Key", tooltip = "Add a new key" });
            toolbar.Add(new ToolbarButton(ShowAddLanguage) { text = "+ Language", tooltip = "Add a language column" });
            root.Add(toolbar);

            _addLanguageBar = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 6, paddingTop = 3, paddingBottom = 3, display = DisplayStyle.None } };
            _addLanguageField = new TextField("New language") { style = { width = 300 } };
            _addLanguageField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { ConfirmLanguageBar(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Escape) { _addLanguageBar.style.display = DisplayStyle.None; e.StopPropagation(); }
            });
            _addLanguageBar.Add(_addLanguageField);
            _addLanguageBar.Add(new Button(ConfirmLanguageBar) { text = "OK" });
            _addLanguageBar.Add(new Button(() => _addLanguageBar.style.display = DisplayStyle.None) { text = "Cancel" });
            root.Add(_addLanguageBar);

            _list = new MultiColumnListView
            {
                fixedItemHeight = 22,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.All,
                showBorder = true,
                horizontalScrollingEnabled = true,
                style = { flexGrow = 1 }
            };
            _list.selectionChanged += _ => ShowDetail();

            _detail = new ScrollView { style = { minHeight = 80, paddingLeft = 8, paddingRight = 8, paddingTop = 4 } };

            var split = new TwoPaneSplitView(1, 230f, TwoPaneSplitViewOrientation.Vertical) { style = { flexGrow = 1 } };
            split.Add(_list);
            split.Add(_detail);
            root.Add(split);

            _status = new Label { style = { paddingLeft = 8, paddingTop = 2, paddingBottom = 3, unityTextAlign = TextAnchor.MiddleLeft } };
            root.Add(_status);

            string last = EditorPrefs.GetString(LastTablePref, "");
            var tables = LocalizationSettings.instance.tables;
            LoadTable(tables.FirstOrDefault(t => AssetDatabase.GetAssetPath(t) == last) ?? tables.FirstOrDefault());
        }

        // ---- loading and structure -------------------------------------------------------------

        private void LoadTable(TextAsset asset)
        {
            LocalizationEditorData.tables.GetHashCode();
            _model = asset == null ? null : new LocalizationEditModel(asset);
            hasUnsavedChanges = false;
            if (_model != null && SessionState.GetString(UnsavedPathKey, "") == _model.path)
            {
                _model.RestoreUnsaved(SessionState.GetString(UnsavedCsvKey, ""));
                hasUnsavedChanges = true;
            }
            if (SessionState.GetString(UnsavedPathKey, "") != "" && (_model == null || SessionState.GetString(UnsavedPathKey, "") == _model.path))
            {
                SessionState.EraseString(UnsavedPathKey);
                SessionState.EraseString(UnsavedCsvKey);
            }
            if (asset != null) EditorPrefs.SetString(LastTablePref, _model.path);
            RebuildTableMenu();
            RebuildAll();
        }

        private void RebuildTableMenu()
        {
            _tableMenu.menu.ClearItems();
            foreach (TextAsset table in LocalizationSettings.instance.tables)
            {
                TextAsset t = table;
                _tableMenu.menu.AppendAction(t.name, _ => SwitchTable(t),
                    _ => _model != null && _model.asset == t ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            _tableMenu.text = _model != null ? _model.asset.name : "Table";
        }

        private void SwitchTable(TextAsset asset)
        {
            if (!ConfirmLeave()) return;
            LoadTable(asset);
        }

        // Asks what to do with unsaved changes, false means the user cancelled
        private bool ConfirmLeave()
        {
            if (_model == null || !_model.dirty) return true;
            int choice = EditorUtility.DisplayDialogComplex("Unsaved changes", $"Save changes to {_model.asset.name}?", "Save", "Cancel", "Discard");
            if (choice == 0) Save();
            return choice != 1;
        }

        private void RebuildAll()
        {
            _duplicates = _model != null ? _model.DuplicateKeys() : new HashSet<string>();
            // Drop the old rows first so no cell is bound to a row of the previous table state
            _view = new List<Row>();
            _list.itemsSource = _view;
            BuildColumns();
            ApplyFilter();
            RefreshStatus();
        }

        private void BuildColumns()
        {
            _list.columns.Clear();
            _headerLabels.Clear();
            if (_model == null) return;

            _list.columns.Add(new Column
            {
                name = "key", title = "key", width = 230, minWidth = 100,
                makeCell = MakeCell,
                bindCell = (element, i) => BindCell(element, _view[i], -1)
            });
            for (int li = 0; li < _model.table.languages.Count; li++)
            {
                int language = li;
                _list.columns.Add(new Column
                {
                    name = "lang" + li, title = _model.table.languages[li], width = 240, minWidth = 80,
                    visible = !_hidden.Contains(_model.table.languages[li]),
                    makeHeader = () => MakeLanguageHeader(language),
                    bindHeader = _ => { },
                    makeCell = MakeCell,
                    bindCell = (element, i) => BindCell(element, _view[i], language)
                });
            }
            RefreshHeaders();
        }

        private void RefreshHeaders()
        {
            if (_model == null) return;
            foreach (var pair in _headerLabels)
            {
                if (pair.Key >= _model.table.languages.Count) continue;
                int missing = _model.MissingCount(pair.Key);
                pair.Value.text = missing > 0 ? $"{_model.table.languages[pair.Key]}  ⚠ {missing}" : _model.table.languages[pair.Key];
            }
        }

        // Header of a language column: name with the number of missing translations and a menu
        private VisualElement MakeLanguageHeader(int language)
        {
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexGrow = 1 } };
            var label = new Label { style = { flexGrow = 1, flexShrink = 1, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis, unityFontStyleAndWeight = FontStyle.Bold } };
            var menuButton = new Button { text = "⋮", tooltip = "Rename, hide or remove this language", style = { width = 20, height = 16, flexShrink = 0, marginLeft = 2, marginRight = 2, marginTop = 0, marginBottom = 0, paddingLeft = 0, paddingRight = 0 } };
            menuButton.clicked += () => ShowLanguageMenu(language);
            label.RegisterCallback<ClickEvent>(e => { if (e.clickCount == 2) RenameLanguage(language); });
            header.Add(label);
            header.Add(menuButton);
            _headerLabels[language] = label;
            RefreshHeaders();
            return header;
        }

        private void ShowLanguageMenu(int language)
        {
            string name = _model.table.languages[language];
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename..."), false, () => RenameLanguage(language));
            menu.AddItem(new GUIContent("Hide"), false, () => ToggleLanguage(name));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Remove"), false, () => RemoveLanguage(name));
            menu.ShowAsContext();
        }

        private void RenameLanguage(int language)
        {
            string old = _model.table.languages[language];
            ShowLanguageBar($"Rename '{old}'", old, newName =>
            {
                if (newName == old) return;
                if (_model.table.languages.Contains(newName)) { ShowNotification(new GUIContent($"'{newName}' already exists")); return; }
                if (!EditorUtility.DisplayDialog("Rename language", $"The game looks languages up by this exact name (for example currentLanguage and the saved language). Rename '{old}' to '{newName}'?", "Rename", "Cancel")) return;
                _model.Record();
                _model.table.languages[language] = newName;
                if (_hidden.Remove(old)) _hidden.Add(newName);
                hasUnsavedChanges = true;
                RebuildAll();
            });
        }

        // ---- cells ------------------------------------------------------------------------------

        private VisualElement MakeCell()
        {
            var field = new TextField { isDelayed = true };
            field.style.marginTop = 0; field.style.marginBottom = 0; field.style.marginLeft = 0; field.style.marginRight = 0;
            field.style.height = Length.Percent(100);
            var state = new CellState();
            field.userData = state;
            field.RegisterValueChangedCallback(e => state.commit?.Invoke(e.newValue));
            return field;
        }

        // language -1 is the key column
        private void BindCell(VisualElement element, Row row, int language)
        {
            var field = (TextField)element;
            var state = (CellState)field.userData;
            var input = field.Q(className: TextField.inputUssClassName);
            if (language < 0)
            {
                state.commit = v => OnKeyEdited(row, v);
                field.SetValueWithoutNotify(row.key);
                field.style.color = _duplicates.Contains(row.key) ? DuplicateColor : new StyleColor(StyleKeyword.Null);
                field.tooltip = _duplicates.Contains(row.key) ? "This key exists more than once (the first one wins in the game)" : "";
                if (input != null) input.style.backgroundColor = new StyleColor(StyleKeyword.Null);
            }
            else
            {
                if (language >= row.values.Length) return;
                state.commit = v => OnValueEdited(row, language, v);
                field.SetValueWithoutNotify(row.values[language] ?? "");
                field.style.color = new StyleColor(StyleKeyword.Null);
                field.tooltip = LocalizationEditModel.IsMissing(row, language) ? "No translation" : "";
                if (input != null)
                    input.style.backgroundColor = LocalizationEditModel.IsMissing(row, language) ? MissingTint : new StyleColor(StyleKeyword.Null);
            }
        }

        // ---- edits --------------------------------------------------------------------------------

        private void OnValueEdited(Row row, int language, string value)
        {
            if ((row.values[language] ?? "") == value) return;
            _model.Record();
            row.values[language] = value;
            AfterEdit(false);
        }

        private void OnKeyEdited(Row row, string value)
        {
            value = value.Trim();
            if (value.Length == 0 || value == row.key)
            {
                _list.RefreshItems();
                return;
            }
            _model.Record();
            row.key = value;
            AfterEdit(true);
        }

        private void AfterEdit(bool keysChanged)
        {
            hasUnsavedChanges = true;
            if (keysChanged) _duplicates = _model.DuplicateKeys();
            _list.RefreshItems();
            RefreshHeaders();
            RefreshStatus();
            ShowDetail();
        }

        private void AddKey()
        {
            if (_model == null) return;
            Row row = _model.AddRow("new_key");
            _search.SetValueWithoutNotify("");
            _filter = Filter.All;
            _filterMenu.text = "Show: All";
            hasUnsavedChanges = true;
            _duplicates = _model.DuplicateKeys();
            ApplyFilter();
            int index = _view.IndexOf(row);
            _list.ScrollToItemById(_list.viewController.GetIdForIndex(index));
            _list.SetSelection(index);
            _detail.schedule.Execute(() => _detail.Q<TextField>("detail-key")?.Focus()).ExecuteLater(50);
        }

        private void DeleteKey(Row row)
        {
            if (!_model.table.rows.Contains(row))
            {
                ShowDetail(true);
                return;
            }
            _model.DeleteRow(row);
            hasUnsavedChanges = true;
            _duplicates = _model.DuplicateKeys();
            ApplyFilter();
        }

        private void ShowAddLanguage()
        {
            if (_model == null) return;
            var known = LocalizationEditorData.Languages().Where(l => !_model.table.languages.Contains(l)).ToList();
            var menu = new GenericMenu();
            foreach (string language in known) menu.AddItem(new GUIContent("Add " + language), false, () => AddLanguage(language));
            if (known.Count > 0) menu.AddSeparator("");
            menu.AddItem(new GUIContent("New language..."), false, () => ShowLanguageBar("New language", "", AddLanguage));
            menu.ShowAsContext();
        }

        private void ShowLanguageBar(string label, string value, Action<string> onConfirm)
        {
            _languageBarAction = onConfirm;
            _addLanguageField.label = label;
            _addLanguageBar.style.display = DisplayStyle.Flex;
            _addLanguageField.value = value;
            _addLanguageField.Focus();
            _addLanguageField.SelectAll();
        }

        private void ConfirmLanguageBar()
        {
            string value = _addLanguageField.value.Trim();
            if (value.Length == 0) return;
            _addLanguageBar.style.display = DisplayStyle.None;
            _languageBarAction?.Invoke(value);
        }

        private void AddLanguage(string language)
        {
            if (_model.table.languages.Contains(language)) return;
            _model.AddLanguage(language);
            hasUnsavedChanges = true;
            RebuildAll();
            ShowDetail(true);
        }

        private void BuildLanguagesMenu(ToolbarMenu button)
        {
            button.menu.ClearItems();
            if (_model == null) return;
            foreach (string language in _model.table.languages)
            {
                string l = language;
                button.menu.AppendAction("Visible/" + l, _ => ToggleLanguage(l),
                    _ => _hidden.Contains(l) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Checked);
            }
            button.menu.AppendSeparator();
            foreach (string language in _model.table.languages)
            {
                string l = language;
                button.menu.AppendAction("Remove language/" + l, _ => RemoveLanguage(l));
            }
        }

        private void ToggleLanguage(string language)
        {
            if (!_hidden.Remove(language)) _hidden.Add(language);
            EditorPrefs.SetString(HiddenPref, string.Join("|", _hidden));
            BuildColumns();
            ApplyFilter();
            ShowDetail(true);
        }

        private void RemoveLanguage(string language)
        {
            if (!EditorUtility.DisplayDialog("Remove language", $"Remove the column '{language}' from {_model.asset.name}? You can undo this with Ctrl+Z.", "Remove", "Cancel")) return;
            _model.RemoveLanguage(_model.table.languages.IndexOf(language));
            hasUnsavedChanges = true;
            RebuildAll();
            ShowDetail(true);
        }

        // ---- filtering and detail ---------------------------------------------------------------

        private bool IsVisible(int language) => !_hidden.Contains(_model.table.languages[language]);

        private bool Matches(Row row)
        {
            string search = _search.value;
            if (!string.IsNullOrEmpty(search))
            {
                bool found = row.key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                for (int i = 0; i < _model.table.languages.Count && !found; i++)
                    found = IsVisible(i) && (row.values[i] ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!found) return false;
            }
            switch (_filter)
            {
                case Filter.Missing:
                    for (int i = 0; i < _model.table.languages.Count; i++)
                        if (IsVisible(i) && LocalizationEditModel.IsMissing(row, i)) return true;
                    return false;
                case Filter.Duplicates:
                    return _duplicates.Contains(row.key);
                default:
                    return true;
            }
        }

        private void ApplyFilter()
        {
            if (_list == null) return;
            string selectedKey = (_list.selectedItem as Row)?.key;
            _view = _model == null ? new List<Row>() : _model.table.rows.Where(Matches).ToList();
            _list.itemsSource = _view;
            _list.RefreshItems();
            if (selectedKey != null)
            {
                int index = _view.FindIndex(r => r.key == selectedKey);
                if (index >= 0) _list.SetSelectionWithoutNotify(new[] { index });
            }
            RefreshStatus();
            ShowDetail();
        }

        private void SelectKey(string key)
        {
            if (_model != null && _model.table.rows.Any(r => r.key == key))
            {
                SelectInCurrentTable(key);
                return;
            }
            foreach (TextAsset asset in LocalizationSettings.instance.tables)
            {
                var table = LocalizationEditorData.tables.FirstOrDefault(t => t.name == asset.name);
                if (table == null || !table.rows.Any(r => r.key == key)) continue;
                if (!ConfirmLeave()) return;
                LoadTable(asset);
                SelectInCurrentTable(key);
                return;
            }
        }

        private void SelectInCurrentTable(string key)
        {
            _search.SetValueWithoutNotify("");
            _filter = Filter.All;
            _filterMenu.text = "Show: All";
            ApplyFilter();
            int index = _view.FindIndex(r => r.key == key);
            if (index < 0) return;
            _list.schedule.Execute(() =>
            {
                _list.ScrollToItemById(_list.viewController.GetIdForIndex(index));
                _list.SetSelection(index);
            }).ExecuteLater(50);
        }

        // Rebuilds the panel only when another key is selected (or force), otherwise values are updated in place
        // so a button under the pointer is never destroyed by the edit that its click caused
        private void ShowDetail(bool force = false)
        {
            if (_detail == null) return;
            var row = _model == null ? null : _list.selectedItem as Row;
            if (!force && row != null && row == _detailRow && _model.table.rows.Contains(row))
            {
                RefreshDetail();
                return;
            }

            _detail.Clear();
            _detailFields.Clear();
            _detailKeyField = null;
            _detailRow = null;
            if (_model == null)
            {
                _detail.Add(new Label("No table. Use New to create one, or add CSV tables in Project Settings > Localization."));
                return;
            }
            if (row == null)
            {
                _detail.Add(new Label("Select a key to edit its translations here."));
                return;
            }

            _detailRow = row;
            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
            _detailKeyField = new TextField("Key") { name = "detail-key", isDelayed = true, value = row.key, style = { flexGrow = 1 } };
            _detailKeyField.RegisterValueChangedCallback(e => OnKeyEdited(row, e.newValue));
            header.Add(_detailKeyField);
            header.Add(new Button(() => LocalizationUsagesWindow.Show(row.key)) { text = "Find usages", tooltip = "Find assets that use this key", style = { marginLeft = 6 } });
            header.Add(new Button(() => DeleteKey(row)) { text = "Delete key", style = { marginLeft = 6 } });
            _detail.Add(header);

            for (int li = 0; li < _model.table.languages.Count; li++)
            {
                if (!IsVisible(li)) continue;
                int language = li;
                var field = new TextField(_model.table.languages[li]) { isDelayed = true, multiline = true, value = row.values[li] ?? "" };
                field.style.whiteSpace = WhiteSpace.Normal;
                field.style.minHeight = 22;
                field.RegisterValueChangedCallback(e => OnValueEdited(row, language, e.newValue));
                _detailFields[li] = field;
                _detail.Add(field);
            }
            RefreshDetail();
        }

        private static bool HasFocus(TextField field)
        {
            var focused = field.panel?.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == field || focused.GetFirstAncestorOfType<TextField>() == field);
        }

        private void RefreshDetail()
        {
            if (_detailRow == null) return;
            if (_detailKeyField != null && !HasFocus(_detailKeyField)) _detailKeyField.SetValueWithoutNotify(_detailRow.key);
            foreach (var pair in _detailFields)
            {
                var field = pair.Value;
                if (!HasFocus(field)) field.SetValueWithoutNotify(_detailRow.values[pair.Key] ?? "");
                field.labelElement.style.color = LocalizationEditModel.IsMissing(_detailRow, pair.Key)
                    ? new StyleColor(new Color(0.95f, 0.7f, 0.3f)) : new StyleColor(StyleKeyword.Null);
            }
        }

        private void RefreshStatus()
        {
            if (_status == null) return;
            if (_model == null) { _status.text = "No table"; return; }
            var table = _model.table;
            var missing = Enumerable.Range(0, table.languages.Count).Select(i => (language: table.languages[i], count: _model.MissingCount(i))).Where(p => p.count > 0);
            string missingText = missing.Any() ? string.Join(", ", missing.Select(p => $"{p.language} {p.count}")) : "none";
            _status.text = $"{table.rows.Count} keys  ·  shown {_view.Count}  ·  missing: {missingText}  ·  duplicates: {_duplicates.Count}" + (_model.dirty ? "  ·  ● unsaved" : "");
        }

        // ---- file operations --------------------------------------------------------------------

        private void Save()
        {
            if (_model == null) return;
            _saving = true;
            try { _model.Save(); }
            finally { _saving = false; }
            hasUnsavedChanges = false;
            RefreshStatus();
        }

        private void Reload()
        {
            if (_model == null) return;
            if (_model.dirty && !EditorUtility.DisplayDialog("Reload", "Discard unsaved changes and read the file again?", "Discard", "Cancel")) return;
            LoadTable(_model.asset);
        }

        private void NewTable()
        {
            if (!ConfirmLeave()) return;
            string path = EditorUtility.SaveFilePanelInProject("New localization table", "NewTable", "csv", "", "Assets");
            if (string.IsNullOrEmpty(path)) return;
            var languages = _model != null ? _model.table.languages : new List<string> { "Russian", "English" };
            File.WriteAllText(path, "key," + string.Join(",", languages.Select(LocalizationTable.EscapeField)) + "\n");
            AssetDatabase.ImportAsset(path);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            LocalizationSettings.instance.AddTable(asset);
            LoadTable(asset);
        }

        private void OnDataChanged()
        {
            if (_saving || _list == null) return;
            RebuildTableMenu();
            if (_model != null && !_model.dirty) LoadTable(_model.asset);
        }

        // ---- keyboard ---------------------------------------------------------------------------

        private void OnKeyDown(KeyDownEvent e)
        {
            if (!(e.ctrlKey || e.commandKey)) return;
            switch (e.keyCode)
            {
                case KeyCode.S:
                    Save();
                    break;
                case KeyCode.Z when !e.shiftKey:
                    ApplyHistory(_model != null && _model.Undo());
                    break;
                case KeyCode.Y:
                case KeyCode.Z when e.shiftKey:
                    ApplyHistory(_model != null && _model.Redo());
                    break;
                case KeyCode.F:
                    _search.Focus();
                    break;
                default:
                    return;
            }
            e.StopPropagation();
        }

        private void ApplyHistory(bool changed)
        {
            if (!changed) return;
            hasUnsavedChanges = true;
            RebuildAll();
        }
    }
}
