#if PG_STORY_GRAPH
// Конвертер нужен только вместе с com.pg.story-graph (ParticlesInvoker)
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PG.VFXPool.Editor
{
    // Переводит ParticlesInvoker на пул: каждая дочерняя ParticleSystem становится префабом в VFXPrefabsContainer,
    // а на её месте остаётся пустышка VFXPoolElement с тем же положением, поворотом и масштабом.
    internal static class VFXPoolConverter
    {
        private const string PrefabFolder = "Assets/Prefabs/VFX/Pooled";
        private const int DefaultPrewarm = 2;

        [MenuItem("CONTEXT/ParticlesInvoker/Convert To VFX Pool")]
        private static void ConvertComponent(MenuCommand command)
        {
            var invoker = (ParticlesInvoker)command.context;
            var log = new List<string>();
            Convert(invoker.gameObject, invoker.transform.root.name, log, useUndo: true);
            Report(invoker.gameObject.name, log);
        }

        [MenuItem("Assets/VFX Pool/Convert ParticlesInvoker In Selected Prefabs")]
        private static void ConvertSelectedPrefabs()
        {
            foreach (var path in Selection.assetGUIDs.Select(AssetDatabase.GUIDToAssetPath).Where(path => path.EndsWith(".prefab")))
            {
                Report(path, ConvertPrefabAsset(path));
            }
        }

        // Для вызова из кода (eval): конвертирует префаб-ассет и возвращает отчёт.
        public static List<string> ConvertPrefabAsset(string path)
        {
            var log = new List<string>();
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var invoker in root.GetComponentsInChildren<ParticlesInvoker>(true))
                {
                    Convert(invoker.gameObject, root.name, log, useUndo: false);
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return log;
        }

        private static void Convert(GameObject owner, string ownerName, List<string> log, bool useUndo)
        {
            var invoker = owner.GetComponent<ParticlesInvoker>();
            if (invoker == null) return;

            var container = FindContainer();
            if (container == null)
            {
                log.Add("Не найден VFXPrefabsContainer - конвертация отменена.");
                return;
            }

            var invokerSo = new SerializedObject(invoker);
            var source = invokerSo.FindProperty("_particles");
            var anchors = new List<(VFXPoolElement anchor, string name)>();

            for (int i = 0; i < source.arraySize; i++)
            {
                var element = source.GetArrayElementAtIndex(i);
                var particleSystem = element.FindPropertyRelative("particleSystem").objectReferenceValue as ParticleSystem;
                var elementName = element.FindPropertyRelative("name").stringValue;
                if (particleSystem == null)
                {
                    log.Add($"#{i} '{elementName}': пустая ParticleSystem, пропущено (индекс сохранён пустым).");
                    anchors.Add((null, elementName));
                    continue;
                }

                var id = $"{ownerName}/{elementName}";
                var prefab = SaveVfxPrefab(particleSystem, ownerName, elementName);
                RegisterInContainer(container, id, prefab);

                var anchor = CreateAnchor(particleSystem, elementName, id, useUndo);
                anchors.Add((anchor, elementName));

                int retargeted = RetargetPlayEvents(owner.transform.root, particleSystem, anchor);
                if (retargeted > 0) log.Add($"#{i} '{elementName}': UnityEvent ParticleSystem.Play -> VFXPoolElement.Play ({retargeted}).");

                if (IsReferencedElsewhere(owner.transform.root, particleSystem, invoker))
                {
                    log.Add($"#{i} '{elementName}': на ParticleSystem ссылается другой компонент - оригинал оставлен, пустышка добавлена.");
                }
                else
                {
                    RemoveOriginal(particleSystem.gameObject, useUndo, log, elementName);
                }

                log.Add($"#{i} '{elementName}' -> '{id}'");
            }

            var vfxInvoker = useUndo ? Undo.AddComponent<VFXPoolElementInvoker>(owner) : owner.AddComponent<VFXPoolElementInvoker>();
            var vfxSo = new SerializedObject(vfxInvoker);
            var target = vfxSo.FindProperty("_particles");
            target.arraySize = anchors.Count;
            for (int i = 0; i < anchors.Count; i++)
            {
                var element = target.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("vfx").objectReferenceValue = anchors[i].anchor;
                element.FindPropertyRelative("name").stringValue = anchors[i].name;
            }
            vfxSo.ApplyModifiedPropertiesWithoutUndo();

            int invokeCalls = RetargetInvokerEvents(owner.transform.root, invoker, vfxInvoker);
            if (invokeCalls > 0) log.Add($"UnityEvent ParticlesInvoker.OnInvoke -> VFXPoolElementInvoker.OnInvoke ({invokeCalls}).");

            if (useUndo) Undo.DestroyObjectImmediate(invoker);
            else Object.DestroyImmediate(invoker);
        }

        // Копия эффекта с нулевым Transform: позу и размер даёт пустышка.
        private static ParticleSystem SaveVfxPrefab(ParticleSystem particleSystem, string ownerName, string elementName)
        {
            var folder = $"{PrefabFolder}/{Sanitize(ownerName)}";
            Directory.CreateDirectory(folder);
            var path = $"{folder}/{Sanitize(elementName)}.prefab";

            var copy = Object.Instantiate(particleSystem.gameObject);
            copy.name = $"{ownerName} {elementName}";
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            copy.transform.localScale = Vector3.one;
            copy.SetActive(true);

            foreach (var system in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.playOnAwake = false;   // иначе при выдаче из пула мигнёт в старой точке до установки позы
                if (main.stopAction == ParticleSystemStopAction.Destroy) main.stopAction = ParticleSystemStopAction.None;
            }

            var saved = PrefabUtility.SaveAsPrefabAsset(copy, path);
            Object.DestroyImmediate(copy);
            return saved.GetComponent<ParticleSystem>();
        }

        private static void RegisterInContainer(VFXPrefabsContainer container, string id, ParticleSystem prefab)
        {
            float duration = 0f;
            bool looping = false;
            foreach (var system in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                looping |= main.loop;
                duration = Mathf.Max(duration, main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
            }

            var so = new SerializedObject(container);
            var list = so.FindProperty("<vfxPrefabs>k__BackingField");
            int index = -1;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == id) index = i;
            }

            if (index < 0)
            {
                index = list.arraySize;
                list.arraySize++;
            }

            var entry = list.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("id").stringValue = id;
            entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            entry.FindPropertyRelative("duration").floatValue = Mathf.Max(0.1f, duration);
            entry.FindPropertyRelative("isLooping").boolValue = looping;
            if (entry.FindPropertyRelative("prewarm").intValue <= 0) entry.FindPropertyRelative("prewarm").intValue = DefaultPrewarm;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(container);
            AssetDatabase.SaveAssetIfDirty(container);
        }

        private static VFXPoolElement CreateAnchor(ParticleSystem particleSystem, string elementName, string id, bool useUndo)
        {
            var source = particleSystem.transform;
            var anchorObject = new GameObject($"{elementName} (VFX)");
            if (useUndo) Undo.RegisterCreatedObjectUndo(anchorObject, "Convert To VFX Pool");

            var anchor = anchorObject.transform;
            anchor.SetParent(source.parent, false);
            anchor.SetSiblingIndex(source.GetSiblingIndex());
            anchor.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
            anchor.localScale = source.localScale;
            anchorObject.layer = source.gameObject.layer;

            bool follow = false;
            foreach (var system in particleSystem.GetComponentsInChildren<ParticleSystem>(true))
            {
                follow |= system.main.loop || system.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            }

            var element = anchorObject.AddComponent<VFXPoolElement>();
            var so = new SerializedObject(element);
            so.FindProperty("_vfxID").stringValue = id;
            so.FindProperty("_follow").boolValue = follow;
            so.FindProperty("_stopWithDisable").boolValue = true;   // как у дочерних частиц: выключили объект - эффект пропал
            so.FindProperty("_shapeRenderer").objectReferenceValue = FindShapeRenderer(particleSystem);
            so.ApplyModifiedPropertiesWithoutUndo();
            return element;
        }

        // Рендерер, на поверхности которого эффект рождает частицы: в префаб пула эта ссылка не попадёт.
        private static Renderer FindShapeRenderer(ParticleSystem particleSystem)
        {
            foreach (var system in particleSystem.GetComponentsInChildren<ParticleSystem>(true))
            {
                var shape = system.shape;
                if (!shape.enabled) continue;
                if (shape.shapeType == ParticleSystemShapeType.SkinnedMeshRenderer && shape.skinnedMeshRenderer != null) return shape.skinnedMeshRenderer;
                if (shape.shapeType == ParticleSystemShapeType.MeshRenderer && shape.meshRenderer != null) return shape.meshRenderer;
            }

            return null;
        }

        private static void RemoveOriginal(GameObject original, bool useUndo, List<string> log, string elementName)
        {
            // Часть чужого префаба удалить нельзя - выключаем.
            if (PrefabUtility.IsPartOfPrefabInstance(original) && !PrefabUtility.IsOutermostPrefabInstanceRoot(original))
            {
                if (useUndo) Undo.RecordObject(original, "Convert To VFX Pool");
                original.SetActive(false);
                log.Add($"'{elementName}': оригинал внутри вложенного префаба - выключен, а не удалён.");
                return;
            }

            if (useUndo) Undo.DestroyObjectImmediate(original);
            else Object.DestroyImmediate(original);
        }

        // UnityEvent с вызовом ParticlesInvoker.OnInvoke(int) (кровь по урону через HealthMob) -> тот же метод у VFXPoolElementInvoker.
        public static int RetargetInvokerEvents(Transform root, Object oldInvoker, VFXPoolElementInvoker vfxInvoker)
        {
            int count = 0;
            string newType = $"{typeof(VFXPoolElementInvoker).FullName}, {typeof(VFXPoolElementInvoker).Assembly.GetName().Name}";
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform) continue;

                var so = new SerializedObject(component);
                var property = so.GetIterator();
                bool changed = false;
                while (property.Next(true))
                {
                    if (property.name != "m_Target" || property.propertyType != SerializedPropertyType.ObjectReference) continue;

                    var call = property.propertyPath.Substring(0, property.propertyPath.LastIndexOf('.'));
                    var method = so.FindProperty(call + ".m_MethodName");
                    var typeName = so.FindProperty(call + ".m_TargetAssemblyTypeName");
                    if (method == null || method.stringValue != "OnInvoke" || typeName == null) continue;

                    // Цель - старый invoker, либо уже пустая (invoker удалён раньше, а тип в событии остался ParticlesInvoker).
                    bool pointsToOld = oldInvoker != null && property.objectReferenceValue == oldInvoker;
                    bool orphaned = property.objectReferenceValue == null && typeName.stringValue.StartsWith("ParticlesInvoker,");
                    if (!pointsToOld && !orphaned) continue;

                    property.objectReferenceValue = vfxInvoker;
                    typeName.stringValue = newType;
                    changed = true;
                    count++;
                }

                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }

            return count;
        }

        // UnityEvent, который вызывает ParticleSystem.Play() у эффекта, теперь вызывает Play() у пустышки: сигнатура та же.
        private static int RetargetPlayEvents(Transform root, ParticleSystem particleSystem, VFXPoolElement anchor)
        {
            int count = 0;
            string anchorType = $"{typeof(VFXPoolElement).FullName}, {typeof(VFXPoolElement).Assembly.GetName().Name}";   // формат Unity: "Тип, Сборка"
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform) continue;

                var so = new SerializedObject(component);
                var property = so.GetIterator();
                bool changed = false;
                while (property.Next(true))
                {
                    if (property.name != "m_Target" || property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (property.objectReferenceValue != particleSystem) continue;

                    var call = property.propertyPath.Substring(0, property.propertyPath.LastIndexOf('.'));
                    var method = so.FindProperty(call + ".m_MethodName");
                    var mode = so.FindProperty(call + ".m_Mode");
                    if (method == null || method.stringValue != "Play" || mode == null || mode.intValue != 1) continue;   // 1 - Void

                    property.objectReferenceValue = anchor;
                    so.FindProperty(call + ".m_TargetAssemblyTypeName").stringValue = anchorType;
                    changed = true;
                    count++;
                }

                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }

            return count;
        }

        // Ссылки на эффект (или его детей) из других компонентов объекта: UnityEvent, поля скриптов.
        private static bool IsReferencedElsewhere(Transform root, ParticleSystem particleSystem, ParticlesInvoker invoker)
        {
            var own = new HashSet<Object>();
            foreach (var transform in particleSystem.GetComponentsInChildren<Transform>(true))
            {
                own.Add(transform.gameObject);
                foreach (var component in transform.GetComponents<Component>()) own.Add(component);
            }

            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform || component == invoker || own.Contains(component)) continue;   // Transform родителя хранит детей - это не ссылка

                var so = new SerializedObject(component);
                var property = so.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference &&
                        property.objectReferenceValue != null && own.Contains(property.objectReferenceValue))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static VFXPrefabsContainer FindContainer()
        {
            var guid = AssetDatabase.FindAssets("t:VFXPrefabsContainer").FirstOrDefault();
            return guid == null ? null : AssetDatabase.LoadAssetAtPath<VFXPrefabsContainer>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        private static void Report(string target, List<string> log)
        {
            Debug.Log($"[VFX Pool] {target}:\n" + string.Join("\n", log));
        }
    }
}
#endif
