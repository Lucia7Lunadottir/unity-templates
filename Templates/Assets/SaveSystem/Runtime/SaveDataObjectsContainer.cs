using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace PG.SaveSystem
{
    // Batches many small saveables behind one SaveManager entry; call EnsureRegistered() from their Start()
    [AutoStaticsCleanup]
    public static class SaveDataObjectsContainer
    {
        [AutoStaticsCleanup]
        public static HashSet<ISaveable> saveables = new HashSet<ISaveable>();
        [AutoStaticsCleanup]
        public static bool isInitialized;

        [AutoStaticsCleanup]
        private static readonly Registrar _registrar = new Registrar();

        public static void EnsureRegistered()
        {
            SaveManager.dynamicSaveables.Add(_registrar);
            isInitialized = true;
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
                foreach (var saveable in saveables)
                {
                    if (saveable == null) continue;
                    saveable.Load();
                }
            }
        }
    }
}
