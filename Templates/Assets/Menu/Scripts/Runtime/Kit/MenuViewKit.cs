using PG.MenuManagement;
using System;
using System.Collections.Generic;
using PG.Localization;
using PG.Tween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Общее для экранов меню в стиле макетов (Assets/UI Mockups/Inventory, Menu.uss).
// Вид экрана (CraftMenuView, ItemsMenuView, …) только показывает чужие данные и вызывает чужие методы; всё повторяющееся - здесь:
//   LocalText  - текст с ключом локализации для полей инспектора;
//   MenuKit    - помощники видов во время игры (курсор, подсветка строк и ячеек, прокрутка, навигация);
//   MenuLayout - кирпичики для ПКМ «Собрать разметку по макету» (только в редакторе).

public static class MenuKit
{
    // Цвета макета; проект в Linear color space - полупрозрачное в игре плотнее, чем в макете, поэтому альфы маленькие
    public static readonly Color Accent = new Color32(0x77, 0xA7, 0xFF, 0xFF);
    public static readonly Color Text = new Color32(0xEC, 0xDF, 0xC4, 0xFF);
    public static readonly Color TextSoft = new Color32(0xB9, 0xAB, 0x92, 0xFF);
    public static readonly Color TextDim = new Color32(0x8A, 0x82, 0x75, 0xFF);
    public static readonly Color OnAccent = new Color32(0x1A, 0x16, 0x22, 0xFF);
    public static readonly Color Missing = new Color32(0xFF, 0x5A, 0x4A, 0xFF);
    public static readonly Color Ok = new Color32(0x9C, 0xC9, 0x7A, 0xFF);
    public static readonly Color SelectedFill = new Color(119 / 255f, 167 / 255f, 1f, 0.12f);
    public static readonly Color ChipIdle = new Color(1f, 1f, 1f, 0.015f);
    public static readonly Color Slot = new Color(1f, 1f, 1f, 0.02f);
    public static readonly Color Clear = new Color(1f, 1f, 1f, 0f);
    // Под курсором: залитая кнопка - Highlighted кнопок меню (#37A4FF), обычная - голубая плашка, как у открытой вкладки
    public static readonly Color Highlighted = new Color32(0x37, 0xA4, 0xFF, 0xFF);
    public static readonly Color LitPlate = new Color(119 / 255f, 167 / 255f, 1f, 0.35f);

    // ---------- Курсор ----------

    public static GameObject Selected => EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

    public static void Select(GameObject target)
    {
        if (target != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(target);
    }

    // ---------- Заполнение шаблонов ----------

    public static TMP_Text SetText(Transform root, string child, string value)
    {
        var text = root.Find(child)?.GetComponent<TMP_Text>();
        if (text != null) text.text = value;
        return text;
    }

    public static void SetImage(Transform root, string child, Sprite sprite) => ShowIcon(root.Find(child)?.GetComponent<Image>(), sprite);

    // Иконка без растягивания; нет спрайта - картинка скрыта
    public static void ShowIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
        image.preserveAspect = true;
    }

    // Полоска-заполнение: Fill растянут по якорям, t = 0..1
    public static void SetFill(RectTransform fill, float t)
    {
        if (fill != null) fill.anchorMax = new Vector2(Mathf.Clamp01(t), fill.anchorMax.y);
    }

    // Заполнение от a до b: так рисуется «сколько добавится» поверх текущего значения
    public static void SetFillRange(RectTransform fill, float a, float b)
    {
        if (fill == null) return;
        fill.anchorMin = new Vector2(Mathf.Clamp01(a), fill.anchorMin.y);
        fill.anchorMax = new Vector2(Mathf.Clamp01(b), fill.anchorMax.y);
    }

    // ---------- Подсветка ----------

