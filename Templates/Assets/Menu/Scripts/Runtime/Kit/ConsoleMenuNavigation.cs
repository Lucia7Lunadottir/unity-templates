using System.Collections.Generic;
using System.Reflection;
using PG.MenuManagement;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.InputSystem;

// Уровень меню, который умеет шагнуть назад сам (запись дневника -> оглавление, всплывающая панель -> закрыться).
// true - шаг назад сделан; false - этот уровень уже корневой, пусть решает следующий.
public interface IBackHandler
{
    bool HandleBack();

    // Куда вернуть курсор, если выделение потерялось (клик в пустоту, выбор вкладки) - null, если некуда
    GameObject DefaultSelection => null;
}

// Консольное поведение меню: B / кружок / Esc - всегда шаг назад, бамперы (LB/RB, Q/E) - соседняя вкладка.
// Вешается на корень каждого меню (меню игрока, пауза, настройки). Уровни регистрируются сами (Register в OnEnable) - кнопки настраивать не нужно.
// Открыто несколько меню (настройки поверх паузы) - ввод обрабатывает только последнее открытое.
public partial class ConsoleMenuNavigation : MonoBehaviour
{
    [SerializeField] private InputActionReference _back;
    [SerializeField] private InputActionReference _previousTab;
    [SerializeField] private InputActionReference _nextTab;
    [SerializeField] private TabsController _tabs;
    [Tooltip("Шаг назад, когда отступать уже некуда - обычно закрыть меню (Player Pause → ChangePause). Пусто - закрыть паузу, чья панель - это меню")]
    [SerializeField] private UnityEvent _onBackAtRoot;
    [Tooltip("Кнопка, которая сама закрывает это меню (Esc у паузы): «назад» на корне её не дублирует, иначе меню закроется и сразу откроется")]
    [SerializeField] private InputActionReference _rootClosedBy;
    [Tooltip("Курсор может стоять на кнопках вкладок (колонка разделов в настройках); иначе он всегда возвращается на страницу")]
    [SerializeField] private bool _cursorMayStayOnTabs;
    [Tooltip("Куда ставить курсор, если ни один уровень его не забрал (Resume в паузе)")]
    [SerializeField] private Selectable _defaultSelection;
    [Tooltip("Пока открыто что-то глубже корня (подтверждение, настройки), кнопка паузы не закрывает меню целиком - Esc только шаг назад")]
    [SerializeField] private bool _lockPauseWhileNested;

    // Открытые уровни по порядку открытия: последний - самый глубокий
    [AutoStaticsCleanup]
    private static readonly List<IBackHandler> s_levels = new List<IBackHandler>();

    public static void Register(IBackHandler level)
    {
        s_levels.Remove(level);
        s_levels.Add(level);
    }

    public static void Unregister(IBackHandler level) => s_levels.Remove(level);

    // Открытые меню: ввод и курсор ведёт последнее
    [AutoStaticsCleanup]
    private static readonly List<ConsoleMenuNavigation> s_navigations = new List<ConsoleMenuNavigation>();
    private bool IsTop => s_navigations.Count > 0 && s_navigations[s_navigations.Count - 1] == this;

    private InputControl _backControl;
    private bool _pauseLocked;

    private int _currentTab = -1;
    private bool _closeRequested;
    private int _markedTab = -1;
    private Selectable _markedButton;
    private GameObject _lastSelected;
    private bool[] _tabWasActive; // какие объекты вкладок были включены в прошлом кадре

    private void OnEnable()
    {
        _currentTab = -1; // при открытии меню определим по видимой вкладке
        _closeRequested = false;
        _markedTab = -1;
        _tabWasActive = null;
        s_navigations.Remove(this);
        s_navigations.Add(this);
        Subscribe(_back, OnBack);
        Subscribe(_previousTab, OnPreviousTab);
        Subscribe(_nextTab, OnNextTab);
    }

