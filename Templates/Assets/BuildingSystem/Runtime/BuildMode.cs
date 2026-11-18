using System.Collections.Generic;
using PG.CraftSystem;
using PG.InventoryManagement;
using PG.MenuManagement;
using UnityEngine;
using UnityEngine.InputSystem;

// Build Mode: расстановка мебели по чертежам (BuildCatalog) и раскладка предметов из сумки. Игра идёт, игрок ходит; перед ним - «призрак».
// Вход - Player/Build (D-pad вниз, B), только на земле, без магии, полёта, телекинеза, диалога и открытых окон.
// Внутри - карта Build: RT ставит, LB/RB что ставить, D-pad ◀▶ поворот, ▲ категория, Y разобрать мебель, D-pad ▼ выйти (B - бег, не трогаем).
// Мебель: материалы списываются при установке, разбор возвращает их. Предмет из сумки кладётся как выброшенный (его модель на земле);
// на лежащие предметы можно класть другие - подбираются они как обычно, не в Build Mode. Place в «Вещах» открывает режим с этим предметом (EnterWith).
// Всё сохраняет DroppedItemManager. Объекты сцены, поставленные не игроком, не трогаются. HUD - BuildModeView.
public class BuildMode : MonoBehaviour, ISaveable
{
    public enum Status { Ok, Lacking, Blocked, BlockedByYou, BlockedByLittle, Steep, NoBuild, NoGround, WrongSurface }

    // Что ставим: мебель по чертежу или предмет из сумки
    public class Entry
    {
        public CraftRecipe recipe;
        public Item item;

        public Item shown => recipe != null ? recipe.resultItem : item;
        public Object key => recipe != null ? recipe : item;
        public Item.DropEntry drop => item != null && item.dropPrefabs != null && item.dropPrefabs.Length > 0 ? item.dropPrefabs[0] : null;
        public GameObject prefab => recipe != null ? BuildCatalog.PlaceableOf(recipe)?.worldPrefab : drop?.prefab;
        // Масштаб корня модели: у мебели - как в префабе, у предмета - как у выброшенного
        public Vector3 scale => recipe != null ? prefab.transform.localScale : Vector3.one * drop.scale;

        // Стоимость: ингредиенты чертежа или сам предмет
        public List<(Item item, int count)> cost
        {
            get
            {
                var list = new List<(Item, int)>();
                if (recipe != null) { foreach (var i in recipe.ingredients) if (i.item != null) list.Add((i.item, i.count)); }
                else if (item != null) list.Add((item, 1));
                return list;
            }
        }
    }

    [SerializeField] private BuildCatalog _catalog;
    [Tooltip("Полупрозрачный материал «призрака» (цвет ставится кодом)")]
    [SerializeField] private Material _ghostMaterial;
    [SerializeField] private Color _okColor = new Color(0.45f, 0.85f, 0.55f, 0.45f);
    [SerializeField] private Color _blockedColor = new Color(0.95f, 0.4f, 0.35f, 0.45f);
    [Tooltip("Записывать постройки сразу. Выключено - вместе с сохранением игры, как у Inventory с выключенным Auto Save")]
    [SerializeField] private bool _autoSave = true;

    [Header("Установка")]
    [Tooltip("Как далеко от игрока можно ставить, м")]
    [SerializeField] private float _reach = 7f;
    [Tooltip("Круче этого (градусы) - не ставится")]
    [SerializeField] private float _maxSlope = 30f;
    [Tooltip("Поворот при удержании, градусов в секунду")]
    [SerializeField] private float _rotateSpeed = 120f;
    [SerializeField] private LayerMask _groundMask = ~0;
    [TagDropdown]
    [SerializeField] private string _playerTag = "Player";
    [TagDropdown]
    [SerializeField] private string _companionTag = "Little Lieris";

    [Header("Ввод (пусто - по имени)")]
    [SerializeField] private InputActionReference _enter;

    public bool isActive { get; private set; }
    public Status status { get; private set; }
    public int categoryIndex { get; private set; }
    public int blueprintIndex { get; private set; }
    public BuildCatalog catalog => _catalog;
    // Своя постройка под прицелом (разобрать) - иначе null
    public Entry target { get; private set; }
    // Точка над «призраком» для подписи статуса
    public Vector3 statusPoint { get; private set; }
    public bool ghostVisible => _ghost != null && _ghost.activeSelf;

    public event System.Action<bool> activeChanged;

    private InputAction _enterAction, _place, _previous, _next, _rotateLeft, _rotateRight, _category, _dismantle, _exit;
    private Transform _player;
    private Inventory _inventory;
    private Camera _camera;

