using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;

[AutoStaticsCleanup]
public static class SaveDataObjectsContainer
{
    [AutoStaticsCleanup]
    public static HashSet<ISaveable> saveables = new HashSet<ISaveable>();
    [AutoStaticsCleanup]
    public static  bool isInitialized;

    // Container batches every registered Lever/IceObject behind a single ISaveable entry
    // instead of one dynamicSaveables entry per instance. Called from each Lever/IceObject's
    // Start() (not Awake, and not a one-shot static ctor) so it's self-healing: SaveManager
    // clears dynamicSaveables in its own OnDestroy when a scene reloads, and HashSet.Add is
    // safe to repeat, so the registrar always ends up back in the set once that scene's
    // objects finish initializing — regardless of how many times SaveManager gets recreated.
    [AutoStaticsCleanup]
    private static readonly Registrar _registrar = new Registrar();

    public static void EnsureRegistered()
    {
        if(Application.isPlaying)
        {
            SaveManager.dynamicSaveables.Add(_registrar);
            isInitialized = true;
        }
    }

    private class Registrar : ISaveable
    {
        public void Save()
        {
            foreach (var saveable in saveables)
            {
                if (saveable == null) continue;
                saveable.Save();
            }
        }

        public void Load()
        {
            if(Application.isPlaying)
            {
                foreach (var saveable in saveables)
                {
                    if (saveable == null) continue;
                    saveable.Load();
                }
            }
        }
    }
}