    private void OnDisable()
    {
        Unsubscribe(_back, OnBack);
        Unsubscribe(_previousTab, OnPreviousTab);
        Unsubscribe(_nextTab, OnNextTab);
        s_navigations.Remove(this);
        LockPause(false);
        // Меню закрыто - вкладка больше не держит Selected, при открытии отметится заново
        if (_markedButton != null && UnityEngine.EventSystems.EventSystem.current != null)
            _markedButton.OnDeselect(new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current));
        _markedButton = null;
        _markedTab = -1;
    }

    private void OnBack(InputAction.CallbackContext context)
    {
        if (!IsTop) return;
        _backControl = context.control;
        Back();
        _backControl = null;
    }
    private void OnPreviousTab(InputAction.CallbackContext _) { if (IsTop) StepTab(-1); }
    private void OnNextTab(InputAction.CallbackContext _) { if (IsTop) StepTab(1); }

    public void Back()
    {
        // Старые панели со своим CancelController обрабатывают Cancel сами - не закрываем меню поверх них
        foreach (var legacy in FindObjectsByType<CancelController>(FindObjectsInactive.Exclude))
            if (legacy.isActiveAndEnabled) return;
        // Раскрытый выпадающий список закрывается сам по Cancel - меню не трогаем
        foreach (var dropdown in FindObjectsByType<TMPro.TMP_Dropdown>(FindObjectsInactive.Exclude))
            if (dropdown.IsExpanded) return;

        for (int i = s_levels.Count - 1; i >= 0; i--)
        {
            var level = s_levels[i];
            if (level is Behaviour behaviour && (behaviour == null || !behaviour.isActiveAndEnabled))
            {
                s_levels.RemoveAt(i);
                continue;
            }
            if (level.HandleBack()) return;
        }
        // Эту же кнопку меню закрывает само (Esc у паузы) - второй раз не закрываем
        if (_backControl != null && _rootClosedBy != null && _rootClosedBy.action != null)
            foreach (var control in _rootClosedBy.action.controls)
                if (control == _backControl) return;
        // Закрываем в конце кадра: Esc - это ещё и игровая пауза, она должна увидеть, что меню открыто, и не открыться следом
        _closeRequested = true;
    }

    private void LateUpdate()
    {
        if (!_closeRequested) return;
        _closeRequested = false;
        if (_onBackAtRoot != null && _onBackAtRoot.GetPersistentEventCount() > 0) _onBackAtRoot.Invoke();
        else OwnerPause()?.ChangePause();
    }

    // Пауза, чья панель - это меню (или его родитель)
    private Pause OwnerPause()
    {
        foreach (var pause in FindObjectsByType<Pause>(FindObjectsInactive.Include))
            if (pause.panel != null && transform.IsChildOf(pause.panel.transform)) return pause;
        return null;
    }

    // Пока открыт уровень глубже корня или меню поверх этого - кнопка паузы молчит (возвращаем как было, когда всё закрыто)
    private void UpdatePauseLock()
    {
        if (!_lockPauseWhileNested) return;
        bool nested = !IsTop;
        foreach (var level in s_levels)
            if (level is Behaviour behaviour && behaviour != null && behaviour.isActiveAndEnabled && behaviour.transform.IsChildOf(transform)) { nested = true; break; }
        LockPause(nested);
    }

    private void LockPause(bool locked)
    {
        if (locked == _pauseLocked) return;
        if (locked && !Pause.isPauseEnable) return; // её уже выключил кто-то другой (катсцена) - не наше
        _pauseLocked = locked;
        Pause.isPauseEnable = !locked;
    }

    private void Update()
    {
        UpdatePauseLock();
        if (!IsTop) return;

        var events = UnityEngine.EventSystems.EventSystem.current;
        if (events == null) return;
        var selected = events.currentSelectedGameObject;

        var tabs = Tabs();
        if (tabs != null) TrackOpenedTab(tabs);

        // Курсор на кнопке вкладки (клик или пауза выбрала кнопку при открытии) - он должен быть на странице
        bool onTab = false;
        if (selected != null && tabs != null && !_cursorMayStayOnTabs)
            for (int i = 0; i < tabs.Length; i++)
                if (tabs[i].selectableObject != null && tabs[i].selectableObject.gameObject == selected) { onTab = true; break; }

        // Курсор всегда на странице: потерялся или остался на вкладке - возвращаем его в открытый уровень
        if (selected == null || !selected.activeInHierarchy || onTab)
        {
            // На странице выбрать нечего (например, пустой дневник) - курсор на кнопке открытой вкладки, а не чужой
            if (!RestoreFocus(events, selected))
            {
                GameObject own = null;
                if (tabs != null && _currentTab >= 0 && _currentTab < tabs.Length && tabs[_currentTab].selectableObject != null)
                    own = tabs[_currentTab].selectableObject.gameObject;
                else if (_defaultSelection != null)
                    own = _defaultSelection.gameObject;
                if (own != null && own != selected && own.activeInHierarchy) events.SetSelectedGameObject(own);
            }
        }

        // В настройках «кнопка» вкладки - первая строка раздела, подсвечивать её как вкладку нельзя
        if (tabs != null && !_cursorMayStayOnTabs) MarkActiveTab(tabs, events.currentSelectedGameObject);
    }

    // Текущая вкладка - та, чей объект только что включился (клик, бамперы, SelectTab из кода), а не та, где курсор
    private void TrackOpenedTab(TabsController.Tab[] tabs)
    {
        if (_tabWasActive == null || _tabWasActive.Length != tabs.Length)
        {
            _tabWasActive = new bool[tabs.Length];
            for (int i = 0; i < tabs.Length; i++) _tabWasActive[i] = tabs[i].gameObject != null && tabs[i].gameObject.activeSelf;
            _currentTab = VisibleTab(tabs);
            return;
        }
        for (int i = 0; i < tabs.Length; i++)
        {
            bool active = tabs[i].gameObject != null && tabs[i].gameObject.activeSelf;
            if (active && !_tabWasActive[i]) _currentTab = i;
            _tabWasActive[i] = active;
        }
    }

    // Активная вкладка держит своё состояние Selected (как его нарисовали в кнопке), даже когда курсор на странице
    private void MarkActiveTab(TabsController.Tab[] tabs, GameObject selected)
    {
        int active = _currentTab >= 0 ? _currentTab : VisibleTab(tabs);
        if (active < 0 || active >= tabs.Length) return;
        var button = tabs[active].selectableObject;
        if (active == _markedTab && selected == _lastSelected) return;

        var events = UnityEngine.EventSystems.EventSystem.current;
        var data = new UnityEngine.EventSystems.BaseEventData(events);
        // Вкладка сменилась - мышь над старой кнопкой больше не держит её подсвеченной (Highlighted выглядит как активная)
        if (active != _markedTab)
        {
            var pointer = new UnityEngine.EventSystems.PointerEventData(events);
            foreach (var tab in tabs)
                if (tab.selectableObject != null && tab.selectableObject != button) tab.selectableObject.OnPointerExit(pointer);
        }
        if (_markedButton != null && _markedButton != button && _markedButton.gameObject != selected) _markedButton.OnDeselect(data);
        if (button != null && button.gameObject != selected) button.OnSelect(data);

        _markedTab = active;
        _markedButton = button;
        _lastSelected = selected;
    }

    // true - курсор поставлен в открытый уровень
    private static bool RestoreFocus(UnityEngine.EventSystems.EventSystem events, GameObject selected)
    {
        for (int i = s_levels.Count - 1; i >= 0; i--)
        {
            if (s_levels[i] is Behaviour behaviour && (behaviour == null || !behaviour.isActiveAndEnabled)) continue;
            var target = s_levels[i].DefaultSelection;
            if (target == null || !target.activeInHierarchy) continue;
            if (target != selected) events.SetSelectedGameObject(target);
            return true;
        }
        return false;
    }

    // Следующая вкладка по кругу; вкладки с выключенной или неактивной кнопкой пропускаются
    public void StepTab(int direction)
    {
        var tabs = Tabs();
        if (tabs == null || tabs.Length == 0) return;

        int current = _currentTab >= 0 ? _currentTab : VisibleTab(tabs);

        for (int step = 1; step <= tabs.Length; step++)
        {
            int i = ((current + direction * step) % tabs.Length + tabs.Length) % tabs.Length;
            var button = tabs[i].selectableObject;
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable) continue;
            _currentTab = i;
            _tabs.SelectTab(i);
            return;
        }
    }

    // Видимая сейчас вкладка: активна и не гаснет (у скрываемой анимацией панели alpha уже падает)
    private static int VisibleTab(TabsController.Tab[] tabs)
    {
        int fallback = 0;
        for (int i = 0; i < tabs.Length; i++)
        {
            var go = tabs[i].gameObject;
            if (go == null || !go.activeInHierarchy) continue;
            if (!go.TryGetComponent(out CanvasGroup group) || group.alpha > 0.5f) return i;
            fallback = i;
        }
        return fallback;
    }

    // Порядок вкладок - как в TabsController (поле приватное в пакете меню)
    private static readonly FieldInfo TabsField = typeof(TabsController).GetField("_tabs", BindingFlags.NonPublic | BindingFlags.Instance);

    private TabsController.Tab[] Tabs() => TabsOf(_tabs);

    public static TabsController.Tab[] TabsOf(TabsController tabs) => tabs != null ? TabsField?.GetValue(tabs) as TabsController.Tab[] : null;

    private static void Subscribe(InputActionReference reference, System.Action<InputAction.CallbackContext> handler)
    {
        if (reference == null || reference.action == null) return;
        reference.action.performed += handler;
        reference.action.Enable();
    }

    private static void Unsubscribe(InputActionReference reference, System.Action<InputAction.CallbackContext> handler)
    {
        if (reference == null || reference.action == null) return;
        reference.action.performed -= handler;
    }

