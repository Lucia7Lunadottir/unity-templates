using System;
using System.Collections.Generic;
using System.IO;
using PG.CraftSystem;
using UnityEngine;

// Найденные чертежи (по имени рецепта), файл Blueprints.json в папке текущего сохранения
public static class BlueprintUnlocks
{
    private const string FileName = "Blueprints.json";

    private static readonly HashSet<string> _unlocked = new();
    private static string _loadedFrom;

    public static event Action<CraftRecipe> unlocked;

    [Serializable]
    private class SaveData { public List<string> recipes = new(); }

    public static bool Has(CraftRecipe recipe)
    {
        EnsureLoaded();
        return recipe != null && _unlocked.Contains(recipe.name);
    }

    public static void Unlock(CraftRecipe recipe)
    {
        EnsureLoaded();
        if (recipe == null || !_unlocked.Add(recipe.name)) return;
        Save();
        BuildBridge.RaiseBlueprintsChanged(_unlocked.Count);
        unlocked?.Invoke(recipe);
    }

    // Новый слот сохранения - свой набор найденных
    private static void EnsureLoaded()
    {
        var path = FilePath();
        if (path == _loadedFrom) return;
        _loadedFrom = path;
        _unlocked.Clear();
        if (!File.Exists(path)) return;
        var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
        if (data?.recipes != null) _unlocked.UnionWith(data.recipes);
    }

    private static void Save()
    {
        var path = FilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(new SaveData { recipes = new List<string>(_unlocked) }));
    }

    private static string FilePath() => Path.Combine(SaveManager.GetCurrentSaveFolderPath(), FileName);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _unlocked.Clear();
        _loadedFrom = null;
        unlocked = null;
    }
}
