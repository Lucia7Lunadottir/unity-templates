using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace PG.Rendering.Editor
{
    // Рисует один override (поля LodLevels/ImpostorMesh/ImpostorMaterial) для
    // TerrainPrototypeOverrideEditorBase - по одному вызову DrawRow на прототип Terrain'а. Держит
    // кэш вложенных ReorderableList'ов (LOD-уровни + части каждого уровня) - создавать заново на
    // каждый OnInspectorGUI нельзя, теряется состояние перетаскивания/выделения.
    public class TerrainLodOverrideRowDrawer
    {
        private static readonly Color[] LodColors =
        {
            new(0.56f, 0.65f, 0.16f), // LOD0 - оливковый, как у настоящего LODGroup
            new(0.16f, 0.35f, 0.52f), // LOD1 - синий
            new(0.55f, 0.35f, 0.62f), // LOD2 - фиолетовый
            new(0.75f, 0.55f, 0.15f)  // LOD3+ - оранжевый, дальше по кругу
        };
        private static readonly Color ImpostorColor = new(0.15f, 0.55f, 0.5f); // бирюзовый, чтобы не путать с LOD

        private readonly Dictionary<int, ReorderableList> _lodLists = new();
        private readonly Dictionary<(int row, int lod), ReorderableList> _partsLists = new();

        public void ClearCache()
        {
            _lodLists.Clear();
            _partsLists.Clear();
        }

        // cullRadius == null - полоску дистанций не рисуем (нет единого значения при мульти-
        // выделении компонентов с разным _cullRadius - см. TerrainPrototypeOverrideEditorBase).
        public void DrawRow(int rowIndex, SerializedProperty overrideElement, float? cullRadius)
        {
            EditorGUILayout.PropertyField(overrideElement.FindPropertyRelative("ScaleMultiplier"), new GUIContent(
                "Scale Multiplier", "Множитель поверх масштаба инстанса (widthScale/heightScale из Terrain) - " +
                "действует на рендер-масштабе и на автоопределении LOD из LODGroup. НЕ трогает ручные " +
                "LodLevels ниже - там дистанции уже финальные, в метрах. Пусто/0 = 1 (без изменений)."));

            EditorGUILayout.PropertyField(overrideElement.FindPropertyRelative("LodSizeOverride"), new GUIContent(
                "Lod Size Override", "Ручной размер для формулы screenRelativeTransitionHeight -> дистанция " +
                "(вместо bounds меша конкретного LOD-уровня) - используется при 'Convert LOD From Terrain " +
                "Prototypes' и автоопределении. Нужен, когда bounds меша (например, плоский impostor) не " +
                "отражают реальный видимый размер. 0 = автоопределение по bounds."));

            if (cullRadius.HasValue)
                DrawDistanceBar(overrideElement, cullRadius.Value);

            GetOrCreateLodList(rowIndex, overrideElement).DoLayoutList();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Импостор (дальний LOD, поверх списка выше)", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(overrideElement.FindPropertyRelative("ImpostorMesh"), new GUIContent(
                "Impostor Mesh", "Начинается сам, ровно там, где кончается последний LOD-уровень выше " +
                "(или последний автоопределённый из LODGroup) - и тянется до _cullRadius."));
            EditorGUILayout.PropertyField(overrideElement.FindPropertyRelative("ImpostorMaterial"), new GUIContent("Impostor Material"));
        }

        // Дыр в полоске не бывает: без импостора последний уровень в рантайме насильно дотягивается
        // до cullRadius (см. TerrainIndirectRendererBase.ResolveLevels), а с импостором - он сам
        // продолжает ровно от конца последнего уровня (см. AppendImpostorLevel) - тут это зеркалится.
        private void DrawDistanceBar(SerializedProperty overrideElement, float cullRadius)
        {
            if (cullRadius <= 0f) return;

            SerializedProperty lodLevelsProp = overrideElement.FindPropertyRelative("LodLevels");
            if (lodLevelsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("LOD-уровни пусты - дистанции определятся автоматически из " +
                                         "LODGroup прототипа в Play Mode, полоску заранее не построить.", MessageType.None);
                return;
            }

            // Ручные LodLevels ResolveLevels больше НЕ домножает на ScaleMultiplier/averageInstanceScale
            // (см. комментарий у ResolveLevels) - они уже хранят финальные метры, что здесь введено,
            // то и используется в рантайме. Полоска просто клампит их по cullRadius.
            var distances = new List<float>(lodLevelsProp.arraySize);
            for (int i = 0; i < lodLevelsProp.arraySize; i++)
            {
                float maxDistance = lodLevelsProp.GetArrayElementAtIndex(i).FindPropertyRelative("MaxDistance").floatValue;
                distances.Add(Mathf.Clamp(maxDistance, 0f, cullRadius));
            }
            distances.Sort();

            var segments = new List<(string label, float start, float end, Color color)>();
            float prevMax = 0f;
            for (int i = 0; i < distances.Count; i++)
            {
                if (distances[i] <= prevMax) continue; // нулевая/отрицательная ширина - дубликат дистанции, пропускаем
                segments.Add(($"LOD{i}", prevMax, distances[i], LodColors[i % LodColors.Length]));
                prevMax = distances[i];
            }
            if (segments.Count == 0) return;

            bool hasImpostor = overrideElement.FindPropertyRelative("ImpostorMesh").objectReferenceValue != null;
            if (hasImpostor)
            {
                if (prevMax < cullRadius - 0.01f)
                {
                    segments.Add(("Impostor", prevMax, cullRadius, ImpostorColor));
                }
                else
                {
                    EditorGUILayout.HelpBox($"LOD-уровни уже доходят до _cullRadius ({cullRadius:0} м) - для " +
                                             "импостора не осталось места, он не будет использован.", MessageType.Warning);
                }
            }
            else
            {
                (string label, float start, float end, Color color) last = segments[^1];
                last.end = cullRadius;
                segments[^1] = last;
            }

            Rect barRect = GUILayoutUtility.GetRect(0, 36f, GUILayout.ExpandWidth(true));
            var labelStyle = new GUIStyle(EditorStyles.whiteMiniLabel) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(4, 2, 3, 0) };
            foreach ((string label, float start, float end, Color color) in segments)
            {
                float x = barRect.x + start / cullRadius * barRect.width;
                float width = (end - start) / cullRadius * barRect.width;
                var segmentRect = new Rect(x, barRect.y, Mathf.Max(width, 1f), barRect.height);
                EditorGUI.DrawRect(segmentRect, color);
                GUI.Label(segmentRect, $"{label}\n{start:0}–{end:0} м", labelStyle);
            }
        }

        // Внешний список - LOD0/LOD1/..., каждая строка - Max Distance + вложенный список частей
        // (см. GetOrCreatePartsList). Кэшируется по rowIndex.
        private ReorderableList GetOrCreateLodList(int rowIndex, SerializedProperty overrideElement)
        {
            if (_lodLists.TryGetValue(rowIndex, out ReorderableList cached))
                return cached;

            SerializedProperty lodLevelsProp = overrideElement.FindPropertyRelative("LodLevels");
            var list = new ReorderableList(overrideElement.serializedObject, lodLevelsProp, true, true, true, true)
            {
                drawHeaderCallback = rect =>
                    EditorGUI.LabelField(rect, "LOD-уровни (пусто = автоопределение из LODGroup прототипа)"),
                elementHeightCallback = lodIndex =>
                    GetOrCreatePartsList(rowIndex, lodIndex, lodLevelsProp).GetHeight()
                    + EditorGUIUtility.singleLineHeight + 10f,
                drawElementCallback = (rect, lodIndex, isActive, isFocused) =>
                {
                    SerializedProperty lodEntry = lodLevelsProp.GetArrayElementAtIndex(lodIndex);
                    float lineHeight = EditorGUIUtility.singleLineHeight;

                    var distanceRect = new Rect(rect.x, rect.y + 2f, rect.width, lineHeight);
                    EditorGUI.PropertyField(distanceRect, lodEntry.FindPropertyRelative("MaxDistance"),
                        new GUIContent($"LOD{lodIndex} Max Distance"));

                    var partsRect = new Rect(rect.x, distanceRect.yMax + 4f, rect.width,
                        rect.height - lineHeight - 6f);
                    GetOrCreatePartsList(rowIndex, lodIndex, lodLevelsProp).DoList(partsRect);
                },
                // Список частей у каждого LOD ссылается на элемент по индексу - после add/remove/
                // reorder внешнего списка эти индексы съезжают, кэш нужно сбросить целиком.
                onAddCallback = rl => { ReorderableList.defaultBehaviours.DoAddButton(rl); _partsLists.Clear(); },
                onRemoveCallback = rl => { ReorderableList.defaultBehaviours.DoRemoveButton(rl); _partsLists.Clear(); },
                onReorderCallback = _ => _partsLists.Clear()
            };
            _lodLists[rowIndex] = list;
            return list;
        }

        // Внутренний список - строки "меш + материал" (несколько частей на один LOD, как несколько
        // Renderer'ов у LODGroup).
        private ReorderableList GetOrCreatePartsList(int rowIndex, int lodIndex, SerializedProperty lodLevelsProp)
        {
            var key = (rowIndex, lodIndex);
            if (_partsLists.TryGetValue(key, out ReorderableList cached))
                return cached;

            SerializedProperty lodEntry = lodLevelsProp.GetArrayElementAtIndex(lodIndex);
            SerializedProperty partsProp = lodEntry.FindPropertyRelative("Parts");
            var list = new ReorderableList(lodLevelsProp.serializedObject, partsProp, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Меши/материалы этого LOD-уровня"),
                elementHeightCallback = _ => EditorGUIUtility.singleLineHeight * 2f + 6f,
                drawElementCallback = (rect, partIndex, isActive, isFocused) =>
                {
                    SerializedProperty part = partsProp.GetArrayElementAtIndex(partIndex);
                    float lineHeight = EditorGUIUtility.singleLineHeight;
                    float y = rect.y + 2f;

                    EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, lineHeight),
                        part.FindPropertyRelative("Mesh"), new GUIContent("Mesh"));
                    y += lineHeight + 2f;
                    EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, lineHeight),
                        part.FindPropertyRelative("Material"), new GUIContent("Material (пусто = материал прототипа)"));
                }
            };
            _partsLists[key] = list;
            return list;
        }
    }
}