    private GameObject _ghost;
    private Object _ghostKey;
    private Entry _ghostEntry;
    private readonly List<Renderer> _ghostRenderers = new List<Renderer>();
    private MaterialPropertyBlock _block;
    private Vector3 _boxCenter, _boxHalf;   // объём модели в её системе координат (для проверки пересечений)
    private float _bottom;                  // от точки опоры до низа модели
    private float _yaw;
    private int _toggleFrame = -1;
    private float _nextPlaceTime;
    private GameObject _targetObject;
    private readonly RaycastHit[] _hits = new RaycastHit[16];
    private readonly Collider[] _overlaps = new Collider[16];
    private readonly List<Entry> _entries = new List<Entry>();
    private int _entriesFrame = -1;
    private Item _pendingItem;
    private float _pendingUntil;

    // ---------- Жизненный цикл ----------

    private void Awake()
    {
        _enterAction = MenuKit.ResolveAction(_enter, "Player/Build");
        _place = Find("Build/Place");
        _previous = Find("Build/Previous");
        _next = Find("Build/Next");
        _rotateLeft = Find("Build/Rotate Left");
        _rotateRight = Find("Build/Rotate Right");
        _category = Find("Build/Category");
        _dismantle = Find("Build/Dismantle");
        _exit = Find("Build/Exit");
        _block = new MaterialPropertyBlock();
    }

    private static InputAction Find(string path) => InputSystem.actions != null ? InputSystem.actions.FindAction(path) : null;

    private void OnEnable()
    {
        if (_enterAction != null) _enterAction.performed += OnEnter;
        if (_exit != null) _exit.performed += OnExit;
        if (_place != null) _place.performed += OnPlace;
        if (_dismantle != null) _dismantle.performed += OnDismantle;
        if (_previous != null) _previous.performed += OnPrevious;
        if (_next != null) _next.performed += OnNext;
        if (_category != null) _category.performed += OnCategory;
    }

    private void OnDisable()
    {
        if (_enterAction != null) _enterAction.performed -= OnEnter;
        if (_exit != null) _exit.performed -= OnExit;
        if (_place != null) _place.performed -= OnPlace;
        if (_dismantle != null) _dismantle.performed -= OnDismantle;
        if (_previous != null) _previous.performed -= OnPrevious;
        if (_next != null) _next.performed -= OnNext;
        if (_category != null) _category.performed -= OnCategory;
        if (isActive) Exit();
    }

    private void Resolve()
    {
        if (_player == null)
        {
            var go = GameObject.FindGameObjectWithTag(_playerTag);
            if (go != null) _player = go.transform;
        }
        if (_inventory == null) _inventory = Inventory.instance != null ? Inventory.instance : FindAnyObjectByType<Inventory>(FindObjectsInactive.Include);
        if (_camera == null) _camera = Camera.main;
    }

    // ---------- Вход / выход ----------

    // Вне режимов D-pad свободен; в магии, полёте, телекинезе, диалоге и окнах - не мешаем им
    public bool CanEnter()
    {
        Resolve();
        if (_catalog == null || _camera == null || _player == null || _inventory == null) return false;
        if (Time.timeScale <= 0f || UIManager.IsAnyPanelOpen) return false;
        // Магия, полёт, телекинез, диалог, нет опоры под ногами - решает игра (BuildBridge.enterBlockers)
        return !BuildBridge.IsEnterBlocked(_player);
    }

    private void OnEnter(InputAction.CallbackContext context)
    {
        if (isActive || Time.frameCount == _toggleFrame || !CanEnter()) return;
        // Рядом несколько взаимодействий - D-pad выбирает из них (клавиатура не мешает: там колёсико)
        if (context.control?.device is Gamepad && PG.InteractSystem.TriggerInteractManager.instance != null && PG.InteractSystem.TriggerInteractManager.instance.hasChoice) return;
        _toggleFrame = Time.frameCount;
        isActive = true;
        SetGameplayInput(false);
        ClampSelection();
        activeChanged?.Invoke(true);
    }

    private void OnExit(InputAction.CallbackContext _)
    {
        if (!isActive || Time.frameCount == _toggleFrame || Paused) return;
        _toggleFrame = Time.frameCount;
        Exit();
    }

    public void Exit()
    {
        if (!isActive) return;
        isActive = false;
        SetGameplayInput(true);
        DestroyGhost();
        target = null;
        _targetObject = null;
        activeChanged?.Invoke(false);
    }

    // Атака, рывок, магия и прочее на тех же кнопках выключены, пока строим (контекст ввода Build)
    private void SetGameplayInput(bool enable)
    {
        BuildBridge.RaiseGameplayInput(this, enable);
    }

