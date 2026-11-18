using System;
using System.Collections.Generic;
using PG.InventoryManagement;
using PG.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PG.CraftSystem
{
    // Вкладка «Создание»: слева изученные рецепты по группам, справа - что получится, чего не хватает, количество и «Создать».
    // Выделение под курсором (полоска слева, белый текст) рисует код - шаблоны строк задают только вид.
    // Геймпад: вверх/вниз - рецепты, A/вправо - к «Создать», там влево/вправо - количество, A - создать, B - назад к рецепту; LT/RT - фильтр.
    // Разметка - обычные объекты сцены (строки клонируются из шаблонов), группы и тексты - списками в инспекторе.
    // Собрать разметку по макету: ПКМ по компоненту → «Собрать разметку по макету».
    public class CraftMenuView : MonoBehaviour, IBackHandler
    {
        [Serializable]
        public class RecipeGroup
        {
            public LocalText title = new LocalText("Group");
            [Tooltip("Свёрнута при открытии меню (A по заголовку разворачивает)")]
            public bool collapsed;
            public List<CraftRecipe> recipes = new List<CraftRecipe>();
        }

        [SerializeField] private CraftController _controller;
        [Tooltip("Затемнение всего экрана за меню - включено, пока открыта вкладка (в книге его нет, там видна сцена)")]
        [SerializeField] private GameObject _backdrop;

        [Header("Группы (рецепт без группы попадает в «Прочее»)")]
        [SerializeField] private List<RecipeGroup> _groups = new List<RecipeGroup>();
        [SerializeField] private LocalText _otherGroup = new LocalText("craft-ui-group-other", "Other");

        [Header("Список")]
        [SerializeField] private ScrollRect _listScroll;
        [SerializeField] private RectTransform _listContent;
        [Tooltip("Заголовок группы: дети Title, Count")]
        [SerializeField] private Button _groupTemplate;
        [Tooltip("Строка рецепта: дети Icon, Name, Parts (с шаблоном Part: Icon, Count), Ready")]
        [SerializeField] private Button _recipeTemplate;
        [SerializeField] private TMP_Text _emptyText;

        [Header("Фильтр (LT / RT)")]
        [SerializeField] private Graphic _filterAll;
        [SerializeField] private Graphic _filterAvailable;
        [SerializeField] private InputActionReference _filterLeft;
        [SerializeField] private InputActionReference _filterRight;

        [Header("Карточка")]
        [SerializeField] private GameObject _detail;
        [SerializeField] private Image _heroIcon;
        [SerializeField] private TMP_Text _heroName;
        [SerializeField] private TMP_Text _heroTag;
        [SerializeField] private GameObject _descriptionBlock;
        [SerializeField] private TMP_Text _description;
        [SerializeField] private RectTransform _needContent;
        [Tooltip("Строка «нужно»: дети Icon, Name, Have, Bar/Fill")]
        [SerializeField] private GameObject _needTemplate;
        [SerializeField] private TMP_Text _amount;
        [SerializeField] private Button _amountLessButton;
        [SerializeField] private Button _amountMoreButton;
        [SerializeField] private Button _createButton;
        [SerializeField] private TMP_Text _hint;

        [Header("Тексты (ключ локализации + запасной текст)")]
        [SerializeField] private LocalText _textGroupCount = new LocalText("{0} / {1}");
        [SerializeField] private LocalText _textMissing = new LocalText("craft-ui-missing", "Missing: {0}");
        [SerializeField] private LocalText _textTooHeavy = new LocalText("craft-ui-too-heavy", "Too heavy to carry");
        [Tooltip("Не показывать рецепты того, что ставится в мир (строится в Build Mode)")]
        [SerializeField] private bool _hideBuildable = true;
        [SerializeField] private LocalText _textNoRecipes = new LocalText("craft-ui-no-recipes", "No recipes yet");
        [SerializeField] private LocalText _textNothingAvailable = new LocalText("craft-ui-nothing-available", "Nothing can be made right now");
        [SerializeField] private LocalText _textPlaceable = new LocalText("craft-ui-placeable", "placed in the world");
        [Tooltip("Названия типов предмета по порядку Item.ItemType: Supplies, Materials, Artifacts, Important")]
        [SerializeField] private LocalText[] _typeNames = { new LocalText("craft-ui-type-supplies", "Supplies"), new LocalText("craft-ui-type-materials", "Materials"), new LocalText("craft-ui-type-artifacts", "Artifacts"), new LocalText("craft-ui-type-important", "Important") };

        [Header("Цвета")]
        [SerializeField] private Color _accent = new Color32(0x77, 0xA7, 0xFF, 0xFF);
        [SerializeField] private Color _text = new Color32(0xEC, 0xDF, 0xC4, 0xFF);
        [SerializeField] private Color _textDim = new Color32(0x8A, 0x82, 0x75, 0xFF);
        [SerializeField] private Color _missing = new Color32(0xFF, 0x5A, 0x4A, 0xFF);
        [SerializeField] private Color _ok = new Color32(0x9C, 0xC9, 0x7A, 0xFF);
        // Проект в линейном цветовом пространстве: полупрозрачное выглядит заметно плотнее, чем в макете (sRGB) - отсюда малые альфы
        [Tooltip("Подсветка строки под курсором")]
        [SerializeField] private Color _selectedFill = new Color(119 / 255f, 167 / 255f, 1f, 0.12f);

        private class Row
        {
            public Selectable selectable;
            public CraftRecipe recipe; // null - заголовок группы
            public int group;
            public bool can;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<GameObject> _spawnedNeeds = new List<GameObject>();
        private bool[] _collapsed;
        private bool _onlyAvailable;

        // Мебель и прочее, что ставится в мир, строится в Build Mode по чертежам, а не крафтится в сумку
        private bool IsBuildable(CraftRecipe recipe) => _hideBuildable && recipe.resultItem is PlaceableItem placeable && placeable.worldPrefab != null;
        private bool _dirty = true;
        private CraftRecipe _shown;
        private int _count = 1;
        private GameObject _lastRow;
        private InputAction _left, _right;
        private Color _createColor;
        private GameObject _painted;
        private CanvasGroup _detailGroup;

        // ---------- Жизненный цикл ----------

        private void Awake()
        {
            if (_groupTemplate != null) _groupTemplate.gameObject.SetActive(false);
            if (_recipeTemplate != null) _recipeTemplate.gameObject.SetActive(false);
            if (_needTemplate != null) _needTemplate.SetActive(false);
            ChipButton(_filterAll, false);
            ChipButton(_filterAvailable, true);
            if (_createButton != null)
            {
                _createButton.onClick.AddListener(Craft);
                _createButton.navigation = new Navigation { mode = Navigation.Mode.None };
                if (_createButton.targetGraphic != null) _createColor = _createButton.targetGraphic.color;
                var relay = _createButton.gameObject.AddComponent<MenuKit.MoveRelay>();
                relay.moved += OnCreateMoved;
            }
        }

        private void OnEnable()
        {
            _amountMoreButton.onClick.AddListener(AddCount);
            _amountLessButton.onClick.AddListener(SubtractCount);
            if (_controller == null) _controller = FindAnyObjectByType<CraftController>(FindObjectsInactive.Include);
            if (_controller != null) _controller.changed += MarkDirty;
            if (_collapsed == null || _collapsed.Length != _groups.Count + 1)
            {
                _collapsed = new bool[_groups.Count + 1];
                for (int i = 0; i < _groups.Count; i++) _collapsed[i] = _groups[i].collapsed;
            }
            _left = MenuKit.ResolveAction(_filterLeft, "UI/TurnPageLeft");
            _right = MenuKit.ResolveAction(_filterRight, "UI/TurnPageRight");
            if (_left != null) { _left.performed += OnFilter; _left.Enable(); }
            if (_right != null) { _right.performed += OnFilter; _right.Enable(); }
            if (LocalizationSystem.instance != null) LocalizationSystem.instance.localizationChanged += OnLocalize;
            ConsoleMenuNavigation.Register(this);
            if (_backdrop != null) _backdrop.SetActive(true);
            _dirty = true;
            Rebuild();
        }

        private void OnDisable()
        {
            _amountMoreButton.onClick.RemoveListener(AddCount);
            _amountLessButton.onClick.RemoveListener(SubtractCount);
            if (_controller != null) _controller.changed -= MarkDirty;
            if (_left != null) _left.performed -= OnFilter;
            if (_right != null) _right.performed -= OnFilter;
            if (LocalizationSystem.instance != null) LocalizationSystem.instance.localizationChanged -= OnLocalize;
            ConsoleMenuNavigation.Unregister(this);
            if (_backdrop != null) _backdrop.SetActive(false);
        }

        private void MarkDirty() => _dirty = true;
        private void OnLocalize(string _) => _dirty = true;

        // Крафт меняет инвентарь много раз за кадр - перестраиваемся один раз в конце кадра
        private void LateUpdate()
        {
            if (_dirty) Rebuild();
        }

        private void Update()
        {
            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == null) return;

            if (selected != _painted) Paint(selected);

            var row = FindRow(selected);
            if (row != null)
            {
                _lastRow = selected;
                if (row.recipe != null && row.recipe != _shown) ShowRecipe(row.recipe);
                MenuKit.ScrollTo(_listScroll, (RectTransform)selected.transform);
            }
        }

        // Строка под курсором: полоска слева и белый текст; карточка приглушается, пока курсор на заголовке группы
        private void Paint(GameObject selected)
        {
            _painted = selected;
            Row current = null;
            foreach (var row in _rows)
            {
                bool on = row.selectable != null && row.selectable.gameObject == selected;
                if (on) current = row;
                Color idle = row.recipe == null ? _accent : row.can ? _text : _textDim;
                MenuKit.PaintRow(row.selectable, on, row.recipe != null ? "Name" : "Title", idle, _selectedFill);
            }

            if (_detail != null)
            {
                // Приглушаем содержимое карточки, а не её фон - иначе панель просвечивает
                var body = _detail.transform.Find("Body") ?? _detail.transform;
                if (_detailGroup == null && !body.TryGetComponent(out _detailGroup)) _detailGroup = body.gameObject.AddComponent<CanvasGroup>();
                bool onHeader = current != null && current.recipe == null;
                _detailGroup.alpha = onHeader ? 0.35f : 1f;
            }
        }

        // ---------- Консольное меню ----------

        // B на «Создать» - назад к рецепту; в списке - дальше решает меню
        public bool HandleBack()
        {
            var selected = MenuKit.Selected;
            if (_createButton != null && selected == _createButton.gameObject)
            {
                SelectRowOf(_shown);
                return true;
            }
            return false;
        }

        public GameObject DefaultSelection
        {
            get
            {
                if (_lastRow != null && _lastRow.activeInHierarchy) return _lastRow;
                foreach (var row in _rows) if (row.recipe != null) return row.selectable.gameObject;
                return _rows.Count > 0 ? _rows[0].selectable.gameObject : null;
            }
        }

        private void OnFilter(InputAction.CallbackContext _)
        {
            if (!isActiveAndEnabled) return;
            SetFilter(!_onlyAvailable);
        }

        // «Все» / «Можно сделать» нажимаются и мышью; курсор геймпада на них не встаёт
        private void ChipButton(Graphic chip, bool onlyAvailable)
        {
            if (chip == null) return;
            chip.raycastTarget = true;
            var button = MenuKit.Ensure<Button>(chip.gameObject);
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => SetFilter(onlyAvailable));
            // Под мышью невыбранный фильтр - голубая плашка (MenuKit.PaintChip)
        }

        private void SetFilter(bool onlyAvailable)
        {
            _onlyAvailable = onlyAvailable;
            Rebuild();
            var target = DefaultSelection;
            if (target != null) EventSystem.current?.SetSelectedGameObject(target);
        }


        void AddCount(){
            OnCreateMoved(MoveDirection.Right);
        }
        void SubtractCount(){
            OnCreateMoved(MoveDirection.Left);
        }
        private void OnCreateMoved(MoveDirection direction)
        {
            if (_shown == null) return;
            int max = Mathf.Max(1, _controller != null ? _controller.MaxCraftCount(_shown) : 1);
            if (direction == MoveDirection.Left) _count = _count <= 1 ? max : _count - 1;
            else if (direction == MoveDirection.Right) _count = _count >= max ? 1 : _count + 1;
            else if (direction == MoveDirection.Up || direction == MoveDirection.Down) { SelectRowOf(_shown); return; }
            UpdateCreate();
        }

        private void Craft()
        {
            if (_shown == null || _controller == null) return;
            int can = _controller.MaxCraftCount(_shown);
            int times = Mathf.Min(_count, can);
            for (int i = 0; i < times; i++) _controller.TryCraft(_shown);
            _count = 1;
            Rebuild();
        }

        // ---------- Список ----------

        private void Rebuild()
        {
            _dirty = false;
            if (_controller == null || _listContent == null) return;

            var selectedRecipe = SelectedRecipe();
            var events = EventSystem.current;
            bool selectionInList = events != null && events.currentSelectedGameObject != null && FindRow(events.currentSelectedGameObject) != null;

            foreach (var row in _rows)
            {
                row.selectable.gameObject.SetActive(false);
                Destroy(row.selectable.gameObject);
            }
            _rows.Clear();

            var known = _controller.knownRecipes;
            int groupCount = _groups.Count;
            for (int g = 0; g <= groupCount; g++)
            {
                var recipes = new List<CraftRecipe>();
                foreach (var recipe in known)
                    if (recipe != null && GroupOf(recipe) == g && !IsBuildable(recipe)) recipes.Add(recipe);
                if (recipes.Count == 0) continue;

                int available = 0;
                foreach (var recipe in recipes) if (_controller.MaxCraftCount(recipe) > 0) available++;
                if (_onlyAvailable && available == 0) continue;

                AddGroup(g, available, recipes.Count);
                if (_collapsed[g]) continue;
                foreach (var recipe in recipes)
                    if (!_onlyAvailable || _controller.MaxCraftCount(recipe) > 0) AddRecipe(recipe, g);
            }

            LinkRows();
            UpdateFilterChips();
            _painted = null;

            if (_emptyText != null)
            {
                _emptyText.gameObject.SetActive(_rows.Count == 0);
                _emptyText.text = known.Count == 0 ? _textNoRecipes.Get() : _textNothingAvailable.Get();
            }

            // Показываем тот же рецепт, что и до перестройки (или первый)
            var show = selectedRecipe != null && Contains(selectedRecipe) ? selectedRecipe : _shown != null && Contains(_shown) ? _shown : FirstRecipe();
            _shown = null;
            if (show != null) ShowRecipe(show);
            else if (_detail != null) _detail.SetActive(false);

            if (selectionInList && events != null)
            {
                var target = RowOf(show)?.selectable.gameObject ?? DefaultSelection;
                if (target != null) events.SetSelectedGameObject(target);
            }
        }

        private int GroupOf(CraftRecipe recipe)
        {
            for (int i = 0; i < _groups.Count; i++)
                if (_groups[i].recipes.Contains(recipe)) return i;
            return _groups.Count;
        }

        private void AddGroup(int g, int available, int total)
        {
            var button = Instantiate(_groupTemplate, _listContent);
            button.transition = Selectable.Transition.None; // подсветку рисует Paint
            if (button.targetGraphic != null) button.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
            button.gameObject.SetActive(true);
            button.name = "Group " + g;
            string title = g < _groups.Count ? _groups[g].title.Get() : _otherGroup.Get();
            SetText(button.transform, "Title", (title ?? "").ToUpperInvariant() + (_collapsed[g] ? "  +" : "  –"));
            SetText(button.transform, "Count", string.Format(_textGroupCount.Get(), available, total));
            int index = g;
            button.onClick.AddListener(() =>
            {
                _collapsed[index] = !_collapsed[index];
                Rebuild();
                var header = _rows.Find(r => r.recipe == null && r.group == index);
                if (header != null) EventSystem.current?.SetSelectedGameObject(header.selectable.gameObject);
            });
            _rows.Add(new Row { selectable = button, group = g, can = available > 0 });
        }

        private void AddRecipe(CraftRecipe recipe, int g)
        {
            var button = Instantiate(_recipeTemplate, _listContent);
            button.transition = Selectable.Transition.None; // подсветку рисует Paint
            if (button.targetGraphic != null) button.targetGraphic.color = new Color(1f, 1f, 1f, 0f);
            button.gameObject.SetActive(true);
            button.name = "Recipe " + (recipe.resultItem != null ? recipe.resultItem.name : recipe.name);
            bool can = _controller.MaxCraftCount(recipe) > 0;

            SetImage(button.transform, "Icon", recipe.resultItem != null ? recipe.resultItem.icon : null);
            var nameText = SetText(button.transform, "Name", ItemName(recipe.resultItem) + (recipe.resultCount > 1 ? $" ×{recipe.resultCount}" : ""));
            if (nameText != null) nameText.color = can ? _text : _textDim;
            var ready = button.transform.Find("Ready")?.GetComponent<Graphic>();
            if (ready != null) ready.color = can ? _ok : new Color(1f, 1f, 1f, 0.15f);

            var parts = button.transform.Find("Parts");
            if (parts != null && parts.childCount > 0)
            {
                var partTemplate = parts.GetChild(0).gameObject;
                partTemplate.SetActive(false);
                foreach (var ing in recipe.ingredients)
                {
                    if (ing.item == null) continue;
                    var part = Instantiate(partTemplate, parts);
                    part.SetActive(true);
                    SetImage(part.transform, "Icon", ing.item.icon);
                    bool enough = _controller.GetItemCount(ing.item) >= ing.count;
                    var countText = SetText(part.transform, "Count", ing.count.ToString());
                    if (countText != null) countText.color = enough ? _text : _missing;
                }
            }

            button.onClick.AddListener(() =>
            {
                ShowRecipe(recipe);
                if (_createButton != null) _createButton.Select();
            });
            _rows.Add(new Row { selectable = button, recipe = recipe, group = g, can = can });
        }

        // Вверх/вниз по строкам с переходом через край, вправо - к «Создать»
        private void LinkRows()
        {
            var selectables = _rows.ConvertAll(r => r.selectable);
            MenuKit.LinkColumn(selectables, i => _rows[i].recipe != null ? _createButton : null);
        }

        private void UpdateFilterChips()
        {
            MenuKit.PaintChip(_filterAll, !_onlyAvailable);
            MenuKit.PaintChip(_filterAvailable, _onlyAvailable);
        }

        // ---------- Карточка ----------

        private void ShowRecipe(CraftRecipe recipe)
        {
            if (recipe != _shown) _count = 1;
            _shown = recipe;
            if (_detail != null) _detail.SetActive(true);
            var item = recipe.resultItem;

            MenuKit.ShowIcon(_heroIcon, item != null ? item.icon : null);
            if (_heroName != null) _heroName.text = ItemName(item) + (recipe.resultCount > 1 ? $"  ×{recipe.resultCount}" : "");
            if (_heroTag != null)
            {
                string tag = item != null && (int)item.itemType < _typeNames.Length ? _typeNames[(int)item.itemType].Get() : "";
                if (item is PlaceableItem) tag += " · " + _textPlaceable.Get();
                _heroTag.text = tag;
            }

            string description = ItemDescription(item);
            if (_descriptionBlock != null) _descriptionBlock.SetActive(!string.IsNullOrWhiteSpace(description));
            if (_description != null) _description.text = description;

            foreach (var go in _spawnedNeeds) { go.SetActive(false); Destroy(go); }
            _spawnedNeeds.Clear();
            if (_needTemplate != null && _needContent != null)
                foreach (var ing in recipe.ingredients)
                {
                    if (ing.item == null) continue;
                    var row = Instantiate(_needTemplate, _needContent);
                    row.SetActive(true);
                    _spawnedNeeds.Add(row);
                    int have = _controller.GetItemCount(ing.item);
                    bool enough = have >= ing.count;
                    SetImage(row.transform, "Icon", ing.item.icon);
                    SetText(row.transform, "Name", ItemName(ing.item));
                    var haveText = SetText(row.transform, "Have", $"{have} / {ing.count}");
                    if (haveText != null) haveText.color = enough ? _text : _missing;
                    var fill = row.transform.Find("Bar/Fill") as RectTransform;
                    MenuKit.SetFill(fill, ing.count > 0 ? (float)have / ing.count : 1f);
                    if (fill != null && fill.TryGetComponent(out Graphic g)) g.color = enough ? _accent : _missing;
                }

            UpdateCreate();
        }

        private void UpdateCreate()
        {
            if (_shown == null || _controller == null) return;
            int max = _controller.MaxCraftCount(_shown);
            _count = Mathf.Clamp(_count, 1, Mathf.Max(1, max));
            //if (_amount != null) _amount.text = max > 1 ? $"‹  {_count}  ›" : _count.ToString();
            if (_amount != null) _amount.text = _count.ToString();
            if(_amountLessButton != null) {
                _amountLessButton.gameObject.SetActive(max > 1);
            }
            if(_amountMoreButton != null) {
                _amountMoreButton.gameObject.SetActive(max > 1);
            }

            bool can = max > 0;
            // Недоступная кнопка - тёмная с тусклой надписью, чтобы не казалась нажимаемой
            if (_createButton != null && _createButton.targetGraphic != null)
                _createButton.targetGraphic.color = can ? _createColor : new Color(1f, 1f, 1f, 0.04f);
            var createLabel = _createButton != null ? _createButton.transform.Find("Label")?.GetComponent<TMP_Text>() : null;
            if (createLabel != null) createLabel.color = can ? MenuKit.OnAccent : _textDim;

            if (_hint == null) return;
            if (can) { _hint.text = ""; return; }
            if (!_controller.HasIngredients(_shown)) _hint.text = string.Format(_textMissing.Get(), MissingList(_shown));
            else _hint.text = _textTooHeavy.Get();
        }

        private string MissingList(CraftRecipe recipe)
        {
            var parts = new List<string>();
            foreach (var ing in recipe.ingredients)
            {
                if (ing.item == null) continue;
                int lack = ing.count - _controller.GetItemCount(ing.item);
                if (lack > 0) parts.Add($"{ItemName(ing.item)} × {lack}");
            }
            return string.Join(", ", parts);
        }

        // ---------- Вспомогательное ----------

        private CraftRecipe SelectedRecipe()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null ? FindRow(selected)?.recipe : null;
        }

        private Row FindRow(GameObject go)
        {
            foreach (var row in _rows) if (row.selectable != null && row.selectable.gameObject == go) return row;
            return null;
        }

        private Row RowOf(CraftRecipe recipe) => recipe == null ? null : _rows.Find(r => r.recipe == recipe);
        private bool Contains(CraftRecipe recipe) => RowOf(recipe) != null;

        private CraftRecipe FirstRecipe()
        {
            foreach (var row in _rows) if (row.recipe != null) return row.recipe;
            return null;
        }

        private void SelectRowOf(CraftRecipe recipe)
        {
            var row = RowOf(recipe);
            var target = row != null ? row.selectable.gameObject : DefaultSelection;
            if (target != null) EventSystem.current?.SetSelectedGameObject(target);
        }

        private static string ItemName(Item item) => item != null ? item.localizedName : "";
        private static string ItemDescription(Item item) => item != null ? item.localizedDescription : "";
        private static TMP_Text SetText(Transform root, string child, string value) => MenuKit.SetText(root, child, value);
        private static void SetImage(Transform root, string child, Sprite sprite) => MenuKit.SetImage(root, child, sprite);
    }
}
