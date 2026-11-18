using PG.MenuManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

// Всплывающая панель меню (действия с предметом и т.п.): B / кружок / Esc закрывает её
// и возвращает курсор туда, где он был до открытия. Настраивать кнопки не нужно.
public class BackClosesPanel : MonoBehaviour, IBackHandler
{
    [Tooltip("Что ещё сделать при закрытии (необязательно)")]
    [SerializeField] private UnityEvent _onClosed;

    private GameObject _previousSelected;

    private void OnEnable()
    {
        var events = EventSystem.current;
        var selected = events != null ? events.currentSelectedGameObject : null;
        // Если панель открыли кнопкой внутри неё самой - запоминать нечего
        _previousSelected = selected != null && !selected.transform.IsChildOf(transform) ? selected : null;
        ConsoleMenuNavigation.Register(this);
    }

    private void OnDisable() => ConsoleMenuNavigation.Unregister(this);

    // Курсор потерялся - остаётся внутри панели (первый доступный пункт)
    public GameObject DefaultSelection
    {
        get
        {
            foreach (var selectable in GetComponentsInChildren<UnityEngine.UI.Selectable>())
                if (selectable.IsInteractable() && selectable.navigation.mode != UnityEngine.UI.Navigation.Mode.None) return selectable.gameObject;
            return null;
        }
    }

    public bool HandleBack()
    {
        var previous = _previousSelected;
        if (TryGetComponent(out UIShowHide showHide)) showHide.Hide();
        else gameObject.SetActive(false);
        _onClosed?.Invoke();

        if (previous != null && previous.activeInHierarchy && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(previous);
        return true;
    }
}