    private bool Paused => Time.timeScale <= 0f || UIManager.IsAnyPanelOpen;

    // ---------- Что ставить ----------

    // Текущая категория: чертежи мебели или вещи из сумки (у которых есть модель на земле)
    public List<Entry> Blueprints
    {
        get
        {
            if (_entriesFrame == Time.frameCount) return _entries;
            _entriesFrame = Time.frameCount;
            _entries.Clear();
            if (_catalog == null || _catalog.categories.Count == 0) return _entries;
            var category = _catalog.categories[Mathf.Clamp(categoryIndex, 0, _catalog.categories.Count - 1)];
            if (category.inventoryItems)
            {
                if (_inventory != null)
                    foreach (var runtime in _inventory.items)
                    {
                        var entry = new Entry { item = runtime.item };
                        if (BuildCatalog.CanLay(runtime.item)) _entries.Add(entry);
                    }
            }
            else
            {
                foreach (var recipe in category.blueprints)
                    if (BuildCatalog.PlaceableOf(recipe) != null && _catalog.IsUnlocked(recipe)) _entries.Add(new Entry { recipe = recipe });
            }
            return _entries;
        }
    }

    public Entry Selected
    {
        get
        {
            var list = Blueprints;
            return list.Count > 0 ? list[Mathf.Clamp(blueprintIndex, 0, list.Count - 1)] : null;
        }
    }

    private void ClampSelection()
    {
        if (_catalog == null || _catalog.categories.Count == 0) { categoryIndex = 0; blueprintIndex = 0; return; }
        categoryIndex = Mathf.Clamp(categoryIndex, 0, _catalog.categories.Count - 1);
        _entriesFrame = -1;
        blueprintIndex = Mathf.Clamp(blueprintIndex, 0, Mathf.Max(0, Blueprints.Count - 1));
    }

    private void OnPrevious(InputAction.CallbackContext _) => Step(-1);
    private void OnNext(InputAction.CallbackContext _) => Step(1);

    private void Step(int direction)
    {
        if (!isActive || Paused) return;
        int count = Blueprints.Count;
        if (count == 0) return;
        blueprintIndex = (Mathf.Clamp(blueprintIndex, 0, count - 1) + direction + count) % count;
    }

    private void OnCategory(InputAction.CallbackContext _)
    {
        if (!isActive || Paused || _catalog == null || _catalog.categories.Count == 0) return;
        categoryIndex = (categoryIndex + 1) % _catalog.categories.Count;
        blueprintIndex = 0;
        _entriesFrame = -1;
    }

    // Хватает ли в сумке того, что нужно (материалов или самого предмета)
    public bool CanAfford(Entry entry)
    {
        if (entry == null || _inventory == null) return false;
        foreach (var (item, count) in entry.cost)
            if (_inventory.GetItemCount(item) < count) return false;
        return true;
    }

    public int Have(Item item) => _inventory != null && item != null ? _inventory.GetItemCount(item) : 0;

    // ---------- Кадр ----------

    // Place в «Вещах»: меню закрывается, режим открывается на категории предметов с этим предметом (ждём, пока закроется пауза)
    public void EnterWith(Item item)
    {
        _pendingItem = item;
        _pendingUntil = Time.unscaledTime + 1f;
    }

    private void TryPendingEnter()
    {
        if (_pendingItem == null || isActive) return;
        if (Time.unscaledTime > _pendingUntil) { _pendingItem = null; return; }
        if (!CanEnter()) return;
        int category = _catalog.categories.FindIndex(c => c.inventoryItems);
        if (category >= 0)
        {
            categoryIndex = category;
            _entriesFrame = -1;
            blueprintIndex = Mathf.Max(0, Blueprints.FindIndex(e => e.item == _pendingItem));
        }
        _pendingItem = null;
        OnEnter(default);
    }

    private void Update()
    {
        TryPendingEnter();
        if (!isActive) return;
        Resolve();
        // Взлетела, начался диалог или пропала камера - выходим
        if (BuildBridge.ShouldExit() || _camera == null) { Exit(); return; }
        if (Paused) return;

        float turn = (_rotateRight != null && _rotateRight.IsPressed() ? 1f : 0f) - (_rotateLeft != null && _rotateLeft.IsPressed() ? 1f : 0f);
        _yaw += turn * _rotateSpeed * Time.deltaTime;

        var ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Aim(ray, out RaycastHit hit))
        {
            ClearTarget();
            ShowGhost(false);
            status = Status.NoGround;
            return;
        }

