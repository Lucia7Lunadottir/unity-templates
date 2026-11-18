using UnityEditor;
using UnityEngine;

namespace PG.Rendering.Editor
{
    // Общий инспектор для TerrainTreeIndirectRenderer/TerrainGrassIndirectRenderer: под стандартными
    // полями рисует список прототипов Terrain'а, у каждого - LOD/импостор через
    // TerrainLodOverrideRowDrawer. Список строк ПОЛНОСТЬЮ автоматический - см. HelpBox в
    // OnInspectorGUI, добавлять/убирать строки руками не нужно. Поддерживает одновременное
    // редактирование нескольких выбранных компонентов (см. [CanEditMultipleObjects] на
    // наследниках) - если у них разный Terrain, честно отказывается показывать список, т.к.
    // общий набор прототипов посчитать не из чего. Не изменяет сам Terrain (TreePrototype/
    // DetailPrototype не трогаются). Наследники только говорят, как называть строки прототипов.
    public abstract class TerrainPrototypeOverrideEditorBase : UnityEditor.Editor
    {
        private SerializedProperty _terrainProp;
        private SerializedProperty _overridesProp;
        private SerializedProperty _cullRadiusProp;
        private readonly TerrainLodOverrideRowDrawer _rowDrawer = new();

        private const string FoldPrefix = "PG.TerrainOverride.Fold.";
        private static object s_clipboard; // скопированный PrototypeOverride (boxedValue)
        private static string s_clipboardName;

        protected virtual void OnEnable()
        {
            _terrainProp = serializedObject.FindProperty("_terrain");
            _overridesProp = serializedObject.FindProperty("_prototypeOverrides");
            _cullRadiusProp = serializedObject.FindProperty("_cullRadius");
            _rowDrawer.ClearCache();
        }

        protected abstract string[] GetPrototypeNames(TerrainData terrainData);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "_prototypeOverrides");

            if (targets.Length > 1 && _terrainProp.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("Выбрано несколько объектов с разным Terrain - общий список " +
                                         "прототипов посчитать не из чего. Выдели объекты с одинаковым " +
                                         "Terrain'ом, чтобы редактировать override сразу у всех, либо " +
                                         "редактируй по одному.", MessageType.Info);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            var terrain = _terrainProp.objectReferenceValue as Terrain;
            if (terrain == null) terrain = Terrain.activeTerrain;

            EditorGUILayout.Space();
            var sectionLabel = new GUIContent(targets.Length > 1
                ? $"Override прототипов - сразу ко всем {targets.Length} выделенным"
                : "Override прототипов (не трогает Terrain)",
                "Строки подстраиваются сами под прототипы Terrain'а (Paint Trees / Paint Details) - добавлять и удалять их руками не нужно.");
            if (!Foldout("section", sectionLabel, EditorStyles.foldoutHeader))
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            if (terrain == null || terrain.terrainData == null)
            {
                EditorGUILayout.HelpBox("Назначь Terrain, чтобы увидеть список прототипов.", MessageType.Info);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            string[] names = GetPrototypeNames(terrain.terrainData);
            if (_overridesProp.arraySize != names.Length)
            {
                _overridesProp.arraySize = names.Length;
                _rowDrawer.ClearCache();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Свернуть все", EditorStyles.miniButtonLeft)) SetAll(names, false);
                if (GUILayout.Button("Развернуть все", EditorStyles.miniButtonRight)) SetAll(names, true);
            }

            float? cullRadius = _cullRadiusProp.hasMultipleDifferentValues ? null : _cullRadiusProp.floatValue;
            for (int i = 0; i < names.Length; i++)
            {
                SerializedProperty element = _overridesProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(GUI.skin.box);
                bool open;
                using (new EditorGUILayout.HorizontalScope())
                {
                    string summary = IsOpen(names[i]) ? "" : "   " + Summary(element);
                    open = Foldout(names[i], new GUIContent($"[{i}] {names[i]}{summary}"), EditorStyles.foldout);
                    if (GUILayout.Button(new GUIContent("Копировать", "Скопировать override этой строки"), EditorStyles.miniButtonLeft, GUILayout.Width(80)))
                    {
                        s_clipboard = element.boxedValue;
                        s_clipboardName = names[i];
                    }
                    using (new EditorGUI.DisabledScope(s_clipboard == null))
                        if (GUILayout.Button(new GUIContent("Вставить", s_clipboard != null ? $"Вставить override из «{s_clipboardName}»" : "Сначала скопируй строку"), EditorStyles.miniButtonRight, GUILayout.Width(70)))
                        {
                            element.boxedValue = s_clipboard;
                            _rowDrawer.ClearCache();
                        }
                }
                if (open) _rowDrawer.DrawRow(i, element, cullRadius);
                EditorGUILayout.EndVertical();
            }

            serializedObject.ApplyModifiedProperties();
        }

        // Состояние сворачивания - по имени прототипа, переживает перезапуск Editor'а
        private static bool IsOpen(string key) => EditorPrefs.GetBool(FoldPrefix + key, false);

        private static bool Foldout(string key, GUIContent label, GUIStyle style)
        {
            bool open = IsOpen(key);
            bool now = EditorGUILayout.Foldout(open, label, true, style);
            if (now != open) EditorPrefs.SetBool(FoldPrefix + key, now);
            return now;
        }

        private static void SetAll(string[] names, bool open)
        {
            foreach (string name in names) EditorPrefs.SetBool(FoldPrefix + name, open);
        }

        // Кратко для свёрнутой строки: что в ней настроено руками
        private static string Summary(SerializedProperty element)
        {
            int lods = element.FindPropertyRelative("LodLevels").arraySize;
            bool impostor = element.FindPropertyRelative("ImpostorMesh").objectReferenceValue != null;
            float size = element.FindPropertyRelative("LodSizeOverride").floatValue;
            var parts = new System.Collections.Generic.List<string>();
            parts.Add(lods > 0 ? $"LOD вручную: {lods}" : "LOD авто");
            if (impostor) parts.Add("импостор");
            if (size > 0f) parts.Add($"размер {size:0.##}");
            return "(" + string.Join(", ", parts) + ")";
        }
    }
}