    // Строка списка: под курсором - полоска Bar слева, голубой фон и белый текст; под мышью - только голубой фон
    public static void PaintRow(Selectable row, bool on, string labelChild, Color idleLabel, Color? litFill = null)
    {
        if (row == null) return;
        var press = Press.Attach(row.gameObject, 0.985f, () => PaintRow(row, on, labelChild, idleLabel, litFill));
        var bar = row.transform.Find("Bar");
        if (bar != null) bar.gameObject.SetActive(on);
        if (row.targetGraphic != null) Fade(row.targetGraphic, on || press.hovered ? litFill ?? SelectedFill : Clear);
        var label = row.transform.Find(labelChild)?.GetComponent<TMP_Text>();
        if (label != null) Fade(label, on ? Color.white : idleLabel);
    }

    // Ячейка сетки: под курсором - рамка Frame, подсветка и чуть крупнее; под мышью - только подсветка
    public static void PaintCell(GameObject cell, bool on, Color idleFill, Color? litFill = null)
    {
        var press = Press.Attach(cell, 0.94f, () => PaintCell(cell, on, idleFill, litFill));
        var frame = cell.transform.Find("Frame");
        if (frame != null) frame.gameObject.SetActive(on);
        if (cell.TryGetComponent(out Selectable s) && s.targetGraphic != null) Fade(s.targetGraphic, on || press.hovered ? litFill ?? SelectedFill : idleFill);
        ScaleTo(cell, on ? 1.06f : 1f);
    }

    // Фильтр-«чипс»: активный залит акцентом, невыбранный под мышью - голубая плашка
    public static void PaintChip(Graphic chip, bool active)
    {
        if (chip == null) return;
        var press = Press.Attach(chip.gameObject, 0.94f, () => PaintChip(chip, active));
        bool lit = !active && press.hovered;
        Fade(chip, active ? Accent : lit ? LitPlate : ChipIdle);
        var label = chip.GetComponentInChildren<TMP_Text>(true);
        if (label != null) Fade(label, active ? OnAccent : lit ? Color.white : TextSoft);
    }

    // ---------- Движение (PGTween, без учёта паузы) ----------