        // Своя постройка под прицелом - карточка «разобрать» вместо «призрака»
        var own = OwnEntry(hit.collider, out var ownObject);
        if (own != null)
        {
            target = own;
            _targetObject = ownObject;
            ShowGhost(false);
            return;
        }
        ClearTarget();

        var selected = Selected;
        if (selected == null || selected.prefab == null) { ShowGhost(false); status = Status.NoGround; return; }
        if (_ghostKey != selected.key) BuildGhost(selected);
        ShowGhost(true);
        PlaceGhost(hit);
        status = Validate(hit, selected);
        Tint(status == Status.Ok ? _okColor : _blockedColor);
    }

    private void ClearTarget()
    {
        target = null;
        _targetObject = null;
    }

    // Ближайшая поверхность под прицелом в пределах досягаемости; игрок, Малышка и триггеры пропускаются
    private bool Aim(Ray ray, out RaycastHit best)
    {
        best = default;
        float reachFromCamera = _reach + Vector3.Distance(_camera.transform.position, _player.position);
        int count = Physics.RaycastNonAlloc(ray, _hits, reachFromCamera, _groundMask, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var h = _hits[i];
            if (h.distance >= bestDistance || IsTagged(h.collider, _playerTag) || IsTagged(h.collider, _companionTag)) continue;
            if (Vector3.Distance(h.point, _player.position) > _reach) continue;
            best = h;
            bestDistance = h.distance;
        }
        return bestDistance < float.MaxValue;
    }

    private static bool IsTagged(Component c, string tag)
    {
        for (var t = c.transform; t != null; t = t.parent)
            if (!string.IsNullOrEmpty(tag) && t.CompareTag(tag)) return true;
        return false;
    }

    // Постройка по чертежу; предметы и объекты сцены - null (на предметы кладут другие)
    private Entry OwnEntry(Collider collider, out GameObject owner)
    {
        owner = null;
        var tag = collider.GetComponentInParent<DroppedItemTag>();
        var container = _inventory != null ? _inventory.itemContainer : null;
        if (tag == null || container == null || tag.itemId < 0 || tag.itemId >= container.items.Length) return null;
        var item = container.items[tag.itemId];
        owner = tag.gameObject;
        if (!tag.isPlaced) return null;
        var recipe = _catalog != null ? _catalog.Find(item) : null;
        return recipe != null ? new Entry { recipe = recipe } : null;
    }

    // ---------- «Призрак» ----------

    // Копия только мешей: скрипты, свет, физика и звук модели не просыпаются
    private void BuildGhost(Entry entry)
    {
        DestroyGhost();
        var prefab = entry.prefab;
        var rootScale = entry.scale;
        _ghostKey = entry.key;
        _ghostEntry = entry;
        _ghost = new GameObject("Build Ghost");
        _ghost.transform.localScale = rootScale;
        var rootInverse = prefab.transform.worldToLocalMatrix;
        var bounds = new Bounds();
        bool first = true;
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer source) || !source.enabled) continue;
            var local = rootInverse * filter.transform.localToWorldMatrix;
            var part = new GameObject(filter.name);
            part.transform.SetParent(_ghost.transform, false);
            part.transform.localPosition = local.GetColumn(3);
            part.transform.localRotation = local.rotation;
            part.transform.localScale = local.lossyScale;
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var renderer = part.AddComponent<MeshRenderer>();
            var materials = new Material[filter.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = _ghostMaterial;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ghostRenderers.Add(renderer);

            // Объём в системе корня (с его масштабом): углы меша через матрицу части
            var m = Matrix4x4.Scale(rootScale) * local;
            var mb = filter.sharedMesh.bounds;
            for (int c = 0; c < 8; c++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(corner);
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                else bounds.Encapsulate(p);
            }
        }
        _bottom = bounds.min.y;
        _boxCenter = bounds.center;
        _boxHalf = bounds.extents;
    }

    private void DestroyGhost()
    {
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
        _ghostKey = null;
        _ghostEntry = null;
        _ghostRenderers.Clear();
    }

    private void ShowGhost(bool on)
    {
        if (_ghost != null && _ghost.activeSelf != on) _ghost.SetActive(on);
    }

    private Quaternion Rotation => Quaternion.Euler(0f, _camera.transform.eulerAngles.y + 180f + _yaw, 0f);

    private void PlaceGhost(RaycastHit hit)
    {
        _ghost.transform.SetPositionAndRotation(hit.point - Vector3.up * _bottom, Rotation);
        statusPoint = hit.point + Vector3.up * (_boxCenter.y + _boxHalf.y - _bottom + 0.4f);
    }

    private void Tint(Color color)
    {
        _block.SetColor("_BaseColor", color);
        foreach (var r in _ghostRenderers) if (r != null) r.SetPropertyBlock(_block);
    }

    // Можно ли поставить: материалы, склон, поверхность, зона «нельзя», пересечения (земля под ногами не считается)
    private Status Validate(RaycastHit hit, Entry entry)
    {
        if (Vector3.Angle(hit.normal, Vector3.up) > _maxSlope) return Status.Steep;
        if (entry.prefab.TryGetComponent(out BuildSurfaceRequirement surface) && !surface.Allows(hit)) return Status.WrongSurface;
        var pose = _ghost.transform;
        // Низ приподнят: пологий склон и пол под моделью - не помеха (у маленьких предметов - не больше трети высоты)
        float lift = Mathf.Min(0.15f, _boxHalf.y * 0.66f);
        var half = new Vector3(_boxHalf.x * 0.9f, Mathf.Max(0.02f, _boxHalf.y - lift * 0.5f), _boxHalf.z * 0.9f);
        var center = pose.position + pose.rotation * (_boxCenter + Vector3.up * lift * 0.5f);
        if (NoBuildZone.Blocks(new Bounds(center, half * 2f))) return Status.NoBuild;

        int count = Physics.OverlapBoxNonAlloc(center, half, _overlaps, pose.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var c = _overlaps[i];
            if (c == hit.collider || c is TerrainCollider) continue;
            // Лежащие предметы не мешают - их можно складывать друг на друга
            var tag = c.GetComponentInParent<DroppedItemTag>();
            if (tag != null && !tag.isPlaced) continue;
            if (IsTagged(c, _playerTag)) return Status.BlockedByYou;
            if (IsTagged(c, _companionTag)) return Status.BlockedByLittle;
            return Status.Blocked;
        }
        return CanAfford(entry) ? Status.Ok : Status.Lacking;
    }

    // ---------- Поставить / разобрать ----------

    private void OnPlace(InputAction.CallbackContext _)
    {
        if (!isActive || Paused || !ghostVisible || status != Status.Ok || Time.time < _nextPlaceTime) return;
        var entry = _ghostEntry;
        if (entry == null || !CanAfford(entry)) return;
        _nextPlaceTime = Time.time + 0.3f;
        var position = _ghost.transform.position;
        var rotation = _ghost.transform.rotation;

        GameObject go;
        if (entry.recipe != null)
        {
            var placeable = BuildCatalog.PlaceableOf(entry.recipe);
            go = Instantiate(placeable.worldPrefab, position, rotation);
            if (go.TryGetComponent(out PlaceableWorldObject world)) world.sourceItem = placeable;
            Physics.SyncTransforms();
            DroppedItemManager.RegisterPlaced(go, placeable, _autoSave);
        }
        else go = DroppedItemManager.PlaceDrop(entry.item, position, rotation, _autoSave);
        // Списываем только если объект появился
        if (go == null) return;
        foreach (var (item, count) in entry.cost)
            _inventory.RemoveItem(item, count);
        BuildBridge.RaiseBuilt(entry.recipe != null);

        if (go.TryGetComponent(out Rigidbody body)) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        // Последний такой предмет положен - список сдвинулся
        _entriesFrame = -1;
        blueprintIndex = Mathf.Clamp(blueprintIndex, 0, Mathf.Max(0, Blueprints.Count - 1));
    }

    // Мебель исчезает и возвращает материалы; что не влезло по весу - падает рядом
    private void OnDismantle(InputAction.CallbackContext _)
    {
        if (!isActive || Paused || target == null || _targetObject == null) return;
        var entry = target;
        var go = _targetObject;
        var dropAt = go.transform.position + Vector3.up * 0.5f;
        ClearTarget();

        // Материалы - сколько влезет по весу, остальное падает рядом
        foreach (var (item, count) in entry.cost)
        {
            int fits = item.weight > 0f ? Mathf.Clamp(Mathf.FloorToInt((_inventory.maxWeight - _inventory.weight) / item.weight + 0.0001f), 0, count) : count;
            if (fits > 0) _inventory.AddItem(item, fits);
            for (int i = fits; i < count; i++) DroppedItemManager.SpawnDrop(item, dropAt);
        }

        DroppedItemManager.OnItemPickedUp(go, _autoSave);
        Destroy(go);
        _entriesFrame = -1;
    }

    // ---------- Сохранение (вместе с игрой) ----------

    public void Save() => DroppedItemManager.SaveDirty();
    public void Load() { } // постройки грузит DroppedItemManager при загрузке сцены
}
