using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

// Дублирование таймлайна вместе с привязками дорожек на всех PlayableDirector открытых сцен.
// Ссылки в клипах (камера Cinemachine Shot, объект Control Track) у копии становятся своими, а не общими с оригиналом.
public static class TimelineBindingsCopy
{
    private const string MENU = "Assets/Timeline/";

    [MenuItem(MENU + "Duplicate with bindings", false, 2000)]
    private static void DuplicateWithBindings()
    {
        var source = (TimelineAsset)Selection.activeObject;
        string path = AssetDatabase.GetAssetPath(source);
        string copyPath = AssetDatabase.GenerateUniqueAssetPath(path);
        if (!AssetDatabase.CopyAsset(path, copyPath)) return;

        var copy = AssetDatabase.LoadAssetAtPath<TimelineAsset>(copyPath);
        int directors = Copy(source, copy, true);
        Selection.activeObject = copy;
        Debug.Log($"Timeline {copy.name}: bindings copied to {directors} PlayableDirector.", copy);
    }

    [MenuItem(MENU + "Duplicate with bindings", true)]
    private static bool DuplicateValidate() => Selection.activeObject is TimelineAsset && Selection.objects.Length == 1;

    // Для уже сделанных копий: выделить копию, затем с Ctrl кликнуть оригинал (он станет активным)
    [MenuItem(MENU + "Take empty bindings from the active one", false, 2001)]
    private static void FillFromActive()
    {
        var source = (TimelineAsset)Selection.activeObject;
        foreach (var obj in Selection.objects)
        {
            if (obj is not TimelineAsset target || target == source) continue;
            int directors = Copy(source, target, false);
            Debug.Log($"Timeline {target.name}: empty bindings taken from {source.name} on {directors} PlayableDirector.", target);
        }
    }

    [MenuItem(MENU + "Take empty bindings from the active one", true)]
    private static bool FillValidate() => Selection.activeObject is TimelineAsset && Selection.objects.Length == 2
        && Array.TrueForAll(Selection.objects, o => o is TimelineAsset);

    // Возвращает, на скольких director-ах что-то скопировалось
    private static int Copy(TimelineAsset source, TimelineAsset target, bool overwrite)
    {
        var targetTracks = new Dictionary<string, TrackAsset>();
        foreach (var (key, track) in Keys(target)) targetTracks[key] = track;
        var exposed = overwrite ? RenameExposedReferences(target) : null;

        int count = 0;
        var directors = UnityEngine.Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var director in directors)
        {
            bool changed = false;
            foreach (var (key, track) in Keys(source))
            {
                var binding = director.GetGenericBinding(track);
                if (binding == null || !targetTracks.TryGetValue(key, out var targetTrack)) continue;
                if (!overwrite && director.GetGenericBinding(targetTrack) != null) continue;
                if (!changed) Undo.RecordObject(director, "Copy Timeline Bindings");
                director.SetGenericBinding(targetTrack, binding);
                changed = true;
            }

            if (exposed != null)
                foreach (var (oldName, newName) in exposed)
                {
                    var value = director.GetReferenceValue(oldName, out bool valid);
                    if (!valid) continue;
                    if (!changed) Undo.RecordObject(director, "Copy Timeline Bindings");
                    director.SetReferenceValue(newName, value);
                    changed = true;
                }

            if (!changed) continue;
            count++;
            EditorUtility.SetDirty(director);
            if (director.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
        }
        return count;
    }

    // Ключ дорожки: путь по группам + имя + тип + номер среди одноимённых
    private static IEnumerable<(string, TrackAsset)> Keys(TimelineAsset timeline)
    {
        var seen = new Dictionary<string, int>();
        foreach (var track in timeline.GetOutputTracks())
        {
            string key = track.GetType().Name + ":" + track.name;
            for (var parent = track.parent as TrackAsset; parent != null; parent = parent.parent as TrackAsset)
                key = parent.name + "/" + key;
            seen[key] = seen.TryGetValue(key, out int n) ? n + 1 : 0;
            yield return (key + "#" + seen[key], track);
        }
    }

    // Новые имена ExposedReference в клипах копии; возвращает пары (старое, новое)
    private static List<(PropertyName, PropertyName)> RenameExposedReferences(TimelineAsset timeline)
    {
        var pairs = new List<(PropertyName, PropertyName)>();
        foreach (var track in timeline.GetOutputTracks())
            foreach (var clip in track.GetClips())
            {
                if (clip.asset == null) continue;
                Rename(clip.asset, pairs, 0);
                EditorUtility.SetDirty(clip.asset);
            }
        return pairs;
    }

    private static void Rename(object owner, List<(PropertyName, PropertyName)> pairs, int depth)
    {
        if (owner == null || depth > 2) return;
        for (var type = owner.GetType(); type != null && type != typeof(UnityEngine.Object); type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var fieldType = field.FieldType;
                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(ExposedReference<>))
                {
                    object reference = field.GetValue(owner);
                    var nameField = fieldType.GetField("exposedName");
                    var oldName = (PropertyName)nameField.GetValue(reference);
                    if (PropertyName.IsNullOrEmpty(oldName)) continue;
                    var newName = new PropertyName(GUID.Generate().ToString());
                    nameField.SetValue(reference, newName);
                    field.SetValue(owner, reference);
                    pairs.Add((oldName, newName));
                }
                else if (!fieldType.IsPrimitive && !typeof(UnityEngine.Object).IsAssignableFrom(fieldType)
                         && fieldType != typeof(string) && fieldType.IsSerializable)
                {
                    object nested = field.GetValue(owner);
                    if (nested == null || nested is System.Collections.IEnumerable) continue;
                    Rename(nested, pairs, depth + 1);
                    if (fieldType.IsValueType) field.SetValue(owner, nested);
                }
            }
    }
}
