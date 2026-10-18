using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PG.SaveSystem
{
    // Slot-based save: every slot is its own folder Saves/Save-{saveIndex}
    public partial class SaveManager : MonoBehaviour
    {
        // Saveables that can be inactive during a save register themselves here
        [AutoStaticsCleanup]
        public static HashSet<ISaveable> dynamicSaveables = new HashSet<ISaveable>();
        private Coroutine _coroutine;
        private bool _isSaving;
        [SerializeField] private int _saveTimeMinutes = 5;
        [Tooltip("Minimum time the save icon stays visible, even if writing took milliseconds.")]
        [SerializeField] private float _minIconVisibleSeconds = 0.6f;

        [SerializeField] private InputActionProperty _saveActionProperty;
        [SerializeField] private InputActionProperty _loadActionProperty;

        public static int saveIndex = 1;

        public static ISaveStorage Storage { get; set; } = new FileSaveStorage();

        public event Action saveStarted;
        public event Action saveCompleted;

        public static string GetSaveFolderPath()
        {
            return Path.Combine(Application.persistentDataPath, "Saves");
        }
        public static string GetCurrentSaveFolderPath()
        {
            string currentFolderSave = Path.Combine(GetSaveFolderPath(), $"Save-{saveIndex}");
            Storage.RecoverIfNeeded(currentFolderSave);
            Storage.EnsureDirectory(currentFolderSave);
            return currentFolderSave;
        }
        public static string GetTargetSaveFolderPath(int value, bool createIfNotExist = true)
        {
            string currentFolderSave = Path.Combine(GetSaveFolderPath(), $"Save-{value}");
            if (createIfNotExist)
            {
                Storage.RecoverIfNeeded(currentFolderSave);
                Storage.EnsureDirectory(currentFolderSave);
            }
            return currentFolderSave;
        }
        public static bool SlotExists(int index) => Storage.DirectoryExists(GetTargetSaveFolderPath(index, false));

        void Awake()
        {
            GuidListSaveUtility.ClearCache();
            WriteLastSceneName(gameObject.scene.name);
            Load();
        }

        private void OnDestroy()
        {
            dynamicSaveables.Clear();
        }

        private void OnEnable()
        {
            if (_coroutine == null)
            {
                _coroutine = StartCoroutine(OnSaving());
            }

            if (_saveActionProperty.action != null) _saveActionProperty.action.performed += SaveInput;
            if (_loadActionProperty.action != null) _loadActionProperty.action.performed += LoadInput;
        }

        void SaveInput(InputAction.CallbackContext context)
        {
            Save();
        }
        void LoadInput(InputAction.CallbackContext context)
        {
            Load();
        }

        private void OnDisable()
        {
            if (_coroutine != null)
            {
                StopCoroutine(_coroutine);
                _coroutine = null;
            }
            if (_saveActionProperty.action != null) _saveActionProperty.action.performed -= SaveInput;
            if (_loadActionProperty.action != null) _loadActionProperty.action.performed -= LoadInput;
        }
        IEnumerator OnSaving()
        {
            while (enabled)
            {
                yield return new WaitForSecondsRealtime(_saveTimeMinutes * 60);
                Save();
            }
            _coroutine = null;
        }

        // Scene objects are found automatically; implementing ISaveable is enough
        private HashSet<ISaveable> GetAllSaveables()
        {
            var all = new HashSet<ISaveable>(dynamicSaveables);
            all.UnionWith(FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include).OfType<ISaveable>());
            return all;
        }

        public void Save()
        {
            if (_isSaving) return;
            StartCoroutine(SaveRoutine());
        }

        // Synchronous write without icon delay; used by SaveRoutine and SetCheckpoint
        private void SaveAllSaveablesImmediate()
        {
            try
            {
                foreach (var saveable in GetAllSaveables())
                {
                    saveable.Save();
                }
                GuidListSaveUtility.FlushPending();
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Save failed: {e}");
            }
        }

        private IEnumerator SaveRoutine()
        {
            _isSaving = true;
            saveStarted?.Invoke();
            float startTime = Time.unscaledTime;

            SaveAllSaveablesImmediate();

            float elapsed = Time.unscaledTime - startTime;
            if (elapsed < _minIconVisibleSeconds)
            {
                yield return new WaitForSecondsRealtime(_minIconVisibleSeconds - elapsed);
            }

            saveCompleted?.Invoke();
            _isSaving = false;
        }

        public void Load()
        {
            foreach (var saveable in GetAllSaveables())
            {
                saveable.Load();
            }
        }
    }
}
