using System.Collections.Generic;
using PG.MenuManagement;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Панель настроек для геймпада: слева разделы (TabsController), справа строки с контролами (слайдер, список, кнопки).
// Вправо с раздела - в его строки; вверх/вниз - строки; влево/вправо - значение слайдера или соседняя кнопка в строке, с края - к разделам.
// B: из строк - к разделам, с разделов - закрыть панель и вернуть курсор туда, откуда её открыли. Навигация строится сама.
public class ConsoleSettingsPanel : MonoBehaviour, IBackHandler
{
    [SerializeField] private TabsController _tabs;
    [Tooltip("Кнопки разделов по порядку вкладок; пусто - кнопки, которые вызывают SelectTab этого TabsController (у вкладки «кнопкой» часто указана первая строка раздела)")]
    [SerializeField] private Selectable[] _sectionButtons;
    [Tooltip("Что ещё сделать при закрытии (необязательно)")]
    [SerializeField] private UnityEvent _onClosed;

    private class Section
    {
        public Selectable tab;
        public GameObject root;
        public ScrollRect scroll;
        public readonly List<List<Selectable>> rows = new List<List<Selectable>>();
        public readonly Dictionary<Selectable, Selectable> rowToFirst = new Dictionary<Selectable, Selectable>();
    }

    private readonly List<Section> _sections = new List<Section>();
    private GameObject _previousSelected;
    private GameObject _last;

    private void OnEnable()
    {
        var events = EventSystem.current;
        var selected = events != null ? events.currentSelectedGameObject : null;
        _previousSelected = selected != null && !selected.transform.IsChildOf(transform) ? selected : null;
        Build();
        ConsoleMenuNavigation.Register(this);
    }

    private void OnDisable() => ConsoleMenuNavigation.Unregister(this);

    private void Update()
    {
        var events = EventSystem.current;
        var selected = events != null ? events.currentSelectedGameObject : null;
        if (selected == null || !selected.transform.IsChildOf(transform)) return;

        // Мышь выбрала саму строку - курсор на её контрол
        foreach (var section in _sections)
            if (selected.TryGetComponent(out Selectable row) && section.rowToFirst.TryGetValue(row, out var first))
            {
                events.SetSelectedGameObject(first.gameObject);
                selected = first.gameObject;
                break;
            }

        _last = selected;
        var current = Current();
        if (current?.scroll != null && selected.transform.IsChildOf(current.scroll.content)) ScrollTo(current.scroll, (RectTransform)selected.transform);
    }

    public bool HandleBack()
    {
        var events = EventSystem.current;
        var selected = events != null ? events.currentSelectedGameObject : null;
        var current = Current();

        // Из строк раздела - к кнопке раздела
        if (current != null && selected != null && selected.transform.IsChildOf(current.root.transform) && current.tab != null)
        {
            current.tab.Select();
            return true;
        }

        Close();
        return true;
    }

    public GameObject DefaultSelection
    {
        get
        {
            if (_last != null && _last.activeInHierarchy && _last.transform.IsChildOf(transform)) return _last;
            var current = Current();
            return current?.tab != null ? current.tab.gameObject : null;
        }
    }

