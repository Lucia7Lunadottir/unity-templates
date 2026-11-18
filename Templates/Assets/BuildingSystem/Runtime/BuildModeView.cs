using System.Collections.Generic;
using PG.CraftSystem;
using PG.InventoryManagement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// HUD Build Mode по макетам (UI Mockups/Inventory/BuildMode, BuildModeDismantle): плашка режима, статус над «призраком»,
// снизу - категории, лента чертежей и стоимость; на своей постройке - карточка «Разобрать». Всё читает из BuildMode.
// Собрать разметку по макету: ПКМ по компоненту → «Собрать разметку по макету».
public class BuildModeView : MonoBehaviour
{
    [Tooltip("Пусто - найдётся сам")]
    [SerializeField] private BuildMode _mode;
    [SerializeField] private GameObject _root;

    [Header("Статус над «призраком»")]
    [SerializeField] private RectTransform _statusPlate;
    [SerializeField] private TMP_Text _status;

    [Header("Лента")]
    [SerializeField] private RectTransform _chips;
    [SerializeField] private Graphic _chipTemplate;
    [SerializeField] private RectTransform _slots;
    [Tooltip("Ячейка: Icon, Frame")]
    [SerializeField] private RectTransform _slotTemplate;
    [SerializeField] private TMP_Text _name;
    [SerializeField] private RectTransform _cost;
    [Tooltip("Материал: Icon, Count")]
    [SerializeField] private RectTransform _partTemplate;

    [Header("Разобрать")]
    [SerializeField] private GameObject _targetCard;
    [SerializeField] private TMP_Text _targetName;
    [SerializeField] private RectTransform _refund;
    [SerializeField] private GameObject _refundCaption;

    [Header("Подсказки")]
    [SerializeField] private TMP_Text _placeHint;
    [Tooltip("Подсказка «▼ Строительство» в Game – Canvas: видна, пока режим выключен")]
    [SerializeField] private GameObject _gameHint;
    [Tooltip("Подсказка «Строительство» видна только вместе с этой (Magic Mode прячется в магии и телекинезе). Пусто - сосед Magic Mode")]
    [SerializeField] private GameObject _gameHintFollow;

    [Header("Тексты (ключ локализации + запасной текст)")]
    [SerializeField] private LocalText _textOk = new LocalText("build-ui-ok", "Can be placed");
    [SerializeField] private LocalText _textLacking = new LocalText("build-ui-lacking", "Not enough materials");
    [SerializeField] private LocalText _textBlocked = new LocalText("build-ui-blocked", "Something is in the way");
    [SerializeField] private LocalText _textBlockedByYou = new LocalText("build-ui-blocked-you", "You are in the way");
    [SerializeField] private LocalText _textBlockedByLittle = new LocalText("build-ui-blocked-little", "Little Lieris is in the way");
    [SerializeField] private LocalText _textSteep = new LocalText("build-ui-steep", "Too steep");
    [SerializeField] private LocalText _textNoBuild = new LocalText("build-ui-no-build", "You can't build here");
    [SerializeField] private LocalText _textWrongSurface = new LocalText("build-ui-wrong-surface", "Only on snow or ice");
    [SerializeField] private LocalText _textDismantle = new LocalText("build-ui-dismantle-target", "Dismantle: {0}");
    [SerializeField] private LocalText _textPickUp = new LocalText("build-ui-pick-up-target", "Pick up: {0}");

    private readonly List<Graphic> _spawnedChips = new List<Graphic>();
    private readonly List<RectTransform> _spawnedSlots = new List<RectTransform>();
    private readonly List<RectTransform> _spawnedParts = new List<RectTransform>();
    private readonly List<RectTransform> _spawnedRefund = new List<RectTransform>();
    private int _shownCategory = -1, _shownBlueprint = -1, _shownCount = -1;
    private Object _shownTarget;
    private Camera _camera;

    private void Awake()
    {
        if (_mode == null) _mode = FindAnyObjectByType<BuildMode>(FindObjectsInactive.Include);
        foreach (var template in new Component[] { _chipTemplate, _slotTemplate, _partTemplate })
            if (template != null) template.gameObject.SetActive(false);
        if (_root != null) _root.SetActive(false);
    }

    private void LateUpdate()
    {
        bool show = _mode != null && _mode.isActive && Time.timeScale > 0f && !PG.MenuManagement.UIManager.IsAnyPanelOpen;
        if (_gameHint != null)
        {
            if (_gameHintFollow == null) _gameHintFollow = _gameHint.transform.parent.Find("Magic Mode")?.gameObject;
            bool hint = _mode != null && !_mode.isActive && (_gameHintFollow == null || _gameHintFollow.activeInHierarchy);
            if (_gameHint.activeSelf != hint) _gameHint.SetActive(hint);
        }
        if (_root != null && _root.activeSelf != show) { _root.SetActive(show); _shownCategory = -1; _shownTarget = null; }
        if (!show) return;
        if (_camera == null) _camera = Camera.main;

        var blueprints = _mode.Blueprints;
        if (_mode.categoryIndex != _shownCategory || _mode.blueprintIndex != _shownBlueprint || blueprints.Count != _shownCount)
            Rebuild(blueprints);
        UpdateCost(_mode.Selected);
        UpdateStatus();
        UpdateTarget();
    }

    // ---------- Лента ----------

