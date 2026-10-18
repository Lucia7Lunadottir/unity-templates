using UnityEngine;

namespace PG.SaveSystem
{
    // Trigger around a dangerous area: sets a checkpoint on enter, clears it on exit
    [RequireComponent(typeof(Collider))]
    public class CheckpointZone : MonoBehaviour
    {
        [SerializeField] private string _playerTag = "Player";
        [Tooltip("Set the checkpoint only on the first enter.")]
        [SerializeField] private bool _triggerOnce = true;

        private bool _triggered;

        private void OnTriggerEnter(Collider other)
        {
            if (_triggerOnce && _triggered) return;
            if (!other.CompareTag(_playerTag)) return;

            SaveManager saveManager = FindAnyObjectByType<SaveManager>();
            if (saveManager == null)
            {
                Debug.LogWarning("[CheckpointZone] SaveManager not found in the scene.");
                return;
            }

            saveManager.SetCheckpoint();
            _triggered = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(_playerTag)) return;

            SaveManager saveManager = FindAnyObjectByType<SaveManager>();
            saveManager?.ClearCheckpoint();
            _triggered = false;
        }

        // OnTriggerExit isn't called when the zone is disabled with the player inside
        private void OnDisable()
        {
            if (!_triggered) return;

            SaveManager saveManager = FindAnyObjectByType<SaveManager>();
            saveManager?.ClearCheckpoint();
            _triggered = false;
        }
    }
}
