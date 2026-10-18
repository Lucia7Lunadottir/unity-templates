using UnityEngine;
using UnityEngine.Events;

namespace PG.SaveSystem
{
    // Hook the save icon show/hide (e.g. UIShowHide.Show/Hide) to these events
    public class SaveIconUI : MonoBehaviour
    {
        [SerializeField] private SaveManager _saveManager;
        [SerializeField] private UnityEvent _saveStarted;
        [SerializeField] private UnityEvent _saveCompleted;

        private void OnEnable()
        {
            if (_saveManager == null) _saveManager = FindAnyObjectByType<SaveManager>();
            if (_saveManager == null) return;
            _saveManager.saveStarted += OnSaveStarted;
            _saveManager.saveCompleted += OnSaveCompleted;
        }

        private void OnDisable()
        {
            if (_saveManager == null) return;
            _saveManager.saveStarted -= OnSaveStarted;
            _saveManager.saveCompleted -= OnSaveCompleted;
        }

        private void OnSaveStarted() => _saveStarted?.Invoke();
        private void OnSaveCompleted() => _saveCompleted?.Invoke();
    }
}
