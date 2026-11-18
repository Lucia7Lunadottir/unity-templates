using PG.Localization;
using PG.NotificationManagement;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PG.MenuManagement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PG.InventoryManagement
{
    public class Inventory : MonoBehaviour, ISaveable
    {
        [field: SerializeField] public Transform bagPoint { get; private set; }
        [field: SerializeField] public ItemContainer itemContainer { get; private set; }
        public List<RuntimeItem> items = new List<RuntimeItem>();
        [Min(0)] public float maxWeight = 100;
        public float weight 
        { 
            get => _weight; 
            private set { 
                _weight = value;
                if (_weightCountText != null) _weightCountText.text = $"{_weight}/{maxWeight}";
            } 
        }
        [SerializeField, Min(0)] private float _weight = 100;
        [SerializeField] private TMP_Text _weightCountText;

        [SerializeField] private Transform _cellContainer;
        [SerializeField] private InventoryCell _cellPrefab;
        [SerializeField] private Selectable _itemsSelectable;

        public Transform cellContainer => _cellContainer;

        [SerializeField] private UIShowHide _cantAddItemsText;
        
        public event System.Action<RuntimeItem> itemAdded;
        public event System.Action<RuntimeItem> itemRemoved;

        public event System.Action<Item> itemUsed;
        public event System.Action<Item> itemDropped;

        [SerializeField] private bool _autoSave = true;
        [SerializeField] private string _filename = "Inventory.json";
        private string _filePath => Path.Combine(SaveManager.GetCurrentSaveFolderPath(), _filename);
        private SaveInventory _saveInventory = new SaveInventory();

        public static Inventory instance { get; private set; }

        [System.Serializable]
        public class RuntimeItem
        {
            public Item item; // ������ ������ �������� � ��������
            public int count;

            public RuntimeItem(Item item, int count = 1)
            {
                this.item = item;
                this.count = count;
            }
        }
        private void OnEnable()
        {
            if (LocalizationSystem.instance != null)
            {
                LocalizationSystem.instance.localizationChanged += DisplayLocalizedItems;
            }
        }
        private void OnDisable()
        {
            if (LocalizationSystem.instance != null)
            {
                LocalizationSystem.instance.localizationChanged -= DisplayLocalizedItems;
            }
        }
        private void Awake()
        {
            instance = this;
            
            Load();
        }

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            DisplayItems();
        }

        void DisplayLocalizedItems(string language)
        {
            DisplayItems();
        }
        public int GetItemID(Item item)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].item == item)
                {
                    return i;
                }
            }
            return -1;
        }

        public int GetItemCount(int itemID)
        {
            return itemID < 0 || itemID >= items.Count ? 0 : items[itemID].count;
        }

        public int GetItemCount(Item item)
        {
            int total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].item == item)
                    total += items[i].count;
            }
            return total;
        }

        public bool CheckItem(Item item)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].item == item)
                {
                    return true;
                }
            }
            return false;
        }

        public bool CheckItem(Item item, out int id)
        {
            id = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].item == item)
                {
                    id = i;
                    return true;
                }
            }
            return false;
        }

        public void AddItemForUnityEvent(Item item) => AddItem(item);
        public bool AddItem(Item item) => AddItem(item, 1);

        // Влезет ли по весу вместе с этим (раньше проверялся вес до добавления - при 99/100 проходил предмет любого веса)
        public bool CanCarry(Item item, int count = 1) => item != null && weight + item.weight * count <= maxWeight;

        // count штук разом: одна перерисовка, одно сохранение, одно уведомление; itemAdded - по разу на штуку, как раньше (CheckItemNode считает вызовы)
        public bool AddItem(Item item, int count)
        {
            if (item == null || count <= 0) return false;
            if (!CanCarry(item, count))
            {
                _cantAddItemsText.Show();
                UIAudioManager.Instance.PlayClick();
                return false;
            }
            for (int n = 0; n < count; n++)
            {
                if (item.isStackable && CheckItem(item, out int id))
                {
                    items[id].count++;
                    itemAdded?.Invoke(items[id]);
                }
                else
                {
                    RuntimeItem runtimeItem = new RuntimeItem(item);
                    items.Add(runtimeItem);
                    itemAdded?.Invoke(runtimeItem);
                }
            }
            weight += item.weight * count;
            DisplayItems();
            if (_autoSave)
            {
                Save();
            }

            NotificationSystem.instance.Add(count > 1 ? "×" + count : "", item.GetNameKey(), item.icon);
            return true;
        }

        public void RemoveItem(Item item) => RemoveItem(item, 1);

        // Убрать до count штук разом (из любых стопок); вернёт, сколько убрано. itemRemoved - по разу на штуку
        public int RemoveItem(Item item, int count)
        {
            int removed = 0;
            for (int i = items.Count - 1; i >= 0 && removed < count; i--)
            {
                if (items[i].item != item) continue;
                while (items[i].count > 0 && removed < count)
                {
                    items[i].count--;
                    removed++;
                    itemRemoved?.Invoke(items[i]);
                }
                if (items[i].count <= 0) items.RemoveAt(i);
            }

            weight -= item != null ? item.weight * removed : 0f;
            DisplayItems();
            if (_autoSave)
            {
                Save();
            }
            return removed;
        }

        public void RemoveItem(int itemID)
        {
            if (itemID < 0 || itemID >= items.Count)
            {
                return;
            }

            items[itemID].count--;
            itemRemoved?.Invoke(items[itemID]);
            weight -= items[itemID].item.weight;

            Item droppedItem = items[itemID].item;

            if (items[itemID].count <= 0)
            {
                items.RemoveAt(itemID);
            }

            itemDropped?.Invoke(droppedItem);

            DisplayItems();
            if (_autoSave)
            {
                Save();
            }
        }

        public void UseItem(int itemIndex)
        {
            if (itemIndex < 0 || itemIndex >= items.Count)
            {
                return;
            }

            items[itemIndex].item.Interact(this);
            itemUsed?.Invoke(items[itemIndex].item);

            RemoveItem(itemIndex);
        }
        // Новое меню вещей (ItemsMenuView) рисует ячейки в свою сетку своим шаблоном - номер ячейки по-прежнему = номер предмета
        public void UseCellContainer(Transform container, InventoryCell cellTemplate)
        {
            if (container == null || cellTemplate == null || container == _cellContainer) return;
            _cellContainer = container;
            _cellPrefab = cellTemplate;
            DisplayItems();
        }

        void DisplayItems()
        {
            // Сетку подключает ItemsMenuView при первом открытии «Вещей» - до этого рисовать некуда
            if (_cellContainer == null || _cellPrefab == null) return;
            if (items.Count > _cellContainer.childCount)
            {
                for (int i = _cellContainer.childCount; i < items.Count; i++)
                {
                    Instantiate(_cellPrefab, _cellContainer);
                }
            }
            else if (items.Count < _cellContainer.childCount)
            {
                // Destroy срабатывает в конце кадра: без отсоединения удаление + добавление в одном кадре (крафт) рисовало предмет в уничтожаемую ячейку
                for (int i = _cellContainer.childCount - 1; i >= items.Count; i--)
                {
                    var cell = _cellContainer.GetChild(i);
                    cell.SetParent(null, false);
                    Destroy(cell.gameObject);
                }
            }

            if (items.Count == 0)
            {
                _itemsSelectable?.Select();
            }


            for (int i = 0; i < items.Count; i++)
            {
                if (_cellContainer.GetChild(i).TryGetComponent(out InventoryCell cell))
                {
                    cell.Render(items[i]);
                }
            }
        }
        

        public void Save()
        {
            _saveInventory = new SaveInventory();
            _saveInventory.items = new SaveInventory.SaveItem[items.Count];
            for (int i = 0; i < _saveInventory.items.Length; i++)
            {
                _saveInventory.items[i] = new SaveInventory.SaveItem(itemContainer.GetItemID(items[i].item), items[i].count);
            }
            _saveInventory.weight = weight;
            _saveInventory.maxWeight = maxWeight;

            string json = JsonUtility.ToJson(_saveInventory);
            File.WriteAllText(_filePath, json);
        }

        public void Load()
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                JsonUtility.FromJsonOverwrite(json, _saveInventory);

                // Safety check: Ensure itemContainer is assigned
                if (itemContainer == null || itemContainer.items == null)
                {
                    Debug.LogError("Inventory: ItemContainer is missing or empty!");
                    return;
                }

                items = new List<RuntimeItem>(_saveInventory.items.Length);

                if (_saveInventory.items.Length > 0)
                {
                    for (int i = 0; i < _saveInventory.items.Length; i++)
                    {
                        if (_saveInventory.items[i] != null)
                        {
                            int savedID = _saveInventory.items[i].id;

                            // --- FIX STARTS HERE ---
                            // Check if the saved ID is actually within the bounds of your current ItemContainer
                            if (savedID >= 0 && savedID < itemContainer.items.Count()) // Or .Length if it's an array
                            {
                                items.Add(new RuntimeItem(itemContainer.items[savedID], _saveInventory.items[i].count));
                            }
                            else
                            {
                                Debug.LogWarning($"Inventory Load: Item ID {savedID} was found in save file but does not exist in ItemContainer. Item skipped.");
                            }
                            // --- FIX ENDS HERE ---
                        }
                    }
                }

                // Вес по тому, что реально загрузилось: пропущенные предметы не должны оставлять «невидимый» вес
                float loadedWeight = 0f;
                foreach (var runtimeItem in items)
                    if (runtimeItem.item != null) loadedWeight += runtimeItem.item.weight * runtimeItem.count;
                weight = loadedWeight;
                maxWeight = _saveInventory.maxWeight;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load inventory: {e.Message}");
                // Optional: Create a fresh save if the file is corrupted
            }
        }

        [System.Serializable]
        public class SaveInventory
        {
            public float weight;
            public float maxWeight;

            public SaveItem[] items;
            [System.Serializable]
            public class SaveItem
            {
                public int id;
                public int count;
                public SaveItem(int targetID, int targetCount)
                {
                    id = targetID;
                    count = targetCount;
                }
            }
        }
    }
}