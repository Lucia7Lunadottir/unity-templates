using System;
using System.Collections.Generic;
using PG.CraftSystem;
using PG.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PG.InventoryManagement
{
    // Вкладка «Вещи»: слева сетка предметов с фильтрами по типу, справа - карточка выбранного и действия с кнопками.
    // Ячейки рисует сам Inventory (номер ячейки = номер предмета), действия выполняет InventoryInteraction - здесь только вид.
    // Геймпад: стик - по сетке, LT/RT - фильтр, X/Y/A/L3/R3 - действия (кнопки InventoryInteraction), B - назад.
    // Собрать разметку по макету: ПКМ по компоненту → «Собрать разметку по макету».
    public class ItemsMenuView : MonoBehaviour, IBackHandler
    {
        [Serializable]
        public class Filter
        {
            public LocalText title = new LocalText("Filter");
            [Tooltip("Какие типы показывать; пусто - все")]
            public List<Item.ItemType> types = new List<Item.ItemType>();
        }

        [SerializeField] private Inventory _inventory;
        [SerializeField] private InventoryInteraction _interaction;
        [SerializeField] private QuickItemSlotInteraction _quickSlots;
        [Tooltip("Затемнение всего экрана за меню (общее с крафтом)")]
        [SerializeField] private GameObject _backdrop;

        [Header("Сетка")]
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private GridLayoutGroup _grid;
        [Tooltip("Ячейка: InventoryCell (Icon, Count), дети Quick (метка быстрого слота), Frame (рамка выделения)")]
        [SerializeField] private InventoryCell _cellTemplate;
        [SerializeField] private TMP_Text _emptyText;

        [Header("Вес")]
        [SerializeField] private TMP_Text _weightText;
        [SerializeField] private RectTransform _weightFill;

        [Header("Фильтры (LT / RT)")]
        [SerializeField] private List<Filter> _filters = new List<Filter>();
        [SerializeField] private RectTransform _chips;
        [SerializeField] private Graphic _chipTemplate;
        [SerializeField] private InputActionReference _filterLeft;
        [SerializeField] private InputActionReference _filterRight;

        [Header("Карточка")]
        [SerializeField] private GameObject _detail;
        [SerializeField] private Image _heroIcon;
        [SerializeField] private TMP_Text _heroName;
        [SerializeField] private TMP_Text _heroTag;
        [SerializeField] private GameObject _descriptionBlock;
        [SerializeField] private TMP_Text _description;
        [SerializeField] private RectTransform _stats;
        [Tooltip("Строка характеристики: дети Name, Value")]
        [SerializeField] private GameObject _statTemplate;

        [Header("Действия (кнопки вызывают InventoryInteraction)")]
        [SerializeField] private Button _useButton;
        [SerializeField] private Button _firstSlotButton;
        [SerializeField] private Button _secondSlotButton;
        [SerializeField] private Button _dropButton;
        [SerializeField] private Button _placeButton;

        [Header("Тексты (ключ локализации + запасной текст)")]
        [SerializeField] private LocalText _textEmpty = new LocalText("items-ui-empty", "The bag is empty");
        [SerializeField] private LocalText _textSatiety = new LocalText("items-ui-satiety", "Satiety");
        [SerializeField] private LocalText _textWeight = new LocalText("items-ui-weight", "Weight");
        [SerializeField] private LocalText _textQuickSlot = new LocalText("items-ui-quick-slot", "Quick slot");
        [SerializeField] private LocalText _textAssignFirst = new LocalText("assign-first-item", "Assign first slot");
        [SerializeField] private LocalText _textAssignSecond = new LocalText("assign-second-item", "Assign second slot");
        [SerializeField] private LocalText _textUnassignFirst = new LocalText("unassign-first-item", "Remove from first slot");
        [SerializeField] private LocalText _textUnassignSecond = new LocalText("unassign-second-item", "Remove from second slot");
        [SerializeField] private LocalText _textPlaceable = new LocalText("craft-ui-placeable", "placed in the world");
        [Tooltip("Названия типов по порядку Item.ItemType: Supplies, Materials, Artifacts, Important")]
        [SerializeField] private LocalText[] _typeNames = { new LocalText("craft-ui-type-supplies", "Supplies"), new LocalText("craft-ui-type-materials", "Materials"), new LocalText("craft-ui-type-artifacts", "Artifacts"), new LocalText("craft-ui-type-important", "Important") };

        [Header("Цвета")]
        [SerializeField] private Color _accent = new Color32(0x77, 0xA7, 0xFF, 0xFF);
        [SerializeField] private Color _text = new Color32(0xEC, 0xDF, 0xC4, 0xFF);
        [SerializeField] private Color _ok = new Color32(0x9C, 0xC9, 0x7A, 0xFF);
        // Linear color space: полупрозрачное в игре плотнее, чем в макете - альфы маленькие
        [SerializeField] private Color _slot = new Color(1f, 1f, 1f, 0.02f);
        [SerializeField] private Color _selectedFill = new Color(119 / 255f, 167 / 255f, 1f, 0.12f);

        private readonly List<Graphic> _spawnedChips = new List<Graphic>();
        private readonly List<GameObject> _spawnedStats = new List<GameObject>();
        private int _filter;
        private int _lastIndex = -1;
        private int _shownIndex = -1;
        private Item _shownItem;
        private bool _dirty = true;
        private GameObject _painted;
        private Item _quick1, _quick2;
        private InputAction _left, _right;

        // ---------- Жизненный цикл ----------

        private void Awake()
        {
            if (_cellTemplate != null) _cellTemplate.gameObject.SetActive(false);
            if (_chipTemplate != null) _chipTemplate.gameObject.SetActive(false);
            if (_statTemplate != null) _statTemplate.SetActive(false);
            Resolve();
            // Ячейки рисует Inventory - теперь в новую сетку
            if (_inventory != null && _grid != null) _inventory.UseCellContainer(_grid.transform, _cellTemplate);

            Bind(_useButton, () => _interaction.Use());
            Bind(_firstSlotButton, () => _interaction.AssignFirstItem());
            Bind(_secondSlotButton, () => _interaction.AssignSecondItem());
            Bind(_dropButton, () => _interaction.Drop());
            Bind(_placeButton, () => _interaction.Place());
        }

        private void Resolve()
        {
            if (_inventory == null) _inventory = Inventory.instance != null ? Inventory.instance : FindAnyObjectByType<Inventory>(FindObjectsInactive.Include);
            if (_interaction == null) _interaction = FindAnyObjectByType<InventoryInteraction>(FindObjectsInactive.Include);
            if (_quickSlots == null) _quickSlots = FindAnyObjectByType<QuickItemSlotInteraction>(FindObjectsInactive.Include);
        }

        // Кнопка действия: мышь нажимает её, геймпад - кнопку InventoryInteraction; курсор с сетки на неё не уходит
        private void Bind(Button button, Action action)
        {
            if (button == null) return;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            MenuKit.ActionLook.Attach(button);
            button.onClick.AddListener(() =>
            {
                if (_interaction == null) return;
                _interaction.targetIndex = _lastIndex;
                action();
                _dirty = true;
                // Карточку перерисовать (подписи «Убрать из слота»): Refresh ниже уже перечитает слоты, и Update изменения не заметит
                _shownItem = null;
                ReselectAfterChange();
            });
        }

        private void OnEnable()
        {
            Resolve();
            if (_inventory != null)
            {
                _inventory.itemAdded += OnItemsChanged;
                _inventory.itemRemoved += OnItemsChanged;
            }
            _shownIndex = -1;
            _shownItem = null;
            _left = MenuKit.ResolveAction(_filterLeft, "UI/TurnPageLeft");
            _right = MenuKit.ResolveAction(_filterRight, "UI/TurnPageRight");
            if (_left != null) { _left.performed += OnFilterLeft; _left.Enable(); }
            if (_right != null) { _right.performed += OnFilterRight; _right.Enable(); }
            if (LocalizationSystem.instance != null) LocalizationSystem.instance.localizationChanged += OnLocalize;
            ConsoleMenuNavigation.Register(this);
            if (_backdrop != null) _backdrop.SetActive(true);
            BuildChips();
            _dirty = true;
            Refresh();
            var target = DefaultSelection;
            if (target != null) EventSystem.current?.SetSelectedGameObject(target);
        }

        private void OnDisable()
        {
            if (_inventory != null)
            {
                _inventory.itemAdded -= OnItemsChanged;
                _inventory.itemRemoved -= OnItemsChanged;
            }
            if (_left != null) _left.performed -= OnFilterLeft;
            if (_right != null) _right.performed -= OnFilterRight;
            if (LocalizationSystem.instance != null) LocalizationSystem.instance.localizationChanged -= OnLocalize;
            ConsoleMenuNavigation.Unregister(this);
            if (_backdrop != null) _backdrop.SetActive(false);
        }

        private void OnItemsChanged(Inventory.RuntimeItem _) => _dirty = true;
        private void OnLocalize(string _) { BuildChips(); _dirty = true; _shownItem = null; }

        private void Update()
        {
            // Быстрые слоты меняются кнопками InventoryInteraction - метки обновляем по факту
            if (_quickSlots != null && (_quickSlots.firstItemSlot.assignedItem != _quick1 || _quickSlots.secondItemSlot.assignedItem != _quick2))
            {
                _dirty = true;
                _shownItem = null;
            }

            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            // Фильтры - для мыши и LT/RT; курсор на них не задерживается
            if (selected != null && _chips != null && selected.transform.IsChildOf(_chips))
            {
                var target = DefaultSelection;
                if (target != null) { events.SetSelectedGameObject(target); selected = target; }
            }
            int index = CellIndex(selected);
            if (index >= 0)
            {
                _lastIndex = index;
                if (_interaction != null) _interaction.targetIndex = index;
                if (index != _shownIndex || _inventory.items[index].item != _shownItem) ShowItem(index);
                MenuKit.ScrollTo(_scroll, (RectTransform)selected.transform);
            }
            if (selected != _painted) Paint(selected);
        }

        // Inventory перерисовывает ячейки сразу при изменении - мы подстраиваемся раз в конце кадра
        private void LateUpdate()
        {
            if (_dirty) Refresh();
        }

        // ---------- Консольное меню ----------

        public bool HandleBack() => false;

        public GameObject DefaultSelection
        {
            get
            {
                var cells = Cells();
                if (_lastIndex >= 0)
                {
                    // Тот же номер или ближайший видимый - после «использовать»/«выбросить» предмет мог исчезнуть
                    for (int i = Mathf.Min(_lastIndex, cells.Count - 1); i >= 0; i--)
                        if (cells[i].activeSelf) return cells[i];
                }
                foreach (var cell in cells) if (cell.activeSelf) return cell;
                return null;
            }
        }

        private void ReselectAfterChange()
        {
            Refresh();
            var target = DefaultSelection;
            if (target != null) EventSystem.current?.SetSelectedGameObject(target);
        }

        // ---------- Фильтры ----------

        private void OnFilterLeft(InputAction.CallbackContext _) => StepFilter(-1);
        private void OnFilterRight(InputAction.CallbackContext _) => StepFilter(1);

        private void StepFilter(int direction)
        {
            if (!isActiveAndEnabled || _filters.Count == 0) return;
            _filter = (_filter + direction + _filters.Count) % _filters.Count;
            _lastIndex = -1;
            ReselectAfterChange();
        }

        private void BuildChips()
        {
            foreach (var chip in _spawnedChips) if (chip != null) Destroy(chip.gameObject);
            _spawnedChips.Clear();
            if (_chipTemplate == null || _chips == null) return;
            for (int i = 0; i < _filters.Count; i++)
            {
                var chip = Instantiate(_chipTemplate, _chips);
                chip.gameObject.SetActive(true);
                chip.name = "Chip " + i;
                var label = chip.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = _filters[i].title.Get();
                int index = i;
                // Категория нажимается мышью; курсор геймпада на неё не встаёт (там LT/RT)
                chip.raycastTarget = true;
                var button = MenuKit.Ensure<Button>(chip.gameObject);
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(() => { _filter = index; _lastIndex = -1; ReselectAfterChange(); });
                // Под мышью невыбранная категория - голубая плашка (MenuKit.PaintChip)
                MenuKit.PaintChip(chip, index == _filter);
                _spawnedChips.Add(chip);
            }
        }

        private bool Passes(Item item)
        {
            if (_filters.Count == 0 || item == null) return true;
            var types = _filters[Mathf.Clamp(_filter, 0, _filters.Count - 1)].types;
            return types.Count == 0 || types.Contains(item.itemType);
        }

        // ---------- Сетка ----------

        private void Refresh()
        {
            _dirty = false;
            if (_inventory == null || _grid == null) return;
            if (_quickSlots != null)
            {
                _quick1 = _quickSlots.firstItemSlot.assignedItem;
                _quick2 = _quickSlots.secondItemSlot.assignedItem;
            }

            var cells = Cells();
            var visible = new List<Selectable>();
            for (int i = 0; i < cells.Count; i++)
            {
                var item = i < _inventory.items.Count ? _inventory.items[i].item : null;
                bool show = item != null && Passes(item);
                cells[i].SetActive(show);
                if (!show) continue;
                if (cells[i].TryGetComponent(out Selectable s))
                {
                    // Мышью предмет выбирается кликом, не наведением - иначе он меняется по пути к кнопкам действий
                    s.transition = Selectable.Transition.None;
                    visible.Add(s);
                    // Под мышью - голубая заливка без рамки (MenuKit.PaintCell; рамка и увеличение - только у выбранного)
                }
                var quick = cells[i].transform.Find("Quick");
                if (quick != null)
                {
                    string mark = item == _quick1 ? "1" : item == _quick2 ? "2" : "";
                    quick.gameObject.SetActive(mark.Length > 0);
                    var t = quick.GetComponentInChildren<TMP_Text>(true);
                    if (t != null) t.text = mark;
                }
            }
            MenuKit.LinkGrid(visible, MenuKit.GridColumns(_grid));

            for (int i = 0; i < _spawnedChips.Count; i++) MenuKit.PaintChip(_spawnedChips[i], i == _filter);

            if (_weightText != null) _weightText.text = $"{_inventory.weight:0.##} / {_inventory.maxWeight:0.##}";
            if (_weightFill != null) _weightFill.anchorMax = new Vector2(_inventory.maxWeight > 0 ? Mathf.Clamp01(_inventory.weight / _inventory.maxWeight) : 0f, 1f);

            if (_emptyText != null)
            {
                _emptyText.gameObject.SetActive(visible.Count == 0);
                _emptyText.text = _textEmpty.Get();
            }
            if ((visible.Count == 0 || _shownIndex < 0) && _detail != null) _detail.SetActive(false);
            _painted = null;
        }

        // Ячейка под курсором: рамка, лёгкая подсветка, чуть крупнее
        private void Paint(GameObject selected)
        {
            _painted = selected;
            foreach (var cell in Cells())
            {
                bool on = cell == selected;
                MenuKit.PaintCell(cell, on, _slot, _selectedFill);
            }
        }

        private List<GameObject> Cells()
        {
            var list = new List<GameObject>();
            if (_grid == null) return list;
            foreach (Transform child in _grid.transform) list.Add(child.gameObject);
            return list;
        }

        private int CellIndex(GameObject go)
        {
            if (go == null || _grid == null || go.transform.parent != _grid.transform) return -1;
            int index = go.transform.GetSiblingIndex();
            return index < _inventory.items.Count ? index : -1;
        }

        // ---------- Карточка ----------

        private void ShowItem(int index)
        {
            _shownIndex = index;
            var runtime = _inventory.items[index];
            var item = runtime.item;
            _shownItem = item;
            if (_detail != null) _detail.SetActive(true);

            MenuKit.ShowIcon(_heroIcon, item.icon);
            if (_heroName != null) _heroName.text = item.localizedName;
            if (_heroTag != null)
            {
                string tag = (int)item.itemType < _typeNames.Length ? _typeNames[(int)item.itemType].Get() : "";
                if (item is PlaceableItem) tag += " · " + _textPlaceable.Get();
                _heroTag.text = tag;
            }

            string description = item.localizedDescription;
            if (_descriptionBlock != null) _descriptionBlock.SetActive(!string.IsNullOrWhiteSpace(description));
            if (_description != null) _description.text = description;

            foreach (var go in _spawnedStats) { go.SetActive(false); Destroy(go); }
            _spawnedStats.Clear();
            if (item.satiety != 0f) AddStat(_textSatiety.Get(), "+" + item.satiety.ToString("0.##"), _ok);
            AddStat(_textWeight.Get(), runtime.count > 1 ? $"{item.weight:0.##} × {runtime.count} = {item.weight * runtime.count:0.##}" : item.weight.ToString("0.##"), _text);
            string slot = item == _quick1 ? "1" : item == _quick2 ? "2" : "";
            if (slot.Length > 0) AddStat(_textQuickSlot.Get(), slot, _accent);

            SetAction(_useButton, item.isUsable);
            SetAction(_firstSlotButton, item.isUsable);
            SetAction(_secondSlotButton, item.isUsable);
            // Уже в слоте - кнопка убирает из него
            SetLabel(_firstSlotButton, "[Inventory/Assign First Item]", item == _quick1 ? _textUnassignFirst : _textAssignFirst);
            SetLabel(_secondSlotButton, "[Inventory/Assign Second Item]", item == _quick2 ? _textUnassignSecond : _textAssignSecond);
            SetAction(_dropButton, item.itemType != Item.ItemType.Important);
            SetAction(_placeButton, InventoryBridge.CanPlace(item));
        }

        private void AddStat(string name, string value, Color valueColor)
        {
            if (_statTemplate == null || _stats == null) return;
            var row = Instantiate(_statTemplate, _stats);
            row.SetActive(true);
            _spawnedStats.Add(row);
            var n = row.transform.Find("Name")?.GetComponent<TMP_Text>();
            if (n != null) n.text = name;
            var v = row.transform.Find("Value")?.GetComponent<TMP_Text>();
            if (v != null) { v.text = value; v.color = valueColor; }
        }

        private static void SetLabel(Button button, string prompts, LocalText text)
        {
            var label = button != null ? button.transform.Find("Label")?.GetComponent<PromptLabel>() : null;
            if (label != null) label.Setup(prompts, text.key, text.text, true);
        }

        // Недоступное действие видно, но тёмное с тусклой надписью - даже главная кнопка не выглядит нажимаемой
        private void SetAction(Button button, bool available)
        {
            if (button == null) return;
            button.interactable = available;
            MenuKit.ActionLook.Attach(button).Refresh();
        }
    }
}