    public const float FadeTime = 0.12f;
    public const float PressTime = 0.18f;
    // Возврат после нажатия с лёгким перелётом
    private static readonly AnimationCurve s_back = new AnimationCurve(new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.55f, 1.12f), new Keyframe(1f, 1f));

    // Плавно к цвету; повторный вызов с той же целью ничего не делает (виды красят каждый кадр)
    public static void Fade(Graphic graphic, Color to)
    {
        if (graphic != null) Ensure<Motion>(graphic.gameObject).Fade(graphic, to);
    }

    // Плавно к масштабу (1 - исходный размер)
    public static void ScaleTo(GameObject target, float scale)
    {
        if (target != null) Ensure<Motion>(target).ScaleTo(scale);
    }

    // Цвет и масштаб одного объекта; масштаб = исходный × наведение × нажатие
    public class Motion : MonoBehaviour
    {
        private Color _to;
        private bool _fading, _hasTarget;
        private int _fadeId, _scaleId, _punchId;
        private Vector3 _origin;
        private bool _hasOrigin;
        private float _scale = 1f, _scaleTo = 1f, _punch = 1f;

        public void Fade(Graphic graphic, Color to)
        {
            if (_hasTarget && _to == to && (_fading || graphic.color == to)) return;
            _to = to;
            _hasTarget = true;
            int id = ++_fadeId;
            if (!isActiveAndEnabled || !Application.isPlaying) { graphic.color = to; _fading = false; return; }
            _fading = true;
            _ = PGTween.OnValueTween(graphic.color, to, FadeTime, true,
                c => { if (id == _fadeId && graphic != null) graphic.color = c; }, null,
                () => { if (id == _fadeId) _fading = false; });
        }

        public void ScaleTo(float scale)
        {
            if (Mathf.Approximately(scale, _scaleTo)) return;
            _scaleTo = scale;
            int id = ++_scaleId;
            if (!isActiveAndEnabled || !Application.isPlaying) { _scale = scale; Apply(); return; }
            _ = PGTween.OnValueTween(_scale, scale, FadeTime, true, v => { if (id == _scaleId) { _scale = v; Apply(); } });
        }

        // Вжать и держать (кнопка мыши зажата)
        public void PressDown(float depth)
        {
            int id = ++_punchId;
            _ = PGTween.OnValueTween(_punch, depth, PressTime * 0.4f, true, v => { if (id == _punchId) { _punch = v; Apply(); } });
        }

        // Отпустить с перелётом
        public void Release()
        {
            int id = ++_punchId;
            _ = PGTween.OnValueTween(_punch, 1f, PressTime, true, v => { if (id == _punchId) { _punch = v; Apply(); } }, s_back);
        }

        // Короткий «тык» (A / Enter)
        public void Punch(float depth)
        {
            int id = ++_punchId;
            _ = PGTween.OnValueTween(_punch, depth, PressTime * 0.4f, true, v => { if (id == _punchId) { _punch = v; Apply(); } }, null,
                () => { if (id == _punchId) Release(); });
        }

        private void Apply()
        {
            if (!_hasOrigin) { _origin = transform.localScale; _hasOrigin = true; }
            transform.localScale = _origin * (_scale * _punch);
        }

        private void OnDisable()
        {
            // Закрытое меню не должно остаться вжатым или в середине перехода
            _punchId++; _scaleId++;
            _punch = 1f; _scale = _scaleTo;
            if (_hasOrigin) Apply();
            _fading = false;
        }
    }

    // Нажатие мышью / A / Enter - вжимается и отскакивает; заодно помнит наведение мыши и перекрашивает объект
    public class Press : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISubmitHandler
    {
        public bool hovered { get; private set; }
        private float _depth = 0.95f;
        private Action _repaint;

        public static Press Attach(GameObject target, float depth, Action repaint = null)
        {
            var press = Ensure<Press>(target);
            press._depth = depth;
            press._repaint = repaint;
            return press;
        }

        private bool CanPress => !TryGetComponent(out Selectable s) || s.IsInteractable();

        public void OnPointerEnter(PointerEventData eventData) { hovered = true; _repaint?.Invoke(); }
        public void OnPointerExit(PointerEventData eventData) { hovered = false; _repaint?.Invoke(); }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && CanPress) Ensure<Motion>(gameObject).PressDown(_depth);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Ensure<Motion>(gameObject).Release();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (CanPress) Ensure<Motion>(gameObject).Punch(_depth);
        }

        private void OnDisable() => hovered = false;
    }

    // ---------- Навигация ----------

    // Вверх/вниз по строкам с переходом через край; вправо - в карточку (если есть куда)
    public static void LinkColumn(IList<Selectable> rows, Func<int, Selectable> right = null)
    {
        int n = rows.Count;
        for (int i = 0; i < n; i++)
            rows[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = n > 1 ? rows[(i - 1 + n) % n] : null,
                selectOnDown = n > 1 ? rows[(i + 1) % n] : null,
                selectOnRight = right?.Invoke(i)
            };
    }

    // Сетка: вверх/вниз - на ряд, влево/вправо - соседняя ячейка (с переходом на следующий ряд)
    public static void LinkGrid(IList<Selectable> cells, int columns)
    {
        columns = Mathf.Max(1, columns);
        int n = cells.Count;
        for (int i = 0; i < n; i++)
            cells[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = i > 0 ? cells[i - 1] : null,
                selectOnRight = i < n - 1 ? cells[i + 1] : null,
                selectOnUp = i - columns >= 0 ? cells[i - columns] : null,
                selectOnDown = i + columns < n ? cells[i + columns] : (i / columns < (n - 1) / columns ? cells[n - 1] : null)
            };
    }

    public static int GridColumns(GridLayoutGroup grid, int fallback = 6) =>
        grid != null && grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount ? Mathf.Max(1, grid.constraintCount) : fallback;

    // Прокрутить список так, чтобы строка под курсором была видна
    public static void ScrollTo(ScrollRect scroll, RectTransform target)
    {
        if (scroll == null || target == null || !target.IsChildOf(scroll.content)) return;
        var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, target);
        float shift = 0f;
        if (b.max.y > viewport.rect.yMax) shift = viewport.rect.yMax - b.max.y;
        else if (b.min.y < viewport.rect.yMin) shift = viewport.rect.yMin - b.min.y;
        if (Mathf.Abs(shift) < 0.5f) return;
        scroll.content.anchoredPosition += new Vector2(0f, shift);
        scroll.velocity = Vector2.zero;
    }

    // ---------- Ввод ----------

    // Действие из поля инспектора, а если поле пустое - по имени («UI/TurnPageLeft»)
    public static InputAction ResolveAction(InputActionReference reference, string path)
    {
        if (reference != null && reference.action != null) return reference.action;
        return InputSystem.actions != null ? InputSystem.actions.FindAction(path) : null;
    }

    public static T Ensure<T>(GameObject go) where T : Component => go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

    // Влево/вправо/вверх/вниз на объекте (количество на «Создать», часы сна) - вешается кодом
    public class MoveRelay : MonoBehaviour, IMoveHandler
    {
        public event Action<MoveDirection> moved;
        public void OnMove(AxisEventData eventData) => moved?.Invoke(eventData.moveDir);
    }

    // Наведение мыши - вешается кодом
    public class PointerRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public event Action<bool> hover;
        public void OnPointerEnter(PointerEventData eventData) => hover?.Invoke(true);
        public void OnPointerExit(PointerEventData eventData) => hover?.Invoke(false);
    }

    // Кнопка действия из макета: обычная / под мышью или курсором / недоступная.
    // Встроенный ColorTint на почти прозрачном фоне не виден, поэтому цвета ставим сами; главная (залитая акцентом) под курсором светлеет и чуть растёт.
    // Можно повесить и в редакторе (при сборке разметки) - кнопку и исходные цвета найдёт сама при первом показе.
    public class ActionLook : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public const float HoverScale = 1.04f;

        private Button _button;
        private TMP_Text _label;
        private Color _background, _labelColor;
        private bool _hovered, _selected;

        public static ActionLook Attach(Button button)
        {
            if (button == null) return null;
            var look = Ensure<ActionLook>(button.gameObject);
            look.Init();
            look.Refresh();
            return look;
        }

        private void Init()
        {
            if (_button != null) return;
            _button = GetComponent<Button>();
            if (_button == null) return;
            _label = transform.Find("Label")?.GetComponent<TMP_Text>();
            _background = _button.targetGraphic != null ? _button.targetGraphic.color : Color.white;
            _labelColor = _label != null ? _label.color : Text;
            _button.transition = Selectable.Transition.None;
            Press.Attach(gameObject, 0.95f);
        }

        private void Awake() { Init(); Refresh(); }

        public void OnPointerEnter(PointerEventData eventData) { _hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData eventData) { _hovered = false; Refresh(); }
        public void OnSelect(BaseEventData eventData) { _selected = true; Refresh(); }
        public void OnDeselect(BaseEventData eventData) { _selected = false; Refresh(); }
        private void OnDisable() { _hovered = false; _selected = false; }

        // Курсор могли поставить до того, как кнопка подписалась на OnSelect (окно только открылось) - сверяемся с EventSystem
        private void Update()
        {
            bool selected = Selected == gameObject;
            if (selected != _selected) { _selected = selected; Refresh(); }
        }

        // Вызывать после смены interactable
        public void Refresh()
        {
            if (_button == null) return;
            bool primary = _background.a > 0.5f;
            bool lit = (_hovered || _selected) && _button.interactable;
            Color background, text;
            if (!_button.interactable) { background = new Color(1f, 1f, 1f, 0.015f); text = TextDim; }
            else if (lit) { background = primary ? Highlighted : LitPlate; text = Color.white; }
            else { background = _background; text = _labelColor; }
            Fade(_button.targetGraphic, background);
            Fade(_label, text);
            ScaleTo(gameObject, lit ? HoverScale : 1f);
            var group = Ensure<CanvasGroup>(gameObject);
            group.alpha = _button.interactable ? 1f : 0.6f;
        }
    }
}
