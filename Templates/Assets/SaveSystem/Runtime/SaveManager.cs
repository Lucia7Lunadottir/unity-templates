using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class SaveManager : MonoBehaviour
{
    [AutoStaticsCleanup]
    public static HashSet<ISaveable> dynamicSaveables = new HashSet<ISaveable>();
    private Coroutine _coroutine;
    private bool _isSaving;
    [SerializeField] private int _saveTimeMinutes = 5;
    [Tooltip("Минимальное время показа иконки сохранения — чтобы она была заметна, даже если запись на диск заняла миллисекунды.")]
    [SerializeField] private float _minIconVisibleSeconds = 0.6f;

    [SerializeField] private InputActionProperty _saveActionProperty;
    [SerializeField] private InputActionProperty _loadActionProperty;
    
    public static int saveIndex = 1;

    public static ISaveStorage Storage { get; set; } = new FileSaveStorage();

    public event System.Action saveStarted;
    public event System.Action saveCompleted;
    
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

        _saveActionProperty.action.performed += SaveInput;
        _loadActionProperty.action.performed += LoadInput;
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
        _saveActionProperty.action.performed -= SaveInput;
        _loadActionProperty.action.performed -= LoadInput;
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

    // Один набор без дублей: обычные объекты сцены находятся через FindObjectsByType,
    // а те, что могут быть неактивны в момент сохранения (и поэтому не найдутся так),
    // сами регистрируются в dynamicSaveables. Чтобы подключить новый сохраняемый объект,
    // достаточно реализовать ISaveable — никаких списков/полей заводить не нужно.
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

    // Синхронная запись, без корутины/иконки/минимальной задержки — использует и SaveRoutine(),
    // и SetCheckpoint() (checkpoint должен гарантированно дождаться записи в этот же кадр).
    private void SaveAllSaveablesImmediate()
    {
        try
        {
            foreach (var saveable in GetAllSaveables())
            {
                saveable.Save();
            }
            // Один реальный файл-write на файл вместо одного на каждый ISaveableGuid-объект
            // (без этого 593 PhysicsPropSaveable в одной сцене превращали сейв в O(n^2)).
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