#if UNITY_EDITOR
    // Полоса вкладок как в макете (Assets/UI Mockups/Inventory): тёмная плашка по ширине содержимого,
    // неактивные вкладки - тусклый текст, активная (Selected) - светлый текст с голубым подчёркиванием, по краям значки LB/RB.
    // Выключенная кнопка вкладки места не занимает - полоса сжимается сама.
    [ContextMenu("Оформить вкладки по макету")]
    private void StyleTabsLikeMockup() => StyleTabs(_tabs);

    public static void StyleTabs(TabsController tabsController)
    {
        var tabs = TabsOf(tabsController);
        if (tabsController == null || tabs == null) { Debug.LogWarning("ConsoleMenuNavigation: нет TabsController."); return; }
        var bar = (RectTransform)tabsController.transform;
        var sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/9Sliced.png");
        var actions = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/ScriptableData/InputSystem_Actions.inputactions");
        var accent = new Color32(0x77, 0xA7, 0xFF, 0xFF);
        UnityEditor.Undo.RegisterFullObjectHierarchyUndo(bar.gameObject, "Вкладки по макету");

        if (bar.TryGetComponent(out Image barImage))
        {
            barImage.sprite = sprite; barImage.type = Image.Type.Sliced; barImage.pixelsPerUnitMultiplier = 2f;
            barImage.color = new Color(12 / 255f, 10 / 255f, 18 / 255f, 0.82f);
        }
        // Ширина - по содержимому, по центру; высота остаётся от якорей
        bar.anchorMin = new Vector2(0.5f, bar.anchorMin.y); bar.anchorMax = new Vector2(0.5f, bar.anchorMax.y);
        bar.pivot = new Vector2(0.5f, bar.pivot.y);
        bar.anchoredPosition = new Vector2(0f, bar.anchoredPosition.y);
        if (!bar.TryGetComponent(out HorizontalLayoutGroup layout)) layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(36, 36, 14, 14); layout.spacing = 16; layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true; layout.childControlHeight = false; layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
        if (!bar.TryGetComponent(out ContentSizeFitter fitter)) fitter = bar.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        var font = tabs[0].selectableObject != null ? tabs[0].selectableObject.GetComponentInChildren<TMPro.TMP_Text>(true)?.font : null;
        PromptEdge(bar, "Prompt Previous", "[UI/PreviousPage]", true, font, actions);
        PromptEdge(bar, "Prompt Next", "[UI/NextPage]", false, font, actions);

        foreach (var tab in tabs)
        {
            if (tab.selectableObject == null) continue;
            var button = tab.selectableObject;
            var rt = (RectTransform)button.transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, 104f);
            if (button.TryGetComponent(out Image hit)) { hit.color = new Color(1, 1, 1, 0); hit.raycastTarget = true; }

            var underline = button.transform.Find("Underline") as RectTransform;
            if (underline == null)
            {
                underline = new GameObject("Underline", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<RectTransform>();
                underline.gameObject.layer = button.gameObject.layer;
                underline.SetParent(button.transform, false);
            }
            underline.GetComponent<LayoutElement>().ignoreLayout = true;
            underline.anchorMin = new Vector2(0, 0); underline.anchorMax = new Vector2(1, 0); underline.pivot = new Vector2(0.5f, 0);
            underline.offsetMin = new Vector2(24, 8); underline.offsetMax = new Vector2(-24, 14);
            var line = underline.GetComponent<Image>();
            line.sprite = sprite; line.type = Image.Type.Sliced; line.pixelsPerUnitMultiplier = 12f; line.color = Color.white; line.raycastTarget = false;
            button.targetGraphic = line;
            var colors = button.colors;
            colors.normalColor = new Color(1, 1, 1, 0);
            colors.highlightedColor = new Color(accent.r / 255f, accent.g / 255f, accent.b / 255f, 0.5f);
            colors.selectedColor = colors.pressedColor = accent;
            colors.colorMultiplier = 1f; colors.fadeDuration = 0.08f;
            button.colors = colors;

            var label = button.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (label != null)
            {
                label.fontSize = 56; label.enableAutoSizing = false;
                label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                label.alignment = TMPro.TextAlignmentOptions.Center;
            }
            // Ширина вкладки = текст + поля (переводы разной длины)
            if (!button.TryGetComponent(out HorizontalLayoutGroup inner)) inner = button.gameObject.AddComponent<HorizontalLayoutGroup>();
            inner.padding = new RectOffset(40, 40, 0, 0); inner.childAlignment = TextAnchor.MiddleCenter;
            inner.childControlWidth = inner.childControlHeight = true; inner.childForceExpandWidth = inner.childForceExpandHeight = true;
            if (button is ButtonWithText withText)
            {
                var textColors = withText.textColors;
                textColors.normalColor = new Color(1, 1, 1, 0.5f);
                textColors.highlightedColor = textColors.selectedColor = textColors.pressedColor = Color.white;
                textColors.disabledColor = new Color(1, 1, 1, 0.25f);
                textColors.colorMultiplier = 1f;
                withText.textColors = textColors;
            }
            if (label != null) label.color = Color.white;
            UnityEditor.EditorUtility.SetDirty(button);
        }
        UnityEditor.EditorUtility.SetDirty(bar.gameObject);
        Debug.Log("ConsoleMenuNavigation: вкладки оформлены по макету. Сохрани префаб/сцену.", bar);
    }

    private static void PromptEdge(RectTransform bar, string name, string tag, bool first, TMPro.TMP_FontAsset font, InputActionAsset actions)
    {
        var t = bar.Find(name);
        if (t == null)
        {
            t = new GameObject(name, typeof(RectTransform), typeof(TMPro.TextMeshProUGUI)).transform;
            t.gameObject.layer = bar.gameObject.layer;
            t.SetParent(bar, false);
            var text = t.GetComponent<TMPro.TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.text = tag; text.fontSize = 56; text.raycastTarget = false;
            text.textWrappingMode = TMPro.TextWrappingModes.NoWrap; text.alignment = TMPro.TextAlignmentOptions.Center;
            var prompt = t.gameObject.AddComponent<InputSystemActionPrompts.PromptText>();
            var so = new UnityEditor.SerializedObject(prompt);
            so.FindProperty("m_InputActionAsset").objectReferenceValue = actions;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        if (first) t.SetAsFirstSibling(); else t.SetAsLastSibling();
    }
#endif
}
