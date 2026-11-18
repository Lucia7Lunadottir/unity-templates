using UnityEngine;

// Готовый триггер для чекпоинтов в открытом мире: вешаешь на объект с Collider (Is Trigger)
// вокруг опасной зоны (арена босса, ловушка) — игрок вошёл, чекпоинт ставится сам,
// никакого ручного вызова SetCheckpoint() не нужно.
[RequireComponent(typeof(Collider))]
public class CheckpointZone : MonoBehaviour
{
    [TagDropdown]
    [SerializeField] private string _playerTag = "Player";
    [Tooltip("Если включено — зона ставит чекпоинт только один раз (первый вход).")]
    [SerializeField] private bool _triggerOnce = true;

    private bool _triggered;

    private void OnTriggerEnter(Collider other)
    {
        if (_triggerOnce && _triggered) return;
        if (!other.CompareTag(_playerTag)) return;

        SaveManager saveManager = FindAnyObjectByType<SaveManager>();
        if (saveManager == null)
        {
            Debug.LogWarning("[CheckpointZone] SaveManager не найден на сцене — чекпоинт не поставлен.");
            return;
        }

        saveManager.SetCheckpoint();
        _triggered = true;
    }

    // Опасность миновала — чекпоинт больше не нужен. Сбрасываем _triggered, чтобы
    // повторный вход снова поставил свежий чекпоинт (не только при первой попытке).
    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(_playerTag)) return;

        SaveManager saveManager = FindAnyObjectByType<SaveManager>();
        saveManager?.ClearCheckpoint();
        _triggered = false;
    }

    // Unity не зовёт OnTriggerExit, если коллайдер зоны отключается/уничтожается, пока
    // игрок всё ещё внутри (например, чанк с зоной выгружается по ходу боя) — без этого
    // чекпоинт остался бы навсегда висеть, хотя зоны уже физически не существует.
    private void OnDisable()
    {
        if (!_triggered) return;

        SaveManager saveManager = FindAnyObjectByType<SaveManager>();
        saveManager?.ClearCheckpoint();
        _triggered = false;
    }
}