    private void Rebuild(List<BuildMode.Entry> blueprints)
    {
        _shownCategory = _mode.categoryIndex;
        _shownBlueprint = _mode.blueprintIndex;
        _shownCount = blueprints.Count;

        var categories = _mode.catalog != null ? _mode.catalog.categories : null;
        int chipCount = categories != null ? categories.Count : 0;
        Fill(_spawnedChips, _chipTemplate, _chips, chipCount);
        for (int i = 0; i < chipCount; i++)
        {
            var label = _spawnedChips[i].GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = categories[i].title.Get();
            MenuKit.PaintChip(_spawnedChips[i], i == _mode.categoryIndex);
        }

        FillRects(_spawnedSlots, _slotTemplate, _slots, blueprints.Count);
        for (int i = 0; i < blueprints.Count; i++)
        {
            var slot = _spawnedSlots[i];
            var item = blueprints[i].shown;
            MenuKit.SetImage(slot, "Icon", item != null ? item.icon : null);
            bool on = i == _mode.blueprintIndex;
            MenuKit.PaintCell(slot.gameObject, on, MenuKit.Slot);
            if (slot.TryGetComponent(out Graphic back)) back.color = on ? MenuKit.SelectedFill : MenuKit.Slot;
        }
        var shown = _mode.Selected?.shown;
        if (_name != null) _name.text = shown != null ? shown.localizedName : "";
    }

    // «есть / нужно» у материалов, нехватка - красным; ячейки без материалов бледнее
    private void UpdateCost(BuildMode.Entry entry)
    {
        var cost = entry != null ? entry.cost : new List<(Item item, int count)>();
        FillRects(_spawnedParts, _partTemplate, _cost, cost.Count);
        for (int i = 0; i < cost.Count; i++)
        {
            var (item, need) = cost[i];
            int have = _mode.Have(item);
            MenuKit.SetImage(_spawnedParts[i], "Icon", item != null ? item.icon : null);
            var text = MenuKit.SetText(_spawnedParts[i], "Count", have + " / " + need);
            if (text != null) text.color = have >= need ? MenuKit.TextSoft : MenuKit.Missing;
        }
        var blueprints = _mode.Blueprints;
        for (int i = 0; i < blueprints.Count && i < _spawnedSlots.Count; i++)
            MenuKit.Ensure<CanvasGroup>(_spawnedSlots[i].gameObject).alpha = _mode.CanAfford(blueprints[i]) ? 1f : 0.45f;
    }

    // ---------- Статус и разбор ----------

    private void UpdateStatus()
    {
        bool visible = _mode.ghostVisible && _camera != null;
        if (_status == null || _statusPlate == null) return;
        if (_statusPlate.gameObject.activeSelf != visible) _statusPlate.gameObject.SetActive(visible);
        if (_placeHint != null) _placeHint.alpha = visible && _mode.status == BuildMode.Status.Ok ? 1f : 0.45f;
        if (!visible) return;

        Vector3 screen = _camera.WorldToScreenPoint(_mode.statusPoint);
        var canvas = _status.canvas;
        if (screen.z > 0f && canvas != null && RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_statusPlate.parent, screen, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var local))
            _statusPlate.anchoredPosition = local;

        bool ok = _mode.status == BuildMode.Status.Ok;
        _status.color = ok ? MenuKit.Ok : MenuKit.Missing;
        _status.text = _mode.status switch
        {
            BuildMode.Status.Ok => _textOk.Get(),
            BuildMode.Status.Lacking => _textLacking.Get(),
            BuildMode.Status.BlockedByYou => _textBlockedByYou.Get(),
            BuildMode.Status.BlockedByLittle => _textBlockedByLittle.Get(),
            BuildMode.Status.Steep => _textSteep.Get(),
            BuildMode.Status.NoBuild => _textNoBuild.Get(),
            BuildMode.Status.WrongSurface => _textWrongSurface.Get(),
            _ => _textBlocked.Get()
        };
    }

    private void UpdateTarget()
    {
        var target = _mode.target;
        if (_targetCard != null && _targetCard.activeSelf != (target != null)) _targetCard.SetActive(target != null);
        var key = target?.key;
        if (key == _shownTarget) return;
        _shownTarget = key;
        if (target == null) return;
        var item = target.shown;
        bool isItem = target.recipe == null;
        if (_targetName != null) _targetName.text = string.Format((isItem ? _textPickUp : _textDismantle).Get(), item != null ? item.localizedName : "");
        // Мебель - что вернётся; предмет - просто подобрать
        var refund = isItem ? new List<(Item item, int count)>() : target.cost;
        if (_refund != null) _refund.gameObject.SetActive(!isItem);
        if (_refundCaption != null) _refundCaption.SetActive(!isItem);
        FillRects(_spawnedRefund, _partTemplate, _refund, refund.Count);
        for (int i = 0; i < refund.Count; i++)
        {
            var (part, count) = refund[i];
            MenuKit.SetImage(_spawnedRefund[i], "Icon", part != null ? part.icon : null);
            var text = MenuKit.SetText(_spawnedRefund[i], "Count", count.ToString());
            if (text != null) text.color = MenuKit.TextSoft;
        }
    }

    // ---------- Шаблоны ----------

    private static void Fill(List<Graphic> spawned, Graphic template, RectTransform parent, int count)
    {
        if (template == null || parent == null) return;
        while (spawned.Count < count) { var g = Instantiate(template, parent); g.gameObject.SetActive(true); spawned.Add(g); }
        for (int i = 0; i < spawned.Count; i++) spawned[i].gameObject.SetActive(i < count);
    }

    private static void FillRects(List<RectTransform> spawned, RectTransform template, RectTransform parent, int count)
    {
        if (template == null || parent == null) return;
        while (spawned.Count < count) { var r = Instantiate(template, parent); r.gameObject.SetActive(true); spawned.Add(r); }
        for (int i = 0; i < spawned.Count; i++) spawned[i].gameObject.SetActive(i < count);
    }
}