    public void Close()
    {
        var previous = _previousSelected;
        if (TryGetComponent(out UIShowHide showHide)) showHide.Hide();
        else gameObject.SetActive(false);
        _onClosed?.Invoke();
        if (previous != null && previous.activeInHierarchy && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(previous);
    }

    // ---------- Навигация ----------

    private void Build()
    {
        _sections.Clear();
        var tabs = ConsoleMenuNavigation.TabsOf(_tabs);
        if (tabs == null) return;

        var buttons = _sectionButtons != null && _sectionButtons.Length > 0 ? new List<Selectable>(_sectionButtons) : FindSectionButtons();
        for (int i = 0; i < tabs.Length; i++)
        {
            var tab = tabs[i];
            if (tab.gameObject == null) continue;
            var button = i < buttons.Count ? buttons[i] : tab.selectableObject;
            var section = new Section { tab = button, root = tab.gameObject, scroll = tab.gameObject.GetComponentInChildren<ScrollRect>(true) };
            CollectRows(section);
            Link(section);
            _sections.Add(section);
        }
    }

    // Кнопки панели, которые открывают вкладки этого TabsController, в порядке иерархии
    private List<Selectable> FindSectionButtons()
    {
        var list = new List<Selectable>();
        foreach (var button in GetComponentsInChildren<Button>(true))
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                if (button.onClick.GetPersistentTarget(i) == _tabs && button.onClick.GetPersistentMethodName(i) == nameof(TabsController.SelectTab))
                {
                    list.Add(button);
                    break;
                }
        return list;
    }

    // Строки - дети Content прокрутки, затем контролы раздела вне прокрутки (сброс, раскладка) - каждый своей строкой
    private static void CollectRows(Section section)
    {
        if (section.scroll != null && section.scroll.content != null)
            foreach (Transform row in section.scroll.content)
            {
                var controls = Controls(row, section.scroll.content);
                if (controls.Count == 0) continue;
                section.rows.Add(controls);
                if (row.TryGetComponent(out Selectable rowSelectable) && !controls.Contains(rowSelectable)) section.rowToFirst[rowSelectable] = controls[0];
            }

        foreach (var selectable in section.root.GetComponentsInChildren<Selectable>(true))
        {
            if (section.scroll != null && selectable.transform.IsChildOf(section.scroll.transform)) continue;
            if (!IsControl(selectable)) continue;
            section.rows.Add(new List<Selectable> { selectable });
        }
    }

    private static List<Selectable> Controls(Transform row, Transform content)
    {
        var list = new List<Selectable>();
        foreach (var selectable in row.GetComponentsInChildren<Selectable>(true))
            if (selectable.transform != row && IsControl(selectable)) list.Add(selectable);
        list.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
        return list;
    }

    // Слайдеры, списки, кнопки; без полос прокрутки и пунктов внутри шаблона выпадающего списка
    private static bool IsControl(Selectable s)
    {
        if (s is Scrollbar) return false;
        var dropdown = s.GetComponentInParent<TMP_Dropdown>(true);
        if (dropdown != null && dropdown != s) return false;
        return s is Slider || s is TMP_Dropdown || s is Button || s is Toggle;
    }

    private static void Link(Section section)
    {
        var rows = section.rows;
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                var nav = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = c > 0 ? row[c - 1] : section.tab,
                    selectOnRight = c < row.Count - 1 ? row[c + 1] : null,
                    selectOnUp = r > 0 ? rows[r - 1][Mathf.Min(c, rows[r - 1].Count - 1)] : null,
                    selectOnDown = r < rows.Count - 1 ? rows[r + 1][Mathf.Min(c, rows[r + 1].Count - 1)] : null
                };
                // Слайдер меняет значение влево/вправо, только если в эту сторону нет соседа
                if (row[c] is Slider slider && (slider.direction == Slider.Direction.LeftToRight || slider.direction == Slider.Direction.RightToLeft))
                {
                    nav.selectOnLeft = null;
                    nav.selectOnRight = null;
                }
                row[c].navigation = nav;
            }
        }

        // С кнопки раздела вправо - в первую строку
        if (section.tab != null && rows.Count > 0)
        {
            var nav = section.tab.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnRight = rows[0][0];
            section.tab.navigation = nav;
        }
    }

    private Section Current()
    {
        foreach (var section in _sections)
            if (section.root != null && section.root.activeInHierarchy) return section;
        return null;
    }

    // Выбранная строка всегда видна в прокрутке
    private static void ScrollTo(ScrollRect scroll, RectTransform target)
    {
        var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        var content = scroll.content;
        Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, target);
        float top = viewport.rect.yMax, bottom = viewport.rect.yMin;
        float shift = 0f;
        if (b.max.y > top) shift = top - b.max.y;
        else if (b.min.y < bottom) shift = bottom - b.min.y;
        if (Mathf.Abs(shift) < 0.5f) return;
        content.anchoredPosition += new Vector2(0f, shift);
        scroll.velocity = Vector2.zero;
    }
}
