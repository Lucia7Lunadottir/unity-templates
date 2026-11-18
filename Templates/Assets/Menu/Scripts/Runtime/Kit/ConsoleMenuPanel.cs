using PG.MenuManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Обычная панель меню (инвентарь, крафт, одежда) как уровень консольного меню: без своего кода.
// Курсор возвращается на последний выбранный в панели пункт (или первый доступный), B - шаг назад к меню.
public class ConsoleMenuPanel : MonoBehaviour, IBackHandler
{
    [Tooltip("Куда ставить курсор при первом открытии; пусто - первый доступный пункт панели")]
    [SerializeField] private Selectable _firstSelected;

    private GameObject _last;

    private void OnEnable() => ConsoleMenuNavigation.Register(this);
    private void OnDisable() => ConsoleMenuNavigation.Unregister(this);

    // Запоминаем, где был курсор внутри панели
    private void Update()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && selected.transform.IsChildOf(transform)) _last = selected;
    }

    public bool HandleBack() => false;

    public GameObject DefaultSelection
    {
        get
        {
            if (Usable(_last)) return _last;
            if (_firstSelected != null && Usable(_firstSelected.gameObject)) return _firstSelected.gameObject;
            foreach (var selectable in GetComponentsInChildren<Selectable>())
                if (Usable(selectable.gameObject)) return selectable.gameObject;
            return null;
        }
    }

    private static bool Usable(GameObject go) =>
        go != null && go.activeInHierarchy && go.TryGetComponent(out Selectable s) && s.IsInteractable();
}
