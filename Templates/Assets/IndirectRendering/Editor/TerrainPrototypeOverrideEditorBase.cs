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
            EditorGUILayout.LabelField(targets.Length > 1
                ? $"Override прототипов (не трогает Terrain) - применится сразу ко всем {targets.Length} выделенным"
                : "Override прототипов (не трогает Terrain)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Список строк ниже подстраивается САМ под фактический список прототипов " +
                                     "Terrain'а - добавил/удалил дерево в Paint Trees (или detail-слой в Paint " +
                                     "Details) - соответствующая строка появляется/пропадает здесь автоматически. " +
                                     "Вручную добавлять или удалять строки не нужно и нельзя.", MessageType.Info);

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

            float? cullRadius = _cullRadiusProp.hasMultipleDifferentValues ? null : _cullRadiusProp.floatValue;
            for (int i = 0; i < names.Length; i++)
            {
                SerializedProperty element = _overridesProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.LabelField($"[{i}] {names[i]}", EditorStyles.boldLabel);
                _rowDrawer.DrawRow(i, element, cullRadius);
                EditorGUILayout.EndVertical();